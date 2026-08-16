using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Interpreter-advanced Phase 5 — custom events (<c>Event</c> / <c>RaiseEvent</c> / <c>WithEvents</c>). A source
/// class raises events; a listener class with a <c>WithEvents</c> field handles them via <c>{var}_{event}</c>,
/// synchronously, with ByRef write-back. VB6 events are multicast (a per-source observer list, attach order).
/// Every expectation is pinned against <c>vb6.exe</c> (docs/vb6-fidelity-oracle.md "Custom events").
/// </summary>
public class ClassEventTests : BaseVBTestFixture
{
    // Source: raises Tick(ByRef Cancel) around "pre"/the cancel value, and Plain (no handler in most tests).
    private const string Clock =
        "Public Id As String\n" +
        "Public Event Tick(ByRef Cancel As Boolean)\n" +
        "Public Event Plain()\n" +
        "Public Sub DoTick()\nDim c As Boolean\nDebug.Print \"pre\"\nRaiseEvent Tick(c)\nDebug.Print c\nEnd Sub\n" +
        "Public Sub DoPlain()\nRaiseEvent Plain\nDebug.Print \"didPlain\"\nEnd Sub\n";

    // Listener: a WithEvents field, an Attach/Detach, and a src_Tick handler that logs and cancels.
    private const string Listener =
        "Public Id As String\n" +
        "Private WithEvents src As Clock\n" +
        "Public Sub Attach(c As Clock)\nSet src = c\nEnd Sub\n" +
        "Public Sub Detach()\nSet src = Nothing\nEnd Sub\n" +
        "Private Sub src_Tick(ByRef Cancel As Boolean)\nDebug.Print \"L\" & Id\nCancel = True\nEnd Sub\n";

    [Fact]
    public async Task Handler_Runs_Synchronously_And_ByRefCancel_SeenByRaiser()
    {
        await RunClasses(
            "Dim ck As Clock\nSet ck = New Clock\n" +
            "Dim L As Listener\nSet L = New Listener\nL.Id = \"1\"\nL.Attach ck\n" +
            "ck.DoTick\n",
            ("Clock", Clock), ("Listener", Listener));
        // Handler fires BETWEEN "pre" and the cancel readout (synchronous), and its ByRef Cancel=True is seen.
        AssertDebugLog(["pre", "L1", true]);
    }

    [Fact]
    public async Task NoSink_RaiseEvent_IsSilentNoOp()
    {
        await RunClasses(
            "Dim ck As Clock\nSet ck = New Clock\nck.DoTick\n",
            ("Clock", Clock), ("Listener", Listener));
        // No listener attached → no handler runs; Cancel stays False.
        AssertDebugLog(["pre", false]);
    }

    [Fact]
    public async Task NoHandler_ForEvent_IsSilentNoOp()
    {
        await RunClasses(
            "Dim ck As Clock\nSet ck = New Clock\n" +
            "Dim L As Listener\nSet L = New Listener\nL.Id = \"1\"\nL.Attach ck\n" +
            "ck.DoPlain\n",
            ("Clock", Clock), ("Listener", Listener));
        // Plain has no src_Plain handler → RaiseEvent Plain is a no-op; DoPlain still finishes.
        AssertDebugLog(["didPlain"]);
    }

    [Fact]
    public async Task Multicast_TwoListeners_BothFire_InAttachOrder_SharingByRef()
    {
        await RunClasses(
            "Dim ck As Clock\nSet ck = New Clock\n" +
            "Dim a As Listener\nSet a = New Listener\na.Id = \"A\"\na.Attach ck\n" +
            "Dim b As Listener\nSet b = New Listener\nb.Id = \"B\"\nb.Attach ck\n" +
            "ck.DoTick\n",
            ("Clock", Clock), ("Listener", Listener));
        // Both listeners fire, in ATTACH order (A then B); the ByRef Cancel is shared (True after A) → raiser True.
        AssertDebugLog(["pre", "LA", "LB", true]);
    }

    [Fact]
    public async Task Detach_UnbindsOneSink_OthersStillFire()
    {
        await RunClasses(
            "Dim ck As Clock\nSet ck = New Clock\n" +
            "Dim a As Listener\nSet a = New Listener\na.Id = \"A\"\na.Attach ck\n" +
            "Dim b As Listener\nSet b = New Listener\nb.Id = \"B\"\nb.Attach ck\n" +
            "a.Detach\n" +
            "ck.DoTick\n",
            ("Clock", Clock), ("Listener", Listener));
        // A detached (Set src = Nothing) → only B fires.
        AssertDebugLog(["pre", "LB", true]);
    }

