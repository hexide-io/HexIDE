using System.Diagnostics;
using System.Diagnostics.Tracing;
using HexIDE.IDE;
using HexIDE.Runtime.Interpreter;

if (Environment.GetEnvironmentVariable("HEXIDE_RECURSION_COST") != "1")
{
    Console.WriteLine("Skipped: set HEXIDE_RECURSION_COST=1 to run #4919's diagnostic.");
    return;
}
var runaway = args.Contains("runaway");
var raised = args.Contains("throw");
using var samples = args.Contains("profile") ? new Allocations() : null;
Exception? failure = null;
var thread = new Thread(() =>
{
    try
    {
        var host = new Host();
        var vb = new BasicInterpreter(host, new ModuleExecutionContext(), new ExecutionEnvironment(),
            "On Error Resume Next\nUp 1\nDebug.Print Err.Number\n" +
            "Sub Up(ByVal n As Long)\n" + (runaway ? "" : raised ? "If n >= 5000 Then Err.Raise 5\n" : "If n >= 5000 Then Exit Sub\n") +
            "Up n + 1\nEnd Sub\n");
        for (var i = 0; i < 4; i++)
        {
            ProbeEvents.Log.Iteration(i);
            if (samples is not null) samples.Sampling = i > 0;
            host.Error = -1;
            var before = GC.GetAllocatedBytesForCurrentThread();
            var gen0 = GC.CollectionCount(0);
            var watch = Stopwatch.StartNew();
            var run = vb.Execute();
            if (!run.IsCompleted) throw new InvalidOperationException("The diagnostic left its thread.");
            run.GetAwaiter().GetResult();
            watch.Stop();
            ProbeEvents.Log.Finished(i);
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Console.WriteLine($"{(runaway ? "runaway" : raised ? "throw" : "return")},{(i == 0 ? "cold" : "warm")},{bytes},{bytes / 5000.0:F1},{GC.CollectionCount(0) - gen0},{watch.Elapsed.TotalMilliseconds:F1},error={host.Error}");
            if (host.Error != (runaway ? 28 : raised ? 5 : 0)) throw new InvalidOperationException("Unexpected program result.");
        }
    }
    catch (Exception error) { failure = error; }
}, 64 * 1024 * 1024);
if (OperatingSystem.IsWindows())
    thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure);

sealed class Host : IBasicStandardLibrary
{
    internal long Error;
    public void DebugPrint(Vb6Value value) => Error = Convert.ToInt64(value.Value);
    public Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon) => throw new NotSupportedException();
    public Task<string?> InputBox(string prompt, string? title, string defaultText) => throw new NotSupportedException();
}

sealed class Allocations : EventListener
{
    internal volatile bool Sampling;
    private readonly Dictionary<string, int> counts = new();
    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "Microsoft-Windows-DotNETRuntime")
            EnableEvents(source, EventLevel.Verbose, (EventKeywords)0x1);
    }
    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        if (!Sampling) return;
        if (e.EventName?.StartsWith("GCAllocationTick", StringComparison.Ordinal) != true) return;
        var index = e.PayloadNames?.IndexOf("TypeName") ?? -1;
        var name = index >= 0 ? e.Payload![index]?.ToString() ?? "unknown" : "unknown";
        lock (counts) counts[name] = counts.GetValueOrDefault(name) + 1;
    }

    public override void Dispose()
    {
        base.Dispose();
        lock (counts)
            foreach (var pair in counts.OrderByDescending(p => p.Value))
                Console.WriteLine($"sample,{pair.Value},{pair.Key}");
    }
}

[EventSource(Name = "HexIDE-RecursionCost")]
sealed class ProbeEvents : EventSource
{
    internal static readonly ProbeEvents Log = new();
    [Event(1)] public void Iteration(int iteration) => WriteEvent(1, iteration);
    [Event(2)] public void Finished(int iteration) => WriteEvent(2, iteration);
}
