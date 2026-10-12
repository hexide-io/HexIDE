using System.Collections.Generic;
using Avalonia.Headless.XUnit;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A start that never becomes a run is reported as one (#759): a syntax error in any module the run loads, no startup
/// form, and a Sub Main project with no usable Main or with two. Each was a message box nothing recorded, or arrived
/// as a run that ended, so <c>run_project</c> answered that the program had run.
/// </summary>
public class SyntaxErrorAtStartTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    private const string MustHaveStartupForm = "Must have startup form or Sub Main()";

    private static string FormWithLoad(string body) =>
        "VERSION 5.00\r\n"
      + "Begin VB.Form Form1 \r\n"
      + "   Caption         =   \"Form1\"\r\n"
      + "   ClientHeight    =   3000\r\n"
      + "   ClientWidth     =   4000\r\n"
      + "End\r\n"
      + "Attribute VB_Name = \"Form1\"\r\n"
      + "Private Sub Form_Load()\r\n"
      + body
      + "End Sub\r\n";

    private sealed record Harness(
        ProjectRunnerService Runner, IWindowManager Windows, DebugController Controller, List<string> Reported);

    private static Harness Runner(ProjectDefinition project)
    {
        var windows = Substitute.For<IWindowManager>();
        var controller = new DebugController();
        var projects = Substitute.For<IProjectManager>();
        projects.StartupProject.Returns(project);
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.ProjectRunner.MustHaveStartupForm").Returns(MustHaveStartupForm);
        var runner = new ProjectRunnerService(
            Substitute.For<IEventBus>(), windows, projects, localization, controller, Substitute.For<IBreakpointService>(),
            new WatchService(), new RunScope());
        var reported = new List<string>();
        runner.StartFailed += reported.Add;
        return new Harness(runner, windows, controller, reported);
    }

    private static ModuleDefinition AddModule(ProjectDefinition project, string name, ModuleKind kind, string code)
    {
        var module = new ModuleDefinition(project, name, kind);
        module.UpdateCode(code);
        project.AddModule(module);
        return module;
    }

    private static ProjectDefinition FormProject(string loadBody = "")
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddForm(new FormDeserializer().Deserialize(project, FormWithLoad(loadBody), NullSink.Instance)!);
        return project;
    }

    private static ProjectDefinition SubMainProject() => new(VBProjectType.EXE, "P") { StartsAtSubMain = true };

    /// <returns>The single failure reported, after checking that nothing ran or stayed claimed.</returns>
    private static string AssertAStartThatNeverRan(Harness h)
    {
        h.Windows.DidNotReceiveWithAnyArgs().MessageBox(default!, default, default, default);
        h.Runner.IsRunning.Should().BeFalse();
        h.Runner.RunningProject.Should().BeNull("a project that never started must not stay claimed");
        h.Controller.IsSessionActive.Should().BeFalse();
        return h.Reported.Should().ContainSingle("the failure is reported once, before the start call returns").Subject;
    }

    [AvaloniaFact]
    public void A_startup_form_with_a_syntax_error_is_a_failed_start_naming_the_form()
    {
        var project = FormProject("    If Then\r\n");
        var h = Runner(project);

        h.Runner.RunProject(project);

        AssertAStartThatNeverRan(h).Should().StartWith("Compile error:").And.Contain("in Form1, line ");
    }

    [AvaloniaFact]
    public void A_syntax_error_in_a_module_on_the_form_path_names_that_module()
    {
        // It was blamed on the startup form, whose code was fine, so the person fixed the wrong file.
        var project = FormProject();
        AddModule(project, "Helpers", ModuleKind.StandardModule, "Sub Help()\r\n    If Then\r\nEnd Sub\r\n");
        var h = Runner(project);

        h.Runner.RunProject(project);

        AssertAStartThatNeverRan(h).Should().StartWith("Compile error:").And.Contain("in Helpers, line 2");
    }

    [AvaloniaFact]
    public void A_Sub_Main_project_with_a_syntax_error_is_a_failed_start_naming_the_module()
    {
        var project = SubMainProject();
        AddModule(project, "Module1", ModuleKind.StandardModule, "Sub Main()\r\n    If Then\r\nEnd Sub\r\n");
        var h = Runner(project);

        h.Runner.RunProject(project);

        AssertAStartThatNeverRan(h).Should().StartWith("Compile error:").And.Contain("in Module1, line 2");
    }

    [AvaloniaFact]
    public void A_syntax_error_in_a_class_on_the_Sub_Main_path_names_the_class()
    {
        // Class modules were not checked here, so the error surfaced from the pre-pass with no module named.
        var project = SubMainProject();
        AddModule(project, "Module1", ModuleKind.StandardModule, "Sub Main()\r\nEnd Sub\r\n");
        AddModule(project, "Widget", ModuleKind.ClassModule, "Public Sub Spin()\r\n    If Then\r\nEnd Sub\r\n");
        var h = Runner(project);

        h.Runner.RunProject(project);

        AssertAStartThatNeverRan(h).Should().Contain("in Widget, line 2");
    }

    [AvaloniaFact]
    public void A_project_with_no_startup_form_is_a_failed_start()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var h = Runner(project);

        h.Runner.RunProject(project);

        AssertAStartThatNeverRan(h).Should().Be(MustHaveStartupForm);
    }

    [AvaloniaFact]
    public void A_Sub_Main_project_with_no_module_is_a_failed_start()
    {
        var project = SubMainProject();
        var h = Runner(project);

        h.Runner.RunProject(project);

        AssertAStartThatNeverRan(h).Should().Be(MustHaveStartupForm);
    }

    [AvaloniaTheory]
    [InlineData("Sub Other()\r\nEnd Sub\r\n", null, "Must have startup form or Sub Main()")]
    [InlineData("Function Main()\r\nEnd Function\r\n", null, "Must have startup form or Sub Main()")]
    [InlineData("Sub Main()\r\nEnd Sub\r\n", "Sub Main()\r\nEnd Sub\r\n", "Ambiguous name detected: Main")]
    public void A_Sub_Main_project_without_one_usable_Main_is_a_failed_start(string first, string? second, string error)
    {
        // These arrived as a run that ended, which run_project answered as a success.
        var project = SubMainProject();
        AddModule(project, "Module1", ModuleKind.StandardModule, first);
        if (second is not null)
            AddModule(project, "Module2", ModuleKind.StandardModule, second);
        var h = Runner(project);

        h.Runner.RunProject(project);

        AssertAStartThatNeverRan(h).Should().EndWith(error);
    }

    [AvaloniaFact]
    public void A_Run_To_Cursor_whose_start_fails_leaves_nothing_armed()
    {
        // It stayed armed, so the next plain run paused at that line as though at a breakpoint.
        var project = FormProject("    If Then\r\n");
        var h = Runner(project);

        h.Runner.RunToCursorProject(DocumentIdentity.For(project.StartupForm!), 3);

        AssertAStartThatNeverRan(h);
        h.Runner.HasPendingRunToCursor.Should().BeFalse();
    }
}
