using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// <c>Implements</c> (#186) — a class claiming an interface, and what an interface-typed variable can reach.
///
/// Every expectation here is measured against vb6.exe; see "Implements, and interface-typed variables" in
/// docs/vb6-fidelity-oracle.md. The measurement mattered: three of these would have been guessed wrong
/// (TypeName reports the CONCRETE class, a Public implementation is accepted, and a failed TypeOf is False
/// rather than an error).
///
/// The one deliberate divergence is TIMING. VB6 rejects a non-conforming class at compile time; HexIDE has no
/// compile step, so the same check with the same message runs at first instantiation.
/// </summary>
public class ImplementsTests : BaseVBTestFixture
{
    // A minimal interface: a class module of empty public members. That is all a VB6 interface is.
    private const string IShape = "Public Function Area() As Integer\nEnd Function\n";

    private const string Square =
        "Implements IShape\n" +
        "Private Function IShape_Area() As Integer\n" +
        "    IShape_Area = 7\n" +
        "End Function\n" +
        "Public Function Own() As Integer\n" +
        "    Own = 99\n" +
        "End Function\n";

    // The same member by name, no Implements — the control for every "does it actually check?" case below.
    private const string Impostor = "Public Function Area() As Integer\n    Area = 1\nEnd Function\n";

    [Fact]
    public async Task InterfaceTypedVariable_DispatchesToTheImplementation()
    {
        // The whole point: `x.Area` with x declared As IShape runs IShape_Area, which is not even named Area.
        await RunClasses(
            "Dim x As IShape\nSet x = New Square\nDebug.Print x.Area\n",
            ("IShape", IShape), ("Square", Square));
        AssertDebugLog([7]);
    }

    [Fact]
    public async Task ConcreteTypedVariable_ReachesItsOwnMembers()
    {
        // The same object through its own class name is unrestricted — the interface view is a property of the
        // NAME it was read through, not of the object.
        await RunClasses(
            "Dim s As Square\nSet s = New Square\nDebug.Print s.Own\n",
            ("IShape", IShape), ("Square", Square));
        AssertDebugLog([99]);
    }

    [Fact]
    public async Task InterfaceTypedVariable_CannotReachTheClassOwnMembers()
    {
        // Measured as a COMPILE error in VB6 ("Method or data member not found"); raised at the call here,
        // which is the same message at the only moment HexIDE has.
        var act = async () => await RunClasses(
            "Dim x As IShape\nSet x = New Square\nDebug.Print x.Own\n",
            ("IShape", IShape), ("Square", Square));
        (await act.Should().ThrowAsync<VBMethodOrDataMemberNotFoundException>())
            .Which.Message.Should().Contain("Own");
    }

    [Fact]
    public async Task TypeName_ReportsTheConcreteClass_NotTheInterface()
    {
        // Measured, and the opposite of the natural guess: an interface-typed variable still names its object's
        // real class. The view restricts what you may CALL, not what the object reports itself to be.
        await RunClasses(
            "Dim x As IShape\nSet x = New Square\nDebug.Print TypeName(x)\n",
            ("IShape", IShape), ("Square", Square));
        AssertDebugLog(["Square"]);
    }

    [Fact]
    public async Task TypeOf_IsTrue_ForAnImplementedInterface()
    {
        await RunClasses(
            "Dim s As Square\nSet s = New Square\nDebug.Print (TypeOf s Is IShape)\n",
            ("IShape", IShape), ("Square", Square));
        AssertDebugLog([true]);
    }

    [Fact]
    public async Task TypeOf_IsFalse_ForAnUnimplementedInterface()
    {
        // False, not an error — measured. Same member name, no Implements, so nothing matches.
        await RunClasses(
            "Dim p As Impostor\nSet p = New Impostor\nDebug.Print (TypeOf p Is IShape)\n",
            ("IShape", IShape), ("Impostor", Impostor));
        AssertDebugLog([false]);
    }

