using HexIDE.IDE;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A module-level array whose bounds are not plain integers is sized where it is allocated, as its module's
/// declarations run. VB6 sizes it from a constant expression before the program starts, and every answer here was
/// measured with <c>vb6.exe /make</c>; the probes are in docs/vb6-fidelity-oracle.md, "What a module-level array's
/// bound may name". Nothing may size one from Empty: a bound HexIDE cannot read yet is refused, loudly.
/// </summary>
public class ModuleLevelArrayBoundTests : BaseVBTestFixture
{
    private const string Twice = "Function Twice(x)\n    Twice = x * 2\nEnd Function";
    private const string Sizes = "Enum Sizes\n    eMin\n    eMax = 4\nEnd Enum";
    private const string Cols = "Public Enum Cols\n    colA\n    colB\n    colMax\nEnd Enum\n";

    [Theory]
    [InlineData("Const GRID = 10\nDim d(0 To GRID - 1) As Long", 9L)]
    [InlineData("Const GRID = 10\nDim d(GRID \\ 3) As Long", 3L)]
    [InlineData(Sizes + "\nDim d(eMax) As Long", 4L)]
    [InlineData(Sizes + "\nDim d(Sizes.eMax) As Long", 4L)]                         // qualified by its Enum
    [InlineData("Dim d(vbKeyA) As Long", 65L)]
    [InlineData("Const A = 2\nConst B = A * 2\nDim d(B) As Long", 4L)]              // through a Const that reads a Const
    [InlineData(Sizes + "\nConst C = eMax + 1\nDim d(C) As Long", 5L)]              // through a Const that reads an Enum
    [InlineData(Sizes + "\nConst C = Sizes.eMax + 1\nDim d(C) As Long", 5L)]
    [InlineData("Const C = vbKeyA - 60\nDim d(C) As Long", 5L)]
    [InlineData("Const K = \"abcd\"\nDim d(Len(K)) As Long", 4L)]                    // a built-in of a Const
    [InlineData("Dim d(Abs(Int(-2.5)) + Len(\"ab\")) As Long", 5L)]
    public async Task A_bound_of_constants_sizes_the_array(string declarations, long upper)
    {
        await Run($"{declarations}\nDebug.Print UBound(d)\n");

        AssertDebugLog([new Vb6Value(upper)]);
    }

    [Theory]
    [InlineData("Len(\"abc\")", 3L)]
    [InlineData("Abs(-3)", 3L)]
    [InlineData("Sgn(5) + 1", 2L)]
    [InlineData("Int(2.5)", 2L)]
    [InlineData("Fix(2.5)", 2L)]
    [InlineData("CInt(2.5)", 2L)]
    [InlineData("CLng(3.5)", 4L)]
    [InlineData("CByte(3)", 3L)]
    [InlineData("-CBool(1) + 1", 2L)]
    [InlineData("CCur(3)", 3L)]
    [InlineData("CDbl(3)", 3L)]
    [InlineData("CSng(3)", 3L)]
    [InlineData("Len(CStr(123))", 3L)]
    public async Task A_built_in_VB6_allows_in_a_bound_sizes_the_array(string bound, long upper)
    {
        // Measured. VB6 allows LenB and CVar too, which HexIDE does not implement anywhere yet.
        await Run($"Dim d({bound}) As Long\nDebug.Print UBound(d)\n");

        AssertDebugLog([new Vb6Value(upper)]);
    }

