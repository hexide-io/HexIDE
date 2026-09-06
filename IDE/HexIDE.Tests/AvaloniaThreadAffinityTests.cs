using Avalonia.Threading;

namespace HexIDE.Tests;

/// <summary>
/// Guards the invariant that closed the suite's longest-standing flake.
///
/// <para>
/// Avalonia binds its dispatcher to the thread that sets it up, and every later access must come from
/// that thread. This project used to call <c>SetupWithoutStarting</c> from whichever test class asked
/// first, and xunit gives each test class its own thread — so when that first call landed on a drifted
/// async continuation, Avalonia bound somewhere the rest of the suite was not, and all 282
/// Avalonia-dependent tests failed together with <c>VerifyAccess</c>. At random, roughly one CI run in
/// twelve (hexide-io/HexIDE#286, #292).
/// </para>
///
/// <para>
/// <c>[AvaloniaFact]</c> removes the invariant instead of trying to satisfy it: Avalonia caches one
/// session per assembly and runs every such test on the single thread that session owns. These tests
/// assert that directly, because "the flake stopped happening" is not evidence about a race that fired
/// one run in twelve — the previous two attempted fixes both looked fine by that standard and were
/// measurably wrong.
/// </para>
/// </summary>
public class AvaloniaThreadAffinityTests
{
    /// <summary>
    /// Every thread an <c>[AvaloniaFact]</c> in this file has been seen on.
    ///
    /// <para>
    /// A set rather than a "first one wins" field, deliberately: each test adds its own thread and then
    /// asserts the set still holds exactly one. That holds whichever class or test runs first, so the
    /// assertion cannot pass or fail for reasons of ordering — which matters, because ordering is
    /// precisely what the old bug turned on.
    /// </para>
    /// </summary>
    internal static readonly HashSet<int> ObservedThreads = [];

    internal static void RecordAndAssertSingleThread()
    {
        lock (ObservedThreads) ObservedThreads.Add(Environment.CurrentManagedThreadId);

        int[] seen;
        lock (ObservedThreads) seen = [.. ObservedThreads];

        seen.Should().ContainSingle(
            "every [AvaloniaFact] in this assembly must run on the one thread Avalonia's session owns; "
          + "more than one here is the 282-failure cascade about to happen, and naming the threads is the "
          + $"whole point: saw [{string.Join(", ", seen)}]");
    }

    [AvaloniaFact]
    public void AnAvaloniaTestCanTouchTheDispatcher()
    {
        // What the 282 failing tests could not do.
        RecordAndAssertSingleThread();

        var touch = () => Dispatcher.UIThread.VerifyAccess();

        touch.Should().NotThrow("an [AvaloniaFact] runs on the thread Avalonia is bound to, by construction");
    }

    [AvaloniaFact]
    public void AnotherTestInTheSameClassIsOnTheSameThread()
    {
        RecordAndAssertSingleThread();
    }

    [AvaloniaFact]
    public async Task ThreadIdentitySurvivesAnAwait()
    {
        // The specific drift that used to move the binding. Under [AvaloniaFact] the continuation comes
        // back to the session's thread rather than a pool thread, so an async test is no longer a hazard
        // to every test that follows it.
        RecordAndAssertSingleThread();
        var before = Environment.CurrentManagedThreadId;

        await Task.Yield();

        Environment.CurrentManagedThreadId.Should().Be(before,
            "a continuation that resumed elsewhere is exactly what used to rebind Avalonia");
        Dispatcher.UIThread.CheckAccess().Should().BeTrue("and it must still be the UI thread afterwards");
    }
}

/// <summary>
/// A second class, deliberately. Per-class threads were the fault — measured at 10 and 13 with a
/// continuation on 18 — so an invariant asserted only within one class would prove nothing about the
/// thing that broke.
/// </summary>
public class AvaloniaThreadAffinityAcrossClassesTests
{
    [AvaloniaFact]
    public void ADifferentClassRunsOnTheSameUiThread()
    {
        AvaloniaThreadAffinityTests.RecordAndAssertSingleThread();
    }

    [AvaloniaFact]
    public void AndCanTouchTheDispatcherFromIt()
    {
        AvaloniaThreadAffinityTests.RecordAndAssertSingleThread();

        var touch = () => Dispatcher.UIThread.VerifyAccess();

        touch.Should().NotThrow();
    }
}
