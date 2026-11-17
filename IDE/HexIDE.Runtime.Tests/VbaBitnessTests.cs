using HexIDE.Language;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Interpreter.ConditionalCompilation;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Tests;

// Measurements: https://github.com/hexide-io/HexIDE/issues/4245, Excel x64 rows K/P1/P2 (PR #3965)
// and the issue's vb6.exe native/p-code probes. 32-bit VBA7 rows: #4399 (oracle PR #4398).
public class VbaBitnessTests : BaseVBTestFixture
{
    private void Target(VbaBitness bitness, string? arguments = null)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Test") { VbaBitness = bitness };
        if (arguments is not null)
            project.UnknownPreSectionLines.Add((0, $"CondComp=\"{arguments}\""));
        Compilation = CompilationConstants.ForProject(project);
    }

    [Theory]
    [InlineData(VbaBitness.None, "Win64", false)]
    [InlineData(VbaBitness.None, "VBA7", false)]
    [InlineData(VbaBitness.None, "Mac", false)]
    [InlineData(VbaBitness.Bit64, "Win64", true)]
    [InlineData(VbaBitness.Bit64, "VBA7", true)]
    [InlineData(VbaBitness.Bit64, "VBA6", true)]
    [InlineData(VbaBitness.Bit64, "Win32", true)]
    [InlineData(VbaBitness.Bit64, "Win16", false)]
    [InlineData(VbaBitness.Bit64, "Mac", false)]
    public async Task Project_selects_measured_branch(VbaBitness bitness, string constant, bool expected)
    {
        Target(bitness);
        await Run($"#If {constant} Then\nDebug.Print \"yes\"\n#Else\nDebug.Print \"no\"\n#End If\n");
        AssertDebugLog([new Vb6Value(expected ? "yes" : "no")]);
    }

    [Theory]
    [InlineData("VBA6", 1)]
    [InlineData("VBA7", 1)]
    [InlineData("Win32", 1)]
    [InlineData("Win64", 1)]
    [InlineData("Win16", 0)]
    [InlineData("Mac", 0)]
    public async Task Project_constants_hold_numbers(string name, int expected)
    {
        Target(VbaBitness.Bit64);
        await Run($"#If {name} = {expected} Then\nDebug.Print \"yes\"\n#Else\nDebug.Print \"no\"\n#End If\n");
        AssertDebugLog([new Vb6Value("yes")]);
    }

    [Fact]
    public async Task Project_argument_shadows_64_bit_builtin()
    {
        Target(VbaBitness.Bit64, "Win64 = 0");
        await Run("#If Win64 Then\nDebug.Print \"yes\"\n#Else\nDebug.Print \"no\"\n#End If\n");
        AssertDebugLog([new Vb6Value("no")]);
    }

    [Fact]
    public void Measured_32_bit_project_selects_its_builtins()
    {
        Target(VbaBitness.Bit32);
        Compilation!.BuiltIns.Should().BeSameAs(CompilationConstants.Vba32BuiltIns);
    }

    [Theory]
    [InlineData(VbaBitness.None, "PtrSafe ", "Expected: Sub or Function")]
    [InlineData(VbaBitness.Bit64, "", "Declare 'Tick' requires PtrSafe in 64-bit VBA7")]
    public async Task Active_incompatible_declare_refuses_at_load(VbaBitness bitness, string modifier, string message)
    {
        Target(bitness);
        Func<Task> load = () => Run($"Private Declare {modifier}Function Tick Lib \"kernel32\" () As Long\nDebug.Print \"ran\"\n");
        await load.Should().ThrowAsync<VBCompileErrorException>().WithMessage("*" + message);
        debug.Should().BeEmpty();
    }

    [Theory]
    [InlineData(VbaBitness.None, "PtrSafe ")]
    [InlineData(VbaBitness.Bit64, "")]
    public async Task Inactive_incompatible_declare_is_not_checked(VbaBitness bitness, string modifier)
    {
        Target(bitness);
        await Run($"#If 0 Then\nPrivate Declare {modifier}Function Tick Lib \"kernel32\" () As Long\n#End If\nDebug.Print \"ran\"\n");
        AssertDebugLog([new Vb6Value("ran")]);
    }

    [Fact]
    public void Interpreter_greying_uses_the_selected_64_bit_constants()
    {
        Target(VbaBitness.Bit64);
        var builtIns = Compilation!.BuiltIns.ToDictionary(pair => pair.Key, pair => (object?)(int)pair.Value.Value!);
        new InterpreterConditionalCode().InactiveLines("#If Win64 Then\nyes\n#Else\nno\n#End If\n",
            new ConditionalConstants(ConditionalConstants.NoArguments) { BuiltIns = builtIns })
            .Should().Contain(new LineRange(4, 4));
    }
}
