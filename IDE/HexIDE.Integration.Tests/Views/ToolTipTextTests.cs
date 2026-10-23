using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// ToolTipText (#1403). Every expectation is vb6.exe's, native and p-code agreeing (the oracle's "ToolTipText: which
/// controls have it"). The probe reached each control late-bound, so one without the member answered 438. It logged what
/// the control read, then what it held after "tip", 5, Null and "".
/// </summary>
public class ToolTipTextTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string Controls = """
           Begin VB.Timer Timer1
              Left            =   4200
              Top             =   2040
           End
           Begin VB.Line Line1
              X1              =   2400
              X2              =   3600
              Y1              =   5400
              Y2              =   5400
           End
           Begin VB.Shape Shape1
              Height          =   375
              Left            =   2400
              Top             =   4800
              Width           =   1215
           End
           Begin VB.Image Image1
              Height          =   375
              Left            =   2400
              Top             =   4200
              Width           =   1215
           End
           Begin VB.Label Label1
              Caption         =   "Label1"
              Height          =   255
              Left            =   2400
              TabIndex        =   9
              Top             =   3720
              Width           =   1215
           End
           Begin VB.PictureBox Picture1
              Height          =   495
              Left            =   2400
              ScaleHeight     =   435
              ScaleWidth      =   1155
              TabIndex        =   8
              Top             =   3000
              Width           =   1215
           End
           Begin VB.VScrollBar VScroll1
              Height          =   1215
              Left            =   2400
              TabIndex        =   7
              Top             =   1560
              Width           =   255
           End
           Begin VB.HScrollBar HScroll1
              Height          =   255
              Left            =   2400
              TabIndex        =   6
              Top             =   1080
              Width           =   1215
           End
           Begin VB.Frame Frame1
              Caption         =   "Frame1"
              Height          =   735
              Left            =   2400
              TabIndex        =   5
              ToolTipText     =   "frame tip"
              Top             =   120
              Width           =   1215
           End
           Begin VB.OptionButton Option1
              Caption         =   "Option1"
              Height          =   255
              Left            =   120
              TabIndex        =   4
              Top             =   3600
              Width           =   1215
           End
           Begin VB.CheckBox Check1
              Caption         =   "Check1"
              Height          =   255
              Left            =   120
              TabIndex        =   3
              Top             =   3120
              Width           =   1215
           End
           Begin VB.ListBox List1
              Height          =   645
              Left            =   120
              TabIndex        =   2
              Top             =   2160
              Width           =   1215
           End
           Begin VB.ComboBox Combo1
              Height          =   315
              Left            =   120
              TabIndex        =   1
              Text            =   "Combo1"
              Top             =   1560
              Width           =   1215
           End
           Begin VB.TextBox Text1
              Height          =   375
              Left            =   120
              TabIndex        =   10
              Text            =   "Text1"
              ToolTipText     =   "text tip"
              Top             =   960
              Width           =   1215
           End
           Begin VB.CommandButton Command1
              Caption         =   "Command1"
              Height          =   495
              Left            =   120
              TabIndex        =   0
              ToolTipText     =   "button tip"
              Top             =   240
              Width           =   1215
           End
           Begin VB.Menu mnuFile
              Caption         =   "File"
           End
        """;

    private const string Code = """""
        Private Sub Probe(ByVal what As String, o As Object)
            Dim v As Variant
            On Error Resume Next
            Err.Clear
            v = o.ToolTipText
            If Err.Number <> 0 Then
                Debug.Print what & ": read err " & Err.Number & " " & Err.Description
                Exit Sub
            End If
            Debug.Print what & ": reads " & TypeName(v) & " [" & v & "]"
            Err.Clear: o.ToolTipText = "tip": Debug.Print "  = ""tip"": err " & Err.Number & ", now [" & o.ToolTipText & "]"
            Err.Clear: o.ToolTipText = 5: Debug.Print "  = 5: err " & Err.Number & ", now [" & o.ToolTipText & "] " & TypeName(o.ToolTipText)
            Err.Clear: o.ToolTipText = Null: Debug.Print "  = Null: err " & Err.Number & " " & Err.Description & ", now [" & o.ToolTipText & "]"
            Err.Clear: o.ToolTipText = "": Debug.Print "  = """": err " & Err.Number & ", now [" & o.ToolTipText & "]"
        End Sub
        """"";

    private sealed record Ran(List<string> Printed, List<System.Exception> Errors);

    /// <summary>Run Form1 with <paramref name="load"/> as its Load; <paramref name="look"/> sees the form before it closes.</summary>
    private static Ran Run(string load, System.Action<Window>? look = null)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var frm = ("VERSION 5.00\nBegin VB.Form Form1\n" + Controls + "\nEnd\nAttribute VB_Name = \"Form1\"\n" + Code + "\n"
                   + "Private Sub Form_Load()\n" + load + "\nEnd Sub\n").ReplaceLineEndings("\r\n");
        var form = new FormDeserializer().Deserialize(project, frm, new NullSink())!;
        project.AddForm(form);

        var ran = new Ran([], []);
        void OnFaulted(VBWindowContext _, System.Exception e) => ran.Errors.Add(e);
        void OnRunTimeError(VBWindowContext _, VBRunTimeException e) => ran.Errors.Add(e);
        void OnCompileError(VBWindowContext _, VBCompileErrorException e) => ran.Errors.Add(e);
        VBDebugConsole.Output += ran.Printed.Add;
        VBWindowContext.Faulted += OnFaulted;
        VBWindowContext.RunTimeError += OnRunTimeError;
        VBWindowContext.CompileError += OnCompileError;
        try
        {
            VBLoader.RunForm(form, CancellationToken.None, out var window);
            Dispatcher.UIThread.RunJobs();
            look?.Invoke(window);
            window.Close();
        }
        finally
        {
            VBDebugConsole.Output -= ran.Printed.Add;
            VBWindowContext.Faulted -= OnFaulted;
            VBWindowContext.RunTimeError -= OnRunTimeError;
            VBWindowContext.CompileError -= OnCompileError;
        }
        return ran;
    }

    private static string[] Has(string what) =>
    [
        $"{what}: reads String []", "  = \"tip\": err 0, now [tip]", "  = 5: err 0, now [5] String",
        "  = Null: err 13 Type mismatch, now [5]", "  = \"\": err 0, now []",
    ];

    [AvaloniaTheory]
    [InlineData("CheckBox", "Check1")]
    [InlineData("OptionButton", "Option1")]
    [InlineData("ComboBox", "Combo1")]
    [InlineData("ListBox", "List1")]
    [InlineData("PictureBox", "Picture1")]
    [InlineData("Label", "Label1")]
    [InlineData("Image", "Image1")]
    public void A_control_with_ToolTipText_reads_and_takes_it_as_vb6_exe_s_does(string what, string control)
    {
        var ran = Run($"Probe \"{what}\", {control}");

        ran.Errors.Should().BeEmpty();
        ran.Printed.Should().Equal(Has(what));
    }

    // VB6 has none on these. Reached late-bound, vb6.exe answers 438; HexIDE stops with the compile error there too, which is
    // #1433's. Named directly it is the compile error in VB6 as well: `Form2.HScroll1.ToolTipText` failed vb6.exe's build.
    [AvaloniaTheory]
    [InlineData("HScroll1")]
    [InlineData("VScroll1")]
    [InlineData("Shape1")]
    [InlineData("Line1")]
    [InlineData("Timer1")]
    [InlineData("mnuFile")]
    [InlineData("Me")]
    public void A_control_without_ToolTipText_has_no_such_member(string control)
    {
        var ran = Run($"Debug.Print {control}.ToolTipText");

        ran.Printed.Should().BeEmpty();
        ran.Errors.Should().ContainSingle().Which.Should().BeAssignableTo<VBCompileErrorException>()
            .Which.Message.Should().Contain("Method or data member not found");
    }

    // The three saved with a tip read it back, as vb6.exe's Form2 did; Command1's is #218's control.
    [AvaloniaTheory]
    [InlineData("CommandButton", "Command1", "button tip")]
    [InlineData("TextBox", "Text1", "text tip")]
    [InlineData("Frame", "Frame1", "frame tip")]
    public void A_saved_tip_reads_back_and_takes_new_values_as_vb6_exe_s_does(string what, string control, string saved)
    {
        var ran = Run($"Debug.Print \"[\" & {control}.ToolTipText & \"]\"\nProbe \"{what}\", {control}");

        ran.Errors.Should().BeEmpty();
        ran.Printed.Should().Equal([$"[{saved}]", $"{what}: reads String [{saved}]", .. Has(what).Skip(1)]);
    }

    // The tip shown is the one the file gave, and the one code gives it after.
    [AvaloniaFact]
    public void The_tip_shown_is_the_file_s_and_then_code_s()
    {
        object? atLoad = null, button = null, cleared = "unread", never = "unread";
        Run("", window =>
        {
            atLoad = ToolTip.GetTip(Named(window, "Command1"));
            never = ToolTip.GetTip(Named(window, "List1"));
        });
        Run("Command1.ToolTipText = \"new tip\"\nText1.ToolTipText = \"\"", window =>
        {
            button = ToolTip.GetTip(Named(window, "Command1"));
            cleared = ToolTip.GetTip(Named(window, "Text1"));
        });

        atLoad.Should().Be("button tip");
        button.Should().Be("new tip");
        // No tip at all, rather than an empty box, for a ToolTipText of "": one never given, and one code cleared.
        never.Should().BeNull();
        cleared.Should().BeNull();
    }

    private static Control Named(Window window, string name) =>
        window.GetVisualDescendants().OfType<Control>().First(c => VBProps.GetName(c) == name);
}
