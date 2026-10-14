using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
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
/// Restart ends the run and begins the next in one turn, and the box's answer arrives after both. By then the new run's
/// Reset had cleared IsAborting. OK/Cancel answered a Cancel nobody pressed, and the old Form_Load ran on beside the new
/// run; Yes/No faulted, and the fault stopped the new run.
/// </para>
/// <para>
/// Form_Load runs before the form is shown since #822, so the box opens over a hidden form, in a window of its own. It is
/// closed when its form closes, and ending the run closed only a form that had been shown, so the old box stayed up,
/// answerable (#1010 review). These tests check that it closes, and answer it if it does not, rather than assume.
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

    // Press the box's first button, as a person answering it would.
    private static void Answer(Window box)
    {
        var button = box.GetLogicalDescendants().OfType<Avalonia.Controls.Button>().First();
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    }

    private static void Pump()
    {
        for (var i = 0; i < 10; i++)
            Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData("MsgBox(\"Restore?\", vbOKCancel)")]
    [InlineData("MsgBox(\"Restore?\", vbYesNo)")]
    [InlineData("InputBox(\"Name?\")")]
    public void Restarting_with_Form_Loads_box_open_abandons_the_old_run_and_leaves_the_new_one_running(string box)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, Form, NullSink.Instance)!;
        form.UpdateCode(
            "Private Sub Form_Load()\r\n"
          + $"    Debug.Print \"answer \" & {box}\r\n"
          + "    Debug.Print \"after\"\r\n"
          + "End Sub\r\n");
        project.AddForm(form);

        // Every box the runs open, and whether it is still open.
        var boxes = new List<Window>();
        var closed = new HashSet<Window>();
        using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) =>
        {
            if (w.Content is HexIDE.Runtime.Dialogs.MessageBox or HexIDE.Runtime.Dialogs.InputBox)
                boxes.Add(w);
        });
        using var closing = Window.WindowClosedEvent.AddClassHandler<Window>((w, _) => closed.Add(w));

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

            boxes.Should().ContainSingle("the first run's Form_Load is waiting on its box");
            var oldBox = boxes[0];

            // Shift+F5 is a Normal-priority job, as a key press or a menu command is.
            Dispatcher.UIThread.Post(runner.RestartProject, DispatcherPriority.Normal);
            Pump();

            boxes.Where(b => !closed.Contains(b)).Should().ContainSingle("the new run's box is the only one open")
                .Which.Should().NotBeSameAs(oldBox);
            closed.Should().Contain(oldBox, "ending a run closes its box, so nobody can answer it");
            // Had it stayed open, a person could still answer it, and the ended run's Load would run on.
            if (!closed.Contains(oldBox))
            {
                Answer(oldBox);
                Pump();
            }

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
