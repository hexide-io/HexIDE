using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// <c>Controls.Add</c> and <c>Controls.Remove</c> (#1556), and a control held in a <c>WithEvents</c> variable. Every
/// expected line is what vb6.exe printed, native and p-code agreeing: the oracle's "Controls.Add and Controls.Remove". The
/// form is the probe's: Arial 12, with Frame1, Picture1 (Courier New), Text1, Command1 and Option1(0), and each program
/// runs as its Form_Load.
/// </summary>
/// <remarks>Generated from the probe's logs; where a test starts from fewer controls than the probe did, its comment says
/// by how many the counts differ.</remarks>
public class ControlsAddTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string Designer = """
        VERSION 5.00
        Begin VB.Form Form1
           BackColor       =   &H00C0FFFF&
           Caption         =   "Form1"
           ClientHeight    =   4800
           ClientLeft      =   1200
           ClientTop       =   1200
           ClientWidth     =   7200
           BeginProperty Font
              Name            =   "Arial"
              Size            =   12
              Charset         =   0
              Weight          =   400
              Underline       =   0   'False
              Italic          =   0   'False
              Strikethrough   =   0   'False
           EndProperty
           LinkTopic       =   "Form1"
           ScaleHeight     =   4800
           ScaleWidth      =   7200
           StartUpPosition =   0  'Manual
           Begin VB.Frame Frame1
              Caption         =   "Frame1"
              Height          =   1215
              Left            =   3600
              TabIndex        =   3
              Top             =   2400
              Width           =   2400
           End
           Begin VB.PictureBox Picture1
              BackColor       =   &H00FFC0C0&
              BeginProperty Font
                 Name            =   "Courier New"
                 Size            =   9.75
                 Charset         =   0
                 Weight          =   400
                 Underline       =   0   'False
                 Italic          =   0   'False
                 Strikethrough   =   0   'False
              EndProperty
              Height          =   1215
              Left            =   3600
              ScaleHeight     =   1155
              ScaleWidth      =   2340
              TabIndex        =   2
              Top             =   960
              Width           =   2400
           End
           Begin VB.TextBox Text1
              Height          =   375
              Left            =   240
              TabIndex        =   1
              Text            =   "Text1"
              Top             =   960
              Width           =   1215
           End
           Begin VB.CommandButton Command1
              Caption         =   "Command1"
              Height          =   495
              Left            =   240
              TabIndex        =   0
              Top             =   240
              Width           =   1215
           End
           Begin VB.OptionButton Option1
              Caption         =   "Option1"
              Height          =   375
              Index           =   0
              Left            =   240
              TabIndex        =   4
              Top             =   1800
              Width           =   1215
           End
        End
        Attribute VB_Name = "Form1"
        """;

    /// <summary>Run <paramref name="load"/> as Form_Load, with <paramref name="module"/> above it.</summary>
    private static (List<string> Printed, List<Exception> Errors) Run(string load, string module, string designer = Designer,
        Action<VBFormRuntime>? inspect = null)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var code = "\r\n" + module + "\r\nPrivate Sub Form_Load()\r\n" + load + "\r\nEnd Sub\r\n";
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
            _ = VBLoader.RunForm(form1, cancel.Token, out var window);
            Pump();
            inspect?.Invoke(window);
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

    // The name of the control a control sits in, or the form's where it sits on the form, as VB6's Container names it.
    // Every control of the form, those inside a Frame's or PictureBox's child host included: the host is a part of the
    // container's template, so the logical tree doesn't lead into it.
    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (var control in root.GetLogicalChildren().OfType<Control>())
        {
            yield return control;
            var host = (control as VBFrame)?.ChildHost ?? (control as VBPictureBox)?.ChildHost;
            foreach (var inner in AllControls(host ?? control))
                yield return inner;
        }
    }

    // The name of the Frame or PictureBox a control sits in, or the form's where it sits on the form, as VB6's Container
    // names it.
    private static string ContainerName(VBFormRuntime window, string name)
    {
        var controls = AllControls(window).ToList();
        foreach (var holder in controls)
            if (((holder as VBFrame)?.ChildHost ?? (holder as VBPictureBox)?.ChildHost) is { } host
                && host.Children.OfType<Control>().Any(c => VBProps.GetName(c) == name))
                return VBProps.GetName(holder)!;
        return controls.Any(c => VBProps.GetName(c) == name) ? "Form1" : "(not found)";
    }

    private static List<string> Printed(string load, string module, string designer = Designer)
    {
        var (printed, errors) = Run(load, module, designer);
        errors.Should().BeEmpty();
        return printed;
    }

    // Two design-time controls, then each class HexIDE implements. pos is the position in Controls, of the count.
    [AvaloniaFact]
    public void Add_makes_each_class_at_0_0_its_own_size_with_nothing_in_it_in_the_forms_font_and_last()
    {
        Printed("""
            Dim c As Object
            Describe "design Command1", Command1, Array("Name", "Left", "Top", "Width", "Height", "Enabled", "Index", "Caption", "BackColor")
            Describe "design Text1", Text1, Array("Name", "Left", "Top", "Width", "Height", "Enabled", "Index", "Text", "BackColor", "ForeColor")
            Set c = Controls.Add("VB.CommandButton", "a0")
            Describe "add VB.CommandButton", c, Array("Name", "Left", "Top", "Width", "Height", "Enabled", "Index", "Caption", "BackColor", "font")
            Set c = Controls.Add("VB.TextBox", "a1")
            Describe "add VB.TextBox", c, Array("Name", "Left", "Top", "Width", "Height", "Enabled", "Index", "Text", "BackColor", "ForeColor", "font")
            Set c = Controls.Add("VB.Label", "a2")
            Describe "add VB.Label", c, Array("Name", "Left", "Top", "Width", "Height", "Enabled", "Index", "Caption", "BackColor", "ForeColor", "font")
            Set c = Controls.Add("VB.CheckBox", "a3")
            Describe "add VB.CheckBox", c, Array("Name", "Left", "Top", "Width", "Height", "Enabled", "Index", "Caption", "BackColor", "ForeColor", "Value", "font")
            Set c = Controls.Add("VB.OptionButton", "a4")
            Describe "add VB.OptionButton", c, Array("Name", "Left", "Top", "Width", "Height", "Enabled", "Index", "Caption", "BackColor", "ForeColor", "Value", "font")
            Set c = Controls.Add("VB.Frame", "a5")
            Describe "add VB.Frame", c, Array("Name", "Left", "Top", "Width", "Height", "Enabled", "Index", "BackColor", "ForeColor", "font")
            Set c = Controls.Add("VB.PictureBox", "a6")
            Describe "add VB.PictureBox", c, Array("Name", "Left", "Top", "Width", "Height", "Enabled", "Index", "BackColor", "ForeColor", "font")
            Set c = Controls.Add("VB.Image", "a7")
            Describe "add VB.Image", c, Array("Name", "Left", "Top", "Width", "Height", "Enabled", "Index")
            Set c = Controls.Add("VB.ListBox", "a8")
            Describe "add VB.ListBox", c, Array("Name", "Left", "Top", "Width", "Height", "Enabled", "Index", "Text", "BackColor", "ForeColor", "font")
            Set c = Controls.Add("VB.ComboBox", "a9")
            Describe "add VB.ComboBox", c, Array("Name", "Left", "Top", "Width", "Height", "Enabled", "Index", "Text", "BackColor", "ForeColor", "font")
            Set c = Controls.Add("VB.HScrollBar", "a10")
            Describe "add VB.HScrollBar", c, Array("Name", "Left", "Top", "Width", "Height", "Enabled", "Index", "Value")
            Set c = Controls.Add("VB.VScrollBar", "a11")
            Describe "add VB.VScrollBar", c, Array("Name", "Left", "Top", "Width", "Height", "Enabled", "Index", "Value")
            Set c = Controls.Add("VB.Timer", "a12")
            Describe "add VB.Timer", c, Array("Name", "Enabled", "Index", "Interval")
            Set c = Controls.Add("VB.Shape", "a13")
            Describe "add VB.Shape", c, Array("Name", "Left", "Top", "Width", "Height", "Index")
            Set c = Controls.Add("VB.Line", "a14")
            Describe "add VB.Line", c, Array("Name", "Index", "X1", "Y1", "X2", "Y2")
            """,
            """
            Private Function G(c As Object, ByVal prop As String) As String
                Dim v
                On Error Resume Next
                Err.Clear
                Select Case prop
                    Case "Name": v = c.Name
                    Case "Left": v = c.Left
                    Case "Top": v = c.Top
                    Case "Width": v = c.Width
                    Case "Height": v = c.Height
                    Case "Enabled": v = c.Enabled
                    Case "Index": v = c.Index
                    Case "Caption": v = c.Caption
                    Case "Text": v = c.Text
                    Case "BackColor": v = c.BackColor
                    Case "ForeColor": v = c.ForeColor
                    Case "X1": v = c.X1
                    Case "Y1": v = c.Y1
                    Case "X2": v = c.X2
                    Case "Y2": v = c.Y2
                    Case "Interval": v = c.Interval
                    Case "Value": v = c.Value
                    Case "Visible": v = c.Visible
                    Case "font": v = c.FontName & "/" & c.FontSize
                End Select
                If Err.Number <> 0 Then G = " " & prop & "=e" & Err.Number Else G = " " & prop & "=" & v
            End Function

            Private Function PosOf(c As Object) As String
                Dim i As Integer
                PosOf = " pos=none"
                For i = 0 To Controls.Count - 1
                    If Controls(i) Is c Then PosOf = " pos=" & i & "/" & Controls.Count
                Next
            End Function

            Private Sub Describe(ByVal label As String, c As Object, props)
                Dim s As String, p
                s = label & " -> " & TypeName(c)
                For Each p In props
                    s = s & G(c, p)
                Next
                Debug.Print s & PosOf(c)
            End Sub

            Private Function E() As String
                E = "err " & Err.Number & IIf(Err.Number <> 0, " [" & Err.Description & "]", "") & " count " & Controls.Count
            End Function

            Private Sub T(ByVal label As String, progid, nm, Optional cont)
                Dim c As Object
                On Error Resume Next
                Err.Clear
                If IsMissing(cont) Then
                    Set c = Controls.Add(progid, nm)
                Else
                    Set c = Controls.Add(progid, nm, cont)
                End If
                Debug.Print label & " -> " & E() & " type " & TypeName(c)
            End Sub

            Private Sub R(ByVal label As String, what)
                On Error Resume Next
                Err.Clear
                Controls.Remove what
                Debug.Print label & " -> " & E()
            End Sub

            """).Should().Equal(
            "design Command1 -> CommandButton Name=Command1 Left=240 Top=240 Width=1215 Height=495 Enabled=True Index=e343 Caption=Command1 BackColor=-2147483633 pos=3/5",
            "design Text1 -> TextBox Name=Text1 Left=240 Top=960 Width=1215 Height=375 Enabled=True Index=e343 Text=Text1 BackColor=-2147483643 ForeColor=-2147483640 pos=2/5",
            "add VB.CommandButton -> CommandButton Name=a0 Left=0 Top=0 Width=1200 Height=480 Enabled=True Index=e343 Caption= BackColor=-2147483633 font=Arial/12 pos=5/6",
            "add VB.TextBox -> TextBox Name=a1 Left=0 Top=0 Width=1200 Height=480 Enabled=True Index=e343 Text= BackColor=-2147483643 ForeColor=-2147483640 font=Arial/12 pos=6/7",
            "add VB.Label -> Label Name=a2 Left=0 Top=0 Width=1200 Height=480 Enabled=True Index=e343 Caption= BackColor=-2147483633 ForeColor=-2147483630 font=Arial/12 pos=7/8",
            "add VB.CheckBox -> CheckBox Name=a3 Left=0 Top=0 Width=1200 Height=480 Enabled=True Index=e343 Caption= BackColor=-2147483633 ForeColor=-2147483630 Value=0 font=Arial/12 pos=8/9",
            "add VB.OptionButton -> OptionButton Name=a4 Left=0 Top=0 Width=1200 Height=480 Enabled=True Index=e343 Caption= BackColor=-2147483633 ForeColor=-2147483630 Value=False font=Arial/12 pos=9/10",
            "add VB.Frame -> Frame Name=a5 Left=0 Top=0 Width=1200 Height=480 Enabled=True Index=e343 BackColor=-2147483633 ForeColor=-2147483630 font=Arial/12 pos=10/11",
            "add VB.PictureBox -> PictureBox Name=a6 Left=0 Top=0 Width=1200 Height=480 Enabled=True Index=e343 BackColor=-2147483633 ForeColor=-2147483630 font=Arial/12 pos=11/12",
            "add VB.Image -> Image Name=a7 Left=0 Top=0 Width=1200 Height=480 Enabled=True Index=e343 pos=12/13",
            "add VB.ListBox -> ListBox Name=a8 Left=0 Top=0 Width=1200 Height=330 Enabled=True Index=e343 Text= BackColor=-2147483643 ForeColor=-2147483640 font=Arial/12 pos=13/14",
            "add VB.ComboBox -> ComboBox Name=a9 Left=0 Top=0 Width=1200 Height=315 Enabled=True Index=e343 Text= BackColor=-2147483643 ForeColor=-2147483640 font=Arial/12 pos=14/15",
            "add VB.HScrollBar -> HScrollBar Name=a10 Left=0 Top=0 Width=1200 Height=240 Enabled=True Index=e343 Value=0 pos=15/16",
            "add VB.VScrollBar -> VScrollBar Name=a11 Left=0 Top=0 Width=240 Height=1200 Enabled=True Index=e343 Value=0 pos=16/17",
            "add VB.Timer -> Timer Name=a12 Enabled=True Index=e343 Interval=0 pos=17/18",
            "add VB.Shape -> Shape Name=a13 Left=0 Top=0 Width=1200 Height=480 Index=e343 pos=18/19",
            "add VB.Line -> Line Name=a14 Index=e343 X1=0 Y1=0 X2=0 Y2=0 pos=19/20");
    }

    // Visible as the control's own flag, which vb6.exe's WS_VISIBLE shows: session 0 never showed the form, so its Visible read False.
    [AvaloniaFact]
    public void An_added_control_is_hidden_until_its_Visible_is_set()
    {
        Printed("""
            Dim c As Object, i As Integer, classes
            classes = Array("VB.CommandButton", "VB.TextBox", "VB.PictureBox", "VB.Frame", "VB.ListBox", "VB.ComboBox", "VB.CheckBox")
            For i = 0 To UBound(classes)
                Set c = Controls.Add(classes(i), "a" & i)
                Debug.Print "added " & classes(i) & G(c, "Visible")
                c.Visible = True
                Debug.Print "  after Visible = True" & G(c, "Visible")
            Next
            """,
            """
            Private Function G(c As Object, ByVal prop As String) As String
                Dim v
                On Error Resume Next
                Err.Clear
                Select Case prop
                    Case "Name": v = c.Name
                    Case "Left": v = c.Left
                    Case "Top": v = c.Top
                    Case "Width": v = c.Width
                    Case "Height": v = c.Height
                    Case "Enabled": v = c.Enabled
                    Case "Index": v = c.Index
                    Case "Caption": v = c.Caption
                    Case "Text": v = c.Text
                    Case "BackColor": v = c.BackColor
                    Case "ForeColor": v = c.ForeColor
                    Case "X1": v = c.X1
                    Case "Y1": v = c.Y1
                    Case "X2": v = c.X2
                    Case "Y2": v = c.Y2
                    Case "Interval": v = c.Interval
                    Case "Value": v = c.Value
                    Case "Visible": v = c.Visible
                    Case "font": v = c.FontName & "/" & c.FontSize
                End Select
                If Err.Number <> 0 Then G = " " & prop & "=e" & Err.Number Else G = " " & prop & "=" & v
            End Function

            Private Function PosOf(c As Object) As String
                Dim i As Integer
                PosOf = " pos=none"
                For i = 0 To Controls.Count - 1
                    If Controls(i) Is c Then PosOf = " pos=" & i & "/" & Controls.Count
                Next
            End Function

            Private Sub Describe(ByVal label As String, c As Object, props)
                Dim s As String, p
                s = label & " -> " & TypeName(c)
                For Each p In props
                    s = s & G(c, p)
                Next
                Debug.Print s & PosOf(c)
            End Sub

            Private Function E() As String
                E = "err " & Err.Number & IIf(Err.Number <> 0, " [" & Err.Description & "]", "") & " count " & Controls.Count
            End Function

            Private Sub T(ByVal label As String, progid, nm, Optional cont)
                Dim c As Object
                On Error Resume Next
                Err.Clear
                If IsMissing(cont) Then
                    Set c = Controls.Add(progid, nm)
                Else
                    Set c = Controls.Add(progid, nm, cont)
                End If
                Debug.Print label & " -> " & E() & " type " & TypeName(c)
            End Sub

            Private Sub R(ByVal label As String, what)
                On Error Resume Next
                Err.Clear
                Controls.Remove what
                Debug.Print label & " -> " & E()
            End Sub

            """).Should().Equal(
            "added VB.CommandButton Visible=False",
            "  after Visible = True Visible=True",
            "added VB.TextBox Visible=False",
            "  after Visible = True Visible=True",
            "added VB.PictureBox Visible=False",
            "  after Visible = True Visible=True",
            "added VB.Frame Visible=False",
            "  after Visible = True Visible=True",
            "added VB.ListBox Visible=False",
            "  after Visible = True Visible=True",
            "added VB.ComboBox Visible=False",
            "  after Visible = True Visible=True",
            "added VB.CheckBox Visible=False",
            "  after Visible = True Visible=True");
    }

    // FontName and FontSize, which are the Font's Name and Size, as the probe read them. Its row for Command1, which kept the
    // form's Arial 12, is left out: a design-time control without a font of its own reads MS Sans Serif here (#2062). A host
    // without Times New Roman gives vb6.exe's answer for a font it hasn't got, 380, and the ListBox test below shows the
    // same rule through the fonts every host can set.
    [AvaloniaFact]
    public void An_added_control_takes_the_forms_font_as_it_stands_when_it_is_added()
    {
        var (printed, errors) = Run("""
            Dim c As Object
            Me.FontName = "Times New Roman"
            Me.FontSize = 14
            Set c = Controls.Add("VB.CommandButton", "s2")
            Describe "added s2 after the form's font changed", c, Array("font")
            """,
            """
            Private Function G(c As Object, ByVal prop As String) As String
                Dim v
                On Error Resume Next
                Err.Clear
                Select Case prop
                    Case "Name": v = c.Name
                    Case "Left": v = c.Left
                    Case "Top": v = c.Top
                    Case "Width": v = c.Width
                    Case "Height": v = c.Height
                    Case "Enabled": v = c.Enabled
                    Case "Index": v = c.Index
                    Case "Caption": v = c.Caption
                    Case "Text": v = c.Text
                    Case "BackColor": v = c.BackColor
                    Case "ForeColor": v = c.ForeColor
                    Case "X1": v = c.X1
                    Case "Y1": v = c.Y1
                    Case "X2": v = c.X2
                    Case "Y2": v = c.Y2
                    Case "Interval": v = c.Interval
                    Case "Value": v = c.Value
                    Case "Visible": v = c.Visible
                    Case "font": v = c.FontName & "/" & c.FontSize
                End Select
                If Err.Number <> 0 Then G = " " & prop & "=e" & Err.Number Else G = " " & prop & "=" & v
            End Function

            Private Function PosOf(c As Object) As String
                Dim i As Integer
                PosOf = " pos=none"
                For i = 0 To Controls.Count - 1
                    If Controls(i) Is c Then PosOf = " pos=" & i & "/" & Controls.Count
                Next
            End Function

            Private Sub Describe(ByVal label As String, c As Object, props)
                Dim s As String, p
                s = label & " -> " & TypeName(c)
                For Each p In props
                    s = s & G(c, p)
                Next
                Debug.Print s
            End Sub

            Private Function E() As String
                E = "err " & Err.Number & IIf(Err.Number <> 0, " [" & Err.Description & "]", "") & " count " & Controls.Count
            End Function

            Private Sub T(ByVal label As String, progid, nm, Optional cont)
                Dim c As Object
                On Error Resume Next
                Err.Clear
                If IsMissing(cont) Then
                    Set c = Controls.Add(progid, nm)
                Else
                    Set c = Controls.Add(progid, nm, cont)
                End If
                Debug.Print label & " -> " & E() & " type " & TypeName(c)
            End Sub

            Private Sub R(ByVal label As String, what)
                On Error Resume Next
                Err.Clear
                Controls.Remove what
                Debug.Print label & " -> " & E()
            End Sub

            """);
        if (VBFontExtensions.IsNameable("Times New Roman"))
        {
            errors.Should().BeEmpty();
            printed.Should().Equal("added s2 after the form's font changed -> CommandButton font=Times New Roman/14.25");
        }
        else
            errors.Should().ContainSingle().Which.Should().BeOfType<VBRunTimeException>().Which.Error.ErrNo.Should().Be(380);
    }

    // The fourteen fonts measured, read through FontName and FontSize. A font the host can't set is left out, since setting it
    // is 380 there, as in vb6.exe; MS Sans Serif and Microsoft Sans Serif can be set on any host, so every host runs some.
    [AvaloniaFact]
    public void An_added_ListBox_is_as_many_rows_of_its_font_as_fit_in_480_twips_and_a_ComboBox_is_315()
    {
        (string Font, int Size, string Row)[] measured =
        [
            ("MS Sans Serif", 8, "MS Sans Serif 8.25 -> ListBox 1200x450 ComboBox 1200x315 (fonts MS Sans Serif/8.25, MS Sans Serif/8.25) err 0"),
            ("MS Sans Serif", 10, "MS Sans Serif 9.75 -> ListBox 1200x300 ComboBox 1200x315 (fonts MS Sans Serif/9.75, MS Sans Serif/9.75) err 0"),
            ("MS Sans Serif", 12, "MS Sans Serif 12 -> ListBox 1200x360 ComboBox 1200x315 (fonts MS Sans Serif/12, MS Sans Serif/12) err 0"),
            ("MS Sans Serif", 14, "MS Sans Serif 13.5 -> ListBox 1200x420 ComboBox 1200x315 (fonts MS Sans Serif/13.5, MS Sans Serif/13.5) err 0"),
            ("Tahoma", 8, "Tahoma 8.25 -> ListBox 1200x450 ComboBox 1200x315 (fonts Tahoma/8.25, Tahoma/8.25) err 0"),
            ("Tahoma", 10, "Tahoma 9.75 -> ListBox 1200x300 ComboBox 1200x315 (fonts Tahoma/9.75, Tahoma/9.75) err 0"),
            ("Arial", 8, "Arial 8.25 -> ListBox 1200x480 ComboBox 1200x315 (fonts Arial/8.25, Arial/8.25) err 0"),
            ("Arial", 10, "Arial 9.75 -> ListBox 1200x300 ComboBox 1200x315 (fonts Arial/9.75, Arial/9.75) err 0"),
            ("Arial", 12, "Arial 12 -> ListBox 1200x330 ComboBox 1200x315 (fonts Arial/12, Arial/12) err 0"),
            ("Times New Roman", 12, "Times New Roman 12 -> ListBox 1200x345 ComboBox 1200x315 (fonts Times New Roman/12, Times New Roman/12) err 0"),
            ("Courier New", 10, "Courier New 9.75 -> ListBox 1200x300 ComboBox 1200x315 (fonts Courier New/9.75, Courier New/9.75) err 0"),
            ("Segoe UI", 9, "Segoe UI 9 -> ListBox 1200x285 ComboBox 1200x315 (fonts Segoe UI/9, Segoe UI/9) err 0"),
            ("Verdana", 8, "Verdana 8.25 -> ListBox 1200x450 ComboBox 1200x315 (fonts Verdana/8.25, Verdana/8.25) err 0"),
            ("Microsoft Sans Serif", 8, "Microsoft Sans Serif 8.25 -> ListBox 1200x450 ComboBox 1200x315 (fonts Microsoft Sans Serif/8.25, Microsoft Sans Serif/8.25) err 0"),
        ];
        var run = measured.Where(m => VBFontExtensions.IsNameable(m.Font)).ToList();
        run.Should().Contain(m => m.Font == "MS Sans Serif");
        var fonts = string.Join(", ", run.Select(m => $"\"{m.Font}\""));
        var sizes = string.Join(", ", run.Select(m => m.Size));
        Printed($"""
            Dim fonts, sizes, i As Integer, lb As Object, cb As Object, n As Integer
            fonts = Array({fonts})
            sizes = Array({sizes})
            For i = 0 To UBound(fonts)
                Me.FontName = fonts(i)
                Me.FontSize = sizes(i)
                n = n + 1
                Set lb = Controls.Add("VB.ListBox", "lb" & n)
                Set cb = Controls.Add("VB.ComboBox", "cb" & n)
                Debug.Print Me.FontName & " " & Me.FontSize & " -> ListBox " & lb.Width & "x" & lb.Height & " ComboBox " & cb.Width & "x" & cb.Height _
                    & " (fonts " & lb.FontName & "/" & lb.FontSize & ", " & cb.FontName & "/" & cb.FontSize & ") err " & Err.Number
            Next
            """,
            "").Should().Equal(run.Select(m => m.Row));
    }

    // A control added at 0, 0 in its container. Where each landed is read from the form's tree: a control's Container isn't
    // implemented at run time.
    [AvaloniaFact]
    public void Adds_third_argument_is_a_Frame_or_PictureBox_or_the_form_and_anything_else_is_refused()
    {
        var landed = new Dictionary<string, string>();
        var (printed, errors) = Run("""
            Dim k As Object
            Set k = Controls.Add("VB.Frame", "a5")
            Set k = Controls.Add("VB.PictureBox", "a6")
            TC "in Picture1", "k1", Picture1
            TC "in Frame1", "k2", Frame1
            TC "in Text1", "k3", Text1
            TC "in Me", "k4", Me
            TC "in Option1(0)", "k5", Option1(0)
            TC "in Nothing", "k6", Nothing
            TC "in a string", "k7", "Picture1"
            TC "in added frame a5", "k8", Controls("a5")
            TC "in added picture a6", "k9", Controls("a6")
            """,
            """
            Private Function G(c As Object, ByVal prop As String) As String
                Dim v
                On Error Resume Next
                Err.Clear
                Select Case prop
                    Case "Name": v = c.Name
                    Case "Left": v = c.Left
                    Case "Top": v = c.Top
                    Case "Width": v = c.Width
                    Case "Height": v = c.Height
                    Case "Enabled": v = c.Enabled
                    Case "Index": v = c.Index
                    Case "Caption": v = c.Caption
                    Case "Text": v = c.Text
                    Case "BackColor": v = c.BackColor
                    Case "ForeColor": v = c.ForeColor
                    Case "X1": v = c.X1
                    Case "Y1": v = c.Y1
                    Case "X2": v = c.X2
                    Case "Y2": v = c.Y2
                    Case "Interval": v = c.Interval
                    Case "Value": v = c.Value
                    Case "Visible": v = c.Visible
                    Case "font": v = c.FontName & "/" & c.FontSize
                End Select
                If Err.Number <> 0 Then G = " " & prop & "=e" & Err.Number Else G = " " & prop & "=" & v
            End Function

            Private Function PosOf(c As Object) As String
                Dim i As Integer
                PosOf = " pos=none"
                For i = 0 To Controls.Count - 1
                    If Controls(i) Is c Then PosOf = " pos=" & i & "/" & Controls.Count
                Next
            End Function

            Private Sub Describe(ByVal label As String, c As Object, props)
                Dim s As String, p
                s = label & " -> " & TypeName(c)
                For Each p In props
                    s = s & G(c, p)
                Next
                Debug.Print s & PosOf(c)
            End Sub

            Private Function E() As String
                E = "err " & Err.Number & IIf(Err.Number <> 0, " [" & Err.Description & "]", "") & " count " & Controls.Count
            End Function

            Private Sub T(ByVal label As String, progid, nm, Optional cont)
                Dim c As Object
                On Error Resume Next
                Err.Clear
                If IsMissing(cont) Then
                    Set c = Controls.Add(progid, nm)
                Else
                    Set c = Controls.Add(progid, nm, cont)
                End If
                Debug.Print label & " -> " & E() & " type " & TypeName(c)
            End Sub

            Private Sub R(ByVal label As String, what)
                On Error Resume Next
                Err.Clear
                Controls.Remove what
                Debug.Print label & " -> " & E()
            End Sub

            Private Sub TC(ByVal label As String, ByVal nm As String, cont)
                Dim c As Object
                On Error Resume Next
                Err.Clear
                Set c = Controls.Add("VB.CommandButton", nm, cont)
                If Err.Number <> 0 Or c Is Nothing Then
                    Debug.Print label & " -> err " & Err.Number & " [" & Err.Description & "]"
                Else
                    Debug.Print label & " ->" & G(c, "Left") & G(c, "Top")
                End If
            End Sub

            """,
            inspect: window =>
            {
                foreach (var name in new[] { "k1", "k2", "k4", "k8", "k9" })
                    landed[name] = ContainerName(window, name);
            });

        errors.Should().BeEmpty();
        printed.Should().Equal(
            "in Picture1 -> Left=0 Top=0",
            "in Frame1 -> Left=0 Top=0",
            "in Text1 -> err 721 ['TextBox' is not a valid control container]",
            "in Me -> Left=0 Top=0",
            "in Option1(0) -> err 721 ['OptionButton' is not a valid control container]",
            "in Nothing -> err 721 ['<Nothing>' is not a valid control container]",
            "in a string -> err -2147352571 [Type mismatch]",
            "in added frame a5 -> Left=0 Top=0",
            "in added picture a6 -> Left=0 Top=0");
        landed.Should().Equal(new Dictionary<string, string> { ["k1"] = "Picture1", ["k2"] = "Frame1", ["k4"] = "Form1", ["k8"] = "a5", ["k9"] = "a6" });
    }

    // Counts 19 fewer than the probe's, which had added every class first. An ActiveX ProgID is refused as not implemented, below.
    [AvaloniaFact]
    public void Add_checks_the_name_for_another_control_and_for_empty_and_the_class_for_one_it_knows()
    {
        Printed("""
            Dim c As Object
            Set c = Controls.Add("VB.CommandButton", "a0")
            T "dup Command1", "VB.CommandButton", "Command1"
            T "dup a0", "VB.CommandButton", "a0"
            T "dup A0 case", "VB.CommandButton", "A0"
            T "array name", "VB.CommandButton", "Option1"
            T "empty name", "VB.CommandButton", ""
            T "digit name", "VB.CommandButton", "1abc"
            T "space name", "VB.CommandButton", "a b"
            T "dot name", "VB.CommandButton", "a.b"
            T "40 name", "VB.CommandButton", "yyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyy"
            T "41 name", "VB.CommandButton", "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            T "keyword name", "VB.CommandButton", "Next"
            T "form name", "VB.CommandButton", "Form1"
            T "variable name", "VB.CommandButton", "b"
            T "lower progid", "vb.commandbutton", "lowerOk"
            T "unknown", "VB.Nope", "u1"
            T "empty progid", "", "u2"
            T "form", "VB.Form", "u3"
            T "mdiform", "VB.MDIForm", "u4"
            T "menu", "VB.Menu", "u5"
            T "usercontrol", "VB.UserControl", "u6"
            """,
            """
            Private Function G(c As Object, ByVal prop As String) As String
                Dim v
                On Error Resume Next
                Err.Clear
                Select Case prop
                    Case "Name": v = c.Name
                    Case "Left": v = c.Left
                    Case "Top": v = c.Top
                    Case "Width": v = c.Width
                    Case "Height": v = c.Height
                    Case "Enabled": v = c.Enabled
                    Case "Index": v = c.Index
                    Case "Caption": v = c.Caption
                    Case "Text": v = c.Text
                    Case "BackColor": v = c.BackColor
                    Case "ForeColor": v = c.ForeColor
                    Case "X1": v = c.X1
                    Case "Y1": v = c.Y1
                    Case "X2": v = c.X2
                    Case "Y2": v = c.Y2
                    Case "Interval": v = c.Interval
                    Case "Value": v = c.Value
                    Case "Visible": v = c.Visible
                    Case "font": v = c.FontName & "/" & c.FontSize
                End Select
                If Err.Number <> 0 Then G = " " & prop & "=e" & Err.Number Else G = " " & prop & "=" & v
            End Function

            Private Function PosOf(c As Object) As String
                Dim i As Integer
                PosOf = " pos=none"
                For i = 0 To Controls.Count - 1
                    If Controls(i) Is c Then PosOf = " pos=" & i & "/" & Controls.Count
                Next
            End Function

            Private Sub Describe(ByVal label As String, c As Object, props)
                Dim s As String, p
                s = label & " -> " & TypeName(c)
                For Each p In props
                    s = s & G(c, p)
                Next
                Debug.Print s & PosOf(c)
            End Sub

            Private Function E() As String
                E = "err " & Err.Number & IIf(Err.Number <> 0, " [" & Err.Description & "]", "") & " count " & Controls.Count
            End Function

            Private Sub T(ByVal label As String, progid, nm, Optional cont)
                Dim c As Object
                On Error Resume Next
                Err.Clear
                If IsMissing(cont) Then
                    Set c = Controls.Add(progid, nm)
                Else
                    Set c = Controls.Add(progid, nm, cont)
                End If
                Debug.Print label & " -> " & E() & " type " & TypeName(c)
            End Sub

            Private Sub R(ByVal label As String, what)
                On Error Resume Next
                Err.Clear
                Controls.Remove what
                Debug.Print label & " -> " & E()
            End Sub

            """).Should().Equal(
            "dup Command1 -> err 727 [There is already a control with the name 'Command1'] count 6 type Nothing",
            "dup a0 -> err 727 [There is already a control with the name 'a0'] count 6 type Nothing",
            "dup A0 case -> err 727 [There is already a control with the name 'A0'] count 6 type Nothing",
            "array name -> err 727 [There is already a control with the name 'Option1'] count 6 type Nothing",
            "empty name -> err 728 ['' is not a legal control name] count 6 type Nothing",
            "digit name -> err 0 count 7 type CommandButton",
            "space name -> err 0 count 8 type CommandButton",
            "dot name -> err 0 count 9 type CommandButton",
            "40 name -> err 0 count 10 type CommandButton",
            "41 name -> err 0 count 11 type CommandButton",
            "keyword name -> err 0 count 12 type CommandButton",
            "form name -> err 0 count 13 type CommandButton",
            "variable name -> err 0 count 14 type CommandButton",
            "lower progid -> err 0 count 15 type CommandButton",
            "unknown -> err 711 [Invalid class string.\nLooking for object with ProgID: VB.Nope] count 15 type Nothing",
            "empty progid -> err 711 [Invalid class string.\nLooking for object with ProgID: ] count 15 type Nothing",
            "form -> err 720 ['Form' is not a valid control type] count 15 type Nothing",
            "mdiform -> err 720 ['MDIForm' is not a valid control type] count 15 type Nothing",
            "menu -> err 720 ['Menu' is not a valid control type] count 15 type Nothing",
            "usercontrol -> err 720 ['UserControl' is not a valid control type] count 15 type Nothing");
    }

    // With the odd argument types. A ProgID with no VB. is an ActiveX one, refused as not implemented, so its two rows are left out.
    [AvaloniaFact]
    public void When_more_than_one_argument_is_wrong_the_name_is_checked_first_then_the_class_then_the_container()
    {
        Printed(""""
            On Error Resume Next
            T "bad class, taken name", "VB.Nope", "Command1"
            T "not a control, taken name", "VB.Form", "Command1"
            T "taken name, bad container", "VB.CommandButton", "Command1", Text1
            T "bad class, bad container", "VB.Nope", "x1", Text1
            T "empty name, bad container", "VB.CommandButton", "", Text1
            T "empty class, empty name", "", ""
            T "Null class", Null, "x2"
            T "Null name", "VB.CommandButton", Null
            T "number name", "VB.CommandButton", 123
            T "Empty name", "VB.CommandButton", Empty
            T "digit-led name", "VB.CommandButton", "1abc"
            Err.Clear
            Debug.Print "Controls(""1abc"") -> " & Controls("1abc").Name & " err " & Err.Number & " [" & D() & "]"
            R "remove ""1abc""", "1abc"
            T "a name with a space", "VB.CommandButton", "a b"
            R "remove ""a b""", "a b"
            R "remove ""Option1""", "Option1"
            """",
            """
            Private Function G(c As Object, ByVal prop As String) As String
                Dim v
                On Error Resume Next
                Err.Clear
                Select Case prop
                    Case "Name": v = c.Name
                    Case "Left": v = c.Left
                    Case "Top": v = c.Top
                    Case "Width": v = c.Width
                    Case "Height": v = c.Height
                    Case "Enabled": v = c.Enabled
                    Case "Index": v = c.Index
                    Case "Caption": v = c.Caption
                    Case "Text": v = c.Text
                    Case "BackColor": v = c.BackColor
                    Case "ForeColor": v = c.ForeColor
                    Case "X1": v = c.X1
                    Case "Y1": v = c.Y1
                    Case "X2": v = c.X2
                    Case "Y2": v = c.Y2
                    Case "Interval": v = c.Interval
                    Case "Value": v = c.Value
                    Case "Visible": v = c.Visible
                    Case "font": v = c.FontName & "/" & c.FontSize
                End Select
                If Err.Number <> 0 Then G = " " & prop & "=e" & Err.Number Else G = " " & prop & "=" & v
            End Function

            Private Function PosOf(c As Object) As String
                Dim i As Integer
                PosOf = " pos=none"
                For i = 0 To Controls.Count - 1
                    If Controls(i) Is c Then PosOf = " pos=" & i & "/" & Controls.Count
                Next
            End Function

            Private Sub Describe(ByVal label As String, c As Object, props)
                Dim s As String, p
                s = label & " -> " & TypeName(c)
                For Each p In props
                    s = s & G(c, p)
                Next
                Debug.Print s & PosOf(c)
            End Sub

            Private Function E() As String
                E = "err " & Err.Number & IIf(Err.Number <> 0, " [" & Err.Description & "]", "") & " count " & Controls.Count
            End Function

            Private Sub T(ByVal label As String, progid, nm, Optional cont)
                Dim c As Object
                On Error Resume Next
                Err.Clear
                If IsMissing(cont) Then
                    Set c = Controls.Add(progid, nm)
                Else
                    Set c = Controls.Add(progid, nm, cont)
                End If
                Debug.Print label & " -> err " & Err.Number & " [" & D() & "] count " & Controls.Count & " type " & TypeName(c)
                If Not c Is Nothing Then Debug.Print "   name [" & c.Name & "]"
            End Sub

            Private Sub R(ByVal label As String, what)
                On Error Resume Next
                Err.Clear
                Controls.Remove what
                Debug.Print label & " -> err " & Err.Number & " [" & D() & "] count " & Controls.Count
            End Sub

            Private Function D() As String
                D = Replace(Replace(Err.Description, vbCr, "<CR>"), vbLf, "<LF>")
            End Function

            """).Should().Equal(
            "bad class, taken name -> err 727 [There is already a control with the name 'Command1'] count 5 type Nothing",
            "not a control, taken name -> err 727 [There is already a control with the name 'Command1'] count 5 type Nothing",
            "taken name, bad container -> err 727 [There is already a control with the name 'Command1'] count 5 type Nothing",
            "bad class, bad container -> err 711 [Invalid class string.<LF>Looking for object with ProgID: VB.Nope] count 5 type Nothing",
            "empty name, bad container -> err 728 ['' is not a legal control name] count 5 type Nothing",
            "empty class, empty name -> err 728 ['' is not a legal control name] count 5 type Nothing",
            "Null class -> err -2147352571 [Type mismatch] count 5 type Nothing",
            "Null name -> err -2147352571 [Type mismatch] count 5 type Nothing",
            "number name -> err 0 [] count 6 type CommandButton",
            "   name [123]",
            "Empty name -> err 728 ['' is not a legal control name] count 6 type Nothing",
            "digit-led name -> err 0 [] count 7 type CommandButton",
            "   name [1abc]",
            "remove \"1abc\" -> err -2147352571 [Type mismatch] count 7",
            "a name with a space -> err 0 [] count 8 type CommandButton",
            "   name [a b]",
            "remove \"a b\" -> err 0 [] count 7",
            "remove \"Option1\" -> err -2147467262 [System Error &H80004002 (-2147467262).   No such interface supported ] count 7");
    }

    // Counts 31 fewer than the probe's, which had added more by then. A control still held is 735 to use. The probe's Me!a2 is left out: Me!name on a form is not implemented.
    [AvaloniaFact]
    public void Remove_takes_an_added_control_by_its_name_itself_or_its_position_and_refuses_the_rest()
    {
        Printed("""
            On Error Resume Next
            Dim c As Object, c2 As Object
            Set c = Controls.Add("VB.Label", "a2")
            Set c = Controls.Add("VB.CheckBox", "a3")
            Set c = Controls.Add("VB.OptionButton", "a4")
            Set c = Controls("a2")
            Err.Clear
            Debug.Print "bang Controls!a2 " & Controls!a2.Name & " " & E()
            Err.Clear
            Debug.Print "item a2 is c " & (Controls("A2") Is c) & " " & E()
            R "remove a2", "a2"
            Debug.Print "held after remove: type " & TypeName(c) & G(c, "Name") & G(c, "Caption") & G(c, "Visible")
            Err.Clear
            c.Visible = True
            Debug.Print "held set Visible -> " & E()
            T "re-add a2", "VB.Label", "a2"
            R "remove A3 case", "A3"
            R "remove Command1", "Command1"
            R "remove Option1", "Option1"
            R "remove Option1(0)", Option1(0)
            R "remove nope", "nope"
            R "remove empty", ""
            R "remove 0", 0
            Set c2 = Controls("a4")
            R "remove object a4", c2
            R "remove a2 again", "a2"
            R "remove a2 twice", "a2"
            R "remove Null", Null
            """,
            """
            Private Function G(c As Object, ByVal prop As String) As String
                Dim v
                On Error Resume Next
                Err.Clear
                Select Case prop
                    Case "Name": v = c.Name
                    Case "Left": v = c.Left
                    Case "Top": v = c.Top
                    Case "Width": v = c.Width
                    Case "Height": v = c.Height
                    Case "Enabled": v = c.Enabled
                    Case "Index": v = c.Index
                    Case "Caption": v = c.Caption
                    Case "Text": v = c.Text
                    Case "BackColor": v = c.BackColor
                    Case "ForeColor": v = c.ForeColor
                    Case "X1": v = c.X1
                    Case "Y1": v = c.Y1
                    Case "X2": v = c.X2
                    Case "Y2": v = c.Y2
                    Case "Interval": v = c.Interval
                    Case "Value": v = c.Value
                    Case "Visible": v = c.Visible
                    Case "font": v = c.FontName & "/" & c.FontSize
                End Select
                If Err.Number <> 0 Then G = " " & prop & "=e" & Err.Number Else G = " " & prop & "=" & v
            End Function

            Private Function PosOf(c As Object) As String
                Dim i As Integer
                PosOf = " pos=none"
                For i = 0 To Controls.Count - 1
                    If Controls(i) Is c Then PosOf = " pos=" & i & "/" & Controls.Count
                Next
            End Function

            Private Sub Describe(ByVal label As String, c As Object, props)
                Dim s As String, p
                s = label & " -> " & TypeName(c)
                For Each p In props
                    s = s & G(c, p)
                Next
                Debug.Print s & PosOf(c)
            End Sub

            Private Function E() As String
                E = "err " & Err.Number & IIf(Err.Number <> 0, " [" & Err.Description & "]", "") & " count " & Controls.Count
            End Function

            Private Sub T(ByVal label As String, progid, nm, Optional cont)
                Dim c As Object
                On Error Resume Next
                Err.Clear
                If IsMissing(cont) Then
                    Set c = Controls.Add(progid, nm)
                Else
                    Set c = Controls.Add(progid, nm, cont)
                End If
                Debug.Print label & " -> " & E() & " type " & TypeName(c)
            End Sub

            Private Sub R(ByVal label As String, what)
                On Error Resume Next
                Err.Clear
                Controls.Remove what
                Debug.Print label & " -> " & E()
            End Sub

            """).Should().Equal(
            "bang Controls!a2 a2 err 0 count 8",
            "item a2 is c True err 0 count 8",
            "remove a2 -> err 0 count 7",
            "held after remove: type Label Name=e735 Caption=e735 Visible=e735",
            "held set Visible -> err 735 [The control is no longer usable because it has been removed from the controls collection] count 7",
            "re-add a2 -> err 0 count 8 type Label",
            "remove A3 case -> err 0 count 7",
            "remove Command1 -> err 729 [Controls.Remove can only remove controls added with Controls.Add] count 7",
            "remove Option1 -> err -2147467262 [System Error &H80004002 (-2147467262).   No such interface supported ] count 7",
            "remove Option1(0) -> err 729 [Controls.Remove can only remove controls added with Controls.Add] count 7",
            "remove nope -> err 730 [Control 'nope' not found] count 7",
            "remove empty -> err 728 ['' is not a legal control name] count 7",
            "remove 0 -> err 729 [Controls.Remove can only remove controls added with Controls.Add] count 7",
            "remove object a4 -> err 0 count 6",
            "remove a2 again -> err 0 count 5",
            "remove a2 twice -> err 730 [Control 'a2' not found] count 5",
            "remove Null -> err -2147352571 [Type mismatch] count 5");
    }

    // The probe's own sequence, from the same five design-time controls. Its Remove from the control's own Click is left out: it clicks with a CommandButton's Value, which isn't implemented.
    [AvaloniaFact]
    public void Remove_takes_a_container_with_what_it_holds_and_a_For_Each_still_reaches_a_control_removed_ahead_of_it()
    {
        Printed("""
            On Error Resume Next
            Dim c As Object, v, n As Integer, vis As String
            Set c = Controls.Add("VB.CommandButton", "s0")
            Set c = Controls.Add("VB.CommandButton", "p0", Picture1)
            Set c = Controls.Add("VB.CommandButton", "s1")
            Set c = Controls.Add("VB.CommandButton", "s2")
            Set c = Controls.Add("VB.TextBox", "s3")
            R "remove s1", "s1"
            Debug.Print "last is " & Controls(Controls.Count - 1).Name
            R "remove position Count-1", Controls.Count - 1
            R "remove position 999", 999
            R "remove position -1", -1
            R "remove position 0.6", 0.6
            Set c = Controls.Add("VB.Frame", "f1")
            Controls.Add "VB.CommandButton", "f1c", c
            Controls.Add "VB.Label", "f1l", c
            Debug.Print "frame with two count " & Controls.Count
            R "remove frame f1", "f1"
            Err.Clear
            Set c = Nothing
            Set c = Controls("f1c")
            Debug.Print "f1c after its frame -> " & E() & " " & (c Is Nothing)
            For Each v In Controls
                n = n + 1
                If n = 1 Then Controls.Add "VB.Label", "fe1"
                If n = 2 Then Controls.Remove "s2"
                If Left$(v.Name, 1) = "s" Or Left$(v.Name, 2) = "fe" Then vis = vis & v.Name & ","
            Next
            Debug.Print "for each visited " & n & " [" & vis & "] " & E()
            """,
            """
            Private Function G(c As Object, ByVal prop As String) As String
                Dim v
                On Error Resume Next
                Err.Clear
                Select Case prop
                    Case "Name": v = c.Name
                    Case "Left": v = c.Left
                    Case "Top": v = c.Top
                    Case "Width": v = c.Width
                    Case "Height": v = c.Height
                    Case "Enabled": v = c.Enabled
                    Case "Index": v = c.Index
                    Case "Caption": v = c.Caption
                    Case "Text": v = c.Text
                    Case "BackColor": v = c.BackColor
                    Case "ForeColor": v = c.ForeColor
                    Case "X1": v = c.X1
                    Case "Y1": v = c.Y1
                    Case "X2": v = c.X2
                    Case "Y2": v = c.Y2
                    Case "Interval": v = c.Interval
                    Case "Value": v = c.Value
                    Case "Visible": v = c.Visible
                    Case "font": v = c.FontName & "/" & c.FontSize
                End Select
                If Err.Number <> 0 Then G = " " & prop & "=e" & Err.Number Else G = " " & prop & "=" & v
            End Function

            Private Function PosOf(c As Object) As String
                Dim i As Integer
                PosOf = " pos=none"
                For i = 0 To Controls.Count - 1
                    If Controls(i) Is c Then PosOf = " pos=" & i & "/" & Controls.Count
                Next
            End Function

            Private Sub Describe(ByVal label As String, c As Object, props)
                Dim s As String, p
                s = label & " -> " & TypeName(c)
                For Each p In props
                    s = s & G(c, p)
                Next
                Debug.Print s & PosOf(c)
            End Sub

            Private Function E() As String
                E = "err " & Err.Number & IIf(Err.Number <> 0, " [" & Err.Description & "]", "") & " count " & Controls.Count
            End Function

            Private Sub T(ByVal label As String, progid, nm, Optional cont)
                Dim c As Object
                On Error Resume Next
                Err.Clear
                If IsMissing(cont) Then
                    Set c = Controls.Add(progid, nm)
                Else
                    Set c = Controls.Add(progid, nm, cont)
                End If
                Debug.Print label & " -> " & E() & " type " & TypeName(c)
            End Sub

            Private Sub R(ByVal label As String, what)
                On Error Resume Next
                Err.Clear
                Controls.Remove what
                Debug.Print label & " -> " & E()
            End Sub

            """).Should().Equal(
            "remove s1 -> err 0 count 9",
            "last is s3",
            "remove position Count-1 -> err 0 count 8",
            "remove position 999 -> err 9 [Subscript out of range] count 8",
            "remove position -1 -> err 9 [Subscript out of range] count 8",
            "remove position 0.6 -> err 729 [Controls.Remove can only remove controls added with Controls.Add] count 8",
            "frame with two count 11",
            "remove frame f1 -> err 0 count 8",
            "f1c after its frame -> err 730 [Control 'f1c' not found] count 8 True",
            "for each visited 8 [s0,] err 735 [The control is no longer usable because it has been removed from the controls collection] count 8");
    }

    // Through a TextBox's Change: a design-time control's own handler runs, then the variable's, and an array element can't be held (459).
    [AvaloniaFact]
    public void An_added_control_is_heard_only_by_a_WithEvents_variable_holding_it_the_one_set_last_first()
    {
        Printed("""
            On Error Resume Next
            Dim c As Object
            Set c = Controls.Add("VB.TextBox", "a0")
            Debug.Print "a0 added, held As Object only"
            c.Text = "x"
            Debug.Print "a0 changed " & Err.Number
            Set t = Controls.Add("VB.TextBox", "a1")
            Debug.Print "a1 added into t"
            t.Text = "x"
            Debug.Print "a1 changed " & Err.Number
            Set t2 = t
            Debug.Print "t2 = t, both hold a1"
            t.Text = "y"
            Debug.Print "a1 changed again " & Err.Number
            Set t = Nothing
            Debug.Print "t = Nothing"
            t2.Text = "z"
            Debug.Print "a1 changed through t2 " & Err.Number
            Set t = Text1
            Debug.Print "t holds design-time Text1"
            Text1.Text = "changed"
            Debug.Print "Text1 changed " & Err.Number
            Err.Clear
            Set o = Option1(0)
            Debug.Print "o = Option1(0) -> " & Err.Number & " [" & Err.Description & "] " & (o Is Nothing)
            Err.Clear
            Set t = Nothing: Set t2 = Nothing
            Set c = Controls("a1")
            c.Text = "w"
            Debug.Print "a1 changed with no WithEvents holding it " & Err.Number
            Err.Clear
            Set t = Controls("a0")
            Controls.Remove "a0"
            Debug.Print "t after its control is removed: Is Nothing " & (t Is Nothing) & " err " & Err.Number
            Err.Clear
            Set t = Nothing
            Debug.Print "t = Nothing after the remove -> " & Err.Number
            """,
            """
            Private WithEvents t As VB.TextBox
            Private WithEvents t2 As VB.TextBox
            Private WithEvents o As VB.OptionButton

            Private Sub a0_Change()
                Debug.Print "  a0_Change (by name)"
            End Sub

            Private Sub t_Change()
                Debug.Print "  t_Change " & t.Name
            End Sub

            Private Sub t2_Change()
                Debug.Print "  t2_Change " & t2.Name
            End Sub

            Private Sub Text1_Change()
                Debug.Print "  Text1_Change"
            End Sub

            Private Sub o_Click()
                Debug.Print "  o_Click"
            End Sub


            """).Should().Equal(
            "a0 added, held As Object only",
            "a0 changed 0",
            "a1 added into t",
            "  t_Change a1",
            "a1 changed 0",
            "t2 = t, both hold a1",
            "  t2_Change a1",
            "  t_Change a1",
            "a1 changed again 0",
            "t = Nothing",
            "  t2_Change a1",
            "a1 changed through t2 0",
            "t holds design-time Text1",
            "  Text1_Change",
            "  t_Change Text1",
            "Text1 changed 0",
            "o = Option1(0) -> 459 [Object or class does not support the set of events] True",
            "a1 changed with no WithEvents holding it 0",
            "t after its control is removed: Is Nothing False err 0",
            "t = Nothing after the remove -> 0");
    }

    // Each measured as its own program.
    [AvaloniaFact]
    public void Too_few_or_too_many_arguments_are_the_late_bound_calls_raw_error_and_the_statement_form_adds()
    {
        Printed("""
            On Error Resume Next
            Dim c As Object
            Set c = Controls.Add("VB.CommandButton")
            Debug.Print "noname -> " & Err.Number & " " & Err.Description
            Err.Clear
            Set c = Controls.Add("VB.CommandButton", "x4", Me, 1)
            Debug.Print "fourargs -> " & Err.Number & " " & Err.Description
            Err.Clear
            Controls.Remove
            Debug.Print "removenone -> " & Err.Number & " " & Err.Description
            Err.Clear
            Controls.Remove "a", "b"
            Debug.Print "removetwo -> " & Err.Number & " " & Err.Description
            Err.Clear
            Controls.Add "VB.Label", "lblStmt"
            Debug.Print "stmt -> " & Err.Number & " count " & Controls.Count
            """,
            "").Should().Equal(
            "noname -> -2147352562 Wrong number of arguments or invalid property assignment",
            "fourargs -> -2147352562 Wrong number of arguments or invalid property assignment",
            "removenone -> -2147352562 Wrong number of arguments or invalid property assignment",
            "removetwo -> -2147352562 Wrong number of arguments or invalid property assignment",
            "stmt -> 0 count 6");
    }

    // vb6.exe answers an ActiveX ProgID from the registry, and makes the five VB controls HexIDE doesn't have.
    [AvaloniaTheory]
    [InlineData("MSComctlLib.ProgressBar")]
    [InlineData("Scripting.Dictionary")]
    [InlineData("CommandButton")]
    [InlineData("VB.DriveListBox")]
    [InlineData("VB.Data")]
    public void A_class_HexIDE_does_not_make_is_refused_as_not_implemented(string progId)
    {
        var (_, errors) = Run($"Dim c As Object\r\nSet c = Controls.Add(\"{progId}\", \"x\")", "");

        errors.Should().ContainSingle().Which.Should().BeOfType<NotImplementedException>();
    }
}
