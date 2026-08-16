using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Interpreter-advanced Phase 4.2a — <c>Class_Terminate</c> via real slot-based reference counting: a
/// <c>Class_Terminate</c> fires the instant an instance's last reference drops. Every expectation here is pinned
/// against <c>vb6.exe</c> (see docs/vb6-fidelity-oracle.md "Class_Terminate lifecycle"). Each class logs
/// <c>"I:"&amp;Id</c> in <c>Class_Initialize</c> (Id is empty at construction) and <c>"T:"&amp;Id</c> in
/// <c>Class_Terminate</c> (Id is set by the time it terminates), so the Debug.Print order reveals firing + timing.
///
/// 4.2a covers the direct cases (named-storage + transient holders + Me). The function-return transfer
/// (factory-return / discarded-result) is 4.2b — those currently under-fire (leak), the safe direction.
/// </summary>
public class ClassTerminateTests : BaseVBTestFixture
{
    // A class that logs its Initialize/Terminate, with a settable Id so terminations are identifiable.
    private const string Ob =
        "Public Id As String\n" +
        "Private Sub Class_Initialize()\nDebug.Print \"I:\" & Id\nEnd Sub\n" +
        "Private Sub Class_Terminate()\nDebug.Print \"T:\" & Id\nEnd Sub\n";

    [Fact]
    public async Task SoleOwner_SetNothing_TerminatesAtTheStatement()
    {
        await RunClasses(
            "Dim c As Ob\nSet c = New Ob\nDebug.Print \"m\"\nSet c = Nothing\nDebug.Print \"n\"\n",
            ("Ob", Ob));
        AssertDebugLog(["I:", "m", "T:", "n"]);
    }

    [Fact]
    public async Task Reassign_NewInitializesBeforeOldTerminates()
    {
        await RunClasses(
            "Dim c As Ob\nSet c = New Ob\nc.Id = \"1\"\nSet c = New Ob\nc.Id = \"2\"\nDebug.Print \"done\"\nSet c = Nothing\n",
            ("Ob", Ob));
        // The second New's Initialize fires before the first instance's Terminate; obj1 terminates at the reassign.
        AssertDebugLog(["I:", "I:", "T:1", "done", "T:2"]);
    }

    // NB (bug-hunt MED — Set slot-ordering): the Set statement now updates the slot/field/element to the new
    // reference BEFORE releasing the old one (oracle-verified: a Class_Terminate fired by the release sees the slot
    // pointing at the NEW value — "0;N;"). It isn't unit-tested here because observing the outer slot from inside a
    // Class_Terminate needs cross-scope access (a module global read from a class) that the interpreter does not yet
    // support — so the triggering scenario isn't currently reachable; the fix is oracle-documented defense-in-depth.
    // The existing Reassign_NewInitializesBeforeOldTerminates test confirms the reorder keeps Terminate timing.
    [Fact]
    public async Task MultipleLocals_TerminateInDeclarationOrder()
    {
        await RunClasses(
            "Make\nDebug.Print \"after\"\n" +
            "Sub Make()\n" +
            "Dim a As Ob\nDim b As Ob\nDim c As Ob\n" +
            "Set a = New Ob\na.Id = \"a\"\nSet b = New Ob\nb.Id = \"b\"\nSet c = New Ob\nc.Id = \"c\"\n" +
            "End Sub\n",
            ("Ob", Ob));
        // At End Sub the locals terminate in DECLARATION order a,b,c (not reverse/LIFO).
        AssertDebugLog(["I:", "I:", "I:", "T:a", "T:b", "T:c", "after"]);
    }

    [Fact]
    public async Task StoredIntoModuleGlobal_NoTerminateAtScopeExit()
    {
        await RunClasses(
            "Dim gOb As Ob\nInit2\nDebug.Print \"back\"\nSet gOb = Nothing\n" +
            "Sub Init2()\nDim t As Ob\nSet t = New Ob\nt.Id = \"g\"\nSet gOb = t\nEnd Sub\n",
            ("Ob", Ob));
        // The local t escapes into the module global gOb, so it does NOT terminate at Init2's End Sub — only when
        // gOb is later cleared (the classic factory-into-global pattern).
        AssertDebugLog(["I:", "back", "T:g"]);
    }

    [Fact]
    public async Task SharedLocal_NoTerminateUntilLastHolderDrops()
    {
        await RunClasses(
            "Sharer\nDebug.Print \"after\"\n" +
            "Sub Sharer()\nDim a As Ob\nDim b As Ob\n" +
            "Set a = New Ob\na.Id = \"s\"\nSet b = a\nSet a = Nothing\nDebug.Print \"mid\"\nEnd Sub\n",
            ("Ob", Ob));
        // `Set a = Nothing` does NOT terminate — b still holds the instance; it terminates at End Sub when b drops.
        AssertDebugLog(["I:", "mid", "T:s", "after"]);
    }

