using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
      + "   Begin VB.Label Label1 \r\n"
      + "      Caption         =   \"label\"\r\n"
      + "      Height          =   255\r\n"
      + "      Left            =   1440\r\n"
      + "      TabIndex        =   2\r\n"
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
        /// <summary>The captions of the windows that have closed.</summary>
        public required HashSet<string> Closed { get; init; }
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

    /// <summary>Run a project of Form1 and Form2, and Form3 if given, from Form1, as F5 does, and let its start run.</summary>
    private static Run Start(string form1Code, string form2Code = Form2Code, string? form3Code = null)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form1 = Form(project, "Form1", form1Code);
        project.AddForm(form1);
        project.AddForm(Form(project, "Form2", form2Code));
        if (form3Code is not null)
            project.AddForm(Form(project, "Form3", form3Code));

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
        var closed = new HashSet<string>();
        var watch = Window.WindowOpenedEvent.AddClassHandler<VBFormRuntime>((w, _) =>
        {
            opened[w.Title ?? ""] = w;
            w.Closed += (_, _) => closed.Add(w.Title ?? "");
        });
        var cancel = new CancellationTokenSource();
        // End ends the run as every host makes it: by cancelling it.
        void OnEnd(BasicInterpreter _) => Dispatcher.UIThread.Post(() => cancel.Cancel());
        BasicInterpreter.EndReached += OnEnd;
        var task = VBLoader.RunForm(form1, cancel.Token, out _);
        Pump();
        return new Run
        {
            Task = task,
            Cancel = cancel,
            Printed = printed,
            Errors = errors,
            Opened = opened,
            Closed = closed,
            Detach = () =>
            {
                watch.Dispose();
                BasicInterpreter.EndReached -= OnEnd;
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
        // The Terminates come at the run's end, in project form order.
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

    [AvaloniaTheory]
    [InlineData("Load Form2")]
    [InlineData("Form2.Show")]
    [InlineData("Form2.Hide")]
    [InlineData("x = Form2.Caption")]
    [InlineData("x = Form2.Text1.Text")]
    public void A_use_whose_load_unloads_the_form_is_Err_364_there(string use)
    {
        using var run = Start($"Private x As Variant\nPrivate Sub Form_Load()\n    On Error Resume Next\n    {use}\n"
            + "    Debug.Print \"after, err \" & Err.Number & \" [\" & x & \"]\"\nEnd Sub\n", """
            Private Sub Form_Load()
                Debug.Print "f2 load"
                Unload Me
                Debug.Print "f2 rest of load"
            End Sub

            """);

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("f2 load", "f2 rest of load", "after, err 364 []");
        run.Opened.Should().NotContainKey("Form2");
    }

    [AvaloniaFact]
    public void After_Set_Nothing_on_a_loaded_forms_name_the_name_makes_a_second_instance()
    {
        // Measured: Initialize and Load again, Forms.Count 2, and the new instance is not Me. The first goes on running.
        using var run = Start("""
            Private Sub Form_Initialize()
                Debug.Print "f1 initialize"
            End Sub
            Private Sub Form_Load()
                Debug.Print "f1 load"
            End Sub
            Private Sub Command1_Click()
                Set Form1 = Nothing
                Debug.Print "caption " & Form1.Caption & ", same " & (Form1 Is Me)
            End Sub
            """);

        run.Click("Form1");
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("f1 initialize", "f1 load", "f1 initialize", "f1 load", "caption Form1, same False");
        run.Task.IsCompleted.Should().BeFalse();
    }

    [AvaloniaFact]
    public void A_form_whose_name_was_cleared_while_another_reference_holds_it_terminates_when_that_reference_goes()
    {
        using var run = Start("""
            Private f As Object
            Private Sub Command1_Click()
                Set f = Form2
                Form2.Show
                Set Form2 = Nothing
                Debug.Print "after Set Form2 = Nothing"
                Unload f
                Debug.Print "after Unload f"
                Set f = Nothing
                Debug.Print "after Set f = Nothing"
            End Sub
            """);

        run.Click("Form1");
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("f2 initialize", "f2 load", "after Set Form2 = Nothing", "f2 queryunload 1", "f2 unload",
            "after Unload f", "f2 terminate", "after Set f = Nothing");
    }

    // ── Other uses of a form's name (#1056 review) ──────────────────────────────────────────────────────

    [AvaloniaFact]
    public void With_a_forms_name_makes_its_instance_and_a_built_in_member_inside_loads_it()
    {
        using var run = Start("""
            Private Sub Form_Load()
                With Form2
                    Debug.Print "in with"
                    .Caption = "set in with"
                End With
                Debug.Print "caption " & Form2.Caption
            End Sub
            """);

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("f2 initialize", "in with", "f2 load", "caption set in with");
    }

    [AvaloniaTheory]
    [InlineData("ByVal f As Object")]
    [InlineData("f As Object")]
    public void A_forms_name_passed_as_an_argument_makes_its_instance_and_does_not_load_it(string parameter)
    {
        using var run = Start($"Private Sub Take({parameter})\n    Debug.Print TypeName(f) & \" \" & (f Is Form2)\nEnd Sub\n"
            + "Private Sub Form_Load()\n    Take Form2\n    Debug.Print \"after\"\nEnd Sub\n");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("f2 initialize", "Form2 True", "after");
    }

    [AvaloniaFact]
    public void Reading_Visible_loads_the_form_and_reads_False_until_it_is_shown()
    {
        // Measured, as `Form2.Visible` after `Load Form2` (the oracle's measurement 2).
        using var run = Start("""
            Private Sub Form_Load()
                If Form2.Visible Then
                    Debug.Print "visible"
                Else
                    Debug.Print "hidden"
                End If
            End Sub
            """);

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("f2 initialize", "f2 load", "hidden");
    }

    [AvaloniaTheory]
    [InlineData("c = Form2.ScaleLeft", "Reading Form2's ScaleLeft is not implemented: the form's coordinate system is #990")]
    public void A_use_HexIDE_lacks_loads_the_form_then_is_refused_naming_its_issue(string use, string refusal)
    {
        // The load is the measured part: a built-in member or a control's loads the form. What follows is HexIDE's gap,
        // and the refusal is the gap itself, the form's own scale, not the chain that reached it.
        using var run = Start($"Private c As Variant\nPrivate Sub Form_Load()\n    {use}\n    Debug.Print \"not reached\"\nEnd Sub\n");

        run.Printed.Should().Equal("f2 initialize", "f2 load");
        run.Errors.Should().ContainSingle().Which.Should().BeOfType<NotImplementedException>()
            .Which.Message.Should().Be(refusal);
    }

    [AvaloniaFact]
    public void Writing_another_forms_controls_loads_it_and_the_values_are_the_ones_it_shows()
    {
        // Every program of several forms does this. The first write loads Form2, as a read of its control does (measured).
        using var run = Start("""
            Private Sub Form_Load()
                Form2.Text1.Text = "typed"
                Form2.Label1.Caption = "labelled"
                Debug.Print Form2.Text1.Text & " / " & Form2.Label1.Caption
                Form2.Show
            End Sub
            """);

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("f2 initialize", "f2 load", "typed / labelled");
        var shown = run.Opened.Should().ContainKey("Form2").WhoseValue;
        shown.GetLogicalDescendants().OfType<VBTextBox>().Single().Text.Should().Be("typed");
        shown.GetLogicalDescendants().OfType<VBLabel>().Single().Text.Should().Be("labelled");
    }

    [AvaloniaFact]
    public void SetFocus_on_another_forms_control_loads_it_and_is_5_while_it_is_not_shown()
    {
        // The two-dot chain loads Form2, as any use of its control does. Form2 is then loaded but not shown, and SetFocus
        // on a control of a form not shown is 5 (measured, #1061).
        using var run = Start("Private Sub Form_Load()\n    On Error Resume Next\n    Form2.Text1.SetFocus\n    Debug.Print \"err \" & Err.Number\nEnd Sub\n");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("f2 initialize", "f2 load", "err 5");
    }

    // ── Show vbModal ──────────────────────────────────────────────────────────────────────────────────

    // Form1's button shows Form2 modally and reports once the statement has returned. It reads nothing of Form2 there, since
    // a built-in member of an unloaded form loads it again (measured).
    private const string ShowsForm2Modally = """
        Private Sub Command1_Click()
            Debug.Print "before"
            Form2.Show vbModal
            Debug.Print "after, form1 enabled " & Me.Enabled
        End Sub

        """;

    private static string Form2Clicked(string body) => Form2Code.Replace("""
        Private Sub Command1_Click()
            Unload Me
        End Sub
        """, "Private Sub Command1_Click()\n" + body + "End Sub\n");

    [AvaloniaFact]
    public void Show_vbModal_waits_until_the_form_unloads_and_the_other_forms_are_disabled_meanwhile()
    {
        using var run = Start(ShowsForm2Modally);
        run.Click("Form1");
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("before", "f2 initialize", "f2 load");
        run.Opened["Form2"].IsVisible.Should().BeTrue();
        run.Opened["Form1"].HeldBy.Should().BeSameAs(run.Opened["Form2"], "a modal form holds every other (measured)");
        run.Opened["Form1"].IsEnabled.Should().BeTrue("it is held, not disabled, so its controls are drawn as they were");
        run.Opened["Form2"].HeldBy.Should().BeNull();

        run.Click("Form2");
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("before", "f2 initialize", "f2 load", "f2 queryunload 1", "f2 unload",
            "after, form1 enabled True");
        run.Opened["Form1"].HeldBy.Should().BeNull();
    }

    [AvaloniaFact]
    public void A_held_form_refuses_its_close_box_its_text_and_its_pointer_and_reads_Enabled_False()
    {
        using var run = Start(ShowsForm2Modally + """
            Private Sub Form_QueryUnload(Cancel As Integer, UnloadMode As Integer)
                Debug.Print "f1 queryunload " & UnloadMode
            End Sub
            Private Sub Text1_Change()
                Debug.Print "f1 change"
            End Sub

            """, Form2Clicked("""
                Debug.Print "form1 enabled " & Form1.Enabled
                Unload Me

            """));
        run.Click("Form1");
        Pump();
        var form1 = run.Opened["Form1"];
        var text1 = form1.GetLogicalDescendants().OfType<VBTextBox>().Single();
        var button = form1.GetLogicalDescendants().OfType<VBCommandButton>().Single();

        // The close box: refused, raising nothing, as VB6's disabled window ignores a click on it.
        form1.Context.UnloadFromCloseBox().Should().BeTrue("the window stays");
        // Typing: swallowed on the way down, so the text is unchanged and Change isn't raised.
        text1.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "z" });
        // The pointer: nothing beneath it takes a hit.
        var centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), form1)!.Value;
        bool OnButton() => form1.InputHitTest(centre) is Visual hit && (ReferenceEquals(hit, button) || button.IsVisualAncestorOf(hit));
        OnButton().Should().BeFalse("nothing beneath the pointer takes a hit");
        Pump();

        text1.Text.Should().Be("Form1 text");
        run.Printed.Should().Equal("before", "f2 initialize", "f2 load");

        run.Click("Form2");
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("before", "f2 initialize", "f2 load", "form1 enabled False", "f2 queryunload 1", "f2 unload",
            "after, form1 enabled True");
        OnButton().Should().BeTrue("freed once the modal form has gone");
    }

    [AvaloniaFact]
    public void A_form_its_program_disabled_stays_disabled_after_the_modal_form_has_gone()
    {
        using var run = Start("""
            Private Sub Command1_Click()
                Me.Enabled = False
                Form2.Show vbModal
                Debug.Print "after, form1 enabled " & Me.Enabled
            End Sub

            """);
        run.Click("Form1");
        Pump();
        run.Click("Form2");
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("f2 initialize", "f2 load", "f2 queryunload 1", "f2 unload", "after, form1 enabled False");
    }

    [AvaloniaTheory]
    [InlineData("    Me.Hide\n")]
    [InlineData("    Me.Visible = False\n")]
    public void Show_vbModal_returns_when_the_form_hides_and_it_stays_loaded(string hide)
    {
        using var run = Start(ShowsForm2Modally.Replace("Me.Enabled", "Me.Enabled & \", form2 visible \" & Form2.Visible"),
            Form2Clicked(hide + "    Debug.Print \"f2 hid itself\"\n"));
        run.Click("Form1");
        Pump();
        run.Click("Form2");
        Pump();

        run.Errors.Should().BeEmpty();
        // The statement after the Show runs once the handler that hid the form has returned (measured).
        run.Printed.Should().Equal("before", "f2 initialize", "f2 load", "f2 hid itself",
            "after, form1 enabled True, form2 visible False");
        run.Closed.Should().NotContain("Form2", "a hidden form is still loaded");
    }

    [AvaloniaFact]
    public void Under_a_modal_form_a_modeless_Show_is_401_hiding_another_form_402_and_its_own_modal_Show_405()
    {
        using var run = Start(ShowsForm2Modally, Form2Clicked("""
                On Error Resume Next
                Form1.Show
                Debug.Print "Form1.Show " & Err.Number & " " & Err.Description
                Err.Clear
                Me.Show
                Debug.Print "Me.Show " & Err.Number
                Err.Clear
                Form1.Hide
                Debug.Print "Form1.Hide " & Err.Number & " " & Err.Description
                Err.Clear
                Me.Show vbModal
                Debug.Print "Me.Show vbModal " & Err.Number & " " & Err.Description
                Err.Clear
                Load Form3
                Debug.Print "Load Form3 " & Err.Number
                Unload Me

            """), form3Code: "");
        run.Click("Form1");
        Pump();
        run.Click("Form2");
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("before", "f2 initialize", "f2 load",
            "Form1.Show 401 Can't show non-modal form when modal form is displayed",
            "Me.Show 401",
            "Form1.Hide 402 Must close or hide topmost modal form first",
            "Me.Show vbModal 405 Unable to show modal form within this context",
            "Load Form3 0",
            "f2 queryunload 1", "f2 unload", "after, form1 enabled True");
    }

    [AvaloniaFact]
    public void A_form_shown_modelessly_turns_modal_with_no_error()
    {
        // VB6's documented 400, "Form already displayed; can't show modally", never came (measured).
        using var run = Start("""
            Private shown As Boolean
            Private Sub Command1_Click()
                If Not shown Then
                    shown = True
                    Form2.Show
                    Exit Sub
                End If
                Form2.Show vbModal
                Debug.Print "after " & Err.Number
            End Sub

            """);
        run.Click("Form1");
        Pump();
        run.Click("Form1");
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("f2 initialize", "f2 load");
        run.Opened["Form1"].HeldBy.Should().NotBeNull();

        run.Click("Form2");
        Pump();
        run.Printed.Should().Equal("f2 initialize", "f2 load", "f2 queryunload 1", "f2 unload", "after 0");
    }

    [AvaloniaFact]
    public void A_modal_form_showing_another_disables_both_beneath_and_each_returns_innermost_first()
    {
        // Measured: each modal returns innermost first, and Form1's Timer ticks throughout. Which forms read Enabled under the
        // inner one was measured on the console for one modal form only; for two it is inferred from that.
        using var run = Start(ShowsForm2Modally, Form2Clicked("""
                Form3.Show vbModal
                Debug.Print "f2 after f3, form1 enabled " & Form1.Enabled & ", form2 enabled " & Me.Enabled
                Unload Me

            """), form3Code: """
            Private Sub Command1_Click()
                Unload Me
            End Sub

            """);
        run.Click("Form1");
        Pump();
        run.Click("Form2");
        Pump();

        run.Opened["Form1"].HeldBy.Should().NotBeNull();
        run.Opened["Form2"].HeldBy.Should().NotBeNull();
        run.Opened["Form3"].HeldBy.Should().BeNull();

        run.Click("Form3");
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("before", "f2 initialize", "f2 load", "f2 after f3, form1 enabled False, form2 enabled True",
            "f2 queryunload 1", "f2 unload", "after, form1 enabled True");
    }

    [AvaloniaFact]
    public void A_form_beneath_the_modal_one_can_turn_modal_over_it_and_is_disabled_again_after()
    {
        // Measured: Form1, beneath Form2's modal, turns modal over it with no error, and Form2's statement returns once Form1
        // hides itself. Form1 being disabled again after is inferred from the single modal's measurement.
        using var run = Start("""
            Private Sub Command1_Click()
                Debug.Print "f1 click, enabled " & Me.Enabled
                If Not Form2.Visible Then
                    Form2.Show vbModal
                    Debug.Print "after form2's modal"
                Else
                    Me.Hide
                End If
            End Sub

            """, Form2Clicked("""
                Form1.Show vbModal
                Debug.Print "f2 after form1's modal, form1 enabled " & Form1.Enabled & ", form2 enabled " & Me.Enabled
                Unload Me

            """));
        run.Click("Form1");
        Pump();
        run.Click("Form2");
        Pump();

        run.Opened["Form1"].HeldBy.Should().BeNull("Form1 is modal now");
        run.Opened["Form2"].HeldBy.Should().NotBeNull();

        run.Click("Form1");
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("f1 click, enabled True", "f2 initialize", "f2 load", "f1 click, enabled True",
            "f2 after form1's modal, form1 enabled False, form2 enabled True", "f2 queryunload 1", "f2 unload",
            "after form2's modal");
    }

    [AvaloniaTheory]
    [InlineData("2")]
    [InlineData("1.5")]
    [InlineData("True")]
    public void A_style_other_than_0_or_1_is_5_after_the_form_has_loaded(string style)
    {
        using var run = Start($$"""
            Private Sub Command1_Click()
                On Error Resume Next
                Form2.Show {{style}}
                Debug.Print Err.Number & " " & Err.Description & ", visible " & Form2.Visible
            End Sub

            """);
        run.Click("Form1");
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("f2 initialize", "f2 load", "5 Invalid procedure call or argument, visible False");
    }

    [AvaloniaTheory]
    [InlineData("0.4", false)]
    [InlineData("0.6", true)]
    [InlineData("\"1\"", true)]
    public void The_style_converts_as_CInt_does(string style, bool modal)
    {
        using var run = Start($$"""
            Private Sub Command1_Click()
                Form2.Show {{style}}
                Debug.Print "returned"
            End Sub

            """);
        run.Click("Form1");
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal(modal ? ["f2 initialize", "f2 load"] : ["f2 initialize", "f2 load", "returned"]);
        run.Opened["Form2"].IsVisible.Should().BeTrue();
    }

    [AvaloniaFact]
    public void Unspaced_parenthesised_member_arguments_show_and_move()
    {
        // Oracle: Several forms, measurement 13 (#1064), native and p-code.
        using var run = Start("""
            Private Sub Command1_Click()
                Form2.Show(0)
                Text1.Move(100)
                Debug.Print "left " & Text1.Left
            End Sub

            """);
        run.Click("Form1");
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("f2 initialize", "f2 load", "left 100");
        run.Opened["Form2"].IsVisible.Should().BeTrue();
    }

    [AvaloniaTheory]
    [InlineData("Form2.Show(1)")]
    [InlineData("Form2.Show (1)")]
    [InlineData("Call Form2.Show(vbModal)")]
    [InlineData("With Form2: .Show vbModal: End With")]
    public void Every_statement_spelling_of_a_modal_Show_waits(string show)
    {
        using var run = Start($$"""
            Private Sub Command1_Click()
                {{show}}
                Debug.Print "returned"
            End Sub

            """);
        run.Click("Form1");
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("f2 initialize", "f2 load");
        run.Opened["Form1"].HeldBy.Should().NotBeNull();

        run.Click("Form2");
        Pump();
        run.Printed.Should().Equal("f2 initialize", "f2 load", "f2 queryunload 1", "f2 unload", "returned");
    }

    [AvaloniaFact]
    public void A_modal_Show_in_an_event_the_code_raised_holds_the_raiser_too_until_the_form_has_gone()
    {
        // Text1_Change runs inside the assignment that raised it (#766), so the raiser's next statement waits for it, modal
        // form and all, as every statement after a call waits for the call.
        using var run = Start("""
            Private Sub Command1_Click()
                Text1.Text = "x"
                Debug.Print "after the assignment"
            End Sub
            Private Sub Text1_Change()
                Debug.Print "change"
                Form2.Show vbModal
                Debug.Print "change after the modal"
            End Sub

            """);
        run.Click("Form1");
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("change", "f2 initialize", "f2 load");

        run.Click("Form2");
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("change", "f2 initialize", "f2 load", "f2 queryunload 1", "f2 unload",
            "change after the modal", "after the assignment");
    }

    [AvaloniaFact]
    public void End_inside_the_modal_form_ends_the_run_and_the_statement_after_the_Show_never_runs()
    {
        using var run = Start(ShowsForm2Modally, Form2Clicked("    End\n"));
        run.Click("Form1");
        Pump();
        run.Click("Form2");
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("before", "f2 initialize", "f2 load");
        run.Task.IsCompleted.Should().BeTrue();
    }

    [AvaloniaFact]
    public void The_run_ended_from_outside_under_a_modal_form_runs_nothing_after_the_Show()
    {
        using var run = Start(ShowsForm2Modally);
        run.Click("Form1");
        Pump();

        run.Cancel.Cancel();
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("before", "f2 initialize", "f2 load");
        run.Task.IsCompleted.Should().BeTrue();
    }

    [AvaloniaFact]
    public void The_startup_forms_Load_can_show_a_form_modally_and_the_startup_form_shows_after()
    {
        using var run = Start("""
            Private Sub Form_Load()
                Debug.Print "f1 load"
                Form2.Show vbModal
                Debug.Print "f1 load after the modal"
            End Sub

            """);

        run.Printed.Should().Equal("f1 load", "f2 initialize", "f2 load");
        run.Opened.Should().NotContainKey("Form1", "the startup form is shown once its Load has returned (measured)");

        run.Click("Form2");
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("f1 load", "f2 initialize", "f2 load", "f2 queryunload 1", "f2 unload", "f1 load after the modal");
        run.Opened["Form1"].IsVisible.Should().BeTrue();
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
    public void The_run_ends_when_the_last_loaded_form_unloads_and_the_Terminates_come_in_project_form_order()
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

    [AvaloniaFact]
    public void End_with_two_forms_showing_closes_both_and_raises_no_Unload_or_Terminate()
    {
        using var run = Start("""
            Private n As Integer
            Private Sub Command1_Click()
                n = n + 1
                If n = 1 Then Form2.Show Else End
            End Sub
            """ + "\n" + Form1Terminate);

        run.Click("Form1");
        Pump();
        run.Opened.Should().ContainKey("Form2");

        run.Click("Form1");
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("f2 initialize", "f2 load");
        run.Closed.Should().BeEquivalentTo(["Form1", "Form2"]);
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
