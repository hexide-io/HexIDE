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

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A control-array element named through its form, <c>Form1.Text1(0).Text</c>, from another form's code (#1386). The forms
/// are the oracle's probe for "A member indexed through its object", printing where the probe wrote to a file, and every
/// line expected is the one vb6.exe logged, native and p-code agreeing.
/// </summary>
public class FormQualifiedMemberTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string Form1 = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "One"
           ClientHeight    =   2000
           ClientLeft      =   60
           ClientTop       =   400
           ClientWidth     =   4000
           LinkTopic       =   "Form1"
           ScaleHeight     =   2000
           ScaleWidth      =   4000
           Begin VB.TextBox Text1
              Height          =   285
              Index           =   1
              Left            =   120
              TabIndex        =   1
              Text            =   "second"
              Top             =   600
              Width           =   2000
           End
           Begin VB.TextBox Text1
              Height          =   285
              Index           =   0
              Left            =   120
              TabIndex        =   0
              Text            =   "first"
              Top             =   120
              Width           =   2000
           End
        End
        Attribute VB_Name = "Form1"
        Attribute VB_GlobalNameSpace = False
        Attribute VB_Creatable = False
        Attribute VB_PredeclaredId = True
        Attribute VB_Exposed = False
        Option Explicit
        Private Sub Form_Load()
            Form2.Show
            Form2.Probe
            Unload Form2
            Unload Me
        End Sub
        """;

    private const string Form2 = """
        VERSION 5.00
        Begin VB.Form Form2
           Caption         =   "Two"
           ClientHeight    =   1000
           ClientLeft      =   60
           ClientTop       =   400
           ClientWidth     =   2000
           LinkTopic       =   "Form2"
           ScaleHeight     =   1000
           ScaleWidth      =   2000
        End
        Attribute VB_Name = "Form2"
        Attribute VB_GlobalNameSpace = False
        Attribute VB_Creatable = False
        Attribute VB_PredeclaredId = True
        Attribute VB_Exposed = False
        Option Explicit
        Private Sub L(ByVal s As String)
            Debug.Print s
        End Sub
        Public Sub Probe()
            Dim v As Variant, k As Class1, o As Object
            On Error Resume Next
            v = Form1.Text1(0).Text
            L "Form1.Text1(0).Text [" & v & "] " & Err.Number: Err.Clear
            v = Form1.Text1(1).Text
            L "Form1.Text1(1).Text [" & v & "] " & Err.Number: Err.Clear
            v = Empty
            v = Form1.Text1(9).Text
            L "Form1.Text1(9).Text [" & v & "] " & Err.Number & " " & Err.Description: Err.Clear
            v = Form1.Text1("1").Text
            L "Form1.Text1(""1"").Text [" & v & "] " & Err.Number: Err.Clear
            v = Form1.Text1.Count
            L "Form1.Text1.Count [" & v & "] " & Err.Number: Err.Clear
            v = Form1.Text1(1).Index
            L "Form1.Text1(1).Index [" & v & "] " & Err.Number: Err.Clear
            Form1.Text1(0).Text = "w"
            L "set Form1.Text1(0).Text " & Err.Number & " [" & Form1.Text1(0).Text & "]": Err.Clear
            Set o = Form1
            v = o.Text1(1).Text
            L "late o.Text1(1).Text [" & v & "] " & Err.Number: Err.Clear
            L "end"
        End Sub
        """;

    // The VB6 IDE probe's Form2: paused at its Stop, the Immediate window was typed into.
    private const string StoppingForm2 = """
        VERSION 5.00
        Begin VB.Form Form2
           Caption         =   "Two"
           ClientHeight    =   1000
           ClientWidth     =   2000
        End
        Attribute VB_Name = "Form2"
        Option Explicit
        Public Sub Probe()
            Dim n As Integer
            n = 7
            Stop
        End Sub
        """;

    // The probe's Form2 with its Probe in place of the measured one: a class's Variant holding an array, indexed.
    private const string IndexingForm2 = """
        VERSION 5.00
        Begin VB.Form Form2
           Caption         =   "Two"
           ClientHeight    =   1000
           ClientWidth     =   2000
        End
        Attribute VB_Name = "Form2"
        Option Explicit
        Public Sub Probe()
            Dim k As Class1, v As Variant
            Set k = New Class1
            k.V = Array(10, 20, 30)
            On Error Resume Next
            v = k.V(1)
            Debug.Print "after " & Err.Number
        End Sub
        """;

    private static (List<string> Printed, List<Exception> Errors) Run(string form2)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Probe");
        var form1 = new FormDeserializer().Deserialize(project, Form1.ReplaceLineEndings("\r\n"), new NullSink())!;
        project.AddForm(form1);
        project.AddForm(new FormDeserializer().Deserialize(project, form2.ReplaceLineEndings("\r\n"), new NullSink())!);
        var class1 = new ModuleDefinition(project, "Class1", ModuleKind.ClassModule);
        class1.UpdateCode("Public V As Variant\r\n");
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
        try
        {
            VBLoader.RunForm(form1, CancellationToken.None, out _, null);
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            VBDebugConsole.Output -= print;
            VBWindowContext.Faulted -= OnFaulted;
            VBWindowContext.RunTimeError -= OnRunTimeError;
            VBWindowContext.CompileError -= OnCompileError;
        }
        return (printed, errors);
    }

    [AvaloniaFact]
    public void A_control_array_element_named_through_its_form_reads_and_takes_a_value_as_vb6_exe_s_does()
    {
        var (printed, errors) = Run(Form2);

        errors.Should().BeEmpty();
        printed.Should().Equal(
            "Form1.Text1(0).Text [first] 0",
            "Form1.Text1(1).Text [second] 0",
            "Form1.Text1(9).Text [] 340 Control array element '9' doesn't exist",
            "Form1.Text1(\"1\").Text [second] 0",
            "Form1.Text1.Count [2] 0",
            "Form1.Text1(1).Index [1] 0",
            "set Form1.Text1(0).Text 0 [w]",
            "late o.Text1(1).Text [second] 0",
            "end");
    }


    // Paused in Form2's code, as the issue found it in the Notepad demo's Find dialog. Each line and what it printed were
    // copied from the VB6 IDE's Immediate window, paused at the same Stop (the oracle's "A ? line in the Immediate window").
    [AvaloniaFact]
    public async Task The_Immediate_window_reads_a_control_array_element_through_its_form_as_the_VB6_IDE_s_does()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Probe");
        var form1 = new FormDeserializer().Deserialize(project, Form1.ReplaceLineEndings("\r\n"), new NullSink())!;
        project.AddForm(form1);
        project.AddForm(new FormDeserializer().Deserialize(project, StoppingForm2.ReplaceLineEndings("\r\n"), new NullSink())!);
        var dbg = new HexIDE.Runtime.Debugging.DebugController { TimeSliceMs = null };
        dbg.Reset();

        var run = VBLoader.RunForm(form1, CancellationToken.None, out var window, dbg);
        try
        {
            Dispatcher.UIThread.RunJobs();
            dbg.State.Should().Be(HexIDE.Runtime.Debugging.DebugState.Paused);
            (await dbg.EvaluateAsync("? Form1.Caption")).Should().Be("One");
            (await dbg.EvaluateAsync("? Form1.Text1(0).Text")).Should().Be("first");
            (await dbg.EvaluateAsync("? Form1.Text1(1).Text; Len(Form1.Text1(1).Text)")).Should().Be("second 6 ");
            (await dbg.EvaluateAsync("? Form1.Text1.Count")).Should().Be(" 2 ");
            (await dbg.EvaluateAsync("? n")).Should().Be(" 7 ");
        }
        finally
        {
            dbg.Stop();
            Dispatcher.UIThread.RunJobs();
            window.Close();
        }
        run.IsCompleted.Should().BeTrue();
    }

    // VB6 gives 20 early-bound and 451 late-bound (measured), which HexIDE can't tell apart, so it's refused (#1435). The
    // index used to be dropped, which read the whole array: no error, and a value VB6 never gives.
    [AvaloniaFact]
    public void An_index_on_a_class_s_Variant_field_is_refused_rather_than_dropped()
    {
        var (printed, errors) = Run(IndexingForm2);

        printed.Should().BeEmpty();
        errors.Should().ContainSingle().Which.Message.Should().Contain("#1435");
    }
}
