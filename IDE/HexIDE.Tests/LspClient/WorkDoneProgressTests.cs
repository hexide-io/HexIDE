using System.Collections.Concurrent;
using System.Text.Json;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;

namespace HexIDE.Tests.LspClient;

public partial class TraceOnTheWireTests
{
    private async Task<VBLspClient> ConnectedForProgressAsync()
    {
        // A missing notification times out after 30s; keep the peer alive long enough to answer shutdown.
        _cts.CancelAfter(TimeSpan.FromMinutes(2));
        return await ConnectedAsync();
    }

    [Fact]
    public async Task WorkDoneProgressIsDeclaredInTheHandshake()
    {
        await ConnectedForProgressAsync();
        FrameFor("initialize").GetProperty("params").GetProperty("capabilities")
            .GetProperty("window").GetProperty("workDoneProgress").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData("\"index\"")]
    [InlineData("1")]
    public async Task CreateIsAnsweredWithANullResultAndProgressReachesTheSubscriber(string token)
    {
        var client = await ConnectedForProgressAsync();
        var received = new ConcurrentQueue<ProgressParams>();
        client.ProgressReceived += (_, p) => received.Enqueue(p);
        await SendAsync("""{"jsonrpc":"2.0","id":360,"method":"window/workDoneProgress/create","params":{"token":TOKEN}}"""
            .Replace("TOKEN", token));
        await EventuallyAsync(() =>
        {
            lock (_received) return _received.Any(d => d.RootElement.TryGetProperty("id", out var id) && id.GetRawText() == "360");
        });
        JsonElement response;
        lock (_received) response = _received.Single(d => d.RootElement.TryGetProperty("id", out var id) && id.GetRawText() == "360").RootElement;
        response.TryGetProperty("error", out _).Should().BeFalse();
        response.GetProperty("result").ValueKind.Should().Be(JsonValueKind.Null);

        foreach (var value in new[] { "{\"kind\":\"begin\",\"title\":\"indexing\",\"percentage\":0}",
                     "{\"kind\":\"report\",\"message\":\"headers\",\"percentage\":50}", "{\"kind\":\"end\"}" })
            await SendAsync("{\"jsonrpc\":\"2.0\",\"method\":\"$/progress\",\"params\":{\"token\":" + token + ",\"value\":" + value + "}}");
        await EventuallyAsync(() => received.Count == 3);
        received.Select(p => p.Value.Kind).Should().Equal("begin", "report", "end");
        received.Select(p => p.Token.GetRawText()).Should().OnlyContain(t => t == token);
        received.ElementAt(1).Value.Message.Should().Be("headers");
        received.ElementAt(1).Value.Percentage.Should().Be(50);
    }

    [Theory]
    [InlineData("\"rumdl-index\"", false)]
    [InlineData("1", false)]
    [InlineData("\"rumdl-index\"", true)]
    [InlineData("1", true)]
    public async Task UnsolicitedProgressBeginsAndConnectionTeardownEndsIt(string token, bool stop)
    {
        var client = await ConnectedForProgressAsync();
        var received = new ConcurrentQueue<ProgressParams>();
        client.ProgressReceived += (_, p) => received.Enqueue(p);
        await SendAsync("{\"jsonrpc\":\"2.0\",\"method\":\"$/progress\",\"params\":{\"token\":" + token + ",\"value\":{\"kind\":\"begin\",\"title\":\"indexing\"}}}");
        await EventuallyAsync(() => received.Count == 1);
        if (stop) await client.StopAsync();
        else _serverSide!.Dispose();
        await EventuallyAsync(() => received.Count == 2);
        received.Select(p => p.Value.Kind).Should().Equal("begin", "end");
    }
}

public class WorkDoneProgressReporterTests
{
    private static (ILspClient Client, StatusBarService Status, LanguageServerMessageReporter Reporter) Setup()
    {
        var client = Substitute.For<ILspClient>();
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.StatusBar.LanguageServerProgress").Returns("Language server: {0}");
        localization.GetString("Str.StatusBar.Ready").Returns("Ready");
        var status = new StatusBarService(localization);
        return (client, status, new LanguageServerMessageReporter(client, status, localization));
    }

    private static void Send(ILspClient client, object sender, string token, string kind,
        string? title = null, string? message = null, uint? percentage = null) =>
        client.ProgressReceived += Raise.Event<EventHandler<ProgressParams>>(sender,
            new ProgressParams(JsonSerializer.SerializeToElement(token), new WorkDoneProgressValue(kind, title, message, percentage)));

    [Fact]
    public void ProgressBeginReportAndEndUpdateThenImmediatelyClearTheStatus()
    {
        var (client, status, reporter) = Setup();
        using var _ = reporter;
        Send(client, client, "one", "begin", "indexing", percentage: 0);
        status.Message.Should().Be("Language server: indexing (0%)");
        Send(client, client, "one", "report", message: "headers", percentage: 50);
        status.Message.Should().Be("Language server: indexing headers (50%)");
        Send(client, client, "one", "report");
        status.Message.Should().Be("Language server: indexing headers (50%)");
        Send(client, client, "one", "end", message: "done");
        status.Message.Should().Be("Ready");
    }

    [Fact]
    public void ProgressServerTextIsFlattenedAndCapped()
    {
        var (client, status, reporter) = Setup();
        using var _ = reporter;
        Send(client, client, "one", "begin", "index\r\n  files", "many\nheaders");
        status.Message.Should().Be("Language server: index files many headers");
        Send(client, client, "one", "report", message: new string('x', 5000));
        status.Message.Length.Should().BeLessThan(230);
        status.Message.Should().EndWith("…");
    }

    [Fact]
    public void ProgressTokensAreScopedToTheirServerAndEndingOneRestoresTheOther()
    {
        var (client, status, reporter) = Setup();
        using var _ = reporter;
        var other = new object();
        Send(client, client, "same", "begin", "first");
        Send(client, other, "same", "begin", "second");
        Send(client, other, "same", "end");
        status.Message.Should().Be("Language server: first");
        Send(client, client, "same", "end");
        status.Message.Should().Be("Ready");
    }

    [Fact]
    public void ProgressEndDoesNotEraseALaterStatusMessage()
    {
        var (client, status, reporter) = Setup();
        using var _ = reporter;
        Send(client, client, "one", "begin", "indexing");
        status.Message.Should().Contain("indexing");
        status.SetMessage("Running");
        Send(client, client, "one", "end");
        status.Message.Should().Be("Running");
    }

    [Fact]
    public void ProgressDisposalStopsUpdates()
    {
        var (client, status, reporter) = Setup();
        Send(client, client, "one", "begin", "indexing");
        status.Message.Should().Contain("indexing");
        reporter.Dispose();
        Send(client, client, "one", "report", message: "after disposal");
        status.Message.Should().NotContain("after disposal");
    }
}
