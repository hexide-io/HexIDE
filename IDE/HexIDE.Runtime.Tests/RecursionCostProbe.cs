using System.Diagnostics;
using System.Globalization;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Diagnostic measurements for https://github.com/hexide-io/HexIDE/issues/4775, not a timing assertion.
[Collection<ErrorUnwindCollection>]
public class RecursionCostProbe : BaseVBTestFixture
{
    public static bool Enabled => Environment.GetEnvironmentVariable("HEXIDE_RECURSION_PROBE") == "1";
    public static bool StackGcEnabled => Environment.GetEnvironmentVariable("HEXIDE_RECURSION_STACK_GC_PROBE") == "1";

    [Fact(Skip = "Opt-in diagnostic: set HEXIDE_RECURSION_STACK_GC_PROBE=1", SkipUnless = nameof(StackGcEnabled))]
    public void MeasureCollectionsWithTheSameActivationsRetainedAfterReturn()
    {
        var output = Environment.GetEnvironmentVariable("HEXIDE_RECURSION_OUTPUT")
            ?? throw new InvalidOperationException("Set HEXIDE_RECURSION_OUTPUT to the CSV output path.");
        foreach (var depth in new[] { 1000, 2000, 3000, 4000, 5000 })
            UiThread.RunOnNewThread(() =>
            {
                BasicInterpreter? vb = null;
                StatementExecutor[]? held = null;
                double[]? deep = null;
                var library = new CollectingLibrary(() =>
                {
                    held = vb!.ActivationStack.ToArray();
                    held.Should().HaveCount(depth);
                    // Promote the live heap before measuring empty-gen0 collections. Keep all these activation
                    // objects alive after the walk returns, so the shallow comparison retains them too.
                    GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                    deep = CollectThreeTimes();
                });
                vb = new BasicInterpreter(library, new ModuleExecutionContext(), new ExecutionEnvironment(),
                    "Up 1\nSub Up(ByVal n As Long)\n" +
                    $"If n < {depth} Then Up n + 1 Else Debug.Print n\nEnd Sub\n");
                var run = vb.Execute();
                run.IsCompleted.Should().BeTrue();
                run.GetAwaiter().GetResult();
                var shallow = CollectThreeTimes();
                GC.KeepAlive(held);
                deep.Should().NotBeNull("the walk must reach its requested depth");
                File.AppendAllText(output, string.Create(CultureInfo.InvariantCulture,
                    $"{depth},{deep![0]:F3},{deep[1]:F3},{deep[2]:F3},{shallow[0]:F3},{shallow[1]:F3},{shallow[2]:F3}{Environment.NewLine}"));
                return 0;
            }, UiThread.StackBytes);
    }

    private static double[] CollectThreeTimes()
    {
        var times = new double[3];
        for (var i = 0; i < times.Length; i++)
        {
            var before = Stopwatch.GetTimestamp();
            GC.Collect(0, GCCollectionMode.Forced, blocking: true);
            times[i] = Stopwatch.GetElapsedTime(before).TotalMilliseconds;
        }
        return times;
    }

    private sealed class CollectingLibrary(Action collect) : MockStdLib([]), IBasicStandardLibrary
    {
        void IBasicStandardLibrary.DebugPrint(Vb6Value value) => collect();
    }

    [Fact(Skip = "Opt-in diagnostic: set HEXIDE_RECURSION_PROBE=1", SkipUnless = nameof(Enabled))]
    public void Measure()
    {
        var output = Environment.GetEnvironmentVariable("HEXIDE_RECURSION_OUTPUT")
            ?? throw new InvalidOperationException("Set HEXIDE_RECURSION_OUTPUT to the CSV output path.");
        var runaway = Environment.GetEnvironmentVariable("HEXIDE_RECURSION_RUNAWAY") == "1";
        var noGcRequested = Environment.GetEnvironmentVariable("HEXIDE_RECURSION_NO_GC") == "1";
        // Warm the parser and tiered JIT before the table; keep the same warm-up in every fresh process. The warm-up
        // runs are recorded too, as "cold": the first run in a process is the one a program meets (#4775 review).
        for (var warm = 0; warm < 2; warm++)
        {
            var (milliseconds, collections, bytes, noGc) = MeasureOne(5000, runaway, noGcRequested);
            File.AppendAllText(output, string.Create(CultureInfo.InvariantCulture,
                $"{(runaway ? "runaway" : "return")}-cold,5000,{warm + 1},{milliseconds:F1},{string.Join(',', collections)},{bytes},{noGc}{Environment.NewLine}"));
        }
        foreach (var depth in runaway ? new[] { BasicInterpreter.MaxCallDepth } : new[] { 1000, 2000, 3000, 4000, 5000 })
        for (var repeat = 0; repeat < 3; repeat++)
        {
            var (milliseconds, collections, bytes, noGc) = MeasureOne(depth, runaway, noGcRequested);
            File.AppendAllText(output, string.Create(CultureInfo.InvariantCulture,
                $"{(runaway ? "runaway" : "return")},{depth},{repeat + 1},{milliseconds:F1},{string.Join(',', collections)},{bytes},{noGc}{Environment.NewLine}"));
        }
    }

    private (double Milliseconds, int[] Collections, long Bytes, bool NoGc) MeasureOne(int depth, bool runaway, bool noGcRequested)
    {
        (double Milliseconds, int[] Collections, long Bytes, bool NoGc) result = default;
        UiThread.RunOnNewThread(() =>
        {
            var log = new List<Vb6Value>();
            var body = runaway
                ? $"If n = {BasicInterpreter.MaxCallDepth} Then Debug.Print n\nUp n + 1\n"
                : $"If n < {depth} Then\nUp n + 1\nElse\nDebug.Print n\nEnd If\n";
            var code = "On Error Resume Next\nUp 1\nDebug.Print Err.Number\n" +
                "Sub Up(ByVal n As Long)\n" + body + "End Sub\n";
            var vb = new BasicInterpreter(new MockStdLib(log), new ModuleExecutionContext(), new ExecutionEnvironment(), code);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var noGc = noGcRequested && GC.TryStartNoGCRegion(256 * 1024 * 1024);
            if (noGcRequested)
                noGc.Should().BeTrue("the diagnostic must report if the no-GC experiment could not start");
            try
            {
                var counts = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                var watch = Stopwatch.StartNew();
                var run = vb.Execute();
                run.IsCompleted.Should().BeTrue("the whole run must stay on the sized thread");
                run.GetAwaiter().GetResult();
                watch.Stop();
                result = (watch.Elapsed.TotalMilliseconds,
                    Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - counts[i]).ToArray(),
                    GC.GetAllocatedBytesForCurrentThread() - allocated, noGc);
            }
            finally
            {
                if (noGc) GC.EndNoGCRegion();
            }
            log.Should().Equal([new Vb6Value((long)depth), new Vb6Value(runaway ? 28L : 0L)]);
            return 0;
        }, UiThread.StackBytes);
        return result;
    }
}
