using System;
using System.Collections.Generic;
using Avalonia.Threading;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A message box open in Form_Load when the run is restarted belongs to the run that ended (#820).
///
/// <para>
/// Restart ends the run and begins the next in one turn, and the box's answer arrives after both: Form_Load runs at
/// Loaded priority, so its continuation is posted rather than run inline. By then the new run's Reset had cleared
/// IsAborting. OK/Cancel answered a Cancel nobody pressed, and the old Form_Load ran on beside the new run; Yes/No
/// faulted, and the fault stopped the new run.
/// </para>
/// </summary>
public class MessageBoxAcrossRestartTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    private const string Form =
        "VERSION 5.00\r\n"
      + "Begin VB.Form Form1 \r\n"
      + "   Caption         =   \"MessageBoxAcrossRestartTests\"\r\n"
      + "   ClientHeight    =   3000\r\n"
      + "   ClientWidth     =   4000\r\n"
      + "End\r\n"
      + "Attribute VB_Name = \"Form1\"\r\n";

    private static void Pump()
    {
        for (var i = 0; i < 10; i++)
            Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData("vbOKCancel")]
    [InlineData("vbYesNo")]
    public void Restarting_with_Form_Loads_box_open_abandons_the_old_run_and_leaves_the_new_one_running(string style)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, Form, NullSink.Instance)!;
        form.UpdateCode(
            "Private Sub Form_Load()\r\n"
          + $"    Debug.Print \"answer \" & MsgBox(\"Restore?\", {style})\r\n"
          + "    Debug.Print \"after\"\r\n"
          + "End Sub\r\n");
        project.AddForm(form);

        var projects = Substitute.For<IProjectManager>();
        projects.StartupProject.Returns(project);
        var controller = new DebugController { TimeSliceMs = null };
        var runner = new ProjectRunnerService(
            Substitute.For<IEventBus>(), Substitute.For<IWindowManager>(), projects, Substitute.For<ILocalizationService>(),
            controller, Substitute.For<IBreakpointService>(), new WatchService(), new RunScope());
        var failed = new List<string>();
        runner.StartFailed += failed.Add;
        runner.RunFailed += failed.Add;
        var printed = new List<string>();
        Action<string> sink = printed.Add;
        VBDebugConsole.Output += sink;
        try
        {
            runner.RunStartupProject();
            Pump();
            runner.IsRunning.Should().BeTrue("Form_Load is waiting on its box");

            // Shift+F5 is a Normal-priority job, as a key press or a menu command is.
            Dispatcher.UIThread.Post(runner.RestartProject, DispatcherPriority.Normal);
            Pump();

            printed.Should().BeEmpty("the ended run's Form_Load must not carry on, and the new one is waiting on its box");
            failed.Should().BeEmpty("nothing faulted: a box closed by ending its run is not a fault");
            runner.IsRunning.Should().BeTrue("the new run is still going");
            controller.IsSessionActive.Should().BeTrue();
        }
        finally
        {
            runner.EndProject();
            Pump();
            VBDebugConsole.Output -= sink;
        }
    }
}
