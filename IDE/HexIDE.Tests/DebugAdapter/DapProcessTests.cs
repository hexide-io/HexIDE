using System.Diagnostics;
using HexIDE.Dap;
using HexIDE.Conversations;
using System.Text.Json;

namespace HexIDE.Tests.DebugAdapter;

public class DapProcessTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
    private static DapProcessOptions Options(string mode) => new("dotnet")
    {
        Arguments = [typeof(Dap.TestAdapter.Program).Assembly.Location, mode],
        ShutdownTimeout = TimeSpan.FromSeconds(1),
    };

    private static async Task Ready(DapProcess adapter) =>
        (await adapter.Connection.Events.ReadAsync(TestToken).AsTask().WaitAsync(Patience, TestToken))
        .Event.Should().Be("ready");

    [Theory]
    [InlineData("setBreakpoints")]
    [InlineData("setExceptionBreakpoints")]
    [InlineData("configurationDone")]
    public async Task Launch_refusal_interrupts_unanswered_configuration(string command)
    {
        await using var log = new ConversationLog();
        await using var session = DapSession.Start(Options("launch-refuse-" + command) with
        {
            Capture = new(log, "dap:refused"),
        });
        Func<Task> start = () => session.LaunchAsync("fixture", JsonSerializer.SerializeToElement(new { }),
            [new("/workspace/Module1.bas", [4]), new("/workspace/Module2.bas", [5])], TestToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestToken);
        var failure = await start.Should().ThrowAsync<DapRequestException>().WithMessage("*fixture launch refusal*");
        session.Snapshot.State.Should().Be(DapSessionState.Faulted);
        session.Snapshot.Failure.Should().BeSameAs(failure.Which);
        await log.DrainAsync();
        var requests = log.Snapshot("dap:refused").Where(e => e.Kind == ConversationEntryKind.Request
            && e.Direction == ConversationDirection.Sent).Select(e => e.Method).ToArray();
        requests.Should().Equal(command switch
        {
            "setBreakpoints" => ["initialize", "launch", "setBreakpoints"],
            "setExceptionBreakpoints" => ["initialize", "launch", "setBreakpoints", "setBreakpoints", "setExceptionBreakpoints"],
            _ => ["initialize", "launch", "setBreakpoints", "setBreakpoints", "setExceptionBreakpoints", "configurationDone"],
        });
        (await session.ProcessCompletion!).ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task Disconnect_reads_what_the_adapter_writes_after_its_reply()
    {
        // debugpy answers disconnect and then sends one more event (#3894). Closing the adapter's output on the reply made
        // that write fail with a broken pipe, and debugpy exit 1. The fixture writes the same event after a pause, and
        // the capture shows whether it was still read.
        await using var log = new ConversationLog();
        await using var session = DapSession.Start(Options("disconnect-late-event") with { Capture = new(log, "dap:late") });
        await session.LaunchAsync("fixture", JsonSerializer.SerializeToElement(new { }), [], TestToken)
            .WaitAsync(Patience, TestToken);
        await session.DisconnectAsync(TestToken).WaitAsync(Patience, TestToken);
        var result = await session.ProcessCompletion!.WaitAsync(Patience, TestToken);
        result.ExitCode.Should().Be(0);
        result.ForcedTermination.Should().BeFalse("the adapter exits by itself once its input ends");
        session.Snapshot.State.Should().Be(DapSessionState.Ended);
        await log.DrainAsync();
        log.Snapshot("dap:late").Should().Contain(e => e.Kind == ConversationEntryKind.Notification
            && e.Direction == ConversationDirection.Received && e.Method == "debugpySockets",
            "the adapter's output stays open, and read, until the adapter closes it");
    }

    [Fact]
    public async Task Disconnect_keeps_stdin_open_for_the_adapters_trailing_event()
    {
        await using var log = new ConversationLog();
        await using var session = DapSession.Start(Options("disconnect-channel-event") with { Capture = new(log, "dap:channel") });
        await session.LaunchAsync("fixture", JsonSerializer.SerializeToElement(new { }), [], TestToken)
            .WaitAsync(Patience, TestToken);
        await session.DisconnectAsync(TestToken).WaitAsync(Patience, TestToken);
        var result = await session.ProcessCompletion!.WaitAsync(Patience, TestToken);
        result.ExitCode.Should().Be(0, "stdin must stay open until the trailing send (#4580)");
        result.ForcedTermination.Should().BeFalse();
        await log.DrainAsync();
        log.Snapshot("dap:channel").Should().Contain(e => e.Kind == ConversationEntryKind.Notification
            && e.Direction == ConversationDirection.Received && e.Method == "debugpySockets");
    }

    [Fact]
    public async Task Disconnect_closes_a_quiet_adapter_without_waiting_for_the_drain_bound()
    {
        await using var session = DapSession.Start(Options("disconnect-quiet") with { ShutdownTimeout = TimeSpan.FromSeconds(10) });
        await session.LaunchAsync("fixture", JsonSerializer.SerializeToElement(new { }), [], TestToken)
            .WaitAsync(Patience, TestToken);
        var elapsed = Stopwatch.StartNew();
        await session.DisconnectAsync(TestToken).WaitAsync(Patience, TestToken);
        elapsed.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(250));
        elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3), "a quiet adapter needs only the quiet window, not the 10 s drain bound");
        var result = await session.ProcessCompletion!.WaitAsync(Patience, TestToken);
        result.ExitCode.Should().Be(0);
        result.ForcedTermination.Should().BeFalse();
    }

    [Fact]
    public async Task Launch_preserves_argument_boundaries_working_directory_and_environment()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dap fixture " + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var marker = Guid.NewGuid() + ".marker";
        await File.WriteAllTextAsync(Path.Combine(directory, marker), "working directory", TestToken);
        try
        {
            var options = Options("echo") with
            {
                Arguments = [.. Options("echo").Arguments, "two words", "quote\"inside", "雪", "", "$literal;value"],
                WorkingDirectory = directory,
                Environment = new Dictionary<string, string?> { ["HEXIDE_DAP_FIXTURE_VALUE"] = "override 雪" },
            };
            await using var adapter = DapProcess.Start(options);
            await Ready(adapter);
            var response = await adapter.Connection.RequestAsync("initialize", cancellationToken: TestToken)
                .WaitAsync(Patience, TestToken);
            var body = response.Body!.Value;
            body.GetProperty("arguments").EnumerateArray().Select(a => a.GetString())
                .Should().Equal("two words", "quote\"inside", "雪", "", "$literal;value");
            // A temporary directory can have a canonical spelling different from its input path (macOS).
            // The unique file proves directory identity without assuming getcwd preserves symlink spelling.
            File.Exists(Path.Combine(body.GetProperty("cwd").GetString()!, marker)).Should().BeTrue();
            body.GetProperty("environment").GetString().Should().Be("override 雪");
            await adapter.DisposeAsync();
            var result = await adapter.Completion;
            result.ExitCode.Should().Be(0);
            result.ForcedTermination.Should().BeFalse();
            result.CleanupFailure.Should().BeNull();
        }
        finally { File.Delete(Path.Combine(directory, marker)); Directory.Delete(directory); }
    }

    [Fact]
    public async Task Stderr_without_newlines_is_drained_and_only_its_tail_is_retained()
    {
        await using var adapter = DapProcess.Start(Options("flood") with { StandardErrorTailCharacters = 1024 });
        await Ready(adapter); // Sent only after 256 KiB of stderr: fails if stderr isn't drained.
        await adapter.DisposeAsync();
        var result = await adapter.Completion;
        result.ExitCode.Should().Be(0);
        result.StandardErrorTail.Should().HaveLength(1024).And.EndWith("stderr-end");
        result.DroppedStandardErrorCharacters.Should().Be(256 * 1024 + "stderr-end".Length - 1024);
        result.CleanupFailure.Should().BeNull();
    }

    [Fact]
    public async Task Immediate_exit_retains_exit_code_and_stderr()
    {
        await using var adapter = DapProcess.Start(Options("exit"));
        var result = await adapter.Completion.WaitAsync(Patience, TestToken);
        result.ExitCode.Should().Be(37);
        result.StandardErrorTail.Should().Be("adapter startup failed");
        result.ForcedTermination.Should().BeFalse();
        result.ConnectionFailure.Should().BeOfType<EndOfStreamException>();
        result.CleanupFailure.Should().BeNull();
    }

    [Fact]
    public async Task Process_exit_does_not_discard_the_last_response()
    {
        await using var adapter = DapProcess.Start(Options("reply-exit"));
        await Ready(adapter);
        var response = await adapter.Connection.RequestAsync("threads", cancellationToken: TestToken)
            .WaitAsync(Patience, TestToken);
        response.Success.Should().BeTrue();
        (await adapter.Completion.WaitAsync(Patience, TestToken)).ExitCode.Should().Be(0);
    }

    [Theory]
    [InlineData("hang")]
    [InlineData("malformed")]
    public async Task Disposal_or_protocol_failure_reaps_an_unresponsive_process(string mode)
    {
        await using var adapter = DapProcess.Start(Options(mode));
        using var process = Process.GetProcessById(adapter.ProcessId);
        if (mode == "hang")
        {
            await Ready(adapter);
            await Task.WhenAll(adapter.DisposeAsync().AsTask(), adapter.DisposeAsync().AsTask())
                .WaitAsync(Patience, TestToken);
        }
        var result = await adapter.Completion.WaitAsync(Patience, TestToken);
        result.ForcedTermination.Should().BeTrue();
        result.ExitCode.Should().NotBeNull();
        result.CleanupFailure.Should().BeNull();
        process.HasExited.Should().BeTrue();
        if (mode == "malformed") result.ConnectionFailure.Should().BeOfType<InvalidDataException>();
        else result.ConnectionFailure.Should().BeNull();
    }

    [Fact]
    public async Task Disposal_settles_an_outstanding_request()
    {
        await using var adapter = DapProcess.Start(Options("silent"));
        await Ready(adapter);
        var pending = adapter.Connection.RequestAsync("launch", cancellationToken: TestToken);
        await adapter.DisposeAsync().AsTask().WaitAsync(Patience, TestToken);
        Func<Task> wait = () => pending;
        await wait.Should().ThrowAsync<IOException>();
        (await adapter.Completion).ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task Forced_cleanup_kills_the_live_adapters_child_process()
    {
        await using var adapter = DapProcess.Start(Options("tree"));
        var ready = await adapter.Connection.Events.ReadAsync(TestToken).AsTask().WaitAsync(Patience, TestToken);
        using var child = Process.GetProcessById(ready.Body!.Value.GetProperty("childId").GetInt32());
        try
        {
            await adapter.DisposeAsync().AsTask().WaitAsync(Patience, TestToken);
            await child.WaitForExitAsync(TestToken).WaitAsync(Patience, TestToken);
            child.HasExited.Should().BeTrue();
            (await adapter.Completion).ForcedTermination.Should().BeTrue();
        }
        finally { if (!child.HasExited) child.Kill(entireProcessTree: true); }
    }

    [Fact]
    public async Task Inherited_pipes_after_parent_exit_cannot_hold_completion_open()
    {
        await using var adapter = DapProcess.Start(Options("orphan"));
        var ready = await adapter.Connection.Events.ReadAsync(TestToken).AsTask().WaitAsync(Patience, TestToken);
        using var child = Process.GetProcessById(ready.Body!.Value.GetProperty("childId").GetInt32());
        try
        {
            var result = await adapter.Completion.WaitAsync(Patience, TestToken);
            result.ExitCode.Should().Be(0);
            result.CleanupFailure.Should().BeOfType<TimeoutException>();
            result.ForcedTermination.Should().BeFalse();
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(TestToken).WaitAsync(Patience, TestToken);
        }
    }

    [Fact]
    public void Invalid_options_and_missing_executables_fail_at_start()
    {
        var invalid = () => DapProcess.Start(Options("echo") with { StandardErrorTailCharacters = 0 });
        invalid.Should().Throw<ArgumentOutOfRangeException>();
        var missing = () => DapProcess.Start(new(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "missing")));
        missing.Should().Throw<System.ComponentModel.Win32Exception>();
    }
}
