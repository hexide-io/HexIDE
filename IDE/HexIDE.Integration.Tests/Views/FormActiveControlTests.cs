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

// Measured in #2437 and docs/vb6-fidelity-oracle/a-forms-own-activecontrol.md (#2279).
public class FormActiveControlTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string Report = """
        Private Sub Form_Load()
            Debug.Print "loaded"
        End Sub
        Public Sub Focus_Text1()
            Text1.SetFocus
        End Sub
        Public Sub Focus_Command1()
            Command1.SetFocus
        End Sub
        Public Sub Report()
            If Me.ActiveControl Is Nothing Then Debug.Print "none" Else Debug.Print Me.ActiveControl.Name
        End Sub
        Public Sub HideAndReportScreen()
            Me.Hide
            Debug.Print Screen.ActiveForm.Name
        End Sub
        Public Sub HideAndReport()
            Me.Hide
            Debug.Print Me.ActiveControl.Name
            Debug.Print Screen.ActiveForm.Name
        End Sub
        Public Sub LoadAndReport()
            Load Form2
            If Form2.ActiveControl Is Nothing Then Debug.Print "none"
        End Sub
        Public Sub OtherAndReport()
            Form2.Show
            Form2.Text1.SetFocus
            Debug.Print Me.ActiveControl.Name
        End Sub
        Public Sub ReloadAndReport()
            Form2.Show
            Form2.Text1.SetFocus
            Unload Form2
            If Form2.ActiveControl Is Nothing Then Debug.Print "none"
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
    public void Own_ActiveControl_tracks_the_last_control_focused()
    {
        using var run = Start(Form(TextAndButton));
        run.Call("Focus_Text1");
        run.Call("Report").Should().Equal("Text1");
        run.Call("Focus_Command1");
        run.Call("Report").Should().Equal("Command1");
        run.Errors.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void An_inactive_form_remembers_its_control()
    {
        using var run = Start(Form(TextAndButton));
        run.Call("Focus_Command1");
        run.Call("OtherAndReport").Should().Equal("loaded", "Command1");
        run.Errors.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void Hiding_the_last_form_retains_its_control_and_Screens_active_form()
    {
        using var run = Start(Form(TextAndButton));
        run.Call("Focus_Command1");
        run.Call("HideAndReport").Should().Equal("Command1", "Form1");
        run.Errors.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void Hiding_the_last_visible_form_keeps_Screens_active_form()
    {
        using var run = Start(Form(TextAndButton));
        run.Call("HideAndReportScreen").Should().Equal("Form1");
        run.Errors.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void A_loaded_but_never_shown_form_has_no_active_control()
    {
        using var run = Start(Form(TextAndButton));
        run.Call("LoadAndReport").Should().Equal("loaded", "none");
        run.Errors.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void Reading_an_unloaded_forms_control_loads_it_with_no_active_control()
    {
        using var run = Start(Form(TextAndButton));
        run.Call("ReloadAndReport").Should().Equal("loaded", "loaded", "none");
        run.Errors.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void A_shown_form_with_only_a_Label_has_no_active_control()
    {
        using var run = Start(Form(Control("Label", "Label1", 120, 0)));
        run.Call("Report").Should().Equal("none");
        run.Errors.Should().BeEmpty();
    }
}
