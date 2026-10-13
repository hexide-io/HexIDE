using HexIDE.IDE;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A module-level array whose bounds are not plain integers is sized where it is allocated, as its module's
/// declarations run. VB6 sizes it from a constant expression before the program starts, and every answer here was
/// measured with <c>vb6.exe /make</c>; the probe is in docs/vb6-fidelity-oracle.md, "What a module-level array's
/// bound may name". Nothing may size one from Empty: a bound HexIDE cannot read yet is refused, loudly.
/// </summary>
public class ModuleLevelArrayBoundTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("Const GRID = 10\nDim b(0 To GRID - 1, 0 To GRID - 1) As Long", "Debug.Print UBound(b, 1)\nDebug.Print UBound(b, 2)", 9L, 9L)]
    [InlineData("Const GRID = 10\nDim h(GRID \\ 3) As Long", "Debug.Print LBound(h)\nDebug.Print UBound(h)", 0L, 3L)]
    [InlineData("Enum Sizes\n    eMax = 4\nEnd Enum\nDim d(eMax) As Long", "Debug.Print LBound(d)\nDebug.Print UBound(d)", 0L, 4L)]
    [InlineData("Dim d(vbKeyA) As Long", "Debug.Print LBound(d)\nDebug.Print UBound(d)", 0L, 65L)]
    [InlineData("Dim d(Len(\"abc\")) As Long", "Debug.Print LBound(d)\nDebug.Print UBound(d)", 0L, 3L)]
    public async Task A_bound_of_constants_sizes_the_array(string declarations, string reads, long first, long second)
    {
        await Run($"{declarations}\n{reads}\n");

        AssertDebugLog([new Vb6Value(first), new Vb6Value(second)]);
    }

    [Theory]
    [InlineData("Dim d(N) As Long\nConst N = 3")]                 // a Const declared below the array
    [InlineData("Dim n As Long\nDim d(n) As Long")]               // a variable
    [InlineData("Dim d(Twice(2)) As Long\nFunction Twice(x)\n    Twice = x * 2\nEnd Function")]   // a user function
    public async Task A_bound_that_is_not_a_constant_expression_is_VB6s_compile_error(string declarations)
    {
        var run = () => Run(declarations + "\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Constant expression required*");
    }

    [Theory]
    [InlineData("Dim d(M) As Long")]
    [InlineData("Dim d(Module2.M) As Long")]
    [InlineData("Dim d(1 To M + 1) As Long")]
    public async Task A_bound_naming_another_modules_Const_is_refused_rather_than_sized_from_Empty(string declaration)
    {
        // VB6 gives 0 To 3, 0 To 3 and 1 To 4. HexIDE does not read another module's Const unqualified (#880), and
        // initialises modules in list order, so a size from it could be Empty's.
        var run = () => RunModules($"{declaration}\n", ("Module2", "Public Const M = 3\n"));

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*d's bounds name*#880*");
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
