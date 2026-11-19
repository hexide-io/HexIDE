using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Integration.Tests.Views;

// VB6 SP6, native and p-code: https://github.com/hexide-io/HexIDE/issues/1563,
// measured in oracle PR #4796. These programs enter through the same form loader as F5.
public class ControlObjectStoreTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string Designer = """
        VERSION 5.00
        Begin VB.Form Form1
            Caption = "Form1"
            Begin VB.Timer Timer1
                Left = 0
            End
            Begin VB.HScrollBar HScroll1
                Left = 0
            End
            Begin VB.VScrollBar VScroll1
                Left = 0
            End
            Begin VB.CommandButton Command1
                Left = 0
            End
            Begin VB.PictureBox Picture1
                Left = 0
            End
            Begin VB.TextBox Text1
                Left = 0
            End
            Begin VB.ComboBox Combo1
                Left = 0
            End
            Begin VB.ListBox List1
                Left = 0
            End
        End
        Attribute VB_Name = "Form1"

        """;

    private static (List<string> Printed, List<Exception> Errors) Run(string load, string designer = Designer)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var code = "Option Explicit\r\nPrivate Sub Form_Load()\r\n" + load + "\r\nEnd Sub\r\n";
        var form1 = new FormDeserializer().Deserialize(project, (designer + code).ReplaceLineEndings("\r\n"), new NullSink())!;
        project.AddForm(form1);
        var class1 = new ModuleDefinition(project, "Class1", ModuleKind.ClassModule);
        class1.UpdateCode("Public N As Integer\r\n");
        project.AddModule(class1);

        var printed = new List<string>();
        var errors = new List<Exception>();
        Action<string> print = printed.Add;
        void OnFaulted(VBWindowContext _, Exception e) => errors.Add(e);
        void OnRunTimeError(VBWindowContext _, VBRunTimeException e) => errors.Add(e);
        void OnCompileError(VBWindowContext _, VBCompileErrorException e) => errors.Add(e);
        VBDebugConsole.Output += print;
        VBWindowContext.Faulted += OnFaulted;
        VBWindowContext.RunTimeError += OnRunTimeError;
        VBWindowContext.CompileError += OnCompileError;
        var cancel = new CancellationTokenSource();
        try
        {
            _ = VBLoader.RunForm(form1, cancel.Token, out _);
            Pump();
        }
        finally
        {
            cancel.Cancel();
            Pump();
            VBDebugConsole.Output -= print;
            VBWindowContext.Faulted -= OnFaulted;
            VBWindowContext.RunTimeError -= OnRunTimeError;
            VBWindowContext.CompileError -= OnCompileError;
        }
        return (printed, errors);
    }

    private static void Pump()
    {
        for (var i = 0; i < 20; i++)
            Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData("TextBox", "", "Text1", 0, "Text1")]
    [InlineData("TextBox", "", "List1", 13, "Nothing")]
    [InlineData("TextBox", "Set target = Text1", "List1", 13, "Text1")]
    [InlineData("HScrollBar", "", "HScroll1", 0, "HScroll1")]
    [InlineData("HScrollBar", "", "VScroll1", 13, "Nothing")]
    [InlineData("VScrollBar", "", "HScroll1", 13, "Nothing")]
    [InlineData("ListBox", "", "Combo1", 13, "Nothing")]
    [InlineData("CommandButton", "", "Text1", 13, "Nothing")]
    [InlineData("Control", "", "Text1", 0, "Text1")]
    [InlineData("Control", "", "Timer1", 0, "Timer1")]
    [InlineData("Object", "", "Text1", 0, "Text1")]
    [InlineData("Form", "", "Me", 0, "Me")]
    [InlineData("Form", "", "Text1", 13, "Nothing")]
    [InlineData("Class1", "", "Text1", 13, "Nothing")]
    [InlineData("TextBox", "", "Me", 13, "Nothing")]
    [InlineData("TextBox", "", "New Class1", 13, "Nothing")]
    [InlineData("Variant", "", "Text1", 0, "Text1")]
    [InlineData("TextBox", "Dim v As Variant: Set v = Text1", "v", 0, "Text1")]
    [InlineData("TextBox", "Dim v As Variant: Set v = List1", "v", 13, "Nothing")]
    [InlineData("TextBox", "Dim o As Object: Set o = List1", "o", 13, "Nothing")]
    [InlineData("VB.TextBox", "", "Text1", 0, "Text1")]
    [InlineData("VB.TextBox", "", "List1", 13, "Nothing")]
    public void Set_checks_the_actual_object_before_replacing_the_slot(
        string declared, string setup, string source, int error, string held)
    {
        var (printed, errors) = Run($"""
            Dim target As {declared}
            {setup}
            On Error Resume Next
            Set target = {source}
            Debug.Print CStr(Err.Number)
            Debug.Print target Is {held}
            """);
        errors.Should().BeEmpty();
        printed.Should().Equal(error.ToString(), "True");
    }

    [AvaloniaFact]
    public void Recording_the_class_preserves_control_and_form_member_access()
    {
        var (printed, errors) = Run("""
            Dim t As VB.TextBox, f As Form
            Set t = Text1
            Set f = Me
            Debug.Print t.Name = Text1.Name
            Debug.Print t.Text = Text1.Text
            Debug.Print f.Name = Me.Name
            Debug.Print f.Caption = Me.Caption
            """);
        errors.Should().BeEmpty();
        printed.Should().Equal("True", "True", "True", "True");
    }

    [AvaloniaFact]
    public void For_Each_checks_each_binding_through_the_store()
    {
        var (printed, errors) = Run("""
            Dim t As TextBox, n As Integer
            On Error Resume Next
            For Each t In Controls
                n = n + 1
            Next
            Debug.Print CStr(Err.Number)
            Debug.Print CStr(n)
            Debug.Print t Is Nothing
            """);
        errors.Should().BeEmpty();
        printed.Should().Equal("13", "1", "True");
    }
}
