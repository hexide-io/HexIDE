using HexIDE.Conversations;
using HexIDE.IDE;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;

namespace HexIDE.Tests.LspClient;

public class SilentStdioRestartTests
{
    [Fact]
    public async Task Stopping_during_restart_teardown_does_not_launch_a_replacement()
    {
        var token = TestContext.Current.CancellationToken;
        var marker = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        try
        {
            var server = typeof(global::HexIDE.Lsp.TestServer.Program).Assembly.Location;
            var transport = new StdioProcessLspTransport(
                new LspServerInfo("dotnet", $"\"{server}\" initialize-silent \"{marker}\"", Path.GetTempPath()),
                Substitute.For<ILogger<StdioProcessLspTransport>>());
            await using var client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(),
                "stopping", initializeTimeout: TimeSpan.FromSeconds(2));
            var stopping = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
            transport.KillProcess = process =>
            {
                stopping.TrySetResult(client.StopAsync());
                process.Kill(entireProcessTree: true);
            };
            var notices = 0;
            client.MessageShown += (_, _) => notices++;
            var start = client.StartAsync(token);
            var stop = await stopping.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
            await Task.WhenAll(start, stop).WaitAsync(TimeSpan.FromSeconds(15), token);
            File.ReadAllLines(marker).Should().HaveCount(1);
            client.IsRunning.Should().BeFalse();
            notices.Should().Be(0);
            client.LastAttempt!.Steps.Last().Outcome.Should().Be(LanguageConnectionStepOutcome.Cancelled);
        }
        finally { File.Delete(marker); }
    }

    [Fact]
    public async Task Registry_uses_the_configured_display_name_for_the_restart_notice()
    {
        var server = Substitute.For<ILspClient>();
        server.IsRunning.Returns(true);
        var registration = new LanguageServerRegistration("server-id", "Friendly Server", [".bas"],
            DocumentLanguage.Vb6, () => server);
        await using var registry = new LspClientRegistry([registration], Substitute.For<ILogger<LspClientRegistry>>());
        ShowMessageParams? received = null;
        registry.MessageShown += (_, message) => received = message;
        await registry.OpenDocumentAsync("file:///test.bas", "", TestContext.Current.CancellationToken);
        server.MessageShown += Raise.Event<EventHandler<ShowMessageParams>>(server,
            new ShowMessageParams(LspMessageType.Warning, "")
            {
                LocalizationKey = "Str.Lsp.RestartedAfterSilence",
                ServerDisplayName = "server-id",
            });
        received.Should().NotBeNull();
        received!.ServerDisplayName.Should().Be("Friendly Server");
    }

    [Fact]
    public void Restart_notice_formats_the_server_name_in_the_status_bar()
    {
        var client = Substitute.For<ILspClient>();
        var status = Substitute.For<IStatusBarService>();
        using var reporter = new LanguageServerMessageReporter(client, status, new HexIDE.Localization.LocalizationService());
        client.MessageShown += Raise.Event<EventHandler<ShowMessageParams>>(client,
            new ShowMessageParams(LspMessageType.Warning, "")
            {
                LocalizationKey = "Str.Lsp.RestartedAfterSilence",
                ServerDisplayName = "Test Server",
            });
        status.Received().SetTemporaryMessage(
            Arg.Is<string>(text => text.Contains("Test Server did not answer; restarted it.")), Arg.Any<TimeSpan>());
    }

    [Theory]
    [InlineData("initialize-exit")]
    [InlineData("initialize-refuse")]
    [InlineData("initialize-partial")]
    public async Task Other_startup_failures_do_not_restart(string mode)
    {
        var token = TestContext.Current.CancellationToken;
        var marker = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        try
        {
            var server = typeof(global::HexIDE.Lsp.TestServer.Program).Assembly.Location;
            var transport = new StdioProcessLspTransport(
                new LspServerInfo("dotnet", $"\"{server}\" {mode} \"{marker}\"", Path.GetTempPath()),
                Substitute.For<ILogger<StdioProcessLspTransport>>());
            // The Unix guard stays alive for two seconds after its child exits.
            await using var client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(),
                "failure", initializeTimeout: TimeSpan.FromSeconds(5));
            await client.StartAsync(token).WaitAsync(TimeSpan.FromSeconds(30), token);
            File.ReadAllLines(marker).Should().HaveCount(1);
            client.IsRunning.Should().BeFalse();
        }
        finally { File.Delete(marker); }
    }

    [Theory]
    [InlineData("initialize-once", true)]
    [InlineData("initialize-silent", false)]
    public async Task Silent_initialize_restarts_once_and_reports_the_result(string mode, bool recovers)
    {
        var token = TestContext.Current.CancellationToken;
        var marker = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        try
        {
            await using var log = new ConversationLog();
            var server = typeof(global::HexIDE.Lsp.TestServer.Program).Assembly.Location;
            var transport = new StdioProcessLspTransport(
                new LspServerInfo("dotnet", $"\"{server}\" {mode} \"{marker}\"", Path.GetTempPath()),
                Substitute.For<ILogger<StdioProcessLspTransport>>());
            await using var client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(),
                "silent", capture: log, connectionId: "silent", initializeTimeout: TimeSpan.FromSeconds(2));
            var connected = 0;
            client.Connected += (_, _) => connected++;
            var notices = new List<ShowMessageParams>();
            client.MessageShown += (_, message) => notices.Add(message);

            await client.StartAsync(token).WaitAsync(TimeSpan.FromSeconds(30), token);

            File.ReadAllLines(marker).Should().HaveCount(2, "both processes must receive initialize");
            File.ReadAllLines(marker).Distinct().Should().HaveCount(2);
            client.IsRunning.Should().Be(recovers);
            connected.Should().Be(recovers ? 1 : 0);
            if (!recovers) client.LastAttempt!.Steps.Last().Detail.Should().Contain("tried twice");
            notices.Should().ContainSingle(p => p.LocalizationKey == "Str.Lsp.RestartedAfterSilence");
            await log.DrainAsync();
            log.Snapshot("silent").Count(e => e.Detail?.StartsWith("process started with pid ") == true).Should().Be(2);
            log.Snapshot("silent").Should().ContainSingle(e =>
                e.Kind == ConversationEntryKind.Lifecycle && e.Detail == "initialize unanswered; restarting stdio server once");
            await client.StopAsync();
        }
        finally { File.Delete(marker); }
    }
}
