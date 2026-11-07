using System.Globalization;
using System.Text;
using System.Text.Json;
using HexIDE.IDE;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

public class ShowDocumentWireTests : IAsyncDisposable
{
    private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(10));
    private readonly Func<HexIDE.Lsp.Messages.ShowDocumentParams, CancellationToken, Task<bool>> _show =
        Substitute.For<Func<HexIDE.Lsp.Messages.ShowDocumentParams, CancellationToken, Task<bool>>>();
    private Stream? _peer;
    private VBLspClient? _client;
    private CancellationToken Token => _timeout.Token;

    private async Task<JsonElement> ConnectAsync(Func<HexIDE.Lsp.Messages.ShowDocumentParams, CancellationToken, Task<bool>>? show = null)
    {

        var (local, peer) = FullDuplexStream.CreatePair();
        _peer = peer;
        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.LeaveServerRunning.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(local, local, ci.Arg<IJsonRpcMessageFormatter>())));
        _client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), "vb6", showDocument: show ?? _show);
        var starting = _client.StartAsync(Token);
        var initialize = await ReadAsync();
        await WriteFrameAsync(peer, "{\"jsonrpc\":\"2.0\",\"id\":" + initialize.GetProperty("id").GetRawText()
            + ",\"result\":{\"capabilities\":{}}}", Token);
        await starting;
        (await ReadAsync()).GetProperty("method").GetString().Should().Be("initialized");
        return initialize;
    }

    private async Task<JsonElement> ReadAsync()
    {
        using var document = JsonDocument.Parse((await ReadFrameAsync(_peer!, Token))!);
        return document.RootElement.Clone();
    }

    [Fact]
    public async Task InitializeDeclaresShowDocument()
    {
        var frame = await ConnectAsync();
        frame.GetProperty("params").GetProperty("capabilities").GetProperty("window")
            .GetProperty("showDocument").GetProperty("support").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DocumentRequestAnswersWithNavigationResult(bool success)
    {
        _show(Arg.Any<HexIDE.Lsp.Messages.ShowDocumentParams>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(success));
        await ConnectAsync();
        await WriteFrameAsync(_peer!, """
            {"jsonrpc":"2.0","id":"document","method":"window/showDocument","params":{
              "uri":"untitled:P/Module1.bas","takeFocus":true,
              "selection":{"start":{"line":1,"character":2},"end":{"line":2,"character":3}}}}
            """, Token);
        var frame = await ReadAsync();
        frame.GetProperty("id").GetString().Should().Be("document");
        frame.GetProperty("result").GetProperty("success").GetBoolean().Should().Be(success);
        await _show.Received()(Arg.Is<HexIDE.Lsp.Messages.ShowDocumentParams>(p =>
            p.Uri == "untitled:P/Module1.bas" && p.TakeFocus && !p.External
            && p.Selection!.Start.Line == 1 && p.Selection.Start.Character == 2
            && p.Selection.End.Line == 2 && p.Selection.End.Character == 3), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("no", false, false)]
    [InlineData("dismiss", false, false)]
    [InlineData("yes", true, true)]
    [InlineData("yes", false, false)]
    public async Task ExternalDocumentAsksEveryTimeAndLaunchesOnlyOnYes(string choice, bool launched, bool success)
    {
        const string uri = "https://example.com/full/path?query=whole#fragment";
        var localization = Substitute.For<HexIDE.Localization.ILocalizationService>();
        localization.GetString("Str.Dialog.MessageBox.Button.Yes").Returns("Yes");
        localization.GetString("Str.Dialog.MessageBox.Button.No").Returns("No");
        localization.GetString("Str.Lsp.OpenExternalUri").Returns("Open outside HexIDE?\n\n{0}");
        var questions = new List<HexIDE.Lsp.Messages.ShowMessageRequestParams>();
        var launcher = Substitute.For<Func<Uri, CancellationToken, Task<bool>>>();
        launcher(Arg.Any<Uri>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(launched));
        var prompts = new ServerMessagePrompt((title, request, _, _) =>
        {
            title.Should().Be("Test server");
            questions.Add(request);
            var answer = choice == "dismiss" ? null : request.Actions![choice == "yes" ? 0 : 1];
            return Task.FromResult<Task<HexIDE.Lsp.Messages.MessageActionItem?>?>(Task.FromResult(answer));
        });
        var navigation = new ServerDocumentNavigation(prompts, localization, launcher);
        await ConnectAsync((request, token) => request.External
            ? navigation.ShowExternalAsync("Test server", request.Uri, token) : _show(request, token));
        for (var i = 0; i < 2; i++)
        {
            await WriteFrameAsync(_peer!, """
                {"jsonrpc":"2.0","id":3,"method":"window/showDocument",
                 "params":{"uri":"https://example.com/full/path?query=whole#fragment","external":true}}
                """, Token);
            var frame = await ReadAsync();
            frame.GetProperty("result").GetProperty("success").GetBoolean().Should().Be(success);
            questions.Should().HaveCount(i + 1);
            questions[i].Message.Should().Be("Open outside HexIDE?\n\n" + uri);
            questions[i].Actions!.Select(a => a.Title).Should().Equal("Yes", "No");
        }
        await launcher.Received(choice == "yes" ? 2 : 0)(Arg.Is<Uri>(u => u.OriginalString == uri), Arg.Any<CancellationToken>());
        await _show.DidNotReceive()(Arg.Any<HexIDE.Lsp.Messages.ShowDocumentParams>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ms-settings:privacy")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a uri")]
    public async Task ExternalDocumentOffersOnlyWebAndMailLinks(string uri)
    {
        var localization = Substitute.For<HexIDE.Localization.ILocalizationService>();
        var asked = 0;
        var launcher = Substitute.For<Func<Uri, CancellationToken, Task<bool>>>();
        var prompts = new ServerMessagePrompt((_, request, _, _) =>
        {
            asked++;
            return Task.FromResult<Task<HexIDE.Lsp.Messages.MessageActionItem?>?>(Task.FromResult(request.Actions?[0]));
        });
        var navigation = new ServerDocumentNavigation(prompts, localization, launcher);
        await ConnectAsync((request, token) => request.External
            ? navigation.ShowExternalAsync("Test server", request.Uri, token) : _show(request, token));
        var param = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object> { ["uri"] = uri, ["external"] = true });
        await WriteFrameAsync(_peer!, "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"window/showDocument\",\"params\":" + param + "}", Token);
        var frame = await ReadAsync();
        frame.GetProperty("result").GetProperty("success").GetBoolean().Should().BeFalse();
        asked.Should().Be(0);
        await launcher.DidNotReceive()(Arg.Any<Uri>(), Arg.Any<CancellationToken>());
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null) await _client.DisposeAsync();
        if (_peer is not null) await _peer.DisposeAsync();
        _timeout.Dispose();
    }

    private static async Task<string?> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        var header = new List<byte>(64);
        var one = new byte[1];

        while (true)
        {
            if (await stream.ReadAsync(one.AsMemory(0, 1), ct) == 0) return null;
            header.Add(one[0]);
            if (header.Count >= 4
                && header[^4] == (byte)'\r' && header[^3] == (byte)'\n'
                && header[^2] == (byte)'\r' && header[^1] == (byte)'\n')
            {
                break;
            }
        }

        var length = 0;
        foreach (var line in Encoding.ASCII.GetString([.. header]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                length = int.Parse(line["Content-Length:".Length..].Trim(), CultureInfo.InvariantCulture);
            }
        }

        if (length <= 0) return null;

        var body = new byte[length];
        var read = 0;
        while (read < length)
        {
            var n = await stream.ReadAsync(body.AsMemory(read, length - read), ct);
            if (n == 0) return null;
            read += n;
        }

        return Encoding.UTF8.GetString(body);
    }

    private static async Task WriteFrameAsync(Stream stream, string json, CancellationToken ct)
    {
        // Collapse the interpolated literals' incidental newlines; the header must state the real length.
        var compact = JsonSerializer.Serialize(JsonDocument.Parse(json).RootElement);
        var body = Encoding.UTF8.GetBytes(compact);
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"), ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }
}
