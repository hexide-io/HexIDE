using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.DebugAdapter;

// #4245: go through the engine's real VBProgram construction, before the first statement runs.
public class VbaBitnessEngineTests
{
    [AvaloniaFact]
    public void Missing_PtrSafe_refuses_with_the_engines_own_compile_error_before_starting()
    {
        // HexIDE's wording, not VBA's (unmeasured), and the engine's own English: the host supplies none (#3687).
        var result = Engine(Substitute.For<IProgramHost>()).Prepare(Project(VbaBitness.Bit64,
            "Private Declare Function Tick Lib \"kernel32\" () As Long\n"));
        result.Run.Should().BeNull();
        result.Failure!.Text.Should().Be("Compile error:\n\nDeclare 'Tick' requires PtrSafe in 64-bit VBA7");
    }

    [AvaloniaFact]
    public void Measured_32_bit_project_prepares_during_real_program_construction()
    {
        var host = Substitute.For<IProgramHost>();
        host.NotImplementedYet.Returns("Not implemented: {0}");
        var result = Engine(host).Prepare(Project(VbaBitness.Bit32, ""));
        // #4399, Excel x86 W1–W6 (oracle PR #4398).
        result.Run.Should().NotBeNull();
        result.Failure.Should().BeNull();
    }

    private static InterpreterRunEngine Engine(IProgramHost host) =>
        new(host, new DebugController());

    private static ProjectDefinition Project(VbaBitness bitness, string declarations)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Test") { VbaBitness = bitness, StartsAtSubMain = true };
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode(declarations + "Sub Main()\nEnd Sub\n");
        project.AddModule(module);
        return project;
    }
}