    [Fact]
    public async Task Rebind_MovesSink_FromOldSourceToNew()
    {
        await RunClasses(
            "Dim ck As Clock\nSet ck = New Clock\nck.Id = \"1\"\n" +
            "Dim ck2 As Clock\nSet ck2 = New Clock\nck2.Id = \"2\"\n" +
            "Dim L As Listener\nSet L = New Listener\nL.Id = \"X\"\nL.Attach ck\nL.Attach ck2\n" +
            "ck.DoTick\n" +   // old source: L no longer bound → no dispatch
            "ck2.DoTick\n",   // new source: L fires
            ("Clock", Clock), ("Listener", Listener));
        AssertDebugLog(["pre", false, "pre", "LX", true]);
    }

    [Fact]
    public async Task AdvisedListener_TerminatesWhenItsOwnRefsDrop_ThenNoDispatch()
    {
        // A source does NOT hold a strong back-ref to its listeners: dropping the listener's only ref terminates
        // it (no leaked cycle), and a later raise on the still-alive source does not reach the dead handler.
        await RunClasses(
            "Dim ck As Clock\nSet ck = New Clock\n" +
            "MakeAndDrop ck\n" +          // creates a listener, attaches, returns → listener local drops → terminates
            "ck.DoTick\n" +
            "Sub MakeAndDrop(c As Clock)\n" +
            "Dim L As Lsnr\nSet L = New Lsnr\nL.Attach c\n" +
            "End Sub\n",
            ("Clock", Clock),
            ("Lsnr",
                "Private WithEvents src As Clock\n" +
                "Public Sub Attach(c As Clock)\nSet src = c\nEnd Sub\n" +
                "Private Sub src_Tick(ByRef Cancel As Boolean)\nDebug.Print \"handler ran\"\nEnd Sub\n" +
                "Private Sub Class_Terminate()\nDebug.Print \"Lterm\"\nEnd Sub\n"));
        // Listener terminates at MakeAndDrop's End Sub; the raise then finds no live sink → no "handler ran".
        AssertDebugLog(["Lterm", "pre", false]);
    }

    // ---- Phase 5 review fixes (both oracle-verified) ----

    [Fact]
    public async Task RaiseEvent_NoSink_StillEvaluatesArgExpressions()
    {
        // VB6 evaluates RaiseEvent arg expressions even with no listener bound (a side-effecting arg runs), then
        // dispatch is a no-op. Bump() increments a module counter; with no listener it must still reach 1.
        await RunClasses(
            "Dim gCount As Integer\n" +
            "Dim ck As Ticker\nSet ck = New Ticker\nck.DoTick\nDebug.Print gCount\n" +
            "Function Bump() As Integer\ngCount = gCount + 1\nBump = gCount\nEnd Function\n",
            ("Ticker", "Public Event Tick(ByVal n As Integer)\nPublic Sub DoTick()\nRaiseEvent Tick(Bump())\nEnd Sub\n"));
        AssertDebugLog([1]);
    }

    [Fact]
    public async Task Unbind_UnadvisesBeforeReleasing_TerminateTimeRaiseDoesNotReachDetachingListener()
    {
        // Set src = Nothing unadvises the connection BEFORE releasing the old source, so if that release triggers
        // the source's Class_Terminate and it raises an event, the just-detached listener does NOT receive it.
        await RunClasses(
            "Dim L As Lsnr\nSet L = New Lsnr\nL.Attach New Src\nL.Detach\nDebug.Print \"done\"\n",
            ("Src", "Public Event Tick()\nPrivate Sub Class_Terminate()\nDebug.Print \"srcterm\"\nRaiseEvent Tick\nEnd Sub\n"),
            ("Lsnr",
                "Private WithEvents src As Src\n" +
                "Public Sub Attach(c As Src)\nSet src = c\nEnd Sub\n" +
                "Public Sub Detach()\nSet src = Nothing\nEnd Sub\n" +
                "Private Sub src_Tick()\nDebug.Print \"HIT\"\nEnd Sub\n"));
        // srcterm fires (the source terminates and raises), but no "HIT" — the listener was already unadvised.
        AssertDebugLog(["srcterm", "done"]);
    }
}
