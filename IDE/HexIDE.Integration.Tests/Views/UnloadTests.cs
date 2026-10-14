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
/// <c>Unload Me</c> on the startup form (#973), through the IDE's own F5 path. Every order here was measured on vb6.exe,
/// native and p-code agreeing (the oracle's <em>Unload Me</em>): QueryUnload with UnloadMode 1 from code and 0 from the
/// close box, then Unload, either with a Cancel that keeps the form; the statements after <c>Unload Me</c> still run; and
/// Terminate comes once the handler has returned, after which the run ends.
/// </summary>
public class UnloadTests
{
    private const string Caption = "UnloadTests";

    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private static readonly string Form =
        "VERSION 5.00\r\n"
      + "Begin VB.Form Form1 \r\n"
      + $"   Caption         =   \"{Caption}\"\r\n"
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
      + "Attribute VB_Name = \"Form1\"\r\n";

    // The lifecycle, logged, with QueryUnload and Unload refusing while their flag is set, and unloading again from
    // inside themselves while nestIn names them.
    private const string Lifecycle = """
        Private refuseQuery As Boolean
        Private refuseUnload As Boolean
        Private nestIn As Integer
        Private Sub Form_QueryUnload(Cancel As Integer, UnloadMode As Integer)
            Debug.Print "queryunload, mode " & UnloadMode & ", cancel in " & Cancel
            If refuseQuery Then Cancel = True: refuseQuery = False: Debug.Print "queryunload refuses"
            If nestIn = 1 Then nestIn = 0: Unload Me: Debug.Print "nested in queryunload"
        End Sub
        Private Sub Form_Unload(Cancel As Integer)
            Debug.Print "unload, cancel in " & Cancel
            If refuseUnload Then Cancel = 1: refuseUnload = False: Debug.Print "unload refuses"
            If nestIn = 2 Then nestIn = 0: Unload Me: Debug.Print "nested in unload"
        End Sub
        Private Sub Form_Terminate()
            Debug.Print "terminate"
        End Sub

        """;

    private sealed class Run : IDisposable
    {
        public required ProjectRunnerService Runner { get; init; }
        public required VBFormRuntime? Window { get; init; }
        public required Action Detach { get; init; }
        public required List<string> Printed { get; init; }
        public required List<string> Failed { get; init; }
        public bool Closed { get; set; }

        public void Click() => Window!.GetLogicalDescendants().OfType<VBCommandButton>().Single()
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        public void Dispose()
        {
            Detach();
            Runner.EndProject();
            Pump();
        }
    }

    private static Run Start(string code, params (string Name, ModuleKind Kind, string Code)[] modules) =>
        Start(code, shown: true, modules);

