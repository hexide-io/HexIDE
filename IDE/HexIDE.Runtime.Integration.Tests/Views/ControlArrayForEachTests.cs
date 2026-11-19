using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Integration.Tests.Views;

// VB6 SP6 native/p-code measurements: https://github.com/hexide-io/HexIDE/issues/4716.
// The TextBox variable over Labels remains #1563's typed-control gap.
public class ControlArrayForEachTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string Designer = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption = "Form1"
           Begin VB.Label L
              Index = 5
              Caption = "L5"
           End
           Begin VB.Label L
              Index = 0
              Caption = "L0"
           End
           Begin VB.Label L
              Index = 2
              Caption = "L2"
           End
           Begin VB.TextBox T
              Index = 0
              Text = "t0"
           End
           Begin VB.TextBox T
              Index = 1
              Text = "t1"
           End
           Begin VB.Menu mnuA
              Index = 0
              Caption = "A0"
           End
           Begin VB.Menu mnuA
              Index = 1
              Caption = "A1"
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

    [AvaloniaFact]
    public void Index_order()
    {
        var (printed, errors) = Run("""
            Dim c
            For Each c In L
            Debug.Print c.Index & ":" & TypeName(c)
            Next
            Debug.Print TypeName(c)
            """);
        errors.Should().BeEmpty();
        printed.Should().Equal("0:Label", "2:Label", "5:Label", "Empty");
    }

    [AvaloniaFact]
    public void Load_order()
    {
        var (printed, errors) = Run("""
            Dim c
            Load L(9)
            Load L(3)
            For Each c In L
            Debug.Print CStr(c.Index)
            Next
            """);
        errors.Should().BeEmpty();
        printed.Should().Equal("0", "2", "3", "5", "9");
    }

    [AvaloniaFact]
    public void Reload_before_walk()
    {
        var (printed, errors) = Run("""
            Dim c
            Load L(9)
            Load L(3)
            Unload L(9)
            Load L(9)
            For Each c In L
            Debug.Print CStr(c.Index)
            Next
            """);
        errors.Should().BeEmpty();
        printed.Should().Equal("0", "2", "3", "5", "9");
    }

    [AvaloniaFact]
    public void Typed_Control()
    {
        var (printed, errors) = Run("""
            Dim c As Control
            For Each c In L
            Debug.Print CStr(c.Index)
            Next
            Debug.Print c Is Nothing
            """);
        errors.Should().BeEmpty();
        printed.Should().Equal("0", "2", "5", "True");
    }

    [AvaloniaFact]
    public void Typed_Object()
    {
        var (printed, errors) = Run("""
            Dim c As Object
            For Each c In L
            Debug.Print CStr(c.Index)
            Next
            Debug.Print c Is Nothing
            """);
        errors.Should().BeEmpty();
        printed.Should().Equal("0", "2", "5", "True");
    }

    [AvaloniaFact]
    public void Typed_Label()
    {
        var (printed, errors) = Run("""
            Dim c As Label
            For Each c In L
            Debug.Print CStr(c.Index)
            Next
            Debug.Print c Is Nothing
            """);
        errors.Should().BeEmpty();
        printed.Should().Equal("0", "2", "5", "True");
    }

    [AvaloniaFact]
    public void Exit_keeps_element()
    {
        var (printed, errors) = Run("""
            Dim c
            For Each c In L
            Debug.Print CStr(c.Index)
            If c.Index = 2 Then Exit For
            Next
            Debug.Print CStr(c.Index)
            """);
        errors.Should().BeEmpty();
        printed.Should().Equal("0", "2", "2");
    }

    [AvaloniaFact]
    public void Resume_next_missing()
    {
        var (printed, errors) = Run("""
            Dim c
            Load L(7)
            Load L(9)
            On Error Resume Next
            For Each c In L
            Debug.Print c.Index & ":" & Err.Number
            If c.Index = 2 Then Unload L(7)
            Next
            Debug.Print TypeName(c)
            """);
        errors.Should().BeEmpty();
        printed.Should().Equal("0:0", "2:0", "5:0", "9:340", "Empty");
    }

    [AvaloniaFact]
    public void New_index_not_visited()
    {
        var (printed, errors) = Run("""
            Dim c
            For Each c In L
            Debug.Print CStr(c.Index)
            If c.Index = 2 Then Load L(4)
            Next
            """);
        errors.Should().BeEmpty();
        printed.Should().Equal("0", "2", "5");
    }

    [AvaloniaFact]
    public void Unload_current()
    {
        var (printed, errors) = Run("""
            Dim c
            Load L(3)
            For Each c In L
            Debug.Print CStr(c.Index)
            If c.Index = 3 Then Unload L(3)
            Next
            """);
        errors.Should().BeEmpty();
        printed.Should().Equal("0", "2", "3", "5");
    }

    [AvaloniaFact]
    public void Reload_uses_new_element()
    {
        var (printed, errors) = Run("""
            Dim c
            Load L(7)
            For Each c In L
            Debug.Print c.Index & "=" & c.Caption
            If c.Index = 2 Then
            Unload L(7)
            Load L(7)
            L(7).Caption = "new"
            End If
            Next
            """);
        errors.Should().BeEmpty();
        printed.Should().Equal("0=L0", "2=L2", "5=L5", "7=new");
    }

    [AvaloniaFact]
    public void Clear_variable()
    {
        var (printed, errors) = Run("""
            Dim c
            For Each c In L
            Debug.Print CStr(c.Index)
            Set c = Nothing
            Next
            """);
        errors.Should().BeEmpty();
        printed.Should().Equal("0", "2", "5");
    }

    [AvaloniaFact]
    public void Nested()
    {
        var (printed, errors) = Run("""
            Dim c, d, n
            n = 0
            For Each c In L
            For Each d In L
            n = n + 1
            Next
            Next
            Debug.Print CStr(n)
            """);
        errors.Should().BeEmpty();
        printed.Should().Equal("9");
    }

    [AvaloniaFact]
    public void TextBoxes()
    {
        var (printed, errors) = Run("""
            Dim c
            For Each c In T
            Debug.Print c.Index & "=" & c.Text
            Next
            """);
        errors.Should().BeEmpty();
        printed.Should().Equal("0=t0", "1=t1");
    }

    [AvaloniaFact]
    public void Menus()
    {
        var (printed, errors) = Run("""
            Dim c
            For Each c In mnuA
            Debug.Print c.Index & "=" & c.Caption
            Next
            """);
        errors.Should().BeEmpty();
        printed.Should().Equal("0=A0", "1=A1");
    }

    [AvaloniaFact]
    public void Alias_Object()
    {
        var (printed, errors) = Run("""
            Dim a As Object
            Dim c
            Set a = L
            Debug.Print TypeName(a)
            For Each c In a
            Debug.Print CStr(c.Index)
            Next
            """);
        errors.Should().BeEmpty();
        printed.Should().Equal("Object", "0", "2", "5");
    }

    [AvaloniaFact]
    public void Alias_Variant()
    {
        var (printed, errors) = Run("""
            Dim a As Variant
            Dim c
            Set a = L
            Debug.Print TypeName(a)
            For Each c In a
            Debug.Print CStr(c.Index)
            Next
            """);
        errors.Should().BeEmpty();
        printed.Should().Equal("Object", "0", "2", "5");
    }

    [AvaloniaFact]
    public void Missing_element_raises_340()
    {
        var (printed, errors) = Run("""
            Dim c
            Load L(9)
            For Each c In L
                Debug.Print CStr(c.Index)
                If c.Index = 2 Then Unload L(9)
            Next
            """);
        printed.Should().Equal("0", "2", "5");
        var error = errors.Should().ContainSingle().Which.Should().BeOfType<VBRunTimeException>().Subject;
        error.Error.ErrNo.Should().Be(340);
        error.Error.Description.Should().Be("Control array element '9' doesn't exist");
    }

    [AvaloniaFact]
    public void String_variable_is_compile_error()
    {
        var (_, errors) = Run("""
            Dim sv As String
            For Each sv In L
            Next
            """);
        errors.Should().ContainSingle().Which.Should().BeOfType<VBCompileErrorException>()
            .Which.Message.ReplaceLineEndings("\n").Should()
            .Be("Compile error:\n\nFor Each control variable must be Variant or Object");
    }
}
