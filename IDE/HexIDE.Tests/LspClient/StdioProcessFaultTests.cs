using System.Diagnostics;
using HexIDE.Conversations;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;

namespace HexIDE.Tests.LspClient;

public class StdioProcessFaultTests
{
    [Theory]
    [InlineData("clean")]
    [InlineData("ignore")]
    [InlineData("tree")]
    [InlineData("crash")]
    [InlineData("nonzero")]
    public async Task Process_lifecycle_is_observable_and_stop_leaves_no_server(string mode)
    {
        var token = TestContext.Current.CancellationToken;
        await using var log = new ConversationLog();
        var server = typeof(global::HexIDE.Lsp.TestServer.Program).Assembly.Location;
        var transport = new StdioProcessLspTransport(
            new LspServerInfo("dotnet", $"\"{server}\" {mode}", Path.GetTempPath()),
            Substitute.For<ILogger<StdioProcessLspTransport>>());
        await using var client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(),
            "fault", capture: log, connectionId: "fault");
        Process? child = null;
        try
        {
            await client.StartAsync(token).WaitAsync(TimeSpan.FromSeconds(30), token);
            var pid = await WaitFor(e => e.Detail?.StartsWith("pid:", StringComparison.Ordinal) == true);
            using var process = Process.GetProcessById(int.Parse(pid.Detail![4..]));
            if (mode == "tree")
            {
                var entry = await WaitFor(e => e.Detail?.StartsWith("child:", StringComparison.Ordinal) == true);
                child = Process.GetProcessById(int.Parse(entry.Detail![6..]));
            }
            if (mode is "crash" or "nonzero")
            {
                await client.RequestHoverAsync("file:///fault.vb", new Position(0, 0), token)
                    .WaitAsync(TimeSpan.FromSeconds(30), token);
                var exit = await WaitFor(e => e.Detail == $"process exited with code {(mode == "crash" ? 23 : 17)}");
                exit.Fault.Should().BeTrue();
                client.IsRunning.Should().BeFalse();
            }
            var timer = Stopwatch.StartNew();
            await client.StopAsync().WaitAsync(TimeSpan.FromSeconds(15), token);
            timer.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
            if (mode is "ignore" or "tree")
                timer.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(2));
            process.HasExited.Should().BeTrue();
            if (child is not null)
            {
                // A reparented Unix child may briefly remain a zombie until the host reaps it.
                await WaitUntilGone(child);
            }
            if (mode == "clean")
            {
                (await WaitFor(e => e.Detail == "process exited with code 0")).Fault.Should().BeFalse();
            }
            else if (mode is "ignore" or "tree")
                (await WaitFor(e => e.Detail?.StartsWith("process exited with code ", StringComparison.Ordinal) == true))
                    .Fault.Should().BeTrue();
        }
        finally
        {
            if (child is not null)
            {
                try { if (!child.HasExited) child.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                child.Dispose();
            }
        }

        async Task<ConversationEnvelope> WaitFor(Func<ConversationEnvelope, bool> match)
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(30))
            {
                await log.DrainAsync();
                var entry = log.Snapshot("fault").FirstOrDefault(match);
                if (entry is not null) return entry;
                await Task.Delay(10, token);
            }
            throw new InvalidOperationException("Expected process evidence did not reach the conversation");
        }

        async Task WaitUntilGone(Process process)
        {
            var timer = Stopwatch.StartNew();
            while (!process.HasExited && timer.Elapsed < TimeSpan.FromSeconds(5))
            {
                if (OperatingSystem.IsLinux() && File.Exists($"/proc/{process.Id}/stat") &&
                    File.ReadAllText($"/proc/{process.Id}/stat").Split(')')[1].TrimStart().StartsWith('Z')) return;
                await Task.Delay(10, token);
            }
            process.HasExited.Should().BeTrue("stopping a wrapper must terminate its server child too");
        }
    }
}
