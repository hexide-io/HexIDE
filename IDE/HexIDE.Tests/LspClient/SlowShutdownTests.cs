using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
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
        var elapsed = Stopwatch.StartNew();
        await client.StopAsync().WaitAsync(TimeSpan.FromSeconds(20), token);
        await log.DrainAsync();
        killHeldExitGate.Should().NotBeNull($"the {mode} server must outlast the exit wait; stop took {elapsed.Elapsed.TotalMilliseconds:F0} ms");
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Slow_server_survives_disconnect_during_exit(bool pipe) =>
        AssertSlowDisconnect(pipe, waitForExit: false);

    internal static async Task AssertSlowDisconnect(bool pipe, bool waitForExit)
    {
        var token = TestContext.Current.CancellationToken;
        var name = $"h3244-{Guid.NewGuid():N}"[..20];
        using var listener = pipe
            ? new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous)
            : null;
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(typeof(global::HexIDE.Lsp.TestServer.Program).Assembly.Location);
        start.ArgumentList.Add("slow");
        if (pipe) start.ArgumentList.Add(name);
        using var process = Process.Start(start)!;
        try
        {
            if (listener is not null)
                await listener.WaitForConnectionAsync(token).WaitAsync(TimeSpan.FromSeconds(30), token);
            var input = listener as Stream ?? process.StandardInput.BaseStream;
            var output = listener as Stream ?? process.StandardOutput.BaseStream;
            var shutdown = Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"shutdown\"}");
            await input.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {shutdown.Length}\r\n\r\n"), token)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(5), token);
            await input.WriteAsync(shutdown, token).AsTask().WaitAsync(TimeSpan.FromSeconds(5), token);
            await input.FlushAsync(token).WaitAsync(TimeSpan.FromSeconds(5), token);
            var reply = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":null}";
            var expected = Encoding.UTF8.GetBytes($"Content-Length: {Encoding.UTF8.GetByteCount(reply)}\r\n\r\n{reply}");
            var received = new byte[expected.Length];
            await output.ReadExactlyAsync(received, token).AsTask().WaitAsync(TimeSpan.FromSeconds(30), token);
            received.Should().Equal(expected, "the server must answer shutdown before disconnecting");

            // Exceed the pipe buffer so a server that stops draining fails here on Linux too.
            await input.WriteAsync(Encoding.ASCII.GetBytes("Content-Length: 2000000\r\n\r\n{"), token)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(5), token);
            await input.WriteAsync(new byte[1_000_000], token).AsTask().WaitAsync(TimeSpan.FromSeconds(5), token);
            await input.FlushAsync(token).WaitAsync(TimeSpan.FromSeconds(5), token);
            if (listener is not null) listener.Dispose();
            else process.StandardInput.Close();
            var grace = Task.Delay(LaunchedServerGuard.ExitTimeout, token);
            var exited = process.WaitForExitAsync(token);
            (await Task.WhenAny(exited, grace)).Should().Be(grace,
                "slow mode must remain alive past the exit wait even when the peer disconnects (#3244)");
            await grace;
            if (waitForExit)
            {
                await exited.WaitAsync(TimeSpan.FromSeconds(70), token);
                process.ExitCode.Should().Be(0, "a disconnected slow server must eventually exit on its own (#3244)");
            }
        }
        finally
        {
            LaunchedServerGuard.Kill(process);
            await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(10), token);
        }
    }

}

public class SlowServerLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Disconnected_slow_server_eventually_exits(bool pipe) =>
        SlowShutdownTests.AssertSlowDisconnect(pipe, waitForExit: true);
}
