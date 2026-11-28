using Avalonia.Controls;
using Avalonia.Platform;
using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Runtime;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Running;

namespace HexIDE.Runtime.Integration.Tests.Debugging;

// #4245: go through the engine's real VBProgram construction, before the first statement runs.
public class VbaBitnessEngineTests
{
    [AvaloniaFact]
    public void Missing_PtrSafe_refuses_with_the_engines_own_compile_error_before_starting() => CheckDefaultFormIconRestored(() =>
    {
        // HexIDE's wording, not VBA's (unmeasured), and the engine's own English: the host supplies none (#3687).
        var result = Prepare(Substitute.For<IProgramHost>(), Project(VbaBitness.Bit64,
            "Private Declare Function Tick Lib \"kernel32\" () As Long\n"));
        result.Run.Should().BeNull();
        result.Failure!.Text.Should().Be("Compile error:\n\nDeclare 'Tick' requires PtrSafe in 64-bit VBA7");
    });

    [AvaloniaFact]
    public void Measured_32_bit_project_prepares_during_real_program_construction() => CheckDefaultFormIconRestored(() =>
    {
        var host = Substitute.For<IProgramHost>();
        host.NotImplementedYet.Returns("Not implemented: {0}");
        var result = Prepare(host, Project(VbaBitness.Bit32, ""));
        // #4399, Excel x86 W1–W6 (oracle PR #4398).
        result.Run.Should().NotBeNull();
        result.Failure.Should().BeNull();
    });

    // #5707: Prepare sets a process-wide default icon, even when the program fails to start.
    private static void CheckDefaultFormIconRestored(Action test)
    {
        using var stream = AssetLoader.Open(new Uri("avares://HexIDE.Runtime/form.ico"));
        var sentinel = new WindowIcon(stream);
        var previous = VBLoader.DefaultFormIcon;
        try
        {
            VBLoader.DefaultFormIcon = sentinel;
            test();
            VBLoader.DefaultFormIcon.Should().BeSameAs(sentinel);
        }
        finally
        {
            VBLoader.DefaultFormIcon = previous;
        }
    }

    private static RunStart Prepare(IProgramHost host, ProjectDefinition project)
    {
        var services = new RunEngineServices(host, Substitute.For<IProgramDialogWindows>(), () => false, () => false);
        var engine = new InterpreterRunEngine(services, new DebugController());
        var previous = VBLoader.DefaultFormIcon;
        try
        {
            return engine.Prepare(project);
        }
        finally
        {
            VBLoader.DefaultFormIcon = previous;
        }
    }

    private static ProjectDefinition Project(VbaBitness bitness, string declarations)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Test") { VbaBitness = bitness, StartsAtSubMain = true };
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode(declarations + "Sub Main()\nEnd Sub\n");
        project.AddModule(module);
        return project;
    }
}