    [Fact]
    public async Task PublicImplementation_IsAccepted()
    {
        // The Private on IShape_Area is convention, not a rule — VB6 compiles the Public form. Pinned so a
        // future "enforce Private" tidy-up has to argue with the oracle first.
        await RunClasses(
            "Dim x As IShape\nSet x = New Loud\nDebug.Print x.Area\n",
            ("IShape", IShape),
            ("Loud", "Implements IShape\nPublic Function IShape_Area() As Integer\n    IShape_Area = 4\nEnd Function\n"));
        AssertDebugLog([4]);
    }

    [Fact]
    public async Task NonConformingClass_IsRefusedAtFirstInstantiation()
    {
        // VB6's own message, verbatim, naming the missing member and the interface. VB6 refuses to build;
        // HexIDE refuses to construct.
        var act = async () => await RunClasses(
            "Dim x As IShape\nSet x = New Hollow\n",
            ("IShape", IShape),
            ("Hollow", "Implements IShape\n"));
        (await act.Should().ThrowAsync<VBCompileErrorException>())
            .Which.Message.Should().EndWith("Object module needs to implement 'Area' for interface 'IShape'");
    }

    [Fact]
    public async Task NeverInstantiated_NonConformingClass_IsNeverChecked()
    {
        // The accepted divergence, pinned deliberately: VB6 would have rejected this program outright. Without
        // a compile step there is no moment to notice, and inventing one would mean analysing relationships
        // between modules before the walk — the thing the pre-pass boundary forbids.
        await RunClasses(
            "Debug.Print 1\n",
            ("IShape", IShape),
            ("Hollow", "Implements IShape\n"));
        AssertDebugLog([1]);
    }

    [Fact]
    public async Task SetOfANonImplementer_IsTypeMismatch()
    {
        await RunClasses(
            "On Error Resume Next\nDim x As IShape\nSet x = New Impostor\nDebug.Print Err.Number\n",
            ("IShape", IShape), ("Impostor", Impostor));
        AssertDebugLog([new Vb6Value(13L)]);
    }

    [Fact]
    public async Task SetOfAnUnrelatedClass_IsTypeMismatch()
    {
        // Not an interface case at all — a class-typed slot enforces its name too. Measured the same: Err 13.
        await RunClasses(
            "On Error Resume Next\nDim s As Square\nSet s = New Impostor\nDebug.Print Err.Number\n",
            ("IShape", IShape), ("Square", Square), ("Impostor", Impostor));
        AssertDebugLog([new Vb6Value(13L)]);
    }

    [Fact]
    public async Task AVariantMayCarryTheReferenceBetweenTwoTypedNames()
    {
        // Measured: routing through a Variant is fine — the Set into `x` re-checks against the object, which
        // does implement IShape. This is the case that proves the check reads the OBJECT and not the source
        // slot's declared type (the Variant has none).
        await RunClasses(
            "Dim v As Variant\nSet v = New Square\nDim x As IShape\nSet x = v\nDebug.Print x.Area\n",
            ("IShape", IShape), ("Square", Square));
        AssertDebugLog([7]);
    }

    [Fact]
    public async Task ThroughAVariant_TheConcreteMembersAreReachableAgain()
    {
        // The view is per-name, so widening to a Variant widens what is reachable. VB6 agrees: a Variant is
        // late-bound and resolves against the real object.
        await RunClasses(
            "Dim x As IShape\nSet x = New Square\nDim v As Variant\nSet v = x\nDebug.Print v.Own\n",
            ("IShape", IShape), ("Square", Square));
        AssertDebugLog([99]);
    }

    [Fact]
    public async Task AClassMayImplementTwoInterfaces()
    {
        await RunClasses(
            "Dim a As IShape\nDim b As INamed\nSet a = New Both\nSet b = a\n" +
            "Debug.Print a.Area\nDebug.Print b.Title\n",
            ("IShape", IShape),
            ("INamed", "Public Function Title() As String\nEnd Function\n"),
            ("Both", "Implements IShape\nImplements INamed\n" +
                     "Private Function IShape_Area() As Integer\n    IShape_Area = 5\nEnd Function\n" +
                     "Private Function INamed_Title() As String\n    INamed_Title = \"both\"\nEnd Function\n"));
        AssertDebugLog([new Vb6Value(5), new Vb6Value("both")]);
    }
}
