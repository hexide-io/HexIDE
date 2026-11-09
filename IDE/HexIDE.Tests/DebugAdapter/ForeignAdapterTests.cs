using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HexIDE.Conversations;
using HexIDE.Dap;

namespace HexIDE.Tests.DebugAdapter;

public sealed class ForeignAdapterFactAttribute : FactAttribute
{
    public ForeignAdapterFactAttribute(string adapter, [CallerFilePath] string? file = null, [CallerLineNumber] int line = -1)
        : base(file, line)
    {
        if (ForeignAdapterAcquisition.Resolve(adapter).Unavailable is { } reason) Skip = reason;
    }
}

public class ForeignAdapterTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("debugpy")]
    [InlineData("netcoredbg")]
    public void Pinned_adapter_is_available_when_required_and_checksums_never_fail_open(string adapter)
    {
        var resolved = ForeignAdapterAcquisition.Resolve(adapter);
        resolved.ChecksumMismatch.Should().BeFalse(resolved.Unavailable);
        if (Environment.GetEnvironmentVariable("HEXIDE_REQUIRE_FOREIGN_LSP") is "1" or "true" or "yes")
            resolved.Options.Should().NotBeNull(resolved.Unavailable);
    }

    [ForeignAdapterFact("debugpy")]
    public Task Debugpy_runs_a_complete_session() => Run("debugpy");

    [ForeignAdapterFact("netcoredbg")]
    public Task Netcoredbg_runs_a_complete_session() => Run("netcoredbg");

    private static async Task Run(string adapter)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        var token = deadline.Token;
        var directory = Path.Combine(ForeignAdapterAcquisition.RepositoryRoot(), "artifacts", "foreign-dap", "sessions", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await using var log = new ConversationLog();
        log.Arm(adapter, true);
        try
        {
            var python = adapter == "debugpy";
            var source = Path.Combine(directory, python ? "probe.py" : "Program.cs");
            await File.WriteAllTextAsync(source, python
                ? "answer = 41\nanswer += 1\nprint(answer)\n"
                : "using System;\nclass Program\n{\n    static void Main()\n    {\n        int answer = 41;\n        answer += 1;\n        Console.WriteLine(answer);\n    }\n}\n", token);
            var breakpointLine = python ? 2 : 7;
            JsonElement launch;
            if (python)
                launch = JsonSerializer.SerializeToElement(new { program = source, cwd = directory, console = "internalConsole", justMyCode = true });
            else
            {
                var project = Path.Combine(directory, "Probe.csproj");
                await File.WriteAllTextAsync(project,
                    "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><DebugType>portable</DebugType><Optimize>false</Optimize></PropertyGroup></Project>", token);
                await Build(project, directory, token);
                launch = JsonSerializer.SerializeToElement(new { program = Path.Combine(directory, "bin", "Debug", "net10.0", "Probe.dll"), cwd = directory });
            }
            var options = ForeignAdapterAcquisition.Resolve(adapter).Options! with { WorkingDirectory = directory, Capture = new(log, adapter) };
            await using var session = DapSession.Start(options);
            var breakpoints = await session.LaunchAsync(adapter, launch, [new(source, [breakpointLine])], token);
            breakpoints.Should().ContainSingle();
            breakpoints[0].Body!.Value.GetProperty("breakpoints").GetArrayLength().Should().Be(1);
            var stopped = await Event(session, "stopped", token);
            stopped.Event!.Body!.Value.GetProperty("reason").GetString().Should().Be("breakpoint");
            var stop = stopped.Snapshot.Stop!;
            var threads = (await session.ThreadsAsync(stop, token)).Body!.Value.GetProperty("threads");
            threads.EnumerateArray().Should().Contain(t => t.GetProperty("id").GetInt32() == stop.ThreadId);
            var stack = (await session.StackTraceAsync(stop, stop.ThreadId!.Value, token)).Body!.Value.GetProperty("stackFrames");
            stack.GetArrayLength().Should().BeGreaterThan(0);
            var frame = stack[0];
            frame.GetProperty("line").GetInt32().Should().Be(breakpointLine);
            Path.GetFullPath(frame.GetProperty("source").GetProperty("path").GetString()!).Should().Be(source);
            var scopes = (await session.ScopesAsync(stop, frame.GetProperty("id").GetInt32(), token)).Body!.Value.GetProperty("scopes");
            var locals = scopes.EnumerateArray().First(s => s.GetProperty("name").GetString()!.Contains("Locals", StringComparison.OrdinalIgnoreCase));
            var variables = (await session.VariablesAsync(stop, locals.GetProperty("variablesReference").GetInt32(), token)).Body!.Value.GetProperty("variables");
            variables.EnumerateArray().Should().Contain(v => v.GetProperty("name").GetString() == "answer" && v.GetProperty("value").GetString() == "41");
            await session.ResumeAsync(stop, "continue", stop.ThreadId.Value, token);
            await Event(session, "terminated", token);
            await session.DisconnectAsync(token);
            var result = await session.ProcessCompletion!.WaitAsync(token);
            result.ExitCode.Should().Be(0, result.StandardErrorTail);
            result.ForcedTermination.Should().BeFalse(result.StandardErrorTail);
            result.CleanupFailure.Should().BeNull();
            await log.DrainAsync();
            AssertConversation(log, adapter, source, breakpointLine);
        }
        catch
        {
            await log.DrainAsync();
            foreach (var entry in log.Snapshot(adapter))
            {
                var body = log.Body(adapter, entry.Sequence);
                TestContext.Current.TestOutputHelper?.WriteLine(body is null ? entry.Detail ?? entry.Method ?? "" : System.Text.Encoding.UTF8.GetString(body.Head));
                if (entry.AnswerSequence is { } answer && log.Body(adapter, answer) is { } reply)
                    TestContext.Current.TestOutputHelper?.WriteLine(System.Text.Encoding.UTF8.GetString(reply.Head));
            }
            throw;
        }
        finally { DeleteWhenReleased(directory); }
    }

    /// <summary>Removes a session's folder once the adapter and its program have let go of it.</summary>
    /// <remarks>
    /// They can still hold it when the session ends: every run on Windows on Arm, and now and then on x64 (#3454). The
    /// session has passed or failed by then, and a folder left in temp harms nothing, so giving up is not a failure.
    /// </remarks>
    private static void DeleteWhenReleased(string directory)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(250);
            }
        }
    }

    private static async Task<DapSessionUpdate> Event(DapSession session, string name, CancellationToken token)
    {
        await foreach (var update in session.Updates.ReadAllAsync(token))
        {
            update.Snapshot.State.Should().NotBe(DapSessionState.Faulted, update.Snapshot.Failure?.ToString());
            if (update.Event?.Event == name) return update;
        }
        throw new IOException($"Adapter closed before {name}.");
    }

    private static void AssertConversation(ConversationLog log, string adapter, string source, int line)
    {
        var entries = log.Snapshot(adapter);
        JsonElement Frame(long sequence)
        {
            using var document = JsonDocument.Parse(log.Body(adapter, sequence)!.Head);
            return document.RootElement.Clone();
        }
        var requests = entries.Where(e => e.Kind == ConversationEntryKind.Request && e.Direction == ConversationDirection.Sent).ToArray();
        requests.Select(e => e.Method).Should().ContainInOrder("initialize", "launch", "setBreakpoints", "configurationDone", "threads", "stackTrace", "scopes", "variables", "continue", "disconnect");
        foreach (var request in requests)
        {
            request.Outcome.Should().Be(ConversationOutcome.Answered, request.Method);
            var sent = Frame(request.Sequence);
            var reply = Frame(request.AnswerSequence!.Value);
            reply.GetProperty("type").GetString().Should().Be("response");
            reply.GetProperty("request_seq").GetInt32().Should().Be(sent.GetProperty("seq").GetInt32());
            reply.GetProperty("command").GetString().Should().Be(request.Method);
            reply.GetProperty("success").GetBoolean().Should().BeTrue();
        }
        JsonElement Arguments(string method) => Frame(requests.Single(e => e.Method == method).Sequence).GetProperty("arguments");
        var initialize = Arguments("initialize");
        initialize.GetProperty("adapterID").GetString().Should().Be(adapter);
        initialize.GetProperty("linesStartAt1").GetBoolean().Should().BeTrue();
        initialize.GetProperty("pathFormat").GetString().Should().Be("path");
        var bp = Arguments("setBreakpoints");
        bp.GetProperty("source").GetProperty("path").GetString().Should().Be(source);
        bp.GetProperty("breakpoints")[0].GetProperty("line").GetInt32().Should().Be(line);
        Arguments("disconnect").GetProperty("terminateDebuggee").GetBoolean().Should().BeTrue();
        var initialBreakpoint = Frame(requests.Single(e => e.Method == "setBreakpoints").AnswerSequence!.Value)
            .GetProperty("body").GetProperty("breakpoints")[0];
        var events = entries.Where(e => e.Kind == ConversationEntryKind.Notification && e.Direction == ConversationDirection.Received).ToArray();
        events.Select(e => e.Method).Should().ContainInOrder("initialized", "stopped", "terminated");
        // netcoredbg initially returns a pending breakpoint; module loading verifies it by event.
        var verifiedBreakpoint = initialBreakpoint.GetProperty("verified").GetBoolean() ? initialBreakpoint
            : events.Where(e => e.Method == "breakpoint").Select(e => Frame(e.Sequence).GetProperty("body").GetProperty("breakpoint"))
                .Last(b => b.GetProperty("id").GetInt32() == initialBreakpoint.GetProperty("id").GetInt32());
        verifiedBreakpoint.GetProperty("verified").GetBoolean().Should().BeTrue();
        verifiedBreakpoint.GetProperty("line").GetInt32().Should().Be(line);
        var output = string.Concat(events.Where(e => e.Method == "output").Select(e => Frame(e.Sequence).GetProperty("body").GetProperty("output").GetString()));
        output.ReplaceLineEndings("\n").Should().Contain("42\n");
        var exited = Frame(events.Single(e => e.Method == "exited").Sequence);
        exited.GetProperty("body").GetProperty("exitCode").GetInt32().Should().Be(0);
        log.QueueDropped.Should().Be(0);
    }

    private static async Task Build(string project, string directory, CancellationToken token)
    {
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = directory };
        foreach (var argument in new[] { "build", project, "-c", "Debug", "--nologo", "-v:q" }) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        process.ExitCode.Should().Be(0, (await stdout) + (await stderr));
    }
}
