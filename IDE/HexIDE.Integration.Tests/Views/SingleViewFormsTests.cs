using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HexIDE.Controls;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Utils;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The single-view host runs a program's forms in managed windows (#822). The startup form's window blocks the IDE behind
/// it; each other form's blocks nothing, so the forms stay usable beside one another. A single form runs as it did before
/// a run became a program of forms.
/// </summary>
public class SingleViewFormsTests
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

    private sealed class Run : IDisposable
    {
        public required ProjectRunnerService Runner { get; init; }
        public required MDIWindow? Shown { get; init; }
        /// <summary>The program's other forms' windows, shown beside the startup form's, by title.</summary>
        public required Dictionary<string, MDIWindow> Beside { get; init; }
        public required List<string> Printed { get; init; }
        public required List<string> Failed { get; init; }
        public required Action Detach { get; init; }

        public void Click() => Click(Shown!);

        public static void Click(MDIWindow window) => window.GetLogicalDescendants().OfType<VBCommandButton>().Single()
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        public void Dispose()
        {
            Detach();
            Runner.EndProject();
            Pump();
        }
    }

    private static Run Start(string form1Code, string? form2Code = null, string? mainCode = null)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddForm(new FormDeserializer().Deserialize(project, (Header("Form1") + form1Code).ReplaceLineEndings("\r\n"), new NullSink())!);
        if (form2Code is not null)
            project.AddForm(new FormDeserializer().Deserialize(project, (Header("Form2") + form2Code).ReplaceLineEndings("\r\n"), new NullSink())!);
        if (mainCode is not null)
        {
            var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
            module.UpdateCode(mainCode);
            project.AddModule(module);
            project.StartsAtSubMain = true;
        }

        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>() switch
        {
            "Str.ProjectRunner.StartupFormFailedToLoad" => "Form '{0}' failed: {1}",
            var key => key + ": {0}",
        });
        MDIWindow? shown = null;
        var windows = Substitute.For<IWindowManager>();
        // The managed window stays up until the run closes it, through the command the real window manager gives it.
        var closed = new TaskCompletionSource();
        windows.ShowManagedWindow(Arg.Do<MDIWindow>(w =>
        {
            shown = w;
            w.CloseCommand = new DelegateCommand(() => closed.TrySetResult(), () => true);
        })).Returns(closed.Task);
        // Another form's window, which blocks nothing behind it, stays up until the run closes it too.
        var beside = new Dictionary<string, MDIWindow>();
        windows.ShowManagedWindow(Arg.Any<MDIWindow>(), false).Returns(call =>
        {
            var w = call.Arg<MDIWindow>();
            beside[w.Title] = w;
            var gone = new TaskCompletionSource();
            w.CloseCommand = new DelegateCommand(() => gone.TrySetResult(), () => true);
            return gone.Task;
        });
        var runner = new ProjectRunnerService(
            Substitute.For<IEventBus>(), windows, Substitute.For<IProjectManager>(),
            localization, new DebugController(), Substitute.For<IBreakpointService>(), new WatchService(), new RunScope());

        var printed = new List<string>();
        var failed = new List<string>();
        Action<string> print = printed.Add;
        VBDebugConsole.Output += print;
        runner.StartFailed += failed.Add;
        runner.RunFailed += failed.Add;
        var wasSingleView = HexIDE.Static.SingleView;
        HexIDE.Static.SingleView = true;
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
            Shown = shown,
            Beside = beside,
            Printed = printed,
            Failed = failed,
            Detach = () => VBDebugConsole.Output -= print,
        };
    }

    private static void Pump()
    {
        for (var i = 0; i < 20; i++)
            Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void A_single_form_starts_handles_its_events_and_ends_its_run_with_Unload_Me_as_before()
    {
        using var run = Start("""
            Private Sub Form_Initialize()
                Debug.Print "initialize"
            End Sub
            Private Sub Form_Load()
                Debug.Print "load"
            End Sub
            Private Sub Command1_Click()
                Debug.Print "click"
                Unload Me
                Debug.Print "after unload"
            End Sub
            Private Sub Form_Terminate()
                Debug.Print "terminate"
            End Sub
            """);

        run.Shown.Should().NotBeNull("the form is shown in a managed window");
        run.Click();
        Pump();

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal("initialize", "load", "click", "after unload", "terminate");
        run.Runner.IsRunning.Should().BeFalse("the form was the only one loaded");
    }

    private const string Form2Logs = """
        Public pub As Integer
        Private Sub Form_Initialize()
            Debug.Print "f2 initialize"
        End Sub
        Private Sub Form_Load()
            Debug.Print "f2 load"
        End Sub
        Private Sub Command1_Click()
            Debug.Print "f2 click"
            Unload Me
        End Sub

        """;

    [AvaloniaFact]
    public void Another_form_is_shown_beside_the_startup_form_blocking_nothing()
    {
        using var run = Start("""
            Private Sub Command1_Click()
                Form2.Show
                Debug.Print "shown"
            End Sub

            """, Form2Logs);

        run.Click();
        Pump();

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal("f2 initialize", "f2 load", "shown");
        run.Beside.Should().ContainKey("Form2").WhoseValue.IsVisible.Should().BeTrue();

        Run.Click(run.Beside["Form2"]);
        Pump();

        run.Printed.Should().Equal("f2 initialize", "f2 load", "shown", "f2 click");
        run.Runner.IsRunning.Should().BeTrue("Form1 is still loaded");
    }

    [AvaloniaTheory]
    [InlineData("Form2.pub = 1", new[] { "f2 initialize" })]
    [InlineData("Load Form2", new[] { "f2 initialize", "f2 load" })]
    public void A_use_of_another_form_makes_or_loads_it_as_on_the_desktop(string use, string[] raised)
    {
        using var run = Start($"Private Sub Command1_Click()\n    {use}\n    Debug.Print \"after\"\nEnd Sub\n", Form2Logs);

        run.Click();
        Pump();

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal([.. raised, "after"]);
        run.Beside.Should().BeEmpty("neither use shows the form");
    }

    [AvaloniaFact]
    public void Sub_Main_shows_a_form_and_returns_and_the_run_lasts_until_the_form_unloads()
    {
        using var run = Start("""
            Private Sub Form_Load()
                Debug.Print "f1 load"
            End Sub
            Private Sub Command1_Click()
                Unload Me
            End Sub

            """, mainCode: """
            Sub Main()
                Debug.Print "main start"
                Form1.Show
                Debug.Print "main end"
            End Sub

            """);

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal("main start", "f1 load", "main end");
        run.Runner.IsRunning.Should().BeTrue("Form1 is loaded, though Main has returned (measured)");

        Run.Click(run.Beside["Form1"]);
        Pump();

        run.Failed.Should().BeEmpty();
        run.Runner.IsRunning.Should().BeFalse("the last form has unloaded and Main has returned");
    }

    [AvaloniaFact]
    public void Show_vbModal_disables_the_startup_form_until_the_modal_form_unloads()
    {
        using var run = Start("""
            Private Sub Command1_Click()
                Form2.Show vbModal
                Debug.Print "after the modal, enabled " & Me.Enabled
            End Sub

            """, Form2Logs);

        run.Click();
        Pump();

        run.Shown!.IsEnabled.Should().BeFalse();
        run.Beside["Form2"].IsEnabled.Should().BeTrue();

        Run.Click(run.Beside["Form2"]);
        Pump();

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal("f2 initialize", "f2 load", "f2 click", "after the modal, enabled True");
        run.Shown.IsEnabled.Should().BeTrue();
    }
}