    [Theory]
    [InlineData("Dim d(N) As Long\nConst N = 3")]                                 // a Const declared below the array
    [InlineData("Dim n As Long\nDim d(n) As Long")]                               // a variable
    [InlineData("Dim d(Twice(2)) As Long\n" + Twice)]                              // a user function
    [InlineData("Const C = Twice(2)\nDim d(C) As Long\n" + Twice)]                 // ... read through a Const
    [InlineData("Dim n As Long\nConst C = n + 1\nDim d(C) As Long")]               // a variable, through a Const
    [InlineData("Const A = B + 1\nConst B = 2\nDim d(A) As Long")]                 // a forward reference, through a Const
    [InlineData("Const A = A + 1\nDim d(A) As Long")]                              // a Const reading itself
    [InlineData("Const C = Len(\"abcd\")\nDim d(C) As Long")]                      // any call in a Const, even Len
    [InlineData("Const C = Int(2.5)\nDim d(C) As Long")]
    [InlineData("Dim s As String\nDim d(Len(s)) As Long")]                         // a built-in of a variable
    [InlineData("Dim d(zz) As Long")]                                              // a name nothing declares, no Option Explicit
    [InlineData("Dim d(Asc(\"A\") - 60) As Long")]
    [InlineData("Dim d(Int(Rnd() * 10)) As Long")]
    [InlineData("Dim d(Val(\"3\")) As Long")]
    [InlineData("Dim d(IIf(True, 2, 3)) As Long")]
    [InlineData("Dim d(Len(Chr$(65))) As Long")]
    [InlineData("Dim d(Len(Mid$(\"abcd\", 2))) As Long")]
    [InlineData("Dim d(Round(2.6)) As Long")]
    [InlineData("Dim d(Sqr(9)) As Long")]
    [InlineData("Dim d(Int(Timer) Mod 5) As Long")]
    [InlineData("Option Explicit\nDim d(Int(Timer) Mod 5) As Long")]           // Timer, a function, not an unknown name
    public async Task A_bound_that_is_not_a_constant_expression_is_VB6s_compile_error(string declarations)
    {
        var run = () => Run(declarations + "\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Constant expression required*");
    }

    [Theory]
    [InlineData("Option Explicit\nDim d(zz) As Long", "")]
    [InlineData("Option Explicit\nDim d(M) As Long", "Private Const M = 3\n")]     // another module's Private Const
    [InlineData("Option Explicit\nDim d(M) As Long", "Const M = 3\n")]             // a Const is Private by default
    [InlineData("Option Explicit\nDim d(colMax) As Long", "Private Enum Cols\n    colA\n    colMax\nEnd Enum\n")]
    public async Task A_name_the_module_cannot_see_is_Variable_not_defined_under_Option_Explicit(string declarations, string module2)
    {
        var run = () => RunModules(declarations + "\n", ("Module2", module2));

        await run.Should().ThrowAsync<VBVariableNotDefinedException>();
    }

    [Theory]
    [InlineData("Option Explicit\nDim d(n) As Long", "Public n As Long\n")]   // another module's Public variable
    public async Task Another_modules_Public_variable_is_a_compile_error(string declaration, string module2)
    {
        var run = () => RunModules(declaration + "\n", ("Module2", module2));

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Constant expression required*");
    }

    [Theory]
    [InlineData("Const N = 3\nDim d(Module1.N) As Long")]
    [InlineData(Sizes + "\nDim d(Module1.eMax) As Long")]
    public async Task The_modules_own_name_as_a_qualifier_is_VB6s_circular_dependency(string declarations)
    {
        var run = () => RunModules(declarations + "\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Circular dependencies between modules*");
    }

    [Theory]
    [InlineData("Dim d(M) As Long")]
    [InlineData("Dim d(Module2.M) As Long")]
    [InlineData("Dim d(1 To M + 1) As Long")]
    public async Task A_bound_reading_another_modules_Const_is_refused_rather_than_sized_from_Empty(string declarations)
    {
        // VB6 gives 0 To 3, 0 To 3 and 1 To 4. HexIDE does not read another module's Const unqualified
        // (#880), and initialises modules in list order, so a size from it could be Empty's.
        var run = () => RunModules($"{declarations}\n", ("Module2", "Public Const M = 3\n"));

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*d's bounds read*M, another module's Public Const*#880*");
    }

    [Theory]
    [InlineData("Const A = B + 1\nConst B = 2\nDebug.Print A")]                  // a forward reference, with no array at all
    [InlineData("Dim n As Long\nConst C = n + 1\nDebug.Print C")]
    [InlineData("Const C = Len(\"x\")\nDebug.Print C")]                           // any function call in a Const
    [InlineData("Const C = Twice(2)\nDebug.Print C\n" + Twice)]
    public async Task A_module_Const_that_is_not_a_constant_expression_is_VB6s_compile_error(string code)
    {
        // Measured with /make: each is "Constant expression required" at the Const. Each ran, from whatever the
        // evaluator made of it: A was 1, and C 1, 1 and 4.
        var run = () => Run(code + "\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Constant expression required*");
    }

