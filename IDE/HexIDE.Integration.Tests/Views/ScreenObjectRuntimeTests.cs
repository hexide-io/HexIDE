using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The members of <c>Screen</c> a running form answers (#2431): <c>ActiveForm</c>, <c>ActiveControl</c>, and the pointer
/// <c>MousePointer</c> gives every form. Measured on vb6.exe, native and p-code agreeing, on the oracle guest's console: the
/// oracle's "The Screen object".
/// </summary>
public class ScreenObjectRuntimeTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string Report = """
        Public Sub Report()
            If Screen.ActiveForm Is Nothing Then Debug.Print "no form" Else Debug.Print "form " & Screen.ActiveForm.Name
            If Screen.ActiveControl Is Nothing Then Debug.Print "no control" Else Debug.Print "control " & Screen.ActiveControl.Name
        End Sub
        Public Sub Focus_Text1()
            Text1.SetFocus
        End Sub
        Public Sub Focus_Command1()
            Command1.SetFocus
        End Sub
        Public Sub Hourglass()
            Screen.MousePointer = vbHourglass
        End Sub
        Public Sub Default()
            Screen.MousePointer = vbDefault
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

        public T Named<T>(string name) where T : Control =>
            Window.GetLogicalDescendants().OfType<T>().Single(c => VBProps.GetName(c) == name);

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
    public void ActiveForm_is_the_shown_form_and_ActiveControl_the_control_with_the_focus()
    {
        using var run = Start(Form(TextAndButton));

        // VB6 gives Text1, first in tab order, the focus as the form is shown; HexIDE gives no control the focus then (#2436).
        run.Call("Focus_Text1");
        run.Call("Report").Should().Equal("form Form1", "control Text1");
        run.Call("Focus_Command1");
        run.Call("Report").Should().Equal("form Form1", "control Command1");
        run.Errors.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void ActiveControl_is_Nothing_on_a_form_with_nothing_that_can_take_the_focus()
    {
        using var run = Start(Form(Control("Label", "Label1", 120, 0)));

        run.Call("Report").Should().Equal("form Form1", "no control");
        run.Errors.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void MousePointer_gives_every_control_on_the_form_its_pointer_until_it_is_0_again()
    {
        using var run = Start(Form(TextAndButton));
        var text = run.Named<VBTextBox>("Text1");
        var button = run.Named<VBCommandButton>("Command1");
        var textBefore = text.Cursor;
        var buttonBefore = button.Cursor;

        run.Call("Hourglass");
        text.Cursor.Should().NotBeNull().And.BeSameAs(button.Cursor, "one pointer covers the form").And.BeSameAs(run.Window.Cursor);
        text.Cursor.Should().NotBeSameAs(textBefore, "the TextBox's I-beam gives way to it");
        button.Cursor.Should().NotBeSameAs(buttonBefore, "and so does the pointer the button's own MousePointer gives it");

        run.Call("Default");
        text.Cursor.Should().BeSameAs(textBefore);
        button.Cursor.Should().BeSameAs(buttonBefore);
        run.Errors.Should().BeEmpty();
    }
}
