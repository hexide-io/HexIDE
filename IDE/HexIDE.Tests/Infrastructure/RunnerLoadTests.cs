using System.Diagnostics;

namespace HexIDE.Tests.Infrastructure;

public class RunnerLoadTests
{
    [Fact]
    public void SummaryUsesOnlyTheWindowAndReportsKnownSamples()
    {
        var ring = new RunnerLoad.Ring();
        var now = Stopwatch.GetTimestamp();
        long Ago(int seconds) => now - seconds * Stopwatch.Frequency;
        RunnerLoad.Dispatch Done(double ms) => new(now) { Completed = true, DelayMs = ms };
        ring.Add(new(Ago(20), [Done(999)], 99, 999, 999, 999, 999));
        ring.Add(new(Ago(2), [Done(2)], 4, 3, 10, 2, 3));
        ring.Add(new(Ago(1), [Done(8)], 6, 7, 30, 4, 5));
        ring.Add(new(now + Stopwatch.Frequency, [Done(999)], 99, 999, 999, 999, 999));

        ring.Describe(TimeSpan.FromSeconds(5), now).Should().StartWith(
            "runner load over the last 5 s: dispatch worst=8.0 ms, median=5.0 ms; queue max=7; threads=4->6; CPU median=20.0%, max=30.0%; GC pause=6.0 ms; lock contention=8;")
            .And.Contain("min threads=").And.Contain("available threads=")
            .And.EndWith($"processors={Environment.ProcessorCount}");
    }

    [Fact]
    public void PendingDispatchCountsItsWaitAndWindowIsCapped()
    {
        var ring = new RunnerLoad.Ring();
        var now = Stopwatch.GetTimestamp();
        ring.Add(new(now, [new(now - Stopwatch.Frequency * 2)], 1, 1, 0, 0, 0));
        ring.Describe(TimeSpan.FromMinutes(10), now).Should()
            .StartWith("runner load over the last 150 s: dispatch worst=2000.0 ms, median=2000.0 ms;");
    }

    [Fact]
    public void RingRetainsOnlySixHundredSamplesAndExplainsAnEmptyWindow()
    {
        var ring = new RunnerLoad.Ring();
        var now = Stopwatch.GetTimestamp();
        ring.Add(new(now, [], 999, 999, 999, 999, 999));
        for (var i = 0; i < 600; i++) ring.Add(new(now, [], 2, 0, 0, 0, 0));
        ring.Describe(TimeSpan.FromSeconds(1), now).Should().Contain("threads=2->2").And.NotContain("999");
        ring.Describe(TimeSpan.Zero, now + Stopwatch.Frequency).Should()
            .StartWith("runner load over the last 0 s: no samples;");
    }
}
