using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace HexIDE.Tests.Infrastructure;

internal static class RunnerLoad
{
    internal static readonly TimeSpan Capacity = TimeSpan.FromSeconds(150);
    private static readonly Ring History = new();

    [ModuleInitializer]
    internal static void Start() => new Thread(Sample) { IsBackground = true, Name = "Runner load" }.Start();

    private static void Sample()
    {
        using var process = Process.GetCurrentProcess();
        var previous = Stopwatch.GetTimestamp();
        var cpu = process.TotalProcessorTime;
        var pause = GC.GetTotalPauseDuration();
        var contention = Monitor.LockContentionCount;
        var pending = new List<Dispatch>();
        while (true)
        {
            Thread.Sleep(250);
            var now = Stopwatch.GetTimestamp();
            var nextCpu = process.TotalProcessorTime;
            var nextPause = GC.GetTotalPauseDuration();
            var nextContention = Monitor.LockContentionCount;
            pending.RemoveAll(d => Volatile.Read(ref d.Completed));
            var dispatch = new Dispatch(now);
            pending.Add(dispatch);
            ThreadPool.UnsafeQueueUserWorkItem(static d =>
            {
                Volatile.Write(ref d.DelayMs, Stopwatch.GetElapsedTime(d.Queued).TotalMilliseconds);
                Volatile.Write(ref d.Completed, true);
            }, dispatch, preferLocal: false);
            History.Add(new SamplePoint(now, pending.ToArray(), ThreadPool.ThreadCount,
                ThreadPool.PendingWorkItemCount,
                (nextCpu - cpu).TotalSeconds / Stopwatch.GetElapsedTime(previous, now).TotalSeconds
                    / Environment.ProcessorCount * 100,
                (nextPause - pause).TotalMilliseconds, nextContention - contention));
            previous = now;
            cpu = nextCpu;
            pause = nextPause;
            contention = nextContention;
        }
    }

    internal static string Describe(TimeSpan window) => History.Describe(window, Stopwatch.GetTimestamp());

    internal sealed class Dispatch(long queued)
    {
        internal readonly long Queued = queued;
        internal double DelayMs;
        internal bool Completed;
        internal double DelayAt(long now) => Volatile.Read(ref Completed)
            ? Volatile.Read(ref DelayMs) : Stopwatch.GetElapsedTime(Queued, now).TotalMilliseconds;
    }

    internal sealed record SamplePoint(long At, Dispatch[] Dispatches, int Threads, long Queue,
        double CpuPercent, double PauseMs, long Contention);

    internal sealed class Ring
    {
        private readonly Queue<SamplePoint> samples = new();
        private readonly Lock gate = new();

        internal void Add(SamplePoint sample)
        {
            lock (gate)
            {
                samples.Enqueue(sample);
                while (samples.Count > 600) samples.Dequeue();
            }
        }

        internal string Describe(TimeSpan window, long now)
        {
            window = window < TimeSpan.Zero ? TimeSpan.Zero : window > Capacity ? Capacity : window;
            SamplePoint[] selected;
            lock (gate)
                selected = samples.Where(s => s.At <= now && Stopwatch.GetElapsedTime(s.At, now) <= window).ToArray();
            ThreadPool.GetMinThreads(out var minWorkers, out var minIo);
            ThreadPool.GetAvailableThreads(out var workers, out var io);
            var prefix = FormattableString.Invariant($"runner load over the last {window.TotalSeconds:0.###} s: ");
            var current = $"min threads={minWorkers}/{minIo}, available threads={workers}/{io}, processors={Environment.ProcessorCount}";
            if (selected.Length == 0) return prefix + "no samples; " + current;
            var delays = selected.SelectMany(s => s.Dispatches).Distinct().Select(d => d.DelayAt(now)).Order().ToArray();
            var cpus = selected.Select(s => s.CpuPercent).Order().ToArray();
            // The sampler is a thread of its own that wakes every 250 ms. A gap of seconds means nothing in the process
            // ran, as when a VM is paused, where a starved pool still leaves the sampler on time (#4433).
            var gap = selected.Skip(1).Zip(selected, (later, earlier) => Stopwatch.GetElapsedTime(earlier.At, later.At))
                .Append(Stopwatch.GetElapsedTime(selected[^1].At, now)).Max().TotalMilliseconds;
            return prefix + string.Create(CultureInfo.InvariantCulture,
                $"dispatch worst={delays.DefaultIfEmpty(0).Max():F1} ms, median={Median(delays):F1} ms; queue max={selected.Max(s => s.Queue)}; threads={selected[0].Threads}->{selected[^1].Threads}; CPU median={Median(cpus):F1}%, max={cpus[^1]:F1}%; GC pause={selected.Sum(s => s.PauseMs):F1} ms; lock contention={selected.Sum(s => s.Contention)}; sampler gap worst={gap:F0} ms; ") + current;
        }

        private static double Median(double[] values) => values.Length == 0 ? 0
            : (values[(values.Length - 1) / 2] + values[values.Length / 2]) / 2;
    }
}
