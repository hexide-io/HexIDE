using System.Collections.Generic;
using System.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A control's Left, Top, Width and Height as a running program reads and writes them (#933, #791): in its container's
/// ScaleMode, as a Single, held as whole twips. Every expectation was measured on vb6.exe, native and p-code agreeing
/// (the oracle's "A control's position at run time"), and each reads a value that came from a .frm through a program,
/// because an assignment read straight back passes in any unit.
/// </summary>
public class ControlPositionTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string TwipsForm = "";
    private const string PixelForm = "   ScaleHeight     =   206\r\n   ScaleMode       =   3  'Pixel\r\n   ScaleWidth      =   312\r\n";

    private const string TextBox =
        "   Begin VB.TextBox Text1 \r\n      Height          =   495\r\n      Left            =   120\r\n"
      + "      Text            =   \"t1\"\r\n      Top             =   120\r\n      Width           =   1215\r\n   End\r\n";

    private const string LabelAndButton =
        "   Begin VB.Label Label1 \r\n      Caption         =   \"l1\"\r\n      Height          =   255\r\n"
      + "      Left            =   240\r\n      Top             =   720\r\n      Width           =   1200\r\n   End\r\n"
      + "   Begin VB.CommandButton Command1 \r\n      Caption         =   \"c1\"\r\n      Height          =   495\r\n"
      + "      Left            =   240\r\n      Top             =   1200\r\n      Width           =   1215\r\n   End\r\n";

    private static string Form(string scale, string controls) =>
        "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   Caption         =   \"Form1\"\r\n   ClientHeight    =   3090\r\n"
      + "   ClientWidth     =   4680\r\n" + scale + controls + "End\r\nAttribute VB_Name = \"Form1\"\r\n";

    private sealed record Run(List<string> Printed, List<Exception> Errors);

    /// <summary>Run Form1 as F5 does, with <paramref name="load"/> as its Load, then close it.</summary>
    private static Run RunLoad(string frm, string load)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var code = "Private Sub Form_Load()\r\n" + load.ReplaceLineEndings("\r\n") + "\r\nEnd Sub\r\n"
            + "Sub S(ByVal what As String, ByVal v As Variant)\r\n    Debug.Print what & \" = \" & v & \" \" & TypeName(v)\r\nEnd Sub\r\n";
        var form = new FormDeserializer().Deserialize(project, frm + code, new NullSink())!;
        project.AddForm(form);

        var run = new Run([], []);
        void OnFaulted(VBWindowContext _, Exception e) => run.Errors.Add(e);
        void OnRunTimeError(VBWindowContext _, VBRunTimeException e) => run.Errors.Add(e);
        void OnCompileError(VBWindowContext _, VBCompileErrorException e) => run.Errors.Add(e);
        VBDebugConsole.Output += run.Printed.Add;
        VBWindowContext.Faulted += OnFaulted;
        VBWindowContext.RunTimeError += OnRunTimeError;
        VBWindowContext.CompileError += OnCompileError;
        try
        {
            VBLoader.RunForm(form, CancellationToken.None, out var window);
            Dispatcher.UIThread.RunJobs();
            window.Close();
        }
        finally
        {
            VBDebugConsole.Output -= run.Printed.Add;
            VBWindowContext.Faulted -= OnFaulted;
            VBWindowContext.RunTimeError -= OnRunTimeError;
            VBWindowContext.CompileError -= OnCompileError;
        }
        return run;
    }

    [AvaloniaTheory]
    [InlineData("0  'Manual")]
    [InlineData("2  'CenterScreen")]
    public void A_forms_Load_reads_the_outer_position_as_Singles(string startup)
    {
        var run = RunLoad(Form("   ClientLeft = 2000\r\n   ClientTop = 1500\r\n   StartUpPosition = " + startup + "\r\n", ""),
            "S \"Left\", Me.Left\nS \"Top\", Me.Top");
        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Left = 1875 Single", "Top = 1035 Single");
    }

    [AvaloniaTheory]
    [InlineData("Left")]
    [InlineData("Top")]
    public void A_Timers_designer_position_cannot_be_read_at_run_time(string property)
    {
        var run = RunLoad(Form(TwipsForm,
            "   Begin VB.Timer Timer1\r\n      Left = 120\r\n      Top = 240\r\n   End\r\n"),
            $"Dim o As Object\nSet o = Timer1\nOn Error Resume Next\nDebug.Print o.{property}\nDebug.Print CStr(Err.Number)");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("393");
    }

    [AvaloniaFact]
    public void A_frm_position_reads_in_twips_as_a_Single()
    {
        // Measured. HexIDE read 8, 8, 81 and 33, each a Double: pixels.
        var run = RunLoad(Form(TwipsForm, TextBox + LabelAndButton),
            "S \"Left\", Text1.Left\nS \"Top\", Text1.Top\nS \"Width\", Text1.Width\nS \"Height\", Text1.Height\nS \"Label\", Label1.Left");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Left = 120 Single", "Top = 120 Single", "Width = 1215 Single", "Height = 495 Single",
            "Label = 240 Single");
    }

    [AvaloniaFact]
    public void An_assigned_position_is_twips_and_keeps_every_whole_twip()
    {
        // Measured: no snapping to whole pixels, for a windowed control or a Label. HexIDE took 1201 for pixels.
        var run = RunLoad(Form(TwipsForm, TextBox + LabelAndButton),
            "Text1.Left = 1201\nS \"Text1\", Text1.Left\nLabel1.Left = 1201\nS \"Label1\", Label1.Left\n"
            + "Text1.Width = 1001\nS \"Width\", Text1.Width\nCommand1.Left = 7\nS \"Command1\", Command1.Left\n"
            + "Text1.Top = -30\nS \"Top\", Text1.Top");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Text1 = 1201 Single", "Label1 = 1201 Single", "Width = 1001 Single",
            "Command1 = 7 Single", "Top = -30 Single");
    }

    [AvaloniaFact]
    public void An_assigned_position_is_rounded_to_whole_twips_half_to_even()
    {
        // Measured (#791): 1.5 and 2.5 both read 2. HexIDE kept the fraction.
        var run = RunLoad(Form(TwipsForm, TextBox),
            "Text1.Left = 1.5\nS \"a\", Text1.Left\nText1.Left = 2.5\nS \"b\", Text1.Left");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("a = 2 Single", "b = 2 Single");
    }

    [AvaloniaFact]
    public void A_Label_is_at_least_a_pixel_wide_and_high()
    {
        // Measured: 7 and 0 read 15, and 20 reads 20, so it is a floor, not a snap.
        var run = RunLoad(Form(TwipsForm, LabelAndButton),
            "Label1.Width = 7\nS \"a\", Label1.Width\nLabel1.Width = 0\nS \"b\", Label1.Width\n"
            + "Label1.Height = 7\nS \"c\", Label1.Height\nLabel1.Width = 20\nS \"d\", Label1.Width");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("a = 15 Single", "b = 15 Single", "c = 15 Single", "d = 20 Single");
    }

    [AvaloniaFact]
    public void A_position_past_a_Long_in_twips_is_Overflow()
    {
        // Measured: `Left = 1E+20`, which a Single holds, is 6.
        var run = RunLoad(Form(TwipsForm, TextBox), "On Error Resume Next\nText1.Left = 1E+20\nDebug.Print Err.Number & \" \" & Text1.Left");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("6 120");
    }

    [AvaloniaFact]
    public void A_pixel_forms_frm_still_holds_twips_and_its_program_reads_pixels()
    {
        // Measured: the .frm's Left = 8 is 8 twips, read as 0.5333334 pixels, and 10.5 pixels is stored as 158 twips.
        var frm = Form(PixelForm,
            "   Begin VB.TextBox Text1 \r\n      Height          =   33\r\n      Left            =   8\r\n"
          + "      Text            =   \"t1\"\r\n      Top             =   8\r\n      Width           =   81\r\n   End\r\n");
        var run = RunLoad(frm, "S \"Left\", Text1.Left\nS \"Width\", Text1.Width\nText1.Left = 10.5\nS \"after\", Text1.Left");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Left = 0.5333334 Single", "Width = 5.4 Single", "after = 10.53333 Single");
    }

    [AvaloniaFact]
    public void A_Frames_children_stay_in_twips_on_a_pixel_form()
    {
        // Measured: a Frame has no ScaleMode, so its children are twips; the Frame itself is the form's child, in pixels.
        var frm = Form(PixelForm,
            "   Begin VB.Frame Frame1 \r\n      Caption         =   \"f1\"\r\n      Height          =   120\r\n"
          + "      Left            =   16\r\n      Top             =   16\r\n      Width           =   200\r\n"
          + "      Begin VB.TextBox Text2 \r\n         Height          =   30\r\n         Left            =   240\r\n"
          + "         Text            =   \"t2\"\r\n         Top             =   24\r\n         Width           =   60\r\n      End\r\n"
          + "   End\r\n");
        var run = RunLoad(frm, "S \"Frame1\", Frame1.Left\nS \"Text2\", Text2.Left\nS \"Width\", Text2.Width");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Frame1 = 1.066667 Single", "Text2 = 240 Single", "Width = 60 Single");
    }

    [AvaloniaFact]
    public void A_PictureBoxs_children_are_in_its_ScaleMode()
    {
        // Measured: in a pixel PictureBox, a TextBox at 150 twips reads 10, and 20 pixels is 300 twips. The PictureBox is
        // the form's child, in twips.
        var frm = Form(TwipsForm,
            "   Begin VB.PictureBox Picture1 \r\n      Height          =   1500\r\n      Left            =   120\r\n"
          + "      ScaleHeight     =   96\r\n      ScaleMode       =   3  'Pixel\r\n      ScaleWidth      =   196\r\n"
          + "      Top             =   120\r\n      Width           =   3000\r\n"
          + "      Begin VB.TextBox Text3 \r\n         Height          =   495\r\n         Left            =   150\r\n"
          + "         Text            =   \"t3\"\r\n         Top             =   75\r\n         Width           =   1215\r\n      End\r\n"
          + "   End\r\n");
        var run = RunLoad(frm, "S \"Picture1\", Picture1.Left\nS \"Left\", Text3.Left\nS \"Top\", Text3.Top\nS \"Width\", Text3.Width");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Picture1 = 120 Single", "Left = 10 Single", "Top = 5 Single", "Width = 81 Single");
    }

    [AvaloniaTheory]
    [InlineData("2  'Point", "S \"Left\", Text1.Left|S \"Width\", Text1.Width", "Left = 6 Single|Width = 60.75 Single")]
    [InlineData("5  'Inch", "S \"Left\", Text1.Left", "Left = 8.333334E-02 Single")]
    [InlineData("4  'Character", "S \"Left\", Text1.Left|S \"Top\", Text1.Top", "Left = 1 Single|Top = 0.5 Single")]
    public void A_forms_other_ScaleModes_divide_whole_twips(string mode, string reads, string expected)
    {
        // Measured by setting the ScaleMode at run time: 120 twips is 6 points and 8.333334E-02 inches, and a character is
        // 120 twips across and 240 down. HexIDE has no run-time ScaleMode yet (#990), so the form declares it here.
        var run = RunLoad(Form($"   ScaleMode       =   {mode}\r\n", TextBox), reads.Replace('|', '\n'));

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal(expected.Split('|'));
    }

    [AvaloniaTheory]
    [InlineData("0  'User")]
    [InlineData("6  'Millimeter")]
    [InlineData("7  'Centimeter")]
    public void A_ScaleMode_HexIDE_cannot_convert_exactly_is_refused(string mode)
    {
        // User needs the form's ScaleLeft and ScaleTop; centimetres and millimetres read 0.2116669 and 2.116669 for 120
        // twips, which 1440 / 2.54 does not give (measured, unexplained). Each is refused rather than guessed.
        var run = RunLoad(Form($"   ScaleMode       =   {mode}\r\n", TextBox), "Debug.Print Text1.Left");

        run.Errors.Should().ContainSingle().Which.Should().BeOfType<NotImplementedException>()
            .Which.Message.Should().Contain("#990");
        run.Printed.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void A_forms_own_Width_is_twips_whatever_its_ScaleMode()
    {
        // Measured: a pixel form's Me.Width is 4800, twips, for ClientWidth 4680, the frame included. That was session 0's
        // unthemed frame; HexIDE adds the themed desktop's, 240 (#1072, FormOuterSizeTests), so only the unit and the type
        // are pinned here. It used to read 312, pixels, a Double.
        var run = RunLoad(Form(PixelForm, TextBox), "Debug.Print TypeName(Me.Width) & \" \" & (Me.Width >= 4680)");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Single True");
    }
}
