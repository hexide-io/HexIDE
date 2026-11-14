using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Integration.Tests.Views;

// #3932: docs/vb6-fidelity-oracle/a-forms-resize-activate-and-deactivate.md, runs 1, 2 and 3a.
// Headless activation follows the handler even without posting; these pin order, not desktop posting.
// Activate models the window notification from a click, rather than driving a window manager.
public class FormSwitchOrderTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string Report = """
        Private Sub Form_Activate()
            Debug.Print Me.Name & " activate"
        End Sub
        Private Sub Form_Deactivate()
            Debug.Print Me.Name & " deactivate"
        End Sub
        Private Sub Text1_GotFocus()
            Debug.Print Me.Name & " Text1 gotfocus"
        End Sub
        Private Sub Text1_LostFocus()
            Debug.Print Me.Name & " Text1 lostfocus"
        End Sub
        Private Sub Form_GotFocus()
            Debug.Print Me.Name & " form gotfocus"
        End Sub
        Private Sub Form_LostFocus()
            Debug.Print Me.Name & " form lostfocus"
        End Sub
        Private Sub Form_Load()
            Debug.Print "loaded"
        End Sub
        Private Sub Command1_GotFocus()
            Debug.Print "Command1 gotfocus"
        End Sub
        Public Sub HideText()
            Text1.Visible = False
            Debug.Print "after hide"
        End Sub
        Public Sub DisableText()
            Text1.Enabled = False
            Debug.Print "after disable"
        End Sub
        Public Sub FocusButton()
            Command1.SetFocus
            Debug.Print "after setfocus"
        End Sub
        Public Sub ShowOther()
            Form2.Show
            Debug.Print "after"
        End Sub
        Public Sub HideOther()
            Form2.Hide
            Debug.Print "after"
        End Sub
        Public Sub FocusMe()
            Me.SetFocus
            Debug.Print "after"
        End Sub

        """;

    private static string Form(string controls) =>
        "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   Caption         =   \"Form1\"\r\n   ClientHeight    =   3000\r\n"
      + "   ClientWidth     =   4000\r\n" + controls + "End\r\nAttribute VB_Name = \"Form1\"\r\n" + Report;

    private static string Control(string type, string name, int top, int tabIndex) =>
        $"   Begin VB.{type} {name} \r\n"
      + "      Height          =   315\r\n"
      + "      Left            =   120\r\n"
      + $"      TabIndex        =   {tabIndex}\r\n"
      + $"      Top             =   {top}\r\n"
      + "      Width           =   1200\r\n"
      + "   End\r\n";

    private static readonly string TextAndButton = Control("TextBox", "Text1", 120, 0) + Control("CommandButton", "Command1", 600, 1);

    private sealed class Run : IDisposable
    {
        public required CancellationTokenSource Cancel { get; init; }
        public required List<string> Printed { get; init; }
        public required List<Exception> Errors { get; init; }
        public required Window Window { get; init; }
        public required Action Detach { get; init; }

        public List<string> Call(string sub)
        {
            var before = Printed.Count;
            ((VBFormRuntime)Window).ExecuteSub(sub);
            Pump();
            return Printed.Skip(before).ToList();
        }

        public void Dispose()
        {
            Cancel.Cancel();
            Pump();
            Window.Close();
            Detach();
        }
    }

    private static Run Start(string form)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form1 = new FormDeserializer().Deserialize(project, form.ReplaceLineEndings("\r\n"), new NullSink())!;
        project.AddForm(form1);
        project.AddForm(new FormDeserializer().Deserialize(project,
            Form(TextAndButton).Replace("Form1", "Form2").ReplaceLineEndings("\r\n"), new NullSink())!);

        var printed = new List<string>();
        var errors = new List<Exception>();
        Action<string> print = text => printed.AddRange(text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
        void OnFaulted(VBWindowContext _, Exception e) => errors.Add(e);
        void OnRunTimeError(VBWindowContext _, VBRunTimeException e) => errors.Add(e);
        VBDebugConsole.Output += print;
        VBWindowContext.Faulted += OnFaulted;
        VBWindowContext.RunTimeError += OnRunTimeError;
        var cancel = new CancellationTokenSource();
        _ = VBLoader.RunForm(form1, cancel.Token, out var window);
        Pump();
        return new Run
        {
            Cancel = cancel,
            Printed = printed,
            Errors = errors,
            Window = window,
            Detach = () =>
            {
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
    public void Next_control_flushes_previous_controls_pending_lostfocus()
    {
        using var run = Start(Form(TextAndButton));
        run.Window.FocusManager!.Focus(null);
        Pump();
        var before = run.Printed.Count;
        run.Call("FocusButton");
        run.Printed.Skip(before).Where(text => text.Contains("focus")).Should().Equal(
            "after setfocus", "Form1 Text1 lostfocus", "Command1 gotfocus");
        run.Errors.Should().BeEmpty();
    }

    // #3932 second-round regression: preserve main's hide/disable timing, independently of form switches.
    [AvaloniaTheory]
    [InlineData("HideText", "after hide")]
    [InlineData("DisableText", "after disable")]
    public void Unavailable_focused_control_loses_focus_before_next_control(string action, string after)
    {
        using var run = Start(Form(TextAndButton));
        run.Printed.Should().Contain("Form1 Text1 gotfocus");
        run.Call(action).Should().Equal("Form1 Text1 lostfocus", after);
        run.Call("FocusButton").Should().Equal("after setfocus", "Command1 gotfocus");
        run.Errors.Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData("show")]
    [InlineData("reshow")]
    [InlineData("focus")]
    [InlineData("hide")]
    [InlineData("window")]
    public void Switching_forms_orders_focus_around_activation(string action)
    {
        using var run = Start(Form(TextAndButton));
        if (action != "show")
            run.Call("ShowOther");
        if (action == "reshow")
            run.Call("HideOther");
        var before = run.Printed.Count;
        if (action == "window")
        {
            run.Window.Activate();
            Pump();
        }
        else
            run.Call(action is "show" or "reshow" ? "ShowOther" : action == "focus" ? "FocusMe" : "HideOther");
        var from = action is "show" or "reshow" ? "Form1" : "Form2";
        var to = action is "show" or "reshow" ? "Form2" : "Form1";
        var expected = new List<string>();
        if (action == "show") expected.Add("loaded");
        if (action != "window") expected.Add("after");
        expected.AddRange([from + " Text1 lostfocus", from + " deactivate", to + " activate", to + " Text1 gotfocus"]);
        run.Printed.Skip(before).Should().Equal(expected);
        run.Errors.Should().BeEmpty();
    }
}
