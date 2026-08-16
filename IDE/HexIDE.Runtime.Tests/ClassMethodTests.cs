using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Interpreter-advanced Phase 3.2 — class methods + Me. Method dispatch runs on the instance (field writes
/// persist; locals are per-call), explicit and implicit Me, bare sibling-method calls, method args, and object
/// fields (Set obj.Field). Object member chains stay single-dot.
/// </summary>
public class ClassMethodTests : BaseVBTestFixture
{
    [Fact]
    public async Task Method_MutatesField_Persists()
    {
        await RunClasses(
            "Dim c As Box\nSet c = New Box\nc.SetVal 7\nDebug.Print c.GetVal\n",
            ("Box",
                "Private mVal As Integer\n" +
                "Public Sub SetVal(n As Integer)\nmVal = n\nEnd Sub\n" +
                "Public Function GetVal() As Integer\nGetVal = mVal\nEnd Function\n"));
        AssertDebugLog([7]);
    }

    // Bug-hunt MED: the `Call`-keyword form of an instance method call (`Call c.SetVal(7)`) threw "Unknown method"
    // because the explicit-Call handler lacked the Object branch the bare-call handler has. `Call` is just sugar —
    // it must dispatch identically.
    [Fact]
    public async Task CallKeyword_InstanceMethod_DispatchesLikeBareCall()
    {
        await RunClasses(
            "Dim c As Box\nSet c = New Box\nCall c.SetVal(7)\nDebug.Print c.GetVal\n",
            ("Box",
                "Private mVal As Integer\n" +
                "Public Sub SetVal(n As Integer)\nmVal = n\nEnd Sub\n" +
                "Public Function GetVal() As Integer\nGetVal = mVal\nEnd Function\n"));
        AssertDebugLog([7]);
    }

    [Fact]
    public async Task BareSiblingMethodCall_RunsOnSameInstance()
    {
        // The critique's linchpin case: Bump bare-calls Increment (implicit Me); both must mutate THIS instance's
        // mCount, not a discarded template clone.
        await RunClasses(
            "Dim c As Counter\nSet c = New Counter\nc.Bump\nc.Bump\nDebug.Print c.Value\n",
            ("Counter",
                "Private mCount As Integer\n" +
                "Public Sub Bump()\nIncrement\nEnd Sub\n" +
                "Private Sub Increment()\nmCount = mCount + 1\nEnd Sub\n" +
                "Public Function Value() As Integer\nValue = mCount\nEnd Function\n"));
        AssertDebugLog([2]);
    }

    [Fact]
    public async Task ExplicitMe_MethodAndField()
    {
        await RunClasses(
            "Dim g As Greeter\nSet g = New Greeter\ng.Run\n",
            ("Greeter",
                "Private mN As Integer\n" +
                "Public Sub Run()\nMe.mN = 5\nMe.Announce\nEnd Sub\n" +
                "Public Sub Announce()\nDebug.Print Me.mN\nEnd Sub\n"));
        AssertDebugLog([5]);
    }

    [Fact]
    public async Task Method_ReadsGlobalDebugAndErr()
    {
        // A method env is cloned from the class template env, which carries the shared Debug/Err slots.
        await RunClasses(
            "Dim c As Thing\nSet c = New Thing\nc.Go\n",
            ("Thing",
                "Public Sub Go()\nOn Error Resume Next\nErr.Raise 5\nDebug.Print Err.Number\nEnd Sub\n"));
        AssertDebugLog([new Vb6Value(5L)]);
    }

    [Fact]
    public async Task Method_ByRefArg_MutatesCaller()
    {
        await RunClasses(
            "Dim c As Doubler\nSet c = New Doubler\nDim x\nx = 10\nc.Double x\nDebug.Print x\n",
            ("Doubler", "Public Sub Double(ByRef n)\nn = n * 2\nEnd Sub\n"));
        AssertDebugLog([20]);
    }

    [Fact]
    public async Task TwoInstances_MethodsAreIndependent()
    {
        await RunClasses(
            "Dim a As Counter\nDim b As Counter\nSet a = New Counter\nSet b = New Counter\n" +
            "a.Bump\na.Bump\nb.Bump\n" +
            "Debug.Print a.Value\nDebug.Print b.Value\n",
            ("Counter",
                "Private mCount As Integer\n" +
                "Public Sub Bump()\nmCount = mCount + 1\nEnd Sub\n" +
                "Public Function Value() As Integer\nValue = mCount\nEnd Function\n"));
        AssertDebugLog([2, 1]);
    }

    [Fact]
    public async Task SetObjectField_SharesReference()
    {
        await RunClasses(
            "Dim a As Node\nDim b As Node\nSet a = New Node\nSet b = New Node\n" +
            "Set a.Child = b\n" +
            "Debug.Print (a.Child Is b)\n",   // True — the field holds the same instance
            ("Node", "Public Val As Integer\nPublic Child As Node\n"));
        AssertDebugLog([true]);
    }

    [Fact]
    public async Task Method_ReturningObject()
    {
        // A Function method can build and return a new instance.
        await RunClasses(
            "Dim f As Factory\nSet f = New Factory\nDim p As Node\nSet p = f.Make\nDebug.Print (p Is Nothing)\n",
            ("Factory", "Public Function Make() As Node\nSet Make = New Node\nEnd Function\n"),
            ("Node", "Public Val As Integer\n"));
        AssertDebugLog([false]);
    }

    [Fact]
    public async Task Method_ReturningObject_Unset_YieldsNothing()
    {
        // Phase-3 review finding: an object-returning method that never Sets its return must yield Nothing (VB6),
        // NOT a UserDefinedType-typed null — the seed for a class-typed return was falling through to the UDT
        // path. Without the fix, `Set p = f.Make` raised a spurious Error 424 'Object required'. This is the
        // classic factory/Find-returns-Nothing-on-miss pattern.
        await RunClasses(
            "Dim f As Factory\nSet f = New Factory\nDim p As Node\nSet p = f.Make\nDebug.Print (p Is Nothing)\n",
            ("Factory", "Public Function Make() As Node\nEnd Function\n"),   // no `Set Make = ...`
            ("Node", "Public Val As Integer\n"));
        AssertDebugLog([true]);
    }

    [Fact]
    public async Task Method_ReturningObject_Unset_ReadsAsObjectNothing()
    {
        // The same unset object return reads as an object that IS Nothing: IsObject True, TypeName "Nothing"
        // (the secondary tells of the pre-fix UserDefinedType/null seed, which gave False / "Variant").
        await RunClasses(
            "Dim f As Factory\nSet f = New Factory\nDebug.Print IsObject(f.Make)\nDebug.Print TypeName(f.Make)\n",
            ("Factory", "Public Function Make() As Node\nEnd Function\n"),
            ("Node", "Public Val As Integer\n"));
        AssertDebugLog([true, "Nothing"]);
    }
}
