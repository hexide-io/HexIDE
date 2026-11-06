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
/// SetFocus (#1061). Every expectation was measured on vb6.exe, native and p-code agreeing, with compiled probes on the
/// oracle guest's console session: the oracle's <i>SetFocus</i>.
/// </summary>
public class SetFocusTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private static string Header(string name, string controls) =>
        "VERSION 5.00\r\n"
      + $"Begin VB.Form {name} \r\n"
      + $"   Caption         =   \"{name}\"\r\n"
      + "   ClientHeight    =   3000\r\n"
      + "   ClientWidth     =   4000\r\n"
      + controls
      + "End\r\n"
      + $"Attribute VB_Name = \"{name}\"\r\n";

    private static string TextBox(string name, int top, string extra = "") =>
        $"   Begin VB.TextBox {name} \r\n"
      + "      Height          =   375\r\n"
      + "      Left            =   120\r\n"
      + $"      Text            =   \"{name}\"\r\n"
      + $"      Top             =   {top}\r\n"
      + "      Width           =   1215\r\n"
      + extra
      + "   End\r\n";

    private static readonly string Form1Controls =
        TextBox("Text1", 120)
      + TextBox("Text2", 600)
      + TextBox("Text3", 1080, "      Enabled         =   0   'False\r\n")
      + TextBox("Text4", 1560, "      Visible         =   0   'False\r\n")
      + "   Begin VB.Label Label1 \r\n"
      + "      Caption         =   \"label\"\r\n"
      + "      Height          =   255\r\n"
      + "      Left            =   1440\r\n"
      + "      Top             =   120\r\n"
      + "      Width           =   1215\r\n"
      + "   End\r\n"
      + "   Begin VB.CommandButton Command1 \r\n"
      + "      Caption         =   \"Command1\"\r\n"
      + "      Height          =   495\r\n"
      + "      Left            =   1440\r\n"
      + "      Top             =   600\r\n"
      + "      Width           =   1215\r\n"
      + "   End\r\n";

    // Each handler logs its name, so the order they come in can be read.
    private const string Logged = """
        Private Sub Text1_GotFocus()
            Debug.Print "Text1_GotFocus"
        End Sub
        Private Sub Text1_LostFocus()
            Debug.Print "Text1_LostFocus"
        End Sub
        Private Sub Text2_GotFocus()
            Debug.Print "Text2_GotFocus"
        End Sub
        Private Sub Text2_LostFocus()
            Debug.Print "Text2_LostFocus"
        End Sub

        """;

    private const string Form2Code = """
        Private Sub Text1_GotFocus()
            Debug.Print "Form2.Text1_GotFocus"
        End Sub

        """;

    private sealed class Run : IDisposable
    {
        public required CancellationTokenSource Cancel { get; init; }
        public required List<string> Printed { get; init; }
        public required List<Exception> Errors { get; init; }
        public required Dictionary<string, VBFormRuntime> Opened { get; init; }
        public required Action Detach { get; init; }

        public void Click() => Opened["Form1"].GetLogicalDescendants().OfType<VBCommandButton>().Single()
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        public Control Focused(string form) => (Control)TopLevel.GetTopLevel(Opened[form])!.FocusManager!.GetFocusedElement()!;

        public void Dispose()
        {
            Cancel.Cancel();
            Pump();
            Detach();
        }
    }

    private static Run Start(string form1Code, string? controls = null, bool modal = false)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form1 = new FormDeserializer().Deserialize(project,
            (Header("Form1", controls ?? Form1Controls) + Logged + form1Code).ReplaceLineEndings("\r\n"), new NullSink())!;
        project.AddForm(form1);
        project.AddForm(new FormDeserializer().Deserialize(project,
            (Header("Form2", TextBox("Text1", 120)) + Form2Code).ReplaceLineEndings("\r\n"), new NullSink())!);

        var printed = new List<string>();
        var errors = new List<Exception>();
        var opened = new Dictionary<string, VBFormRuntime>();
        Action<string> print = printed.Add;
        void OnFaulted(VBWindowContext _, Exception e) => errors.Add(e);
        void OnRunTimeError(VBWindowContext _, VBRunTimeException e) => errors.Add(e);
        VBDebugConsole.Output += print;
        VBWindowContext.Faulted += OnFaulted;
        VBWindowContext.RunTimeError += OnRunTimeError;
        var watch = Window.WindowOpenedEvent.AddClassHandler<VBFormRuntime>((w, _) => opened[w.Title ?? ""] = w);
        var cancel = new CancellationTokenSource();
        if (modal)
        {
            var main = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
            main.UpdateCode("Sub Main()\n    Form1.Show vbModal\nEnd Sub\n");
            project.AddModule(main);
            project.StartsAtSubMain = true;
            _ = VBLoader.RunMadeProgram(project, cancel.Token, out _);
        }
        else
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
            },
        };
    }

    private static void Pump()
    {
        for (var i = 0; i < 20; i++)
            Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Before_the_form_is_shown_SetFocus_is_5_on_a_control_and_on_the_form()
    {
        using var run = Start("""
            Private Sub Form_Load()
                On Error Resume Next
                Text2.SetFocus
                Debug.Print "control " & Err.Number
                Err.Clear
                Me.SetFocus
                Debug.Print "form " & Err.Number
            End Sub
            """);

        run.Errors.Should().BeEmpty();
        run.Printed.Should().StartWith(["control 5", "form 5"]);
    }

    [AvaloniaFact]
    public void The_control_has_the_focus_at_once_and_its_events_come_after_the_handler()
    {
        using var run = Start("""
            Private Sub Command1_Click()
                Text1.SetFocus
            End Sub
            Public Sub Move()
                Debug.Print "begins"
                Text2.SetFocus
                Debug.Print "after"
            End Sub
            """);
        run.Click();
        Pump();
        run.Printed.Clear();

        var form = run.Opened["Form1"];
        form.ExecuteSub("Move");
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("begins", "after", "Text1_LostFocus", "Text2_GotFocus");
        VBProps.GetName(run.Focused("Form1")).Should().Be("Text2");
    }

    [AvaloniaTheory]
    [InlineData("Text3", "disabled")]
    [InlineData("Text4", "invisible")]
    public void On_a_disabled_or_invisible_control_it_is_5_and_nothing_changes(string control, string why)
    {
        using var run = Start($$"""
            Private Sub Command1_Click()
                On Error Resume Next
                Text1.SetFocus
                {{control}}.SetFocus
                Debug.Print "{{why}} " & Err.Number
            End Sub
            """);

        run.Click();
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Contain($"{why} 5");
        VBProps.GetName(run.Focused("Form1")).Should().Be("Text1");
    }

    [AvaloniaFact]
    public void On_a_control_disabled_at_run_time_it_is_5()
    {
        using var run = Start("""
            Private Sub Command1_Click()
                On Error Resume Next
                Text2.Enabled = False
                Text2.SetFocus
                Debug.Print "disabled " & Err.Number
            End Sub
            """);

        run.Click();
        Pump();

        run.Printed.Should().Contain("disabled 5");
    }

    [AvaloniaFact]
    public void On_a_control_of_a_form_loaded_but_not_shown_it_is_5()
    {
        using var run = Start("""
            Private Sub Command1_Click()
                On Error Resume Next
                Load Form2
                Form2.Text1.SetFocus
                Debug.Print "hidden form " & Err.Number
            End Sub
            """);

        run.Click();
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Contain("hidden form 5");
        run.Printed.Should().NotContain("Form2.Text1_GotFocus");
    }

    [AvaloniaFact]
    public void On_the_control_that_has_the_focus_it_raises_nothing()
    {
        using var run = Start("""
            Private Sub Command1_Click()
                Text2.SetFocus
            End Sub
            Public Sub Again()
                Text2.SetFocus
                Debug.Print "again"
            End Sub
            """);
        run.Click();
        Pump();
        run.Printed.Clear();

        run.Opened["Form1"].ExecuteSub("Again");
        Pump();

        run.Printed.Should().Equal("again");
    }

    [AvaloniaFact]
    public void The_forms_own_SetFocus_on_a_shown_form_raises_nothing()
    {
        using var run = Start("""
            Private Sub Command1_Click()
                Text2.SetFocus
            End Sub
            Public Sub Itself()
                Me.SetFocus
                Debug.Print "form " & Err.Number
            End Sub
            """);
        run.Click();
        Pump();
        run.Printed.Clear();

        run.Opened["Form1"].ExecuteSub("Itself");
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("form 0");
        VBProps.GetName(run.Focused("Form1")).Should().Be("Text2");
    }

    [AvaloniaFact]
    public void A_shown_forms_control_takes_the_focus_from_another_form()
    {
        using var run = Start("""
            Private Sub Command1_Click()
                Form2.Show
                Form2.Text1.SetFocus
                Debug.Print "after " & Err.Number
            End Sub
            """);

        run.Click();
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().ContainInOrder("after 0", "Form2.Text1_GotFocus");
        VBProps.GetName(run.Focused("Form2")).Should().Be("Text1");
    }

    [AvaloniaFact]
    public void A_Label_has_no_SetFocus_and_is_refused_as_not_implemented()
    {
        using var run = Start("""
            Private Sub Command1_Click()
                Label1.SetFocus
            End Sub
            """);

        run.Click();
        Pump();

        run.Errors.Should().ContainSingle().Which.Should().BeOfType<NotImplementedException>()
            .Which.Message.Should().Be("The SetFocus method is not implemented for Label1");
    }
    // #2436: the issue's console measurements, native and p-code agreeing.
    [AvaloniaTheory]
    [InlineData("", "Text1")]
    [InlineData("Enabled = 0", "Text2")]
    [InlineData("Visible = 0", "Text2")]
    [InlineData("TabStop = 0", "Text2")]
    [InlineData("Label", "Text1")]
    public void Showing_focuses_the_first_eligible_control_after_Activate(string first, string expected)
    {
        var controls = TextBox("Text2", 600, "      TabIndex = 2\n")
            + TextBox("Text1", 120, "      TabIndex = 1\n" + (first == "Label" ? "" : "      " + first + "\n"));
        if (first == "Label")
            controls += "   Begin VB.Label Label1\n      TabIndex = 0\n   End\n";
        using var run = Start("""
            Private Sub Form_Load()
                Debug.Print "load"
            End Sub
            Private Sub Form_Activate()
                Debug.Print "activate"
            End Sub
            """, controls);

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("load", "activate", expected + "_GotFocus");
        VBProps.GetName(run.Focused("Form1")).Should().Be(expected);
    }

    [AvaloniaFact]
    public void Rejected_SetFocus_in_Load_still_leaves_the_first_control_focused()
    {
        using var run = Start("""
            Private Sub Form_Load()
                On Error Resume Next
                Text2.SetFocus
                Debug.Print "error " & Err.Number
            End Sub
            Private Sub Form_Activate()
                Debug.Print "activate"
            End Sub
            """);

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("error 5", "activate", "Text1_GotFocus");
        VBProps.GetName(run.Focused("Form1")).Should().Be("Text1");
    }

    [AvaloniaFact]
    public void Show_then_SetFocus_in_Load_raises_Activate_and_both_GotFocus_inside_Load()
    {
        using var run = Start("""
            Private Sub Form_Load()
                Debug.Print "load"
                Me.Show
                Text2.SetFocus
                Debug.Print "loaded"
            End Sub
            Private Sub Form_Activate()
                Debug.Print "activate"
            End Sub
            """);

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("load", "activate", "Text1_GotFocus", "Text1_LostFocus", "Text2_GotFocus", "loaded");
        VBProps.GetName(run.Focused("Form1")).Should().Be("Text2");
    }

    [AvaloniaFact]
    public void Modal_Show_from_Main_focuses_the_first_control_after_Activate()
    {
        using var run = Start("""
            Private Sub Form_Load()
                Debug.Print "load"
            End Sub
            Private Sub Form_Activate()
                Debug.Print "activate"
            End Sub
            """, modal: true);

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("load", "activate", "Text1_GotFocus");
        VBProps.GetName(run.Focused("Form1")).Should().Be("Text1");
    }

}
