using System;
using System.Collections.Generic;
using System.Linq;
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
/// A form's Controls collection (#1060): <c>For Each c In Controls</c>, <c>Controls.Count</c>, and <c>Controls(i)</c> and
/// <c>Controls("Name")</c>. Every expectation was measured on vb6.exe, native and p-code agreeing: the oracle's "A form's
/// Controls collection".
/// </summary>
/// <remarks>
/// The form below is the probe's: its blocks are a Timer, a CommandButton, element 0 of a TextBox array, a Frame holding a
/// CheckBox, element 1 of the array, a Label, a Line, then a menu holding a submenu. Run from <see cref="VBLoader.RunForm"/>,
/// as F5 runs it.
/// </remarks>
public class FormControlsTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string Designer = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Form1"
           ClientHeight    =   3015
           ClientWidth     =   4560
           Begin VB.Timer Timer1
              Left            =   4080
              Top             =   2520
           End
           Begin VB.CommandButton Command1
              Caption         =   "Command1"
              Height          =   495
              Left            =   120
              TabIndex        =   0
              Top             =   120
              Width           =   1215
           End
           Begin VB.TextBox Text1
              Height          =   285
              Index           =   0
              Left            =   1440
              TabIndex        =   1
              Text            =   "Text1"
              Top             =   120
              Width           =   1215
           End
           Begin VB.Frame Frame1
              Caption         =   "Frame1"
              Height          =   975
              Left            =   120
              TabIndex        =   2
              Top             =   720
              Width           =   2055
              Begin VB.CheckBox Check1
                 Caption         =   "Check1"
                 Height          =   255
                 Left            =   120
                 TabIndex        =   3
                 Top             =   360
                 Width           =   1215
              End
           End
           Begin VB.TextBox Text1
              Height          =   285
              Index           =   1
              Left            =   1440
              TabIndex        =   4
              Text            =   "Text1"
              Top             =   480
              Width           =   1215
           End
           Begin VB.Label Label1
              Caption         =   "Label1"
              Height          =   255
              Left            =   2400
              TabIndex        =   5
              Top             =   840
              Width           =   1215
           End
           Begin VB.Line Line1
              X1              =   120
              X2              =   2400
              Y1              =   2040
              Y2              =   2040
           End
           Begin VB.Menu mnuFile
              Caption         =   "File"
              Begin VB.Menu mnuOpen
                 Caption         =   "Open"
              End
           End
        End
        Attribute VB_Name = "Form1"

        """;

    // The second probe's, where a CommonDialog, a control HexIDE keeps as the text it was read as, sits between two others.
    private const string WithDialog = """
        VERSION 5.00
        Object = "{F9043C88-F6F2-101A-A3C9-08002B2F49FB}#1.2#0"; "comdlg32.ocx"
        Begin VB.Form Form1
           Caption         =   "Form1"
           Begin VB.CommandButton Command1
              Caption         =   "Command1"
              Height          =   495
              Left            =   120
              TabIndex        =   0
              Top             =   120
              Width           =   1215
           End
           Begin MSComDlg.CommonDialog CommonDialog1
              Left            =   3960
              Top             =   1680
              _ExtentX        =   847
              _ExtentY        =   847
              _Version        =   393216
           End
           Begin VB.TextBox Text1
              Height          =   285
              Left            =   1440
              TabIndex        =   1
              Text            =   "Text1"
              Top             =   120
              Width           =   1215
           End
        End
        Attribute VB_Name = "Form1"

        """;

    private const string Form2 = """
        VERSION 5.00
        Begin VB.Form Form2
           Caption         =   "Form2"
           Begin VB.TextBox Text1
              Height          =   495
              Left            =   120
              TabIndex        =   0
              Top             =   120
              Width           =   1215
           End
           Begin VB.Label Label1
              Caption         =   "label"
              Height          =   255
              Left            =   1440
              TabIndex        =   1
              Top             =   120
              Width           =   1215
           End
        End
        Attribute VB_Name = "Form2"
        Private Sub Form_Load()
            Debug.Print "f2 load"
        End Sub

        """;

    /// <summary>Run a program whose startup form is <paramref name="designer"/> with <paramref name="load"/> as its Form_Load.</summary>
    private static (List<string> Printed, List<Exception> Errors) Run(string load, string designer = Designer)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var code = "Option Explicit\r\nPrivate Sub Form_Load()\r\n" + load + "\r\nEnd Sub\r\n";
        var form1 = new FormDeserializer().Deserialize(project, (designer + code).ReplaceLineEndings("\r\n"), new NullSink())!;
        project.AddForm(form1);
        project.AddForm(new FormDeserializer().Deserialize(project, Form2.ReplaceLineEndings("\r\n"), new NullSink())!);

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

    [AvaloniaFact]
    public void Timer_Parent_returns_the_owning_form_by_both_routes()
    {
        Printed("Dim o As Object\nSet o = Timer1\nDebug.Print Timer1.Parent.Name\nDebug.Print o.Parent.Name\n"
                + "Set o = Timer2(0)\nDebug.Print Timer2(0).Parent.Name\nDebug.Print o.Parent.Name",
                Designer.Replace("Begin VB.Timer Timer1", "Begin VB.Timer Timer2\nIndex = 0\nEnd\nBegin VB.Timer Timer1"))
            .Should().Equal("Form1", "Form1", "Form1", "Form1");
    }

    private static void Pump()
    {
        for (var i = 0; i < 20; i++)
            Dispatcher.UIThread.RunJobs();
    }

    private static List<string> Printed(string load, string designer = Designer)
    {
        var (printed, errors) = Run(load, designer);
        errors.Should().BeEmpty();
        return printed;
    }

    // Names every control the loop reaches, an array element with its index.
    private const string NameOf = """
            n = -1
            If TypeOf c Is TextBox Then n = c.Index
            s = s & c.Name & IIf(n = -1, "", "(" & n & ")") & " "
        """;

    private const string Declare = "Dim c As Object, s As String, n As Integer\r\n";

    private static string Loop(string body = "") => "\r\ns = \"\"\r\nFor Each c In Controls\r\n" + NameOf + "\r\n" + body + "\r\nNext\r\n";

    // ── what it holds, and in what order ─────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void For_Each_reaches_every_control_in_the_order_the_file_holds_them_menus_and_each_array_element_included()
    {
        var printed = Printed("""
            Dim c As Object
            For Each c In Controls
                Debug.Print c.Name & " " & TypeName(c)
            Next
            Debug.Print Controls.Count & " " & Me.Controls.Count & " " & TypeName(Controls)
            """);

        printed.Should().Equal(
            "Timer1 Timer", "Command1 CommandButton", "Text1 TextBox", "Frame1 Frame", "Check1 CheckBox", "Text1 TextBox",
            "Label1 Label", "Line1 Line", "mnuFile Menu", "mnuOpen Menu", "10 10 Object");
    }

    [AvaloniaFact]
    public void A_control_HexIDE_carries_as_text_is_reached_where_the_file_holds_it()
    {
        var printed = Printed("""
            Dim c As Object
            For Each c In Controls
                Debug.Print c.Name & " " & TypeName(c)
            Next
            """, WithDialog);

        printed.Should().Equal("Command1 CommandButton", "CommonDialog1 CommonDialog", "Text1 TextBox");
    }

    [AvaloniaFact]
    public void The_idiom_a_loop_over_every_TextBox_reaches_each_array_element()
    {
        var printed = Printed("""
            Dim c As Control
            For Each c In Me.Controls
                If TypeOf c Is TextBox Then c.Text = "cleared"
            Next
            Debug.Print Text1(0).Text & " " & Text1(1).Text
            """);

        printed.Should().Equal("cleared cleared");
    }

    [AvaloniaFact]
    public void ControlsEnumeration_ReadsDisabledContainers()
    {
        var printed = Printed("""
            Frame1.Enabled = False
            Dim c As Control
            For Each c In Me.Controls
                If c.Name = "Check1" Then Debug.Print c.Enabled
            Next
            """);

        printed.Should().Equal("False");
    }

    // ── Item ─────────────────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Item_counts_from_zero_and_finds_a_name_in_any_case_nested_or_a_menu()
    {
        var printed = Printed("""
            Dim i As Integer, s As String
            For i = 0 To Controls.Count - 1
                s = s & Controls(i).Name & " "
            Next
            Debug.Print s
            Debug.Print Controls.Item(0).Name & " " & Controls("command1").Name & " " & Controls("Check1").Name & " " & Controls("mnuOpen").Name
            Debug.Print Controls!Label1.Name & " " & Me.Controls(1).Name & " " & (Controls("Command1") Is Command1)
            """);

        printed.Should().Equal(
            "Timer1 Command1 Text1 Frame1 Check1 Text1 Label1 Line1 mnuFile mnuOpen ",
            "Timer1 Command1 Check1 mnuOpen",
            "Label1 Command1 True");
    }

    [AvaloniaFact]
    public void Bang_on_Me_reads_a_controls_name()
    {
        Printed("Debug.Print Me!Command1.Name").Should().Equal("Command1");
    }

    [AvaloniaFact]
    public void Bang_on_Me_returns_the_same_control_as_Controls()
    {
        Printed("""
            Dim c As Object
            Set c = Me!Command1
            Debug.Print c Is Controls!Command1
            Debug.Print c Is Controls("Command1")
            """).Should().Equal("True", "True");
    }

    // A number is an index as CInt makes one, so 2.5 is 2. A String is an index only when it starts with a digit: VB6 reads
    // "1e0" as 1 and "-1" as the name of a control.
    [AvaloniaTheory]
    [InlineData("1.6", "Text1")]
    [InlineData("2.5", "Text1")]
    [InlineData("-0.4", "Timer1")]
    [InlineData("CByte(2)", "Text1")]
    [InlineData("\"1\"", "Command1")]
    [InlineData("\"01\"", "Command1")]
    [InlineData("\"1 \"", "Command1")]
    [InlineData("\"1e0\"", "Command1")]
    [InlineData("\"1.5\"", "Text1")]
    public void An_index_converts_as_CInt_does(string index, string name)
    {
        Printed($"Debug.Print Controls({index}).Name").Should().Equal(name);
    }

    [AvaloniaTheory]
    [InlineData("Controls.Count", 9, "Subscript out of range")]
    [InlineData("-1", 9, "Subscript out of range")]
    [InlineData("True", 9, "Subscript out of range")]
    [InlineData("100000", 6, "Overflow")]
    [InlineData("Null", 13, "Type mismatch")]
    [InlineData("\"Nope\"", 730, "Control 'Nope' not found")]
    [InlineData("\"-1\"", 730, "Control '-1' not found")]
    [InlineData("\" 1\"", 730, "Control ' 1' not found")]
    [InlineData("\"\"", 728, "'' is not a legal control name")]
    public void An_index_out_of_range_or_a_name_not_there_raises_VB6s_error(string index, int number, string description)
    {
        Printed($"""
            Dim s As String
            On Error Resume Next
            s = Controls({index}).Name
            Debug.Print Err.Number & " " & Err.Description
            """).Should().Equal($"{number} {description}");
    }

    // ── TypeOf ───────────────────────────────────────────────────────────────────────────────────────────

    // Every loop over Controls picks its controls this way. HexIDE answered False for every control, so the loop did
    // nothing, silently.
    [AvaloniaTheory]
    [InlineData("Text1(0) Is TextBox", true)]
    [InlineData("Text1(0) Is VB.TextBox", true)]
    [InlineData("Text1(0) Is Control", true)]
    [InlineData("Text1(0) Is Object", true)]
    [InlineData("Text1(0) Is CommandButton", false)]
    [InlineData("Command1 Is CommandButton", true)]
    [InlineData("mnuOpen Is Menu", true)]
    [InlineData("mnuOpen Is Control", true)]
    [InlineData("Controls Is Object", true)]
    [InlineData("Controls Is Collection", false)]
    [InlineData("Text1 Is Object", true)]
    [InlineData("Me Is Form", true)]
    [InlineData("Me Is Form1", true)]
    [InlineData("Me Is Control", false)]
    public void TypeOf_matches_a_control_by_its_class_and_as_a_Control(string test, bool expected)
    {
        Printed($"""
            Dim o As Object
            Set o = {test.Split(' ')[0]}
            Debug.Print CStr(TypeOf o Is {test.Split(' ')[2]})
            """).Should().Equal(expected ? "True" : "False");
    }

    [AvaloniaFact]
    public void TypeOf_matches_a_CommonDialog_by_its_class_and_its_library()
    {
        Printed("""
            Debug.Print CStr(TypeOf CommonDialog1 Is CommonDialog) & " " & CStr(TypeOf CommonDialog1 Is MSComDlg.CommonDialog) _
                & " " & CStr(TypeOf CommonDialog1 Is Control)
            """, WithDialog).Should().Equal("True True True");
    }

    [AvaloniaFact]
    public void A_control_arrays_name_gives_the_array()
    {
        Printed("""
            Debug.Print TypeName(Controls("Text1")) & " " & Controls("Text1").Count
            """).Should().Equal("Object 2");
    }

    [AvaloniaFact]
    public void Set_takes_the_collection_and_it_answers_through_the_variable()
    {
        Printed("""
            Dim o As Object
            Set o = Controls
            Debug.Print TypeName(o) & " " & o.Count & " " & o(1).Name
            """).Should().Equal("Object 10 Command1");
    }

    [AvaloniaFact]
    public void Let_of_the_collection_into_a_Variant_is_450()
    {
        Printed("""
            Dim v As Variant
            On Error Resume Next
            v = Controls
            Debug.Print Err.Number & " " & TypeName(v)
            """).Should().Equal("450 Empty");
    }

    // ── controls loaded and unloaded ─────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void An_element_loaded_goes_at_the_end_and_one_unloaded_goes_out()
    {
        Printed(Declare + """
            Load Text1(2): Load Text1(3)
            Debug.Print CStr(Controls.Count)
            """ + Loop() + """
            Debug.Print s
            Unload Text1(2)
            """ + Loop() + """
            Debug.Print s
            """).Should().Equal(
            "12",
            "Timer1 Command1 Text1(0) Frame1 Check1 Text1(1) Label1 Line1 mnuFile mnuOpen Text1(2) Text1(3) ",
            "Timer1 Command1 Text1(0) Frame1 Check1 Text1(1) Label1 Line1 mnuFile mnuOpen Text1(3) ");
    }

    // The usual way to clear a form of the elements it loaded: each is unloaded as the loop reaches it, and the loop goes
    // on to the next.
    [AvaloniaFact]
    public void A_loop_that_unloads_the_control_it_is_on_goes_on_to_the_next()
    {
        Printed(Declare + "Load Text1(2): Load Text1(3): Load Text1(4)\r\n" + Loop("If TypeOf c Is TextBox Then If c.Index > 1 Then Unload c") + """
            Debug.Print s
            Debug.Print CStr(Controls.Count)
            """).Should().Equal(
            "Timer1 Command1 Text1(0) Frame1 Check1 Text1(1) Label1 Line1 mnuFile mnuOpen Text1(2) Text1(3) Text1(4) ",
            "10");
    }

    [AvaloniaFact]
    public void A_loop_does_not_reach_a_control_unloaded_ahead_of_it_or_one_loaded_while_it_runs()
    {
        Printed(Declare + "Load Text1(2): Load Text1(3)\r\n" + Loop("If c.Name = \"Command1\" Then Unload Text1(3): Load Text1(4)") + """
            Debug.Print s
            Debug.Print CStr(Controls.Count)
            """).Should().Equal(
            "Timer1 Command1 Text1(0) Frame1 Check1 Text1(1) Label1 Line1 mnuFile mnuOpen Text1(2) ",
            "12");
    }

    // ── another form's ───────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Another_forms_Controls_loads_it_and_lists_its_own()
    {
        Printed("""
            Dim c As Object
            For Each c In Form2.Controls
                Debug.Print TypeName(c)
            Next
            Debug.Print Form2.Controls.Count & " " & Form2.Controls(1).Name
            """).Should().Equal("f2 load", "TextBox", "Label", "2 Label1");
    }
}
