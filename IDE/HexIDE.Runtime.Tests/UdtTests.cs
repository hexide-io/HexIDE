using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Interpreter-advanced Phase 2 — user-defined types (Type…End Type). The defining property is VALUE semantics:
/// copy-on-assign at Let/ByVal/return, ByRef aliases, nested-field navigation. Arrays-of/in UDTs and
/// fixed-length strings are deferred (spec walls).
/// </summary>
public class UdtTests : BaseVBTestFixture
{
    private const string Point = "Type Point\nX As Integer\nY As Integer\nEnd Type\n";

    [Fact]
    public async Task Declare_ReadWrite_Fields()
    {
        await Run(Point +
            "Dim p As Point\n" +
            "p.X = 3\n" +
            "p.Y = 4\n" +
            "Debug.Print p.X\n" +
            "Debug.Print p.Y\n");
        AssertDebugLog([3, 4]);
    }

    [Fact]
    public async Task Fields_ZeroInitialised()
    {
        await Run(
            "Type T\nN As Integer\nS As String\nEnd Type\n" +
            "Dim t As T\n" +
            "Debug.Print t.N\n" +           // 0 (Integer)
            "Debug.Print \"[\" & t.S & \"]\"\n");  // "[]" (String default "")
        AssertDebugLog([new Vb6Value(0), "[]"]);
    }

    [Fact]
    public async Task CopyOnAssign_IsIndependent()
    {
        // The defining UDT test: b = a copies; mutating b never touches a.
        await Run(Point +
            "Dim a As Point\n" +
            "Dim b As Point\n" +
            "a.X = 3\n" +
            "b = a\n" +
            "b.X = 99\n" +
            "Debug.Print a.X\n" +   // 3 — unchanged
            "Debug.Print b.X\n");   // 99
        AssertDebugLog([3, 99]);
    }

    [Fact]
    public async Task NestedField_ReadWrite()
    {
        await Run(
            "Type Addr\nCity As String\nEnd Type\n" +
            "Type Person\nHome As Addr\nEnd Type\n" +
            "Dim per As Person\n" +
            "per.Home.City = \"London\"\n" +
            "Debug.Print per.Home.City\n");
        AssertDebugLog(["London"]);
    }

    [Fact]
    public async Task NestedCopy_IsIndependent()
    {
        // c = e.Address copies the nested bag; mutating c must not write back to e.
        await Run(
            "Type Addr\nCity As String\nEnd Type\n" +
            "Type Person\nHome As Addr\nEnd Type\n" +
            "Dim per As Person\n" +
            "per.Home.City = \"London\"\n" +
            "Dim a As Addr\n" +
            "a = per.Home\n" +
            "a.City = \"Paris\"\n" +
            "Debug.Print per.Home.City\n" +   // London — unchanged
            "Debug.Print a.City\n");          // Paris
        AssertDebugLog(["London", "Paris"]);
    }

    [Fact]
    public async Task ByRef_MutationPropagates()
    {
        await Run(Point +
            "Sub Fill(p As Point)\n" +   // ByRef default
            "p.X = 42\n" +
            "End Sub\n" +
            "Dim q As Point\n" +
            "Fill q\n" +
            "Debug.Print q.X\n");        // 42
        AssertDebugLog([42]);
    }

    [Fact]
    public async Task ByVal_DoesNotMutate()
    {
        await Run(Point +
            "Sub NoFill(ByVal p As Point)\n" +
            "p.X = 42\n" +
            "End Sub\n" +
            "Dim q As Point\n" +
            "q.X = 1\n" +
            "NoFill q\n" +
            "Debug.Print q.X\n");        // 1 — the copy was mutated, not q
        AssertDebugLog([1]);
    }

    [Fact]
    public async Task ReturnedFromFunction()
    {
        await Run(Point +
            "Function MakePoint() As Point\n" +
            "MakePoint.X = 7\n" +
            "End Function\n" +
            "Dim p As Point\n" +
            "p = MakePoint()\n" +
            "Debug.Print p.X\n");        // 7
        AssertDebugLog([7]);
    }

    [Fact]
    public async Task CrossModule_PublicType()
    {
        // A Public Type declared in another module is usable from the primary.
        await RunModules(
            "Dim e As Emp\ne.Id = 5\nDebug.Print e.Id\n",
            ("Shared", "Public Type Emp\nId As Integer\nEnd Type\n"));
        AssertDebugLog([5]);
    }

    [Fact]
    public async Task With_OverNestedUdtField()
    {
        // Regression (review): `With p.Home` (a member-access target) used to throw a raw NRE at With-entry;
        // now the With target resolves through the full member path and the write persists.
        await Run(
            "Type Addr\nCity As String\nEnd Type\n" +
            "Type Person\nHome As Addr\nEnd Type\n" +
            "Dim p As Person\n" +
            "With p.Home\n" +
            ".City = \"London\"\n" +
            "End With\n" +
            "Debug.Print p.Home.City\n");
        AssertDebugLog(["London"]);
    }
}
