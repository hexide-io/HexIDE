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
/// The single-view host runs one form, in a managed window, and so far only the startup form (#822): a program's other
/// forms come with slice 2a.2. Its single form runs as it did before a run became a program of forms, and a use of a
/// second form is refused, naming #822, rather than read wrongly.
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
        public required List<string> Printed { get; init; }
        public required List<string> Failed { get; init; }
        public required Action Detach { get; init; }

        public void Click() => Shown!.GetLogicalDescendants().OfType<VBCommandButton>().Single()
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        public void Dispose()
        {
            Detach();
            Runner.EndProject();
            Pump();
        }
    }

    private static Run Start(string form1Code, string? form2Code = null)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddForm(new FormDeserializer().Deserialize(project, (Header("Form1") + form1Code).ReplaceLineEndings("\r\n"), new NullSink())!);
        if (form2Code is not null)
            project.AddForm(new FormDeserializer().Deserialize(project, (Header("Form2") + form2Code).ReplaceLineEndings("\r\n"), new NullSink())!);

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

    [AvaloniaTheory]
    [InlineData("Form2.Show")]
    [InlineData("Form2.pub = 1")]
    [InlineData("Load Form2")]
    public void A_second_form_is_refused_naming_822(string use)
    {
        using var run = Start($"Private Sub Command1_Click()\n    {use}\n    Debug.Print \"not reached\"\nEnd Sub\n",
            "Public pub As Integer\nPrivate Sub Form_Initialize()\n    Debug.Print \"f2 initialize\"\nEnd Sub\n");

        run.Click();
        Pump();

        run.Printed.Should().BeEmpty();
        run.Failed.Should().ContainSingle().Which.Should().Contain("another form").And.Contain("#822");
        run.Runner.IsRunning.Should().BeFalse("a refusal is a fault, which ends the run");
    }
}
