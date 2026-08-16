using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Interpreter-advanced Phase 3.1 — class object model foundation: instantiation (New), reference semantics
/// (Set shares, Is/Nothing by reference identity), TypeOf, class-typed Dim defaults to Nothing, and instance
/// fields (obj.Field read/write, per-instance isolation). Methods + Me are Phase 3.2.
/// </summary>
public class ClassTests : BaseVBTestFixture
{
    private const string Point = "Public X As Integer\nPublic Y As Integer\n";

    [Fact]
    public async Task New_ReadWrite_Fields()
    {
        await RunClasses(
            "Dim p As Point\nSet p = New Point\np.X = 3\np.Y = 4\nDebug.Print p.X\nDebug.Print p.Y\n",
            ("Point", Point));
        AssertDebugLog([3, 4]);
    }

    [Fact]
    public async Task NewInstance_FieldsDefaultToZero()
    {
        await RunClasses(
            "Dim p As Point\nSet p = New Point\nDebug.Print p.X\n",
            ("Point", Point));
        AssertDebugLog([0]);
    }

    [Fact]
    public async Task Set_SharesReference()
    {
        // Objects are reference types: Set b = a shares one instance; mutating b.X is visible through a.
        await RunClasses(
            "Dim a As Point\nDim b As Point\n" +
            "Set a = New Point\na.X = 1\n" +
            "Set b = a\n" +
            "b.X = 99\n" +
            "Debug.Print a.X\n",   // 99 — shared
            ("Point", Point));
        AssertDebugLog([99]);
    }

    [Fact]
    public async Task TwoInstances_HaveIndependentFields()
    {
        await RunClasses(
            "Dim a As Point\nDim b As Point\n" +
            "Set a = New Point\nSet b = New Point\n" +
            "a.X = 1\nb.X = 2\n" +
            "Debug.Print a.X\nDebug.Print b.X\n",   // 1, 2 — independent
            ("Point", Point));
        AssertDebugLog([1, 2]);
    }

    [Fact]
    public async Task DimAsClass_IsNothing_UntilSet()
    {
        await RunClasses(
            "Dim p As Point\nDebug.Print (p Is Nothing)\nSet p = New Point\nDebug.Print (p Is Nothing)\n",
            ("Point", Point));
        AssertDebugLog([true, false]);
    }

    [Fact]
    public async Task Is_ReferenceIdentity()
    {
        await RunClasses(
            "Dim a As Point\nDim b As Point\nDim c As Point\n" +
            "Set a = New Point\nSet b = a\nSet c = New Point\n" +
            "Debug.Print (a Is b)\n" +   // True — same instance
            "Debug.Print (a Is c)\n",    // False — different instances
            ("Point", Point));
        AssertDebugLog([true, false]);
    }

    [Fact]
    public async Task SetToNothing_ThenIsNothing()
    {
        await RunClasses(
            "Dim p As Point\nSet p = New Point\nSet p = Nothing\nDebug.Print (p Is Nothing)\n",
            ("Point", Point));
        AssertDebugLog([true]);
    }

    [Fact]
    public async Task TypeOf_MatchesClass()
    {
        await RunClasses(
            "Dim p As Point\nSet p = New Point\n" +
            "Debug.Print (TypeOf p Is Point)\n" +   // True
            "Debug.Print (TypeOf p Is Other)\n",    // False
            ("Point", Point),
            ("Other", "Public Z As Integer\n"));
        AssertDebugLog([true, false]);
    }

    [Fact]
    public async Task LetOfObject_Errors()
    {
        // A plain `x = obj` (Let, not Set) must error — object assignment requires Set.
        Func<Task> act = () => RunClasses(
            "Dim p As Point\nDim v\nSet p = New Point\nv = p\n",
            ("Point", Point));
        await act.Should().ThrowAsync<VBRunTimeException>();
    }

    [Fact]
    public async Task SetToNonObject_Errors()
    {
        Func<Task> act = () => RunClasses(
            "Dim p As Point\nSet p = 5\n",
            ("Point", Point));
        await act.Should().ThrowAsync<VBRunTimeException>();
    }

    [Fact]
    public async Task NothingMemberAccess_IsError91()
    {
        Func<Task> act = () => RunClasses(
            "Dim p As Point\nDebug.Print p.X\n",   // p is Nothing (never Set)
            ("Point", Point));
        (await act.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(91);
    }
}
