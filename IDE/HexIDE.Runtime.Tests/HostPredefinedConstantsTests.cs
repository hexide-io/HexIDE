using System.Runtime.InteropServices;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Interpreter.ConditionalCompilation;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Tests;

// #5036 installation flag; recorded table from #4890.
// #4821: RDCore.SDK PredefinedConditionalConstants.cs (Of), SDK 0.1.11-alpha (#4890), and Excel for Mac 16.115 measurements.
// https://github.com/hexide-io/HexIDE/issues/4821
public class HostPredefinedConstantsTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("WINDOWS", nameof(ProjectTarget.RDCore), false, "0,1,1,0,0,1,1,1")]
    [InlineData("WINDOWS", nameof(ProjectTarget.RDCore), true, "0,1,1,0,0,1,1,1")]
    [InlineData("OSX", nameof(ProjectTarget.RDCore), false, "0,0,1,1,0,1,1,1")]
    [InlineData("OSX", nameof(ProjectTarget.RDCore), true, "0,0,0,1,0,1,1,1")]
    [InlineData("LINUX", nameof(ProjectTarget.RDCore), false, "0,0,0,0,1,1,1,1")]
    [InlineData("LINUX", nameof(ProjectTarget.RDCore), true, "0,0,0,0,1,1,1,1")]
    [InlineData("OSX", nameof(ProjectTarget.None), false, "0,0,1,1,0,0,1,1")]
    [InlineData("OSX", nameof(ProjectTarget.None), true, "0,0,1,1,0,0,1,1")]
    public async Task Target_and_host_select_the_recorded_row(string host, string targetName, bool windowsOnly, string row)
    {
        var target = Enum.Parse<ProjectTarget>(targetName);
        Compilation = CompilationConstants.ForProject(new ProjectDefinition(VBProjectType.EXE, "Test")
            { Target = target, VbaBitness = VbaBitness.Bit64 }, OSPlatform.Create(host), windowsOnly);
        var names = new[] { "Win16", "Win32", "Win64", "Mac", "Linux", "Is64BitEnv", "VBA6", "VBA7" };
        var values = row.Split(',');
        for (var i = 0; i < names.Length; i++)
            await Run($"#If {names[i]} = {values[i]} Then\nDebug.Print \"{names[i]}\"\n#Else\nDebug.Print \"wrong\"\n#End If\n");
        AssertDebugLog(names.Select(name => new Vb6Value(name)).ToList());
        Compilation.BuiltIns.Should().NotContainKey("DEBUG");
    }

    [Theory]
    [InlineData("WINDOWS")]
    [InlineData("OSX")]
    [InlineData("LINUX")]
    public void RDCore_defines_all_eight_platform_names(string host)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Test") { Target = ProjectTarget.RDCore, VbaBitness = VbaBitness.Bit64 };
        CompilationConstants.ForProject(project, OSPlatform.Create(host)).BuiltIns.Keys.Should()
            .BeEquivalentTo("Win16", "Win32", "Win64", "Mac", "Linux", "Is64BitEnv", "VBA6", "VBA7");
    }

    // Retired in SDK 0.1.11-alpha; project definitions still override absence (#4890).
    [Theory]
    [InlineData("WINDOWS", false)]
    [InlineData("OSX", false)]
    [InlineData("LINUX", false)]
    [InlineData("WINDOWS", true)]
    [InlineData("OSX", true)]
    [InlineData("LINUX", true)]
    public async Task Linux64_is_undefined_unless_the_project_defines_it(string host, bool defined)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Test") { Target = ProjectTarget.RDCore };
        if (defined) project.UnknownPreSectionLines.Add((0, "CondComp=\"Linux64 = 1\""));
        Compilation = CompilationConstants.ForProject(project, OSPlatform.Create(host));
        Compilation.BuiltIns.Should().NotContainKey("Linux64");
        await Run("#If Linux64 Then\nDebug.Print 1\n#Else\nDebug.Print 0\n#End If");
        AssertDebugLog([new Vb6Value(defined ? 1 : 0)]);
    }

    [Fact]
    public void Mac_VBA7_leaves_Linux_names_undefined()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Test") { VbaBitness = VbaBitness.Bit64 };
        CompilationConstants.ForProject(project, OSPlatform.OSX).BuiltIns.Keys.Should()
            .BeEquivalentTo("Win16", "Win32", "Win64", "Mac", "VBA6", "VBA7");
    }

    [Theory]
    [InlineData("OSX", nameof(ProjectTarget.RDCore), false, "Declare")]
    [InlineData("OSX", nameof(ProjectTarget.RDCore), true, "Timer")]
    [InlineData("WINDOWS", nameof(ProjectTarget.RDCore), false, "Declare")]
    [InlineData("WINDOWS", nameof(ProjectTarget.RDCore), true, "Declare")]
    [InlineData("LINUX", nameof(ProjectTarget.RDCore), false, "Timer")]
    [InlineData("LINUX", nameof(ProjectTarget.RDCore), true, "Timer")]
    [InlineData("OSX", nameof(ProjectTarget.None), false, "Declare")]
    [InlineData("OSX", nameof(ProjectTarget.None), true, "Declare")]
    public async Task NavalBattle_pause_selects_the_target_branch(string host, string targetName, bool windowsOnly, string expected)
    {
        var target = Enum.Parse<ProjectTarget>(targetName);
        Compilation = CompilationConstants.ForProject(new ProjectDefinition(VBProjectType.EXE, "NavalBattle")
            { Target = target, VbaBitness = VbaBitness.Bit64 }, OSPlatform.Create(host), windowsOnly);
        await Run("""
            #If Win32 Or Win64 Then
            Private Declare PtrSafe Sub Sleep Lib "kernel32" (ByVal milliseconds As Long)
            #Else
            Private Sub Pause()
                Dim started As Single
                started = Timer
                Do While Timer - started < 0
                Loop
                Debug.Print "Timer"
            End Sub
            #End If
            Sub Main()
            #If Win32 Or Win64 Then
                Debug.Print "Declare"
            #Else
                Pause
            #End If
            End Sub
            Main
            """);
        AssertDebugLog([new Vb6Value(expected)]);
    }

    // Shadowing is measured in #4821 on vb6.exe SP6; the table being shadowed does not change precedence.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Program_definitions_shadow_RDCore_constants(bool module)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Test") { Target = ProjectTarget.RDCore };
        project.UnknownPreSectionLines.Add((0, "CondComp=\"Mac = 0\""));
        Compilation = CompilationConstants.ForProject(project, OSPlatform.OSX);
        await Run((module ? "#Const Mac = 1\n" : "") +
            "#If Mac Then\nDebug.Print 1\n#Else\nDebug.Print 0\n#End If\n");
        AssertDebugLog([new Vb6Value(module ? 1 : 0)]);
    }

    [Theory]
    [InlineData("WINDOWS", VbaBitness.None)]
    [InlineData("OSX", VbaBitness.None)]
    [InlineData("LINUX", VbaBitness.None)]
    [InlineData("WINDOWS", VbaBitness.Bit32)]
    [InlineData("OSX", VbaBitness.Bit32)]
    [InlineData("LINUX", VbaBitness.Bit32)]
    [InlineData("WINDOWS", VbaBitness.Bit64)]
    [InlineData("LINUX", VbaBitness.Bit64)]
    public void Other_targets_retain_their_tables(string host, VbaBitness bitness)
    {
        var expected = bitness switch
        {
            VbaBitness.Bit32 => CompilationConstants.Vba32BuiltIns,
            VbaBitness.Bit64 => CompilationConstants.Vba64BuiltIns,
            _ => CompilationConstants.Vb6BuiltIns,
        };
        CompilationConstants.ForProject(new ProjectDefinition(VBProjectType.EXE, "Test") { VbaBitness = bitness },
            OSPlatform.Create(host), true).BuiltIns.Should().BeSameAs(expected);
    }

    [Fact]
    public void Default_selection_uses_the_current_host()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Test") { Target = ProjectTarget.RDCore };
        var expected = OperatingSystem.IsWindows() ? OSPlatform.Windows : OperatingSystem.IsMacOS() ? OSPlatform.OSX : OSPlatform.Linux;
        var selected = CompilationConstants.ForProject(project).BuiltIns;
        selected.Should().BeSameAs(CompilationConstants.ForProject(project, expected).BuiltIns);
        selected.Keys.Should().Contain("Linux").And.Contain("Is64BitEnv");
        selected["Linux"].Value.Should().Be(OperatingSystem.IsLinux() ? 1 : 0);
        selected["Win32"].Value.Should().Be(OperatingSystem.IsWindows() ? 1 : 0);
        selected["Mac"].Value.Should().Be(OperatingSystem.IsMacOS() ? 1 : 0);
    }
}
