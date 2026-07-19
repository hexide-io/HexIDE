using System;
using HexIDE.IDE;

namespace HexIDE.Tests.IDE;

public class ChangeCoalescerTests
{
    private sealed class FakeClock : IClock
    {
        public DateTime UtcNow { get; private set; } = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public void Advance(int ms) => UtcNow = UtcNow.AddMilliseconds(ms);
    }

    private static ChangeCoalescer Sut(FakeClock clock) =>
        new(clock, quietPeriod: TimeSpan.FromMilliseconds(400), maxWait: TimeSpan.FromMilliseconds(3000));

    [Fact]
    public void TryDrain_False_WhenNothingPending()
    {
        var c = Sut(new FakeClock());
        c.TryDrain(out var batch).Should().BeFalse();
        batch.Should().BeEmpty();
    }

    [Fact]
    public void TryDrain_False_BeforeQuietPeriod_True_After()
    {
        var clock = new FakeClock();
        var c = Sut(clock);
        c.Notify("a.bas");

        clock.Advance(300);
        c.TryDrain(out _).Should().BeFalse();

        clock.Advance(100); // now 400ms since the notify
        c.TryDrain(out var batch).Should().BeTrue();
        batch.Should().Contain("a.bas");

        // Draining clears the pending set.
        c.TryDrain(out _).Should().BeFalse();
    }

    [Fact]
    public void EachNotify_ResetsTheQuietWindow_AndCoalescesIntoOneBatch()
    {
        var clock = new FakeClock();
        var c = Sut(clock);

        c.Notify("a.bas");
        clock.Advance(300);
        c.Notify("b.cls");   // resets the quiet window

        clock.Advance(300);  // 300ms since the last notify — not quiet yet
        c.TryDrain(out _).Should().BeFalse();

        clock.Advance(100);  // 400ms since the last notify
        c.TryDrain(out var batch).Should().BeTrue();
        batch.Should().BeEquivalentTo(new[] { "a.bas", "b.cls" });
    }

    [Fact]
    public void RepeatedPath_IsDeduped()
    {
        var clock = new FakeClock();
        var c = Sut(clock);
        c.Notify("a.bas");
        c.Notify("a.bas");
        c.Notify("A.BAS"); // same path, different case

        clock.Advance(400);
        c.TryDrain(out var batch).Should().BeTrue();
        batch.Should().HaveCount(1);
    }

    [Fact]
    public void MaxWait_ForcesDrain_DuringContinuousChurn()
    {
        var clock = new FakeClock();
        var c = Sut(clock);
        c.Notify("a.bas"); // burst starts at t0

        // Churn just under the max-wait cap; the quiet window never elapses because we keep notifying.
        clock.Advance(2900);
        c.Notify("a.bas");
        c.TryDrain(out _).Should().BeFalse();

        // Cross the max-wait cap: drains even though the last notify was 0ms ago.
        clock.Advance(200);
        c.Notify("a.bas");
        c.TryDrain(out var batch).Should().BeTrue();
        batch.Should().Contain("a.bas");
    }
}