    [Fact]
    public async Task RunningMethodHoldsMe_DefersTerminateToReturn()
    {
        await RunClasses(
            "Dim g As Ob\nSet g = New Ob\ng.Id = \"me\"\ng.Suicide g\nDebug.Print \"out\"\n",
            ("Ob", Ob +
                // Drops the last external reference (via the ByRef alias) WHILE running on the instance; Me holds a
                // reference for the call, so Terminate defers to method return.
                "Public Sub Suicide(ByRef r As Ob)\nSet r = Nothing\nDebug.Print \"in\"\nEnd Sub\n"));
        AssertDebugLog(["I:", "in", "T:me", "out"]);
    }

    [Fact]
    public async Task NewAsCallArg_ByVal_TerminatesAfterTheCall()
    {
        await RunClasses(
            "Debug.Print \"before\"\nTake New Ob\nDebug.Print \"after\"\n" +
            "Sub Take(ByVal x As Ob)\nDebug.Print \"used\"\nEnd Sub\n",
            ("Ob", Ob));
        // The temporary is held for the call's duration (the ByVal param), and terminates right after the call.
        AssertDebugLog(["before", "I:", "used", "T:", "after"]);
    }

    [Fact]
    public async Task NewAsCallArg_ByRef_TerminatesAfterTheCall()
    {
        await RunClasses(
            "Debug.Print \"before\"\nTake New Ob\nDebug.Print \"after\"\n" +
            "Sub Take(ByRef x As Ob)\nDebug.Print \"used\"\nEnd Sub\n",
            ("Ob", Ob));
        // `New` is an rvalue (no caller slot to alias), so ByRef behaves like ByVal here — same timing.
        AssertDebugLog(["before", "I:", "used", "T:", "after"]);
    }

    [Fact]
    public async Task Cycle_LeaksNeverTerminates()
    {
        await RunClasses(
            "Dim a As Node\nDim b As Node\nSet a = New Node\na.Id = \"a\"\nSet b = New Node\nb.Id = \"b\"\n" +
            "Set a.Other = b\nSet b.Other = a\nSet a = Nothing\nSet b = Nothing\nDebug.Print \"end\"\n",
            ("Node", "Public Id As String\nPublic Other As Node\n" +
                "Private Sub Class_Initialize()\nDebug.Print \"I:\" & Id\nEnd Sub\n" +
                "Private Sub Class_Terminate()\nDebug.Print \"T:\" & Id\nEnd Sub\n"));
        // A 2-cycle: neither count reaches zero, so NEITHER terminates — faithful to VB6, which leaks cycles.
        AssertDebugLog(["I:", "I:", "end"]);
    }

    [Fact]
    public async Task SelfAssign_SetXX_DoesNotTerminate()
    {
        await RunClasses(
            "Dim c As Ob\nSet c = New Ob\nSet c = c\nDebug.Print \"ok\"\nSet c = Nothing\n",
            ("Ob", Ob));
        // AddRef-before-Release keeps `Set c = c` from transiently hitting zero and self-terminating.
        AssertDebugLog(["I:", "ok", "T:"]);
    }

    // ---- Phase 4.2b: function-return / statement-temporary transfer ----

    [Fact]
    public async Task FactoryReturn_Assigned_TransfersNotTerminatesEarly()
    {
        await RunClasses(
            "Dim g As Ob\nSet g = Make()\nDebug.Print \"mid\"\nSet g = Nothing\n" +
            "Function Make() As Ob\nDim t As Ob\nSet t = New Ob\nt.Id = \"r\"\nSet Make = t\nEnd Function\n",
            ("Ob", Ob));
        // The local t's hold transfers out via the return: NO Terminate at End Function; the instance lives in g
        // and terminates only at `Set g = Nothing`.
        AssertDebugLog(["I:", "mid", "T:r"]);
    }

    [Fact]
    public async Task FactoryResult_Discarded_TerminatesAfterTheStatement()
    {
        await RunClasses(
            "Make\nDebug.Print \"after\"\n" +
            "Function Make() As Ob\nSet Make = New Ob\nMake.Id = \"r\"\nEnd Function\n",
            ("Ob", Ob));
        // A discarded object return terminates right after the call statement (when the statement frame drains).
        AssertDebugLog(["I:", "T:r", "after"]);
    }

