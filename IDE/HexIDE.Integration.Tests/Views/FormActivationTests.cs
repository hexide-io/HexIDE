using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Automation;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// When a running form raises Resize, Activate and Deactivate (#994). Every expectation was measured on vb6.exe in the
/// console, native and p-code agreeing line for line: the oracle's <i>A form's Resize, Activate and Deactivate</i>. Run from
/// <see cref="VBLoader.RunForm"/>, where F5 starts on the desktop.
/// </summary>
/// <remarks>
/// Headless, a window is activated when it is shown and when the program activates it, as on the desktop. Nothing else
/// activates one: the window that was active is not told, and when the active window hides or closes no other is
/// activated, which on the desktop Windows does. So the rows where VB6 activates the next form after a Hide or an Unload
/// are the desktop's, and are checked there.
/// </remarks>
public class FormActivationTests
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
      + "      TabIndex        =   0\r\n"
      + "      Top             =   120\r\n"
      + "      Width           =   1215\r\n"
      + "   End\r\n"
      + "End\r\n"
      + $"Attribute VB_Name = \"{name}\"\r\n";

    // Every form logs the events this is about, prefixed f1 or f2.
    private static string Logged(string prefix) => $"""
        Private Sub Form_Initialize()
            Debug.Print "{prefix} initialize"
        End Sub
        Private Sub Form_Load()
            Debug.Print "{prefix} load"
        End Sub
        Private Sub Form_Resize()
            Debug.Print "{prefix} resize"
        End Sub
        Private Sub Form_Activate()
            Debug.Print "{prefix} activate"
        End Sub
        Private Sub Form_Deactivate()
            Debug.Print "{prefix} deactivate"
        End Sub
        Private Sub Form_Unload(Cancel As Integer)
            Debug.Print "{prefix} unload"
        End Sub

        """;

    // Form1's button runs the next step of the test's script, one a click.
    private static string Steps(params string[] steps)
    {
        var cases = string.Concat(steps.Select((step, i) => $"    Case {i + 1}\n        {step.Replace("\n", "\n        ")}\n"));
        return "Private n As Integer\nPrivate Sub Command1_Click()\n    n = n + 1\n    Select Case n\n" + cases
            + "    End Select\nEnd Sub\n";
    }

    // Form2's button unloads it.
    private static readonly string Form2Code = Logged("f2") + "Private Sub Command1_Click()\n    Unload Me\nEnd Sub\n";

    private sealed class Run : IDisposable
    {
        public required CancellationTokenSource Cancel { get; init; }
        public required List<string> Printed { get; init; }
        public required List<Exception> Errors { get; init; }
        public required Dictionary<string, VBFormRuntime> Opened { get; init; }
        public required Action Detach { get; init; }

        /// <summary>What was printed since the start's own lines.</summary>
        public List<string> Since(int count) => Printed.Skip(count).ToList();

        public void Click(string form)
        {
            Opened[form].GetLogicalDescendants().OfType<VBCommandButton>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
        }

        public void Dispose()
        {
            Cancel.Cancel();
            Pump();
            Detach();
        }
    }

    private static FormDefinition Form(ProjectDefinition project, string name, string code) =>
        new FormDeserializer().Deserialize(project, (Header(name) + code).ReplaceLineEndings("\r\n"), new NullSink())!;

    /// <summary>Run Form1, with Form2 in the project, as F5 does, and let its start run.</summary>
    private static Run Start(string form1Code, bool logEvents = true)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form1 = Form(project, "Form1", (logEvents ? Logged("f1") : "") + form1Code);
        project.AddForm(form1);
        project.AddForm(Form(project, "Form2", Form2Code));

        var printed = new List<string>();
        var errors = new List<Exception>();
        var opened = new Dictionary<string, VBFormRuntime>();
        Action<string> print = printed.Add;
        void OnFaulted(VBWindowContext _, Exception e) => errors.Add(e);
        void OnRunTimeError(VBWindowContext _, VBRunTimeException e) => errors.Add(e);
        void OnCompileError(VBWindowContext _, VBCompileErrorException e) => errors.Add(e);
        VBDebugConsole.Output += print;
        VBWindowContext.Faulted += OnFaulted;
        VBWindowContext.RunTimeError += OnRunTimeError;
        VBWindowContext.CompileError += OnCompileError;
        var watch = Window.WindowOpenedEvent.AddClassHandler<VBFormRuntime>((w, _) => opened[w.Title ?? ""] = w);
        var cancel = new CancellationTokenSource();
        _ = VBLoader.RunForm(form1, cancel.Token, out _);
        Pump();
        return new Run
        {
            Cancel = cancel,
            Printed = printed,
            Errors = errors,
            Opened = opened,
            Detach = () =>
            {
                watch.Dispose();
                VBDebugConsole.Output -= print;
                VBWindowContext.Faulted -= OnFaulted;
                VBWindowContext.RunTimeError -= OnRunTimeError;
                VBWindowContext.CompileError -= OnCompileError;
            },
        };
    }

    private static void Pump()
    {
        for (var i = 0; i < 20; i++)
            Dispatcher.UIThread.RunJobs();
    }

    private static readonly string[] Started = ["f1 initialize", "f1 load", "f1 resize", "f1 activate"];

    // ── On show ──────────────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void The_startup_form_raises_Initialize_Load_one_Resize_then_Activate()
    {
        using var run = Start("");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal(Started, "showing the window raises Resize once, as VB6 does, however the window settles");
    }

    [AvaloniaFact]
    public void Load_raises_no_Resize_and_Show_raises_one_inside_the_statement_with_Activate_after_it()
    {
        using var run = Start(Steps(
            "Debug.Print \"load\"\nLoad Form2\nDebug.Print \"loaded\"",
            "Debug.Print \"show\"\nForm2.Show\nDebug.Print \"shown\""));

        run.Click("Form1");
        run.Since(Started.Length).Should().Equal("load", "f2 initialize", "f2 load", "loaded");

        run.Click("Form1");
        run.Errors.Should().BeEmpty();
        run.Since(Started.Length + 4).Should().Equal("show", "f2 resize", "shown", "f1 deactivate", "f2 activate");
    }

    [AvaloniaFact]
    public void A_form_shown_again_after_Hide_raises_no_Resize_and_a_hidden_form_that_was_not_active_raises_nothing()
    {
        using var run = Start(Steps("Form2.Show", "Me.SetFocus\nDebug.Print \"focused\"", "Form2.Hide", "Form2.Show"));
        run.Click("Form1");
        var before = run.Printed.Count;

        run.Click("Form1");
        run.Since(before).Should().Equal(["focused", "f2 deactivate", "f1 activate"],
            "SetFocus activates the form, and the activation comes after the handler, as Windows posts it");
        run.Click("Form1");
        run.Since(before + 3).Should().BeEmpty("Form2 was not the active form");
        run.Click("Form1");

        run.Errors.Should().BeEmpty();
        run.Since(before + 3).Should().Equal("f1 deactivate", "f2 activate");
    }

    [AvaloniaFact]
    public void The_active_form_unloaded_raises_no_Deactivate_and_loaded_again_raises_Load_and_Resize()
    {
        using var run = Start(Steps("Form2.Show", "Unload Form2", "Form2.Show"));
        run.Click("Form1");
        var before = run.Printed.Count;

        run.Click("Form1");
        run.Since(before).Should().Equal("f2 unload");
        run.Click("Form1");

        // On the desktop Windows activates Form1 once Form2 has gone, and VB6 raises Form1's Activate there, then its
        // Deactivate at this Show. Headless nothing activates Form1, so neither comes.
        run.Errors.Should().BeEmpty();
        run.Since(before + 1).Should().Equal("f2 load", "f2 resize", "f2 activate");
    }

    [AvaloniaFact]
    public void Show_vbModal_raises_Resize_inside_the_Show_and_Activate_while_the_modal_form_runs()
    {
        using var run = Start(Steps("Debug.Print \"modal\"\nForm2.Show vbModal\nDebug.Print \"back\""));

        run.Click("Form1");
        run.Since(Started.Length).Should().Equal("modal", "f2 initialize", "f2 load", "f2 resize", "f1 deactivate", "f2 activate");

        run.Click("Form2");
        run.Errors.Should().BeEmpty();
        run.Since(Started.Length + 6).Should().Equal("f2 unload", "back");
    }

    // ── What raises nothing ──────────────────────────────────────────────────────────────────────────────

    // Leaving for another application raises nothing, and coming back to the form that was active raises nothing either
    // (measured with Notepad). Headless, the window is activated again as it is coming back.
    [AvaloniaFact]
    public void The_active_form_activated_again_raises_nothing()
    {
        using var run = Start("");

        run.Opened["Form1"].Activate();
        Pump();

        run.Printed.Should().Equal(Started);
    }

    [AvaloniaFact]
    public void A_form_held_by_a_modal_form_is_not_activated()
    {
        using var run = Start(Steps("Form2.Show vbModal"));
        run.Click("Form1");
        var before = run.Printed.Count;

        run.Opened["Form1"].Activate();
        Pump();

        run.Errors.Should().BeEmpty();
        run.Since(before).Should().BeEmpty("Windows doesn't activate a window the modal form has disabled");
    }

    // ── Resize after the show ────────────────────────────────────────────────────────────────────────────

    [AvaloniaTheory]
    [InlineData("Width")]
    [InlineData("Height")]
    public void A_size_assignment_raises_Resize_before_the_next_statement_and_layout_does_not_repeat_it(string dimension)
    {
        using var run = Start(Steps($"Debug.Print \"before\"\nMe.{dimension} = Me.{dimension} + 300\nDebug.Print \"after\"",
            $"Me.{dimension} = Me.{dimension}\nDebug.Print \"same\""));

        run.Click("Form1");
        run.Since(Started.Length).Should().Equal("before", "f1 resize", "after");
        run.Click("Form1");
        run.Errors.Should().BeEmpty();
        run.Since(Started.Length).Should().Equal("before", "f1 resize", "after", "same");
    }

    [AvaloniaFact]
    public void Size_assignments_in_Load_raise_Resize_while_hidden_and_Show_raises_its_own()
    {
        using var run = Start("""
            Private Sub Form_Load()
                Me.Width = 6000
                Debug.Print "width"
                Me.Height = 4000
                Debug.Print "height"
            End Sub
            Private Sub Form_Resize()
                Debug.Print "resize " & Me.Visible
            End Sub
            """, logEvents: false);

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("resize False", "width", "resize False", "height", "resize True");
    }

    [AvaloniaFact]
    public void Resize_comes_once_for_each_change_of_size_and_not_at_all_for_the_same_size()
    {
        using var run = Start("");
        var window = run.Opened["Form1"];

        window.Width += 30;
        Pump();
        run.Since(Started.Length).Should().Equal("f1 resize");

        window.Width += 0;
        window.InvalidateMeasure();
        Pump();

        run.Errors.Should().BeEmpty();
        run.Since(Started.Length).Should().Equal("f1 resize");
    }
    private static void NativeSize(VBFormRuntime window, double width, double height)
    {
        var platform = window.PlatformImpl!;
        // Resize() posts asynchronously headlessly; model a native notification with its backend size already updated.
        var size = new Avalonia.Size(width, height);
        platform.GetType().GetProperty("ClientSize")!.SetValue(platform, size);
        var resized = (Action<Avalonia.Size, WindowResizeReason>)platform.GetType().GetProperty("Resized")!.GetValue(platform)!;
        resized(size, WindowResizeReason.User);
    }

    [AvaloniaTheory]
    [InlineData(WindowState.Normal, WindowState.Maximized, true)]
    [InlineData(WindowState.Maximized, WindowState.Normal, false)]
    [InlineData(WindowState.Normal, WindowState.Minimized, false)]
    [InlineData(WindowState.Minimized, WindowState.Normal, true)]
    public async Task A_native_state_change_raises_one_Resize_then_Paint_only_when_the_client_grows(
        WindowState from, WindowState to, bool paint)
    {
        // The automation route uses the same state setter, then waits for the queued window work.
        foreach (var toolRoute in new[] { false, true })
        {
            using var run = Start("""
                Private Sub Form_Resize()
                    Debug.Print "resize " & Me.WindowState
                    If Me.WindowState <> 1 Then Debug.Print "client " & Me.ScaleWidth & "x" & Me.ScaleHeight
                End Sub
                Private Sub Form_Paint()
                    Debug.Print "paint"
                End Sub
                Private Sub Form_Activate()
                    Debug.Print "activate"
                End Sub
                Private Sub Form_Deactivate()
                    Debug.Print "deactivate"
                End Sub
                Private Sub Form_GotFocus()
                    Debug.Print "focus"
                End Sub
                Private Sub Form_LostFocus()
                    Debug.Print "blur"
                End Sub
                """, logEvents: false);
            var window = run.Opened["Form1"];
            window.WindowState = from;
            NativeSize(window, from == WindowState.Maximized ? 1024 : 300, from == WindowState.Maximized ? 705 : 200);
            Pump();
            run.Printed.Clear();

            var applied = toolRoute
                ? WindowChange.ApplyAsync(window, to, null, null, null, null, TestContext.Current.CancellationToken)
                : null;
            if (!toolRoute)
                window.WindowState = to;
            if (to != WindowState.Minimized)
            {
                // A native state change can report intermediate client sizes before it settles.
                NativeSize(window, 600, 400);
                NativeSize(window, to == WindowState.Maximized ? 1024 : 300, to == WindowState.Maximized ? 705 : 200);
            }
            if (from == WindowState.Minimized)
                window.Activate();
            if (applied is not null)
                (await applied).Should().BeTrue();
            Pump();

            run.Errors.Should().BeEmpty();
            var expected = new List<string> { $"resize {(int)to}" };
            if (to != WindowState.Minimized)
                expected.Add(to == WindowState.Maximized ? "client 15360x10575" : "client 4500x3000");
            if (paint)
                expected.Add("paint");
            run.Printed.Should().Equal(expected);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_native_border_or_corner_drag_raises_Resize_then_Paint_per_move_and_nothing_on_release(bool corner)
    {
        using var run = Start("""
            Private Sub Form_Resize()
                Debug.Print "resize"
            End Sub
            Private Sub Form_Paint()
                Debug.Print "paint"
            End Sub
            """, logEvents: false);
        var window = run.Opened["Form1"];
        NativeSize(window, 300, 200);
        Pump();
        run.Printed.Clear();
        for (var i = 1; i <= (corner ? 1 : 5); i++)
        {
            NativeSize(window, 300 + (corner ? 40 : 20 * i), 200 + (corner ? 30 : 0));
            Pump();
            run.Printed.Should().Equal(Enumerable.Range(0, i).SelectMany(_ => new[] { "resize", "paint" }));
        }
        run.Printed.Clear();
        Pump();
        run.Printed.Should().BeEmpty("releasing a border does not change the size");
        run.Errors.Should().BeEmpty();
    }

}
