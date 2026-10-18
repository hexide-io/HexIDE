using System.Text.Json;
using HexIDE.Dap;

// Explicit opt-in acceptance tool. Uses an installed external adapter; never downloads or bundles it.
if (args.Length != 5 || !int.TryParse(args[4], out var line) || line < 1)
{
    Console.Error.WriteLine("Usage: HexIDE.Dap.Smoke <adapter-executable> <workspace> <module> <source-path> <breakpoint-line>");
    return 2;
}

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
var token = timeout.Token;
await using var session = DapSession.Start(new(Path.GetFullPath(args[0]))
{
    Arguments = ["--dap", "--workspace", Path.GetFullPath(args[1])],
    WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(args[0])),
    ShutdownTimeout = TimeSpan.FromSeconds(10),
});
var stopped = new TaskCompletionSource<DapStop>(TaskCreationOptions.RunContinuationsAsynchronously);
var terminated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var output = new System.Text.StringBuilder();
var observer = Task.Run(async () =>
{
    await foreach (var update in session.Updates.ReadAllAsync(token))
    {
        Console.WriteLine($"state: {update.Snapshot.State}; event: {update.Event?.Event}");
        if (update.Snapshot.Stop is { } stop) stopped.TrySetResult(stop);
        if (update.Event?.Event == "terminated") terminated.TrySetResult();
        if (update.Event?.Event == "output" && update.Event.Body is { } body && body.TryGetProperty("output", out var text))
            output.Append(text.GetString());
    }
});

var result = 1;
try
{
    var replies = await session.LaunchAsync("external", JsonSerializer.SerializeToElement(new { module = args[2], entryPoint = "Main" }),
        [new(Path.GetFullPath(args[3]), [line])], token);
    var verified = replies[0].Body!.Value.GetProperty("breakpoints")[0];
    Console.WriteLine($"breakpoint: {verified}");
    if (!verified.GetProperty("verified").GetBoolean()) throw new InvalidOperationException("Breakpoint was not verified.");
    var stop = await stopped.Task.WaitAsync(token);
    if (stop.Reason != "breakpoint") throw new InvalidOperationException($"Unexpected stop: {stop.Reason}");
    var threads = await session.ThreadsAsync(stop, token);
    var thread = threads.Body!.Value.GetProperty("threads")[0].GetProperty("id").GetInt32();
    var stack = await session.StackTraceAsync(stop, thread, token);
    Console.WriteLine($"stack: {stack.Body}");
    var frame = stack.Body!.Value.GetProperty("stackFrames")[0];
    if (frame.GetProperty("line").GetInt32() != line) throw new InvalidOperationException("Stopped at an unexpected line.");
    var scopes = await session.ScopesAsync(stop, frame.GetProperty("id").GetInt32(), token);
    var variableCount = 0;
    foreach (var scope in scopes.Body!.Value.GetProperty("scopes").EnumerateArray())
    {
        var variables = await session.VariablesAsync(stop, scope.GetProperty("variablesReference").GetInt32(), token);
        Console.WriteLine($"variables ({scope.GetProperty("name")}): {variables.Body}");
        variableCount += variables.Body!.Value.GetProperty("variables").GetArrayLength();
    }
    if (variableCount == 0) throw new InvalidOperationException("Acceptance program must expose at least one variable at the breakpoint.");
    await session.ResumeAsync(stop, "continue", thread, token);
    await terminated.Task.WaitAsync(token);
    await session.DisconnectAsync(token);
    await session.Completion.WaitAsync(token);
    await observer;
    Console.WriteLine($"output: {output}");
    var process = await session.ProcessCompletion!.WaitAsync(token);
    Console.WriteLine($"exit: {process.ExitCode}; forced: {process.ForcedTermination}; cleanup: {process.CleanupFailure}");
    if (process.ExitCode != 0 || process.ForcedTermination || process.CleanupFailure is not null)
        throw new InvalidOperationException("Adapter did not exit cleanly.");
    result = 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
}
finally
{
    await session.DisposeAsync();
    try { await observer; } catch (OperationCanceledException) { }
    var process = await session.ProcessCompletion!;
    if (result != 0) Console.Error.WriteLine(process.StandardErrorTail);
}
return result;
