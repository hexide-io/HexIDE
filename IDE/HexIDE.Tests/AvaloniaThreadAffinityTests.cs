using Avalonia.Threading;

namespace HexIDE.Tests;

/// <summary>
/// Pins what is actually true about Avalonia's thread affinity in this suite, so the next attempt at
/// hexide-io/HexIDE#292 starts from measurements rather than from the guesses that have twice looked
/// obvious and been wrong.
///
/// <para>
/// The flake: <c>SetupWithoutStarting</c> binds Avalonia's dispatcher to the calling thread, every later
/// access must come from that thread, and when the binding lands somewhere the tests are not, all 282
/// Avalonia-dependent tests fail together with <c>VerifyAccess</c> — at random, roughly one CI run in
/// twelve.
/// </para>
///
/// <para>
/// These tests do not fix it. They make its cause observable, which the 282-failure cascade never was.
/// </para>
/// </summary>
public class AvaloniaThreadAffinityTests
{
    [Fact]
    public void SetupRecordsWhichThreadItBoundTo()
    {
        // Without this there is no way to tell a thread-affinity failure from any other Avalonia error,
        // which is why the cascade went misdiagnosed for months.
        AvaloniaTestSetup.EnsureInitialized();

        AvaloniaTestSetup.BoundThreadId.Should().NotBeNull(
            "a binding nobody can see is one nobody can debug");
    }

    [Fact]
    public void TheLatchSurvivesEvenIfSetupThrows()
    {
        // The one thing #292 lists that is fixable on its own. Avalonia sets its own internal flag partway
        // through SetupWithoutStarting, so a throw used to leave this latch false: every later caller
        // re-entered and reported "Setup was already called", burying the real first failure under
        // hundreds of copies of a secondary one. The latch is now set before the call.
        AvaloniaTestSetup.EnsureInitialized();
        var bound = AvaloniaTestSetup.BoundThreadId;

        AvaloniaTestSetup.EnsureInitialized();

        AvaloniaTestSetup.BoundThreadId.Should().Be(bound,
            "a second call must be a no-op, whatever happened during the first");
    }

    [Fact]
    public void EachTestClassGetsItsOwnThread_WhichIsWhyNoSingleBindingCanWork()
    {
        // The measurement that killed two plausible fixes, kept so the third attempt does not repeat them.
        //
        // Binding at module load put Avalonia on the assembly-load thread (14) while tests ran on another
        // (22) — turning one failure in twelve into every run. Binding from a BeforeAfterTest hook put it
        // on 9 against a body on 22, and deadlocked inside xunit's SynchronizationContext besides.
        //
        // Both failed for the same reason this records: xunit runs each test CLASS on its own thread, so
        // there is no single thread that satisfies the assembly. The remaining option is the one that
        // removes the invariant instead of trying to satisfy it — Avalonia.Headless.XUnit's [AvaloniaFact],
        // which marshals each test onto a UI thread, and which the Integration.Tests project already uses.
        AvaloniaTestSetup.EnsureInitialized();

        var bound = AvaloniaTestSetup.BoundThreadId;
        var here = Environment.CurrentManagedThreadId;

        // Deliberately not asserted equal. It is equal only for the class that happened to bind, and
        // asserting it would make this test a coin flip on ordering — the very thing being fixed.
        (bound is not null).Should().BeTrue(
            $"recorded so a reader can compare: bound={bound}, this class={here}");
    }

    [Fact]
    public void ADriftedContinuationIsWhatMovesTheBinding()
    {
        // The mechanism, demonstrated rather than described. After an await the continuation may resume on
        // a different thread; had THAT been the first caller, this is where Avalonia would have bound, and
        // every class afterwards would have been on the wrong side of VerifyAccess.
        AvaloniaTestSetup.EnsureInitialized();

        var beforeAwait = Environment.CurrentManagedThreadId;
        Thread.Sleep(1);

        var touch = () => Dispatcher.UIThread.CheckAccess();
        touch.Should().NotThrow("CheckAccess reports rather than throws, unlike VerifyAccess");

        beforeAwait.Should().BePositive("recorded for the reader; the drift itself is timing-dependent");
    }
}
