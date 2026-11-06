using HexIDE.Conversations;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;

namespace HexIDE.Tests.LspClient;

public class SlowShutdownTests
{
    [Theory]
    [InlineData(false, "slow")]
    [InlineData(true, "slow")]
    [InlineData(false, "unanswered")]
    [InlineData(true, "unanswered")]
    public async Task Timeout_kill_is_a_stop_only_after_answered_shutdown(bool pipe, string mode)
    {
        var token = TestContext.Current.CancellationToken;
        var server = typeof(global::HexIDE.Lsp.TestServer.Program).Assembly.Location;
        var name = $"h3217-{Guid.NewGuid():N}"[..20];
        ILspTransport transport = pipe
            ? new NamedPipeLspTransport(name, NamedPipeRole.Listen,
                Substitute.For<ILogger<NamedPipeLspTransport>>(),
                new NamedPipeLaunch("dotnet", "", ArgumentList: [server, mode, name]))
            : new StdioProcessLspTransport(new LspServerInfo("dotnet", "", Path.GetTempPath(), ArgumentList: [server, mode]),
                Substitute.For<ILogger<StdioProcessLspTransport>>());
        bool? killHeldExitGate = null;
        bool? stopFlagSetBeforeKill = null;
        void Kill(System.Diagnostics.Process process)
        {
            var type = transport.GetType();
            var fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var gate = (Lock)type.GetField("_exitGate", fields)!.GetValue(transport)!;
            killHeldExitGate = gate.IsHeldByCurrentThread;
            stopFlagSetBeforeKill = (bool)type.GetField("_endedAfterShutdown", fields)!.GetValue(transport)!;
            LaunchedServerGuard.Kill(process);
        }
        if (transport is StdioProcessLspTransport stdio) stdio.KillProcess = Kill;
        else ((NamedPipeLspTransport)transport).KillProcess = Kill;
        await using var log = new ConversationLog();
        await using var client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(),
            "slow", capture: log, connectionId: "slow");
        await client.StartAsync(token).WaitAsync(TimeSpan.FromSeconds(30), token);
        client.IsRunning.Should().BeTrue();
        await client.StopAsync().WaitAsync(TimeSpan.FromSeconds(20), token);
        await log.DrainAsync();
        killHeldExitGate.Should().BeFalse("process termination must never hold the exit handler's lock (#3217)");
        stopFlagSetBeforeKill.Should().Be(mode == "slow");
        var entries = log.Snapshot("slow");
        if (mode == "slow")
        {
            entries.Should().Contain(e => e.Kind == ConversationEntryKind.Request && e.Method == "shutdown" && e.Outcome == ConversationOutcome.Answered);
            entries.Should().ContainSingle(e => e.Detail ==
                $"stopped after shutdown; still exiting after {LaunchedServerGuard.ExitTimeout.TotalSeconds:g} s, so ended" && !e.Fault);
            entries.Should().NotContain(e => e.Kind == ConversationEntryKind.Lifecycle && e.Fault);
            entries.Should().NotContain(e => e.Detail != null && e.Detail.StartsWith("process exited with code ", StringComparison.Ordinal));
        }
        else
        {
            entries.Should().Contain(e => e.Kind == ConversationEntryKind.Lifecycle && e.Fault
                && e.Detail != null && e.Detail.StartsWith("process exited with code ", StringComparison.Ordinal));
            entries.Should().NotContain(e => e.Detail != null && e.Detail.StartsWith("stopped after shutdown", StringComparison.Ordinal));
        }
    }
}