    [Theory]
    [InlineData("Const C = M + 1\nDebug.Print C")]
    [InlineData("Const C = Module2.M + 1\nDebug.Print C")]
    public async Task A_module_Const_reading_another_modules_Const_is_refused_rather_than_read_as_Empty(string code)
    {
        // VB6 gives 4 for both (measured). HexIDE gave 1: M unqualified is not found, and Module2 may not have run.
        var run = () => RunModules(code + "\n", ("Module2", "Public Const M = 3\n"));

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*Const C's value reads*M, another module's Public Const*#880*");
    }

    [Theory]
    [InlineData("Const A = 2\nConst B = A * 2\nDebug.Print B", 4L)]
    [InlineData(Sizes + "\nConst C = eMax + 1\nDebug.Print C", 5L)]
    [InlineData("Const C = vbKeyA - 60\nDebug.Print C", 5L)]
    public async Task A_module_Const_of_constants_is_its_value(string code, long expected)
    {
        await Run(code + "\n");
        debug.Should().ContainSingle().Which.Value.Should().Match<object>(v => Convert.ToInt64(v) == expected);
    }

    [Fact]
    public async Task A_bound_reading_the_startup_modules_Const_from_another_module_is_refused()
    {
        // Measured: Module2's `Public d(C)`, with `Const C = Module1.M + 1`, is 0 To 4. HexIDE initialises the primary
        // module last, so M would still be Empty there.
        var run = () => RunModules("Public Const M = 3\n", ("Module2", "Const C = Module1.M + 1\nPublic d(C) As Long\n"));

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*Const C's value reads Module1.M*#880*");
    }

    [Fact]
    public async Task A_bound_reading_another_modules_Enum_member_unqualified_is_refused_rather_than_sized_from_Empty()
    {
        // VB6 gives 0 To 2. HexIDE reads another module's Enum member unqualified as Empty.
        var run = () => RunModules("Dim d(colMax) As Long\n", ("Module2", Cols));

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*d's bounds read colMax*another module's Enum*#889*");
    }

    [Fact]
    public async Task Another_modules_Enum_member_qualified_by_its_Enum_sizes_the_array()
    {
        await RunModules("Dim d(Cols.colMax) As Long\nDebug.Print UBound(d)\n", ("Module2", Cols));

        AssertDebugLog([new Vb6Value(2L)]);
    }

    [Fact]
    public async Task A_built_in_nobody_has_measured_in_a_bound_is_refused()
    {
        var run = () => Run("Dim d(Tan(1)) As Long\n");

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*d's bounds call Tan*");
    }

    [Fact]
    public async Task A_forms_run_refuses_an_array_sized_by_a_Const_naming_the_module_and_the_array()
    {
        // A form's run enters through ExecuteSub and never runs the module's declarations (#256), so the array would
        // stay unallocated and its first use would be a trappable 9.
        var vb = new BasicInterpreter(new NoLibrary(), context, rootEnv,
            "Private Const GRID = 10\nPrivate mBoard(0 To GRID - 1) As Long\nSub Form_Load()\n    mBoard(0) = 1\nEnd Sub\n", "Form1");

        var run = () => vb.ExecuteSub("Form_Load", null, true);

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*Form1's mBoard is sized by a constant*#256*");
    }

    [Fact]
    public async Task A_forms_run_with_only_literal_bounds_is_not_refused()
    {
        var vb = new BasicInterpreter(new NoLibrary(), context, rootEnv,
            "Private mBoard(0 To 9) As Long\nSub Form_Load()\n    mBoard(9) = 1\nEnd Sub\n", "Form1");

        var run = () => vb.ExecuteSub("Form_Load", null, true);

        await run.Should().NotThrowAsync();
    }

    private sealed class NoLibrary : IBasicStandardLibrary
    {
        public Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon) =>
            Task.FromResult(default(MessageBoxResult));
        public Task<string?> InputBox(string prompt, string? title, string defaultText) => Task.FromResult<string?>(null);
        public void DebugPrint(Vb6Value value) { }
    }
}