    [Fact]
    public async Task SetArrayElement_HoldsACountedReference_KeepsObjectAliveAfterLocalDrops()
    {
        // `Set arr(i) = obj` must AddRef the element: an object held ONLY by an array element must stay alive
        // after the local that created it goes out of scope (else it would drop to RefCount 0 and terminate). The
        // Add sub's local `o` drops at End Sub; the object survives in mArr and is still usable afterwards.
        await RunClasses(
            "Dim h As Holder\nSet h = New Holder\nh.Add\nDebug.Print \"mid\"\nh.PingFirst\n",
            ("Holder",
                "Private mArr(0 To 2) As Ob\n" +
                "Public Sub Add()\nDim o As Ob\nSet o = New Ob\no.Id = \"x\"\nSet mArr(0) = o\nEnd Sub\n" +
                "Public Sub PingFirst()\nDim o As Ob\nSet o = mArr(0)\no.Ping\nEnd Sub\n"),
            ("Ob", Ob + "Public Sub Ping()\nDebug.Print \"ping\" & Id\nEnd Sub\n"));
        // No "T:x" — the array element kept it alive across the scope exit; PingFirst reads it back and it works.
        AssertDebugLog(["I:", "mid", "pingx"]);
    }

    [Fact]
    public async Task MethodReturn_Discarded_TerminatesAfterTheStatement()
    {
        await RunClasses(
            "Dim f As Factory\nSet f = New Factory\nf.Make\nDebug.Print \"after\"\nSet f = Nothing\n",
            ("Factory", "Public Function Make() As Ob\nSet Make = New Ob\nMake.Id = \"m\"\nEnd Function\n"),
            ("Ob", Ob));
        // A factory METHOD's discarded object return adopts + terminates the same way (object-method return path).
        AssertDebugLog(["I:", "T:m", "after"]);
    }

    // ---- Phase 4.2c: adversarial-review fixes (cascade, Let-over-object, error-exit, transient New) ----

    [Fact]
    public async Task Composition_TerminatingOwnerCascadesToSoleOwnedField()
    {
        await RunClasses(
            "Dim o As Outer\nSet o = New Outer\nDebug.Print \"mid\"\nSet o = Nothing\nDebug.Print \"after\"\n",
            ("Outer",
                "Private mInner As Inner\n" +
                "Private Sub Class_Initialize()\nSet mInner = New Inner\nmInner.Id = \"in\"\nEnd Sub\n" +
                "Private Sub Class_Terminate()\nDebug.Print \"T:outer\"\nEnd Sub\n"),
            ("Inner",
                "Public Id As String\n" +
                "Private Sub Class_Terminate()\nDebug.Print \"T:\" & Id\nEnd Sub\n"));
        // Terminating Outer releases its field ref to the sole-owned Inner, which then terminates too (VB6 tears
        // members down AFTER the owner's Class_Terminate). Review finding #2 (no cascade) — was a leak.
        AssertDebugLog(["mid", "T:outer", "T:in", "after"]);
    }

    [Fact]
    public async Task LetScalarOverObjectVariant_ReleasesTheObject()
    {
        await RunClasses(
            "Dim v\nSet v = New Ob\nv.Id = \"v\"\nv = 42\nDebug.Print \"after\"\n",
            ("Ob", Ob));
        // A Variant that held an object, overwritten by a scalar via Let, drops the reference → Terminate fires at
        // `v = 42`. Review finding #3 (Set/Let asymmetry) — was a leak.
        AssertDebugLog(["I:", "T:v", "after"]);
    }

    [Fact]
    public async Task FunctionFaultsAfterSetReturn_TerminatesTheOrphanedReturn()
    {
        await RunClasses(
            "On Error Resume Next\nDim x As Ob\nSet x = Make()\nDebug.Print \"after\"\n" +
            "Function Make() As Ob\nSet Make = New Ob\nMake.Id = \"r\"\nErr.Raise 5\nEnd Function\n",
            ("Ob", Ob));
        // The function faults AFTER assigning its return; on the error unwind no caller adopts the return, so
        // RunProcedure releases the return-name → Terminate. Review finding #4 — was a leak on every error-exit.
        AssertDebugLog(["I:", "T:r", "after"]);
    }

    [Fact]
    public async Task TransientNew_InBuiltinArg_TerminatesAfterTheStatement()
    {
        await RunClasses(
            "Debug.Print TypeName(New Ob)\nDebug.Print \"after\"\n",
            ("Ob", Ob));
        // A `New` consumed by a built-in (never stored) is held in the statement frame and terminates at
        // statement-end. Review finding #5 — was a leak.
        AssertDebugLog(["I:", "Ob", "T:", "after"]);
    }
}
