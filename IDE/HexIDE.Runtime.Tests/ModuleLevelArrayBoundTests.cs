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
    [InlineData("Dim d(1 To M + 1) As Long")]
    public async Task A_bound_reading_another_modules_Const_is_refused_rather_than_sized_from_Empty(string declarations)
    {
        // VB6 gives 0 To 3 and 1 To 4. HexIDE does not read another module's Const unqualified
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
    public async Task A_module_Const_reading_another_modules_Const_is_refused_rather_than_read_as_Empty(string code)
    {
        // VB6 gives 4 (measured). HexIDE gave 1: M unqualified is not found (#880).
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
    public async Task A_Const_reading_a_module_whose_declarations_have_not_run_is_refused()
    {
        // Measured: Module2's `Public d(C)`, with `Const C = Module1.M + 1`, is 0 To 4, in either list order. HexIDE
        // initialises the primary module last, so M would still be Empty there.
        var run = () => RunModules("Public Const M = 3\n", ("Module2", "Const C = Module1.M + 1\nPublic d(C) As Long\n"));

        (await run.Should().ThrowAsync<NotImplementedException>())
            .WithMessage("*Const C's value reads Module1.M, a Public Const of Module1, whose declarations have not run yet*#893*");
    }

    [Theory]
    [InlineData("Dim d(Module2.M) As Long\nDebug.Print UBound(d)", 3L)]
    [InlineData("Const C = Module2.M + 1\nDebug.Print C", 4L)]
    public async Task Another_modules_Const_qualified_is_read_once_that_module_has_run(string code, long expected)
    {
        // Measured: 0 To 3 and 4, in either list order. Module2 runs before the primary, so its value is final.
        await RunModules(code + "\n", ("Module2", "Public Const M = 3\n"));

        debug.Should().ContainSingle().Which.Value.Should().Match<object>(v => Convert.ToInt64(v) == expected);
    }

    [Fact]
    public async Task A_class_Const_reads_the_startup_modules_Const_once_its_declarations_have_run()
    {
        // Measured, native and in the IDE: a class's `Private Const C = Module1.M + 1` is 4. The startup module runs
        // last, its body doubling as its declarations, so a class created after that reads M's final value.
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv,
            "Public Const M = 3\nSub Go()\n    Dim h As Holder\n    Set h = New Holder\n    Debug.Print h.GetC\nEnd Sub\n",
            "Module1", null, [("Holder", "Private Const C = Module1.M + 1\nPublic Function GetC()\n    GetC = C\nEnd Function\n")]);

        await vb.Execute();
        await vb.ExecuteSub("Go");

        debug.Should().ContainSingle().Which.Value.Should().Match<object>(v => Convert.ToInt64(v) == 4);
    }

    [Theory]
    [InlineData("Const A = 2\nConst A2 = Module1.A + 1")]
    [InlineData("Const A = Module1.B + 1\nConst B = 2")]       // a qualified forward reference is this error too
    [InlineData("Private n As Long\nConst C = Module1.n + 1")]   // whatever it names: a variable,
    [InlineData("Public n As Long\nDim d(Module1.n) As Long")]   // in a bound too,
    [InlineData("Const C = Module1.Twice(2)\n" + Twice)]          // a function,
    [InlineData("Const C = Module1.Nope + 1")]                     // or nothing at all (all measured)
    public async Task A_module_Const_naming_its_own_module_is_VB6s_circular_dependency(string code)
    {
        var run = () => RunModules(code + "\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Circular dependencies between modules*");
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

    [Theory]
    [InlineData("Private Const NL = VBA.vbCrLf\nDebug.Print Len(NL)", 2L)]           // a library-qualified constant
    [InlineData("Const K = KeyCodeConstants.vbKeyA\nDebug.Print K", 65L)]             // a container-qualified one
    [InlineData("Const K = VBRUN.KeyCodeConstants.vbKeyA\nDebug.Print K", 65L)]       // both
    [InlineData("Dim d(VBRUN.vbKeyA) As Long\nDebug.Print UBound(d)", 65L)]
    [InlineData("Const C = Module2.colMax + 1\nDebug.Print C", 3L)]                   // another module's Enum member
    [InlineData("Dim d(Module2.Cols.colMax) As Long\nDebug.Print UBound(d)", 2L)]     // the three-part form
    [InlineData("Const C = Project1.Module2.Cols.colMax\nDebug.Print C", 2L)]         // the project name in front
    [InlineData("Const C = Project1.Module2.M + 1\nDebug.Print C", 4L)]
    public async Task A_qualified_constant_main_read_is_still_read(string code, long expected)
    {
        // Measured: 2, 65, 65, 0 To 65, colMax + 1, 0 To colMax, colMax and 4. The check refused every qualified name
        // but `Module.PublicConst` and `Enum.member`, so these stopped the load where main read them.
        await RunModules(code + "\n", ("Module2", Cols + "Public Const M = 3\n"));

        debug.Should().ContainSingle().Which.Value.Should().Match<object>(v => Convert.ToInt64(v) == expected);
    }

    [Theory]
    [InlineData("Const K = KeyCodeConstants.vbKeyA", "Long")]
    [InlineData("Const C = Module2.colMax + 1", "Long")]                 // an Enum member is a Long
    [InlineData("Const C = Project1.Module2.Cols.colMax", "Long")]
    [InlineData("Const C = Project1.Module2.M + 1", "Integer")]          // M = 3 is an Integer, and so is M + 1
    public async Task A_qualified_constant_has_VB6s_type(string declaration, string typeName)
    {
        // Measured with /make, each value written with its TypeName.
        await RunModules($"{declaration}\nDebug.Print TypeName({declaration.Split(' ')[1]})\n", ("Module2", Cols + "Public Const M = 3\n"));

        AssertDebugLog([typeName]);
    }

    [Fact]
    public async Task A_classs_literal_range_with_no_values_is_Range_has_no_values()
    {
        // Measured with /make: `Private d(5 To 2)` in a class does not compile. It raised 9 at New.
        var run = () => RunClasses("Dim c As C1\nSet c = New C1\n", ("C1", "Private d(5 To 2) As Long\n"));

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Range has no values*");
    }

    [Theory]
    [InlineData(Sizes + "\nConst C = Module1.Sizes.eMax", "Circular dependencies between modules")]
    [InlineData("Const C = Sizes.eMax\n" + Sizes, "Constant expression required")]     // the Enum below
    [InlineData("Dim d(VBA.Len(\"ab\")) As Long", "Constant expression required")]    // a call through a qualifier
    [InlineData("Const C = Module2.pMax", "Method or data member not found")]          // a Private Enum's member
    [InlineData("Const C = KeyCodeConstants.vbCancel", "Method or data member not found")]   // another container's
    public async Task A_qualified_name_VB6_refuses_is_its_compile_error(string declarations, string error)
    {
        // Measured with /make, one project each.
        var run = () => RunModules(declarations + "\n", ("Module2", "Private Enum P\n    pMax = 4\nEnd Enum\n"));

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage($"*{error}*");
    }

    [Theory]
    [InlineData("Const S = \"abc\"\nDim d(Len(S)) As Long", 3L)]
    [InlineData("Const S As String = \"abcd\"\nDim d(Len(S)) As Long", 4L)]
    [InlineData("Const S = \"ab\"\nDim d(Len(S & \"c\")) As Long", 3L)]
    [InlineData("Dim d(Len(\"ab\" + \"c\")) As Long", 3L)]                 // a String added to a String is one
    [InlineData("Const S = \"ab\" + \"c\"\nDim d(Len(S)) As Long", 3L)]
    public async Task Len_of_a_String_constant_sizes_the_array(string declarations, long upper)
    {
        // Measured, with LenB("ab") 0 To 4 and LenB of "abc" 0 To 6 too. HexIDE has no LenB to size from yet.
        await Run($"{declarations}\nDebug.Print UBound(d)\n");

        AssertDebugLog([new Vb6Value(upper)]);
    }

    [Theory]
    [InlineData("Dim d(Len(12345)) As Long")]
    [InlineData("Dim d(LenB(12345)) As Long")]
    [InlineData("Const N1 = 12345\nDim d(Len(N1)) As Long")]
    [InlineData("Const N As Long = 5\nDim d(Len(N)) As Long")]
    [InlineData("Const F = 2.5\nDim d(Len(F)) As Long")]
    [InlineData("Const D = #1/2/2003#\nDim d(Len(D)) As Long")]
    [InlineData("Dim d(Len(\"ab\" + 1)) As Long")]                        // a String added to a number is not a String
    public async Task Len_of_a_constant_that_is_not_a_String_is_VB6s_Variable_required(string declarations)
    {
        // Measured with /make: "Variable required - can't assign to this expression". HexIDE sized each from the number
        // of characters the value prints as.
        var run = () => Run(declarations + "\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Variable required*");
    }

    [Fact]
    public async Task Another_modules_Public_Const_is_found_before_a_built_in_constant()
    {
        // Measured: Module2's `Public Const vbKeyA = 3` sizes Module1's `Dim d(vbKeyA)` 0 To 3, the project beating the
        // library. HexIDE sized it 0 To 65; it now finds the project's, which it cannot read unqualified yet (#880).
        var run = () => RunModules("Dim d(vbKeyA) As Long\nDebug.Print UBound(d)\n", ("Module2", "Public Const vbKeyA = 3\n"));

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*vbKeyA, another module's Public Const*#880*");
    }

    [Theory]
    [InlineData("Dim d(-1) As Long")]
    [InlineData("Const N = 0\nDim d(1 To N) As Long")]
    [InlineData("Const N = -1\nDim d(N) As Long")]
    [InlineData("Dim d(5, 2 To 1) As Long")]
    [InlineData("Dim d(5 To 2) As Long")]          // a literal range inverted by more: the pre-pass builds it, and raised 9
    [InlineData("Dim d(0 To -5) As Long")]
    [InlineData("Dim d(1, 5 To 2) As Long")]
    [InlineData("Go\nSub Go()\n    Dim d(5 To 2) As Long\nEnd Sub")]
    [InlineData("Go\nSub Go()\n    Dim d(-1) As Long\nEnd Sub")]                       // in a procedure too
    [InlineData("Go\nSub Go()\n    Const N = 0\n    Dim d(1 To N) As Long\nEnd Sub")]
    public async Task A_dimension_with_no_values_is_VB6s_Range_has_no_values(string code)
    {
        // Measured with /make, at module and procedure level. HexIDE built an array with no elements, so UBound read -1
        // and a loop over it ran no passes.
        var run = () => Run(code + "\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Range has no values*");
    }

    [Fact]
    public async Task A_dimension_of_one_element_is_not_empty()
    {
        await Run("Dim d(0 To 0) As Long\nDebug.Print UBound(d)\n");   // measured: 0 To 0

        AssertDebugLog([new Vb6Value(0L)]);
    }

    [Theory]
    [InlineData("Const A = eLater + 1\nEnum E\n    eLater = 5\nEnd Enum")]
    [InlineData("Dim d(eLater) As Long\nEnum E\n    eLater = 5\nEnd Enum")]
    [InlineData("Dim d(E.eLater) As Long\nEnum E\n    eLater = 5\nEnd Enum")]
    public async Task An_Enum_below_is_not_yet_a_constant(string declarations)
    {
        // Measured: "Constant expression required" for each. With the Enum first, A is 6 and d is 0 To 5.
        var run = () => Run(declarations + "\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Constant expression required*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Option Explicit\n")]
    public async Task Another_modules_Global_variable_is_a_compile_error(string option)
    {
        // Measured: "Constant expression required", with and without Option Explicit. Global was not counted as Public,
        // so with Option Explicit it was "Variable not defined".
        var run = () => RunModules($"{option}Dim d(n) As Long\n", ("Module2", "Global n As Long\n"));

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Constant expression required*");
    }

    private sealed class NoLibrary : IBasicStandardLibrary
    {
        public Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon) =>
            Task.FromResult(default(MessageBoxResult));
        public Task<string?> InputBox(string prompt, string? title, string defaultText) => Task.FromResult<string?>(null);
        public void DebugPrint(Vb6Value value) { }
    }
}
