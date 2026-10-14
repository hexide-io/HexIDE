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
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A program of several forms (#822, slice 2a): default instances made where a form's name is first used, Load, Show,
/// Hide, Unload of another form, and the run lasting until no form is loaded. Every expectation here was measured on
/// vb6.exe, native and p-code agreeing: the oracle's <i>Several forms: load, show, hide, modal, and when the run ends</i>.
/// Run from <see cref="VBLoader.RunForm"/>, where F5 starts on the desktop and where the Standalone runner starts.
/// </summary>
public class SeveralFormsTests
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
      + "   Begin VB.TextBox Text1 \r\n"
      + "      Height          =   495\r\n"
      + "      Left            =   120\r\n"
      + "      TabIndex        =   0\r\n"
      + $"      Text            =   \"{name} text\"\r\n"
      + "      Top             =   120\r\n"
      + "      Width           =   1215\r\n"
      + "   End\r\n"
      + "   Begin VB.CommandButton Command1 \r\n"
      + "      Caption         =   \"Command1\"\r\n"
      + "      Height          =   495\r\n"
      + "      Left            =   120\r\n"
      + "      TabIndex        =   1\r\n"
      + "      Top             =   720\r\n"
      + "      Width           =   1215\r\n"
      + "   End\r\n"
      + "End\r\n"
      + $"Attribute VB_Name = \"{name}\"\r\n";

    // Form2 logs its lifecycle, has a Public variable and a Public Sub, and unloads itself when its button is clicked.
    private const string Form2Code = """
        Public pub As Integer
        Private Sub Form_Initialize()
            Debug.Print "f2 initialize"
        End Sub
        Private Sub Form_Load()
            Debug.Print "f2 load"
        End Sub
        Private Sub Form_QueryUnload(Cancel As Integer, UnloadMode As Integer)
            Debug.Print "f2 queryunload " & UnloadMode
        End Sub
        Private Sub Form_Unload(Cancel As Integer)
            Debug.Print "f2 unload"
        End Sub
        Private Sub Form_Terminate()
            Debug.Print "f2 terminate"
        End Sub
        Public Sub Foo()
            Debug.Print "f2 foo"
        End Sub
        Private Sub Command1_Click()
            Unload Me
        End Sub

        """;

    private const string Form1Terminate = """
        Private Sub Form_Terminate()
            Debug.Print "f1 terminate"
        End Sub

        """;

    private sealed class Run : IDisposable
    {
        public required Task Task { get; init; }
        public required CancellationTokenSource Cancel { get; init; }
        public required List<string> Printed { get; init; }
        public required List<Exception> Errors { get; init; }
        /// <summary>The windows that have been shown, by caption.</summary>
        public required Dictionary<string, VBFormRuntime> Opened { get; init; }
        public required Action Detach { get; init; }

        public void Click(string form) => Opened[form].GetLogicalDescendants().OfType<VBCommandButton>().Single()
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        public void Dispose()
        {
            Cancel.Cancel();
            Pump();
            Detach();
        }
    }

    private static FormDefinition Form(ProjectDefinition project, string name, string code) =>
        new FormDeserializer().Deserialize(project, (Header(name) + code).ReplaceLineEndings("\r\n"), new NullSink())!;

    /// <summary>Run a project of Form1 and Form2 from Form1, as F5 does, and let its start run.</summary>
    private static Run Start(string form1Code, string form2Code = Form2Code)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form1 = Form(project, "Form1", form1Code);
        project.AddForm(form1);
        project.AddForm(Form(project, "Form2", form2Code));

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
        var task = VBLoader.RunForm(form1, cancel.Token, out _);
        Pump();
        return new Run
        {
            Task = task,
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

    // ── Which use makes the instance, and which loads it ──────────────────────────────────────────────

    [AvaloniaTheory]
    [InlineData("x = Form2.Caption", new[] { "f2 initialize", "f2 load" })]
    [InlineData("x = Form2.Text1.Text", new[] { "f2 initialize", "f2 load" })]
    [InlineData("x = Form2.pub", new[] { "f2 initialize" })]
    [InlineData("Form2.pub = 3", new[] { "f2 initialize" })]
    [InlineData("Form2.Foo", new[] { "f2 initialize", "f2 foo" })]
    [InlineData("Set o = Form2: Debug.Print TypeName(o)", new[] { "f2 initialize", "Form2" })]
    public void Any_use_of_a_forms_name_makes_its_instance_and_only_a_control_or_built_in_member_loads_it(string use, string[] raised)
    {
        using var run = Start($"Private x As Variant\nPrivate o As Object\nPrivate Sub Form_Load()\n    {use}\n    Debug.Print \"after\"\nEnd Sub\n");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal([.. raised, "after"]);
        run.Opened.Should().NotContainKey("Form2", "none of them shows the form");
    }

    [AvaloniaFact]
    public void Load_loads_the_form_hidden_and_Show_shows_it_without_loading_it_again()
    {
        using var run = Start("""
            Private Sub Form_Load()
                Load Form2
                Debug.Print "loaded"
            End Sub
            Private Sub Command1_Click()
                Form2.Show
                Debug.Print "shown"
            End Sub
            """);

        run.Printed.Should().Equal("f2 initialize", "f2 load", "loaded");
        run.Opened.Should().NotContainKey("Form2");

        run.Click("Form1");
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("f2 initialize", "f2 load", "loaded", "shown");
        run.Opened.Should().ContainKey("Form2").WhoseValue.IsVisible.Should().BeTrue();
    }

    [AvaloniaFact]
    public void A_hidden_form_stays_loaded_and_with_every_form_hidden_the_run_goes_on()
    {
        using var run = Start("""
            Private n As Integer
            Private Sub Command1_Click()
                n = n + 1
                Select Case n
                Case 1
                    Form2.Show
                Case 2
                    Form2.Hide
                    Debug.Print "caption " & Form2.Caption
                Case 3
                    Me.Hide
                Case 4
                    Unload Form2
                    Unload Me
                End Select
            End Sub
            """ + "\n" + Form1Terminate);

        run.Click("Form1");
        Pump();
        run.Errors.Should().BeEmpty();
        run.Click("Form1");
        Pump();
        run.Opened["Form2"].IsVisible.Should().BeFalse();
        run.Click("Form1");
        Pump();
        run.Opened["Form1"].IsVisible.Should().BeFalse();
        run.Task.IsCompleted.Should().BeFalse("both forms are loaded, though neither shows");

        run.Click("Form1");
        Pump();

        run.Errors.Should().BeEmpty();
        // The Terminates come at the run's end, in the order the forms were made.
        run.Printed.Should().Equal("f2 initialize", "f2 load", "caption Form2", "f2 queryunload 1", "f2 unload",
            "f1 terminate", "f2 terminate");
        run.Task.IsCompleted.Should().BeTrue("no form is loaded");
    }

    // ── Unload, and using the form again ────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void After_Unload_a_variable_survives_a_control_loads_it_again_and_Set_Nothing_ends_the_instance()
    {
        // The oracle's measurement 4, statement for statement.
        using var run = Start("""
            Private Sub Command1_Click()
                Form2.Show
                Form2.pub = 5
                Unload Form2
                Debug.Print "pub after unload " & Form2.pub
                Debug.Print "caption after unload " & Form2.Caption
                Unload Form2
                Set Form2 = Nothing
                Debug.Print "after Set Form2 = Nothing"
                Debug.Print "pub after Nothing " & Form2.pub
            End Sub
            """);

        run.Click("Form1");
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("f2 initialize", "f2 load", "f2 queryunload 1", "f2 unload", "pub after unload 5",
            "f2 load", "caption after unload Form2", "f2 queryunload 1", "f2 unload", "f2 terminate",
            "after Set Form2 = Nothing", "f2 initialize", "pub after Nothing 0");
        run.Task.IsCompleted.Should().BeFalse("Form1 is still loaded");
    }

    [AvaloniaTheory]
    [InlineData("Unload Form2")]
    [InlineData("Form2.pub = 1: Unload Form2")]
    public void Unload_of_a_form_that_is_not_loaded_does_nothing(string unload)
    {
        using var run = Start($"Private Sub Form_Load()\n    {unload}\n    Debug.Print \"after, pub \" & Form2.pub\nEnd Sub\n");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("f2 initialize", "after, pub " + (unload.StartsWith("Form2.pub") ? "1" : "0"));
    }

    [AvaloniaFact]
    public void Unload_Me_in_Form_Initialize_does_nothing_and_the_form_still_loads()
    {
        using var run = Start("""
            Private Sub Form_Initialize()
                Unload Me
                Debug.Print "after unload in initialize"
            End Sub
            Private Sub Form_Load()
                Debug.Print "load"
            End Sub
            """);

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("after unload in initialize", "load");
        run.Opened.Should().ContainKey("Form1");
    }

    // ── When the run ends ──────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void The_run_goes_on_after_the_startup_form_unloads_while_another_form_is_loaded_hidden()
    {
        using var run = Start("""
            Private Sub Command1_Click()
                Load Form2
                Unload Me
            End Sub
            """);

        run.Click("Form1");
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("f2 initialize", "f2 load");
        run.Task.IsCompleted.Should().BeFalse("Form2 is loaded, though no form shows");
    }

    [AvaloniaFact]
    public void The_run_ends_when_the_last_loaded_form_unloads_and_the_Terminates_come_in_the_order_the_forms_were_made()
    {
        using var run = Start("""
            Private Sub Command1_Click()
                Form2.Show
                Unload Me
            End Sub
            """ + "\n" + Form1Terminate);

        run.Click("Form1");
        Pump();
        run.Errors.Should().BeEmpty();
        run.Task.IsCompleted.Should().BeFalse("Form2 is showing");

        run.Click("Form2");
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("f2 initialize", "f2 load", "f2 queryunload 1", "f2 unload", "f1 terminate", "f2 terminate");
        run.Task.IsCompleted.Should().BeTrue();
    }

    // ── Errors in another form's Initialize and Load ─────────────────────────────────────────────────────

    [AvaloniaFact]
    public void An_error_in_another_forms_Initialize_is_trapped_by_the_On_Error_of_the_statement_that_used_its_name()
    {
        using var run = Start("""
            Private Sub Form_Load()
                On Error GoTo h
                Form2.pub = 1
                Debug.Print "not reached"
                Exit Sub
            h:
                Debug.Print "caught " & Err.Number
            End Sub
            """, """
            Public pub As Integer
            Private Sub Form_Initialize()
                Dim d As Integer
                Debug.Print 1 / d
            End Sub

            """);

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("caught 11");
    }

    [AvaloniaTheory]
    [InlineData("Load Form2")]
    [InlineData("x = Form2.Text1.Text")]
    [InlineData("Form2.Show")]
    public void An_error_in_another_forms_Load_is_not_the_callers_to_trap(string load)
    {
        // Form_Load is an event: the error is reported as unhandled in it, and the On Error around the statement that
        // loaded the form never sees it (measured). VB6 then stops; HexIDE leaves the run going, as after any event's
        // unhandled error (debugger-vb6-divergences D23).
        using var run = Start($"Private x As Variant\nPrivate Sub Form_Load()\n    On Error GoTo h\n    {load}\n    Debug.Print \"after\"\n"
            + "    Exit Sub\nh:\n    Debug.Print \"caught\"\nEnd Sub\n", """
            Private Sub Form_Load()
                Debug.Print "f2 load"
                Dim d As Integer
                Debug.Print 1 / d
                Debug.Print "not reached"
            End Sub

            """);

        run.Printed.Should().Equal("f2 load", "after");
        run.Errors.Should().ContainSingle().Which.Should().BeOfType<VBRunTimeException>();
    }
}
