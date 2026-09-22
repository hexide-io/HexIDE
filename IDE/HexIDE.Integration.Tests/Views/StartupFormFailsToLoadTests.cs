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
/// A startup form that cannot be built ends the start cleanly and says so (hexide-io/HexIDE#590).
///
/// <para>
/// Before, <c>VBLoader.RunForm</c> threw after the run had been claimed and the controller reset to
/// Running, and the exception reached only the log. The IDE was left claiming a project that was not
/// running, the debugger said Running, and every tool that starts a run answered success.
/// </para>
/// </summary>
public class StartupFormFailsToLoadTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    // A negative Width is what #589 lets through the designer tools; Avalonia refuses it when the control is
    // placed, which is the failure this test needs. Any exception while building the form takes the same path.
    private static string FormWithButtonWidth(int width) =>
        "VERSION 5.00\r\n"
      + "Begin VB.Form Form1 \r\n"
      + "   Caption         =   \"Form1\"\r\n"
      + "   ClientHeight    =   3000\r\n"
      + "   ClientWidth     =   4000\r\n"
      + "   Begin VB.CommandButton Command1 \r\n"
      + "      Caption         =   \"Command1\"\r\n"
      + "      Height          =   495\r\n"
      + "      Left            =   120\r\n"
      + "      Top             =   120\r\n"
      + $"      Width           =   {width}\r\n"
      + "   End\r\n"
      + "End\r\n"
      + "Attribute VB_Name = \"Form1\"\r\n";

    private static (ProjectRunnerService Runner, DebugController Controller, ProjectDefinition Project) Build(int width)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, FormWithButtonWidth(width), NullSink.Instance)!;
        project.AddForm(form);

        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.ProjectRunner.StartupFormFailedToLoad").Returns("Form '{0}' failed: {1}");

        var controller = new DebugController();
        var runner = new ProjectRunnerService(
            Substitute.For<IEventBus>(), Substitute.For<IWindowManager>(), Substitute.For<IProjectManager>(),
            localization, controller, Substitute.For<IBreakpointService>(), new WatchService(), new RunScope());
        return (runner, controller, project);
    }

    [AvaloniaFact]
    public void A_form_that_cannot_be_built_leaves_nothing_running_and_says_why()
    {
        var (runner, controller, project) = Build(width: -300);
        var reported = new List<string>();
        runner.StartFailed += reported.Add;

        runner.RunProject(project);

        reported.Should().ContainSingle("the failure is reported once, before the start call returns")
            .Which.Should().StartWith("Form 'Form1' failed: ").And.Contain("Width");
        runner.IsRunning.Should().BeFalse();
        runner.RunningProject.Should().BeNull("a project that never started must not stay claimed");
        controller.State.Should().Be(DebugState.Stopped);
        controller.IsSessionActive.Should().BeFalse();
    }

    [AvaloniaFact]
    public void A_form_that_builds_still_runs()
    {
        // The control: the catch must not swallow an ordinary start.
        var (runner, controller, project) = Build(width: 1215);
        var reported = new List<string>();
        runner.StartFailed += reported.Add;

        runner.RunProject(project);

        reported.Should().BeEmpty();
        runner.IsRunning.Should().BeTrue();
        runner.RunningProject.Should().BeSameAs(project);
        controller.IsSessionActive.Should().BeTrue();

        runner.EndProject();
        runner.IsRunning.Should().BeFalse();
    }
}
