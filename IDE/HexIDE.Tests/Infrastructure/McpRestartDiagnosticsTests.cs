using System.IO;
using Microsoft.Extensions.Logging;

namespace HexIDE.Tests.Infrastructure;

public class McpRestartDiagnosticsTests
{
    [Fact]
    public async Task Failure_includes_every_phase_and_the_inner_exception_chain()
    {
        var trace = new McpRestartDiagnostics((_, _) => { });
        await trace.PhaseAsync("first connect attempt", () => Task.FromResult(1));
        await trace.PhaseAsync("stop", () => Task.CompletedTask);
        await trace.PhaseAsync("restart", () => Task.CompletedTask);
        var failure = new TaskCanceledException("call bound", new IOException("read aborted",
            new System.Net.Sockets.SocketException(10054)));
        var call = () => trace.PhaseAsync<int>("call after", () => Task.FromException<int>(failure));
        (await call.Should().ThrowAsync<TaskCanceledException>()).Which.Should().BeSameAs(failure);

        var message = trace.Failure(failure);

        foreach (var phase in new[] { "stop", "restart", "first connect attempt", "call after" })
            message.Should().MatchRegex(phase + @": [0-9.]+ ms");
        message.Should().Contain("Reconnect attempts unavailable: the SDK does not expose HTTP connection attempts or retries.")
            .And.Contain("System.Threading.Tasks.TaskCanceledException: call bound")
            .And.Contain("System.IO.IOException: read aborted")
            .And.Contain("System.Net.Sockets.SocketException");
    }

    [Fact]
    public async Task A_phase_still_pending_at_the_call_bound_is_in_the_failure()
    {
        var trace = new McpRestartDiagnostics((_, _) => { });
        var completion = new TaskCompletionSource<int>();
        var connect = trace.PhaseAsync("call after", () => completion.Task);
        try
        {
            trace.Failure(new TimeoutException("call bound")).Should()
                .MatchRegex(@"call after: [0-9.]+ ms .*; pending")
                .And.Contain("Reconnect attempts unavailable:");
        }
        finally
        {
            completion.SetResult(1);
            await connect;
        }
    }

    [Fact]
    public async Task Successful_phases_are_written_at_debug_level_with_elapsed_time()
    {
        var logs = new List<(LogLevel Level, string Message)>();
        var trace = new McpRestartDiagnostics((level, message) => logs.Add((level, message)));

        (await trace.PhaseAsync("call after", () => Task.FromResult("after"))).Should().Be("after");

        logs.Should().Contain(entry => entry.Level == LogLevel.Debug
            && entry.Message.Contains("Reconnect attempts unavailable:", StringComparison.Ordinal)
            && System.Text.RegularExpressions.Regex.IsMatch(entry.Message, @"call after: [0-9.]+ ms"));
    }
}
