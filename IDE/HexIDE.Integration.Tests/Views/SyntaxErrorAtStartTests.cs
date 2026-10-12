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
/// A syntax error found before the run begins is a start that never became a run, and is reported as one (#759).
/// It was a message box that nothing recorded, so <c>run_project</c> answered that the program had run and ended.
/// </summary>
public class SyntaxErrorAtStartTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    private const string FormWithASyntaxError =
        "VERSION 5.00\r\n"
      + "Begin VB.Form Form1 \r\n"
      + "   Caption         =   \"Form1\"\r\n"
      + "   ClientHeight    =   3000\r\n"
      + "   ClientWidth     =   4000\r\n"
      + "End\r\n"
      + "Attribute VB_Name = \"Form1\"\r\n"
      + "Private Sub Form_Load()\r\n"
      + "    If Then\r\n"
      + "End Sub\r\n";

    private static (ProjectRunnerService Runner, IWindowManager Windows, DebugController Controller) Runner()
    {
        var windows = Substitute.For<IWindowManager>();
        var controller = new DebugController();
        var runner = new ProjectRunnerService(
            Substitute.For<IEventBus>(), windows, Substitute.For<IProjectManager>(),
            Substitute.For<ILocalizationService>(), controller, Substitute.For<IBreakpointService>(), new WatchService(),
            new RunScope());
        return (runner, windows, controller);
    }

    private static void AssertReportedAsAStartThatNeverRan(
        List<string> reported, ProjectRunnerService runner, IWindowManager windows, DebugController controller)
    {
        reported.Should().ContainSingle("the failure is reported once, before the start call returns")
            .Which.Should().StartWith("Compile error:");
        windows.DidNotReceiveWithAnyArgs().MessageBox(default!, default, default, default);
        runner.IsRunning.Should().BeFalse();
        runner.RunningProject.Should().BeNull("a project that never started must not stay claimed");
        controller.IsSessionActive.Should().BeFalse();
    }

    [AvaloniaFact]
    public void A_startup_form_with_a_syntax_error_is_a_failed_start()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddForm(new FormDeserializer().Deserialize(project, FormWithASyntaxError, NullSink.Instance)!);
        var (runner, windows, controller) = Runner();
        var reported = new List<string>();
        runner.StartFailed += reported.Add;

        runner.RunProject(project);

        AssertReportedAsAStartThatNeverRan(reported, runner, windows, controller);
    }

    [AvaloniaFact]
    public void A_Sub_Main_project_with_a_syntax_error_is_a_failed_start()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P") { StartsAtSubMain = true };
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode("Sub Main()\r\n    If Then\r\nEnd Sub\r\n");
        project.AddModule(module);
        var (runner, windows, controller) = Runner();
        var reported = new List<string>();
        runner.StartFailed += reported.Add;

        runner.RunProject(project);

        AssertReportedAsAStartThatNeverRan(reported, runner, windows, controller);
    }
}
