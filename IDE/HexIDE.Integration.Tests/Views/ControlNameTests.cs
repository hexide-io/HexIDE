using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A control's and a form's Name at run time (#1431). The form is the oracle's probe for "A control's Name, at run time",
/// printing where the probe wrote to a file, and every line expected is the one vb6.exe logged, native and p-code agreeing.
/// The probe's rows for a form made with <c>New Form2</c> are left out: HexIDE makes no form with New yet (#822).
/// </summary>
public class ControlNameTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string Form1 = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "NMKinds"
           ClientHeight    =   3000
           ClientLeft      =   60
           ClientTop       =   400
           ClientWidth     =   5000
           LinkTopic       =   "Form1"
           ScaleHeight     =   3000
           ScaleWidth      =   5000
           Begin VB.Timer Timer1
              Left            =   4200
              Top             =   2400
           End
           Begin VB.Frame Frame1
              Caption         =   "Frame1"
              Height          =   735
              Left            =   2640
              TabIndex        =   3
              Top             =   1200
              Width           =   1575
           End
           Begin VB.CommandButton Command1
              Caption         =   "Command1"
              Height          =   375
              Left            =   2640
              TabIndex        =   2
              Top             =   600
              Width           =   1215
           End
           Begin VB.OptionButton Option1
              Caption         =   "b"
              Height          =   255
              Index           =   1
              Left            =   120
              TabIndex        =   5
              Top             =   1560
              Width           =   1215
           End
           Begin VB.OptionButton Option1
              Caption         =   "a"
              Height          =   255
              Index           =   0
              Left            =   120
              TabIndex        =   4
              Top             =   1200
              Width           =   1215
           End
           Begin VB.ComboBox Combo1
              Height          =   315
              Left            =   120
              TabIndex        =   0
              Text            =   "Combo1"
              Top             =   120
              Width           =   2000
           End
           Begin VB.TextBox Text1
              Height          =   285
              Left            =   120
              TabIndex        =   1
              Text            =   "Text1"
              Top             =   600
              Width           =   2000
           End
           Begin VB.Image Image1
              Height          =   375
              Left            =   1560
              Top             =   2400
              Width           =   375
           End
           Begin VB.Shape Shape1
              Height          =   375
              Left            =   120
              Top             =   2400
              Width           =   1095
           End
           Begin VB.Line Line1
              X1              =   120
              X2              =   2000
              Y1              =   2160
              Y2              =   2160
           End
           Begin VB.Label Label1
              Caption         =   "Label1"
              Height          =   255
              Left            =   2640
              TabIndex        =   6
              Top             =   120
              Width           =   1215
           End
           Begin VB.Menu mnuFile
              Caption         =   "File"
              Begin VB.Menu mnuOpen
                 Caption         =   "Open"
                 Index           =   0
              End
           End
        End
        Attribute VB_Name = "Form1"
        Attribute VB_GlobalNameSpace = False
        Attribute VB_Creatable = False
        Attribute VB_PredeclaredId = True
        Attribute VB_Exposed = False
        Option Explicit
        Private Sub L(ByVal s As String)
            Debug.Print s
        End Sub
        Private Sub Form_Load()
            Dim o As Object, c As Control, f As Form2, frm As Form
            On Error Resume Next
            L "Label1 [" & Label1.Name & "] " & Err.Number: Err.Clear
            L "Line1 [" & Line1.Name & "] " & Err.Number: Err.Clear
            L "Shape1 [" & Shape1.Name & "] " & Err.Number: Err.Clear
            L "Image1 [" & Image1.Name & "] " & Err.Number: Err.Clear
            L "Timer1 [" & Timer1.Name & "] " & Err.Number: Err.Clear
            L "Frame1 [" & Frame1.Name & "] " & Err.Number: Err.Clear
            L "Command1 [" & Command1.Name & "] " & Err.Number: Err.Clear
            L "Text1 [" & Text1.Name & "] " & Err.Number: Err.Clear
            L "Option1(1) [" & Option1(1).Name & "] " & TypeName(Option1(1).Name) & " " & Err.Number: Err.Clear
            L "Option1(1).Index " & Option1(1).Index & " " & TypeName(Option1(1).Index) & " " & Err.Number: Err.Clear
            Set o = Combo1
            L "mnuFile [" & mnuFile.Name & "] " & Err.Number: Err.Clear
            L "mnuOpen(0) [" & mnuOpen(0).Name & "] " & Err.Number: Err.Clear
            L "Me [" & Me.Name & "] " & TypeName(Me.Name) & " " & Err.Number: Err.Clear
            L "Form1 [" & Form1.Name & "] " & Err.Number: Err.Clear
            L "Form2 [" & Form2.Name & "] " & Err.Number: Err.Clear
            Set o = Me
            L "late Me [" & o.Name & "] " & Err.Number: Err.Clear
            Set o = Combo1
            o.Name = "zz"
            L "late set " & Err.Number & " " & Err.Description: Err.Clear
            L "after [" & Combo1.Name & "]": Err.Clear
            Set c = Text1
            c.Name = "zz"
            L "Control set " & Err.Number & " " & Err.Description: Err.Clear
            Set o = Me
            o.Name = "zz"
            L "late form set " & Err.Number & " " & Err.Description: Err.Clear
            Set o = Form2
            L "late Form2 [" & o.Name & "] " & Err.Number: Err.Clear
            o.Name = "zz"
            L "late Form2 set " & Err.Number & " " & Err.Description: Err.Clear
            L "end"
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
        """;

    // The oracle's probe for Index, on the same form; its For Each over the form's controls is #1060's.
    private const string IndexCode = """
        Option Explicit
        Private Sub L(ByVal s As String)
            Debug.Print s
        End Sub
        Private Sub Form_Load()
            Dim o As Object, c As Control, v As Variant
            On Error Resume Next
            Set o = Combo1
            v = o.Index
            L "late Combo1.Index " & Err.Number & " " & Err.Description & " [" & v & "]": Err.Clear
            Set c = Label1
            v = c.Index
            L "Control Label1.Index " & Err.Number & " " & Err.Description: Err.Clear
            Set o = Line1
            v = o.Index
            L "late Line1.Index " & Err.Number & " " & Err.Description: Err.Clear
            L "Option1(0).Index " & Option1(0).Index & " " & TypeName(Option1(0).Index) & " " & Err.Number: Err.Clear
            L "mnuOpen(0).Index " & mnuOpen(0).Index & " " & TypeName(mnuOpen(0).Index) & " " & Err.Number: Err.Clear
            Set o = Option1(1)
            L "late Option1(1).Index " & o.Index & " " & Err.Number: Err.Clear
            o.Index = 5
            L "late set Index " & Err.Number & " " & Err.Description: Err.Clear
            L "after " & Option1(1).Index: Err.Clear
            Set o = Me
            v = o.Index
            L "late Me.Index " & Err.Number & " " & Err.Description: Err.Clear
            L "end"
            Unload Me
        End Sub
        """;

    private static (List<string> Printed, List<Exception> Errors) Run(string? code = null)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Probe");
        var source = code is null ? Form1 : Form1[..Form1.IndexOf("Option Explicit", StringComparison.Ordinal)] + code;
        var form1 = new FormDeserializer().Deserialize(project, source.ReplaceLineEndings("\r\n"), new NullSink())!;
        var form2 = new FormDeserializer().Deserialize(project, Form2.ReplaceLineEndings("\r\n"), new NullSink())!;
        project.AddForm(form1);
        project.AddForm(form2);
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
    public void Every_control_s_and_form_s_Name_reads_and_refuses_assignment_as_vb6_exe_s_does()
    {
        var (printed, errors) = Run();

        errors.Should().BeEmpty();
        printed.Should().Equal(
            "Label1 [Label1] 0",
            "Line1 [Line1] 0",
            "Shape1 [Shape1] 0",
            "Image1 [Image1] 0",
            "Timer1 [Timer1] 0",
            "Frame1 [Frame1] 0",
            "Command1 [Command1] 0",
            "Text1 [Text1] 0",
            "Option1(1) [Option1] String 0",
            "Option1(1).Index 1 Integer 0",
            "mnuFile [mnuFile] 0",
            "mnuOpen(0) [mnuOpen] 0",
            "Me [Form1] String 0",
            "Form1 [Form1] 0",
            "Form2 [Form2] 0",
            "late Me [Form1] 0",
            "late set 382 'Name' property cannot be set at run time",
            "after [Combo1]",
            "Control set 382 'Name' property cannot be set at run time",
            "late form set 382 'Name' property cannot be set at run time",
            "late Form2 [Form2] 0",
            "late Form2 set 382 'Name' property cannot be set at run time",
            "end");
    }

    [AvaloniaFact]
    public void An_array_element_s_Index_reads_and_every_other_control_s_is_343_as_vb6_exe_s_is()
    {
        var (printed, errors) = Run(IndexCode);

        errors.Should().BeEmpty();
        printed.Should().Equal(
            "late Combo1.Index 343 Object not an array []",
            "Control Label1.Index 343 Object not an array",
            "late Line1.Index 343 Object not an array",
            "Option1(0).Index 0 Integer 0",
            "mnuOpen(0).Index 0 Integer 0",
            "late Option1(1).Index 1 0",
            "late set Index 382 'Index' property cannot be set at run time",
            "after 1",
            "late Me.Index 438 Object doesn't support this property or method",
            "end");
    }
}