    private static Run Start(string code, bool shown, params (string Name, ModuleKind Kind, string Code)[] modules)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, Form, new NullSink())!;
        form.UpdateCode((Lifecycle + code).ReplaceLineEndings("\r\n"));
        project.AddForm(form);
        foreach (var (name, kind, moduleCode) in modules)
        {
            var module = new ModuleDefinition(project, name, kind);
            module.UpdateCode(moduleCode.ReplaceLineEndings("\r\n"));
            project.AddModule(module);
        }

        var localization = Substitute.For<ILocalizationService>();
        // Each message's own placeholders, as the packs have them: a template with too many fails the report.
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>() switch
        {
            "Str.ProjectRunner.StartupFormFailedToLoad" => "Form '{0}' failed: {1}",
            var key => key + ": {0}",
        });
        // A yield at every statement, so nothing here holds only because no handler gave up the thread (#766).
        var controller = new DebugController { TimeSliceMs = -1 };
        var runner = new ProjectRunnerService(
            Substitute.For<IEventBus>(), Substitute.For<IWindowManager>(), Substitute.For<IProjectManager>(),
            localization, controller, Substitute.For<IBreakpointService>(), new WatchService(), new RunScope());

        VBFormRuntime? window = null;
        using var opened = Avalonia.Controls.Window.WindowOpenedEvent.AddClassHandler<VBFormRuntime>((w, _) =>
        {
            if (w.Title == Caption)
                window = w;
        });
        var printed = new List<string>();
        var failed = new List<string>();
        Action<string> print = printed.Add;
        VBDebugConsole.Output += print;
        runner.StartFailed += failed.Add;
        runner.RunFailed += failed.Add;
        runner.RunProject(project);
        Pump();
        if (shown)
            window.Should().NotBeNull("the startup form opens before anything is clicked");
        else
            window.Should().BeNull("the startup form unloaded itself in Load");
        var run = new Run
        {
            Runner = runner,
            Window = window,
            Printed = printed,
            Failed = failed,
            Detach = () => VBDebugConsole.Output -= print,
        };
        if (window is not null)
            window.Closed += (_, _) => run.Closed = true;
        return run;
    }

    private static void Pump()
    {
        for (var i = 0; i < 20; i++)
            Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Unload_Me_raises_QueryUnload_then_Unload_runs_the_rest_of_the_handler_then_Terminate_and_ends_the_run()
    {
        using var run = Start("""
            Private Sub Command1_Click()
                Debug.Print "before unload"
                Unload Me
                Debug.Print "after unload"
                Debug.Print "after unload, still in the handler"
            End Sub
            """);

        run.Click();
        Pump();

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal("before unload", "queryunload, mode 1, cancel in 0", "unload, cancel in 0",
            "after unload", "after unload, still in the handler", "terminate");
        run.Closed.Should().BeTrue("the startup form was the last one loaded");
        run.Runner.IsRunning.Should().BeFalse();
    }

    [AvaloniaTheory]
    [InlineData("refuseQuery", new[] { "queryunload, mode 1, cancel in 0", "queryunload refuses" })]
    [InlineData("refuseUnload", new[] { "queryunload, mode 1, cancel in 0", "unload, cancel in 0", "unload refuses" })]
    public void A_handler_that_sets_Cancel_keeps_the_form_loaded_and_a_second_Unload_goes_through(string refuse, string[] refused)
    {
        using var run = Start($"""
            Private tries As Integer
            Private Sub Command1_Click()
                tries = tries + 1
                If tries = 1 Then {refuse} = True
                Unload Me
                Debug.Print "after unload " & tries
            End Sub
            """);

        run.Click();
        Pump();

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal([.. refused, "after unload 1"]);
        run.Closed.Should().BeFalse("the form was kept");
        run.Runner.IsRunning.Should().BeTrue();

        run.Printed.Clear();
        run.Click();
        Pump();

        run.Printed.Should().Equal("queryunload, mode 1, cancel in 0", "unload, cancel in 0", "after unload 2", "terminate");
        run.Closed.Should().BeTrue();
    }

    [AvaloniaFact]
    public void The_close_box_asks_with_UnloadMode_0_and_a_refusal_keeps_the_window()
    {
        // A person's close can't be raised headless, so this is the path the window's Closing handler takes for one.
        using var run = Start("""
            Private Sub Form_Load()
                refuseQuery = True
            End Sub
            """);

        run.Window!.Context.UnloadFromCloseBox().Should().BeTrue("the program decides whether the form closes");
        Pump();

        run.Printed.Should().Equal("queryunload, mode 0, cancel in 0", "queryunload refuses");
        run.Closed.Should().BeFalse();

        run.Printed.Clear();
        run.Window!.Context.UnloadFromCloseBox().Should().BeTrue();
        Pump();

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal("queryunload, mode 0, cancel in 0", "unload, cancel in 0", "terminate");
        run.Closed.Should().BeTrue();
        run.Runner.IsRunning.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Two_presses_of_the_close_box_ask_once_and_lose_neither_Terminate_nor_the_run()
    {
        // Both presses wait in the queue, one behind the other, as two quick clicks on the box do (#1046 review).
        using var run = Start("");

        run.Window!.Context.UnloadFromCloseBox().Should().BeTrue();
        run.Window.Context.UnloadFromCloseBox().Should().BeTrue();
        Pump();

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal("queryunload, mode 0, cancel in 0", "unload, cancel in 0", "terminate");
        run.Closed.Should().BeTrue();
        run.Runner.IsRunning.Should().BeFalse();
    }

    [AvaloniaFact]
    public void A_press_of_the_close_box_behind_a_handler_that_unloads_asks_nothing()
    {
        // The press comes while the click's handler is still running, every statement yielding, so it waits behind it.
        using var run = Start("""
            Private Sub Command1_Click()
                Debug.Print "click"
                Unload Me
                Debug.Print "after unload"
            End Sub
            """);

        run.Click();
        run.Window!.Context.UnloadFromCloseBox().Should().BeTrue("the window stays until the program has decided");
        Pump();

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal("click", "queryunload, mode 1, cancel in 0", "unload, cancel in 0", "after unload",
            "terminate");
        run.Closed.Should().BeTrue();
        run.Runner.IsRunning.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Unload_Me_in_Form_Load_runs_the_rest_of_Load_and_Terminate_and_ends_the_run_without_showing_the_form()
    {
        // Measured: QueryUnload, Unload, the rest of Load, Terminate, and the run ends silently, never shown (#1046 review).
        using var run = Start("""
            Private Sub Form_Load()
                Debug.Print "load"
                Unload Me
                Debug.Print "after unload, in load"
            End Sub
            Private Sub Form_Resize()
                Debug.Print "resize"
            End Sub
            """, shown: false);

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal("load", "queryunload, mode 1, cancel in 0", "unload, cancel in 0", "after unload, in load",
            "terminate");
        run.Runner.IsRunning.Should().BeFalse();
    }

    [AvaloniaFact]
    public void A_refused_Unload_Me_in_Form_Load_shows_the_form_and_the_run_goes_on()
    {
        // Measured: the form is shown, with Resize and Activate, and the run goes on.
        using var run = Start("""
            Private Sub Form_Load()
                refuseQuery = True
                Unload Me
                Debug.Print "after the refused unload, in load"
            End Sub
            """);

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal("queryunload, mode 1, cancel in 0", "queryunload refuses", "after the refused unload, in load");
        run.Closed.Should().BeFalse();
        run.Runner.IsRunning.Should().BeTrue();
    }

    [AvaloniaTheory]
    [InlineData(0, new[] { "queryunload, mode 1, cancel in 0", "unload, cancel in 0", "after the first", "after the second" })]
    [InlineData(1, new[] { "queryunload, mode 1, cancel in 0", "nested in queryunload", "unload, cancel in 0", "after the first",
        "after the second" })]
    [InlineData(2, new[] { "queryunload, mode 1, cancel in 0", "unload, cancel in 0", "nested in unload", "after the first",
        "after the second" })]
    public void An_Unload_of_a_form_already_unloading_or_unloaded_does_nothing(int nest, string[] expected)
    {
        // Measured: a second Unload Me, and one from inside QueryUnload or Unload, raise nothing, report no error and reload
        // nothing. The outer unload goes on.
        using var run = Start($"""
            Private Sub Command1_Click()
                nestIn = {nest}
                Unload Me
                Debug.Print "after the first"
                Unload Me
                Debug.Print "after the second"
            End Sub
            """);

        run.Click();
        Pump();

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal([.. expected, "terminate"]);
        run.Closed.Should().BeTrue();
    }

    [AvaloniaFact]
    public void A_form_field_is_released_after_Terminate()
    {
        // Measured: the form's own Terminate, then its fields', as a class tears down after Class_Terminate.
        using var run = Start("""
            Private o As Class1
            Private Sub Form_Load()
                Set o = New Class1
            End Sub
            Private Sub Command1_Click()
                Unload Me
            End Sub
            """, ("Class1", ModuleKind.ClassModule, """
            Private Sub Class_Terminate()
                Debug.Print "class terminate"
            End Sub
            """));

        run.Click();
        Pump();

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal("queryunload, mode 1, cancel in 0", "unload, cancel in 0", "terminate", "class terminate");
    }

    [AvaloniaFact]
    public void Touching_the_form_after_Unload_is_refused_rather_than_ending_the_run_where_VB6_loads_it_again()
    {
        // Measured: VB6 loads the form again at the touch, raising Load, and the run carries on. HexIDE can't yet (#822).
        using var run = Start("""
            Private Sub Command1_Click()
                Unload Me
                Debug.Print "caption " & Me.Caption
            End Sub
            """);

        run.Click();
        Pump();

        run.Printed.Should().Equal("queryunload, mode 1, cancel in 0", "unload, cancel in 0");
        run.Failed.Should().ContainSingle().Which.Should().Contain("after Form1 has unloaded").And.Contain("#822");
    }
}
