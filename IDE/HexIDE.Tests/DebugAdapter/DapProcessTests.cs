using System.Diagnostics;
using HexIDE.Dap;

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
