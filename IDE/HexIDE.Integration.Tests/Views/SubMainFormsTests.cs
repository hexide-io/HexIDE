using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A program that starts at <c>Sub Main</c> and shows forms (#822, slice 2a.2), run on the desktop as F5 runs it. Main is the
/// program's first walk, its forms are made where their names are first used, and the run lasts until Main has returned
/// and no form is loaded. Measured on vb6.exe, native and p-code agreeing: the oracle's <i>Several forms</i>, measurement 7,
/// and <i>A form as an object</i> for the order of Main's own lines.
/// </summary>
public class SubMainFormsTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private static string Header(string name) =>
        "VERSION 5.00\r\n"
      + $"Begin VB.Form {name} \r\n"
      + $"   Caption         =   \"{name}\"\r\n"
      + "   ClientHeight    =   3000\r\n"
      + "   ClientWidth     =   4000\r\n"
      + "   Begin VB.CommandButton Command1 \r\n"
      + "      Caption         =   \"Command1\"\r\n"
      + "      Height          =   495\r\n"
      + "      Left            =   120\r\n"
      + "      Top             =   120\r\n"
      + "      Width           =   1215\r\n"
      + "   End\r\n"
      + "End\r\n"
      + $"Attribute VB_Name = \"{name}\"\r\n";

    // Form1 logs its lifecycle and unloads itself when its button is clicked.
    private const string Form1Code = """
        Private Sub Form_Initialize()
            Debug.Print "f1 initialize"
        End Sub
        Private Sub Form_Load()
            Debug.Print "f1 load"
        End Sub
        Private Sub Form_Unload(Cancel As Integer)
            Debug.Print "f1 unload"
        End Sub
        Private Sub Form_Terminate()
            Debug.Print "f1 terminate"
        End Sub
        Private Sub Command1_Click()
            Unload Me
        End Sub

        """;

    private sealed class Run : IDisposable
    {
        public required ProjectRunnerService Runner { get; init; }
        public required List<string> Printed { get; init; }
        public required List<string> Failed { get; init; }
        public required Dictionary<string, VBFormRuntime> Opened { get; init; }
        public required HashSet<string> Closed { get; init; }
        public required Action Detach { get; init; }

        public void Click(string form) => Opened[form].GetLogicalDescendants().OfType<VBCommandButton>().Single()
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        public void Dispose()
        {
            Runner.EndProject();
            Pump();
            Detach();
        }
    }

    private static Run Start(string mainCode, string form1Code = Form1Code)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddForm(new FormDeserializer().Deserialize(project, (Header("Form1") + form1Code).ReplaceLineEndings("\r\n"), new NullSink())!);
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode(mainCode);
        project.AddModule(module);
        project.StartsAtSubMain = true;

        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>() + ": {0}");
        var runner = new ProjectRunnerService(
            Substitute.For<IEventBus>(), Substitute.For<IWindowManager>(), Substitute.For<IProjectManager>(),
            localization, new DebugController(), Substitute.For<IBreakpointService>(), new WatchService(), new RunScope());

        var printed = new List<string>();
        var failed = new List<string>();
        var opened = new Dictionary<string, VBFormRuntime>();
        var closed = new HashSet<string>();
        Action<string> print = printed.Add;
        VBDebugConsole.Output += print;
        runner.StartFailed += failed.Add;
        runner.RunFailed += failed.Add;
        var watch = Window.WindowOpenedEvent.AddClassHandler<VBFormRuntime>((w, _) =>
        {
            opened[w.Title ?? ""] = w;
            w.Closed += (_, _) => closed.Add(w.Title ?? "");
        });
        var wasSingleView = HexIDE.Static.SingleView;
        HexIDE.Static.SingleView = false;
        try
        {
            runner.RunProject(project);
        }
        finally
        {
            HexIDE.Static.SingleView = wasSingleView;
        }
        Pump();
        return new Run
        {
            Runner = runner,
            Printed = printed,
            Failed = failed,
            Opened = opened,
            Closed = closed,
            Detach = () =>
            {
                watch.Dispose();
                VBDebugConsole.Output -= print;
            },
        };
    }

    private static void Pump()
    {
        for (var i = 0; i < 20; i++)
            Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Main_shows_a_form_and_returns_and_the_run_lasts_until_the_form_unloads()
    {
        using var run = Start("""
            Sub Main()
                Debug.Print "main start"
                Form1.Show
                Debug.Print "main end"
            End Sub

            """);

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal("main start", "f1 initialize", "f1 load", "main end");
        run.Opened["Form1"].IsVisible.Should().BeTrue();
        run.Runner.IsRunning.Should().BeTrue("Form1 is loaded, though Main has returned (measured)");

        run.Click("Form1");
        Pump();

        run.Failed.Should().BeEmpty();
        // The default instance's Terminate comes at the run's end (measured).
        run.Printed.Should().Equal("main start", "f1 initialize", "f1 load", "main end", "f1 unload", "f1 terminate");
        run.Runner.IsRunning.Should().BeFalse();
        run.Closed.Should().Contain("Form1");
    }

    [AvaloniaFact]
    public void Main_loading_a_form_hidden_and_returning_leaves_the_run_going()
    {
        using var run = Start("""
            Sub Main()
                Load Form1
                Debug.Print "main end, visible " & Form1.Visible
            End Sub

            """);

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal("f1 initialize", "f1 load", "main end, visible False");
        run.Runner.IsRunning.Should().BeTrue("a hidden form is loaded (measured)");
    }

    [AvaloniaFact]
    public void Main_showing_a_form_modally_waits_and_goes_on_once_it_has_unloaded()
    {
        using var run = Start("""
            Sub Main()
                Form1.Show vbModal
                Debug.Print "main after the modal"
            End Sub

            """);

        run.Printed.Should().Equal("f1 initialize", "f1 load");
        run.Runner.IsRunning.Should().BeTrue();

        run.Click("Form1");
        Pump();

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal("f1 initialize", "f1 load", "f1 unload", "main after the modal", "f1 terminate");
        run.Runner.IsRunning.Should().BeFalse("Main has returned with no form loaded (measured)");
    }

    [AvaloniaFact]
    public void Main_with_no_form_ends_when_it_returns()
    {
        using var run = Start("""
            Sub Main()
                Debug.Print "main"
            End Sub

            """);

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal("main");
        run.Runner.IsRunning.Should().BeFalse();
        run.Opened.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void End_in_a_form_Main_showed_ends_the_run_and_closes_its_window_with_no_Terminate()
    {
        using var run = Start("""
            Sub Main()
                Form1.Show
            End Sub

            """, Form1Code.Replace("""
            Private Sub Command1_Click()
                Unload Me
            End Sub
            """, """
            Private Sub Command1_Click()
                End
            End Sub
            """));

        run.Click("Form1");
        Pump();

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal("f1 initialize", "f1 load");
        run.Runner.IsRunning.Should().BeFalse();
        run.Closed.Should().Contain("Form1");
    }

    [AvaloniaFact]
    public void A_fault_in_a_form_Main_showed_ends_the_run_and_is_reported()
    {
        using var run = Start("""
            Sub Main()
                Form1.Show
            End Sub

            """, Form1Code.Replace("""
            Private Sub Command1_Click()
                Unload Me
            End Sub
            """, """
            Private Sub Command1_Click()
                Command1.SetFocus
            End Sub
            """));

        run.Click("Form1");
        Pump();

        run.Failed.Should().ContainSingle().Which.Should().Contain("SetFocus");
        run.Runner.IsRunning.Should().BeFalse();
        run.Closed.Should().Contain("Form1");
    }
}
