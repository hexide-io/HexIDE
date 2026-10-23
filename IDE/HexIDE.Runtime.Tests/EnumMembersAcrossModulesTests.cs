using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Another module's Public Enum member, named bare (#889). Every expectation is measured with <c>vb6.exe /make</c>, native
/// and p-code agreeing (the oracle's "Another module's Enum members, bare"). The primary module's top-level statements run
/// after the other modules' declarations, as Main would.
/// </summary>
public class EnumMembersAcrossModulesTests : BaseVBTestFixture
{
    private const string Module2 = "Public Enum Cols\n    colA\n    colB\n    colMax\nEnd Enum\nPrivate Enum Hidden\n    hidA = 7\nEnd Enum\n";

    private const string Class1 = "Public Enum ClsE\n    clsA = 40\nEnd Enum\nPublic Function ColsMax() As Long\n    ColsMax = colMax\nEnd Function\n";

    private const string Module3Enum = "Public Enum E3\n    colB = 20\nEnd Enum\n";

    private void Printed(params string[] lines) => AssertDebugLog([.. lines.Select(l => new Vb6Value(l))]);

    private async Task RunProject(string primary, (string Name, string Code)[] modules, (string Name, string Code)[] classes)
    {
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, primary, "Module1", modules, classes);
        await vb.Execute();
    }

    [Fact]
    public async Task Another_modules_Public_Enum_member_is_read_bare_a_classs_included()
    {
        // HexIDE read colMax as an undefined name: Empty without Option Explicit, so the loop ran once.
        await RunProject("Option Explicit\nDim i As Integer, n As Integer, obj As Class1\nDebug.Print \"colMax \" & colMax & \" \" & TypeName(colMax)\nDebug.Print \"colA + colMax \" & (colA + colMax)\nFor i = 0 To colMax: n = n + 1: Next\nDebug.Print \"For 0 To colMax runs \" & n\nDebug.Print \"Module2.colMax \" & Module2.colMax\nDebug.Print \"Cols.colB \" & Cols.colB\nDebug.Print \"clsA \" & clsA & \" \" & TypeName(clsA)\nSet obj = New Class1\nDebug.Print \"from a class \" & obj.ColsMax()\n",
            [("Module2", Module2)], [("Class1", Class1)]);

        Printed("colMax 2 Long", "colA + colMax 2", "For 0 To colMax runs 3", "Module2.colMax 2", "Cols.colB 1", "clsA 40 Long",
            "from a class 2");
    }

    [Fact]
    public async Task Another_modules_Enum_member_is_a_constant_in_a_module_level_Const_and_bound()
    {
        // #873 refused these as not implemented, naming #889.
        await RunProject("Option Explicit\nPrivate Const K = colMax + 1\nPrivate Const KC = clsA + 1\nPrivate d1(colMax) As Long\nPrivate d2(clsA) As Long\nDebug.Print \"K \" & K & \" \" & TypeName(K)\nDebug.Print \"KC \" & KC & \" \" & TypeName(KC)\nDebug.Print \"d1 \" & LBound(d1) & \" To \" & UBound(d1)\nDebug.Print \"d2 \" & LBound(d2) & \" To \" & UBound(d2)\n",
            [("Module2", Module2)], [("Class1", Class1)]);

        Printed("K 3 Long", "KC 41 Long", "d1 0 To 2", "d2 0 To 40");
    }

    [Fact]
    public async Task A_local_of_the_name_comes_first()
    {
        await RunProject("Option Explicit\nDim colB As Integer\ncolB = 9\nDebug.Print \"local colB \" & colB\nDebug.Print \"colA \" & colA\n", [("Module2", Module2)], []);

        Printed("local colB 9", "colA 0");
    }

    [Fact]
    public async Task A_member_of_another_modules_Private_Enum_is_not_defined_under_Option_Explicit()
    {
        var run = () => RunProject("Option Explicit\nDebug.Print \"hidA \" & hidA\n", [("Module2", Module2)], []);

        (await run.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().Contain("Variable not defined");
    }

    [Fact]
    public async Task Assigning_another_modules_Enum_member_is_refused()
    {
        var run = () => RunProject("Option Explicit\ncolA = 5\n", [("Module2", Module2)], []);

        (await run.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().Contain("Assignment to constant not permitted");
    }

    [Fact]
    public async Task Two_modules_Enum_members_of_one_name_are_ambiguous_bare_and_only_bare()
    {
        // Measured: the bare read is "Ambiguous name detected: colB", and each qualified read is its own.
        var bare = () => RunProject("Option Explicit\nDebug.Print \"colB \" & colB\n",
            [("Module2", Module2), ("Module3", Module3Enum)], []);
        (await bare.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().Contain("Ambiguous name detected: colB");

        debug.Clear();
        await RunProject("Option Explicit\nDebug.Print \"Cols.colB \" & Cols.colB & \" E3.colB \" & E3.colB & \" Module3.colB \" & Module3.colB\n",
            [("Module2", Module2), ("Module3", Module3Enum)], []);
        Printed("Cols.colB 1 E3.colB 20 Module3.colB 20");
    }

    [Fact]
    public async Task An_Enum_member_beside_another_modules_Public_variable_of_the_name_is_ambiguous()
    {
        var run = () => RunProject("Option Explicit\nDebug.Print \"colB \" & colB\n",
            [("Module2", Module2), ("Module3", "Public colB As Long\n")], []);

        (await run.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().Contain("Ambiguous name detected: colB");
    }

    [Fact]
    public async Task Two_modules_Enum_members_of_one_name_are_ambiguous_in_a_module_level_Const()
    {
        var run = () => RunProject("Option Explicit\nPrivate Const KB = colB\nDebug.Print \"KB \" & KB\n",
            [("Module2", Module2), ("Module3", Module3Enum)], []);

        (await run.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().Contain("Ambiguous name detected: colB");
    }
}
