using Avalonia;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Integration.Tests.Views;

// #988: SP6 native/p-code measurements in docs/vb6-fidelity-oracle/
// the-drawing-methods-line-pset-circle-cls-and-point.md and
// the-coordinate-system-scalemode-scale-scalex-and-scaley.md. Point is the oracle's readback path.
public partial class DrawingMethodsTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption = "Form1"
           ClientHeight = 3195
           ClientWidth = 4680
           Begin VB.PictureBox Picture1
              Left = 2160
              Top = 1200
              Width = 2415
              Height = 1815
           End
        End
        Attribute VB_Name = "Form1"

        """;

    private static void Check(string code, params string[] expected)
    {
        var run = RunLoad(Form, code);
        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal(expected);
    }

    private sealed record Run(List<string> Printed, List<Exception> Errors);

    /// <summary>Run Form1 as F5 does, with <paramref name="load"/> as its Load, then close it.</summary>
    private static Run RunLoad(string frm, string load, Action<Window>? inspect = null, string procedures = "")
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var code = "Private Sub Form_Load()\r\n" + load.ReplaceLineEndings("\r\n") + "\r\nEnd Sub\r\n"
            + "Sub S(ByVal what As String, ByVal v As Variant)\r\n    Debug.Print what & \" = \" & v & \" \" & TypeName(v)\r\nEnd Sub\r\n"
            + procedures.ReplaceLineEndings("\r\n");
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
            try
            {
                Dispatcher.UIThread.RunJobs();
                inspect?.Invoke(window);
            }
            finally
            {
                window.Close();
            }
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


    private static string Position(string target = "") =>
        $"CStr({target}CurrentX) & \"|\" & CStr({target}CurrentY) & \"|\" & TypeName({target}CurrentX) & \"|\" & TypeName({target}CurrentY)";

    public static IEnumerable<object[]> Defaults()
    {
        foreach (var target in new[] { "Me.", "Picture1." })
        foreach (var (member, value) in new[] {
            ("CurrentX", "0 Single"), ("CurrentY", "0 Single"), ("DrawWidth", "1 Integer"),
            ("DrawStyle", "0 Integer"), ("DrawMode", "13 Integer"), ("FillStyle", "1 Integer"),
            ("FillColor", "0 Long"), ("ForeColor", "-2147483630 Long"), ("BackColor", "-2147483633 Long"),
            ("AutoRedraw", "False Boolean"), ("FontTransparent", "True Boolean") })
            yield return [target, member, value];
    }

    [AvaloniaTheory]
    [MemberData(nameof(Defaults))]
    public void New_form_and_PictureBox_read_measured_defaults(string target, string member, string expected) =>
        Check($"Debug.Print CStr({target}{member}) & \" \" & TypeName({target}{member})", expected);

    [AvaloniaFact]
    public void Saved_state_is_read_without_adding_designer_properties()
    {
        const string state = "DrawWidth = 3\nDrawStyle = 2\nDrawMode = 7\nFillStyle = 0\nFillColor = &H000000FF&\nAutoRedraw = -1\nFontTransparent = 0\n";
        var frm = Form.ReplaceLineEndings("\n").Replace("   Caption", state + "   Caption")
            .Replace("      Left", state + "      Left");
        var run = RunLoad(frm, "Debug.Print CStr(DrawWidth) & CStr(DrawStyle) & CStr(DrawMode) & CStr(FillStyle) & CStr(FillColor) & CStr(AutoRedraw) & CStr(FontTransparent)\n"
            + "Debug.Print CStr(Picture1.DrawWidth) & CStr(Picture1.DrawStyle) & CStr(Picture1.DrawMode) & CStr(Picture1.FillStyle) & CStr(Picture1.FillColor) & CStr(Picture1.AutoRedraw) & CStr(Picture1.FontTransparent)");
        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("3270255TrueFalse", "3270255TrueFalse");
    }

    [AvaloniaTheory]
    [InlineData("   Caption")]
    [InlineData("      Left")]
    public void An_unrepresentable_saved_FillColor_is_refused_instead_of_replaced_by_black(string before)
    {
        var frm = Form.Replace(before, "FillColor = &H01000000&\n" + before);
        Run? run = null;
        var attempt = () => run = RunLoad(frm, "");
        var exception = Record.Exception(attempt);
        var errors = exception is null ? run!.Errors : new List<Exception> { exception };
        errors.Should().ContainSingle().Which.Should().BeOfType<NotImplementedException>()
            .Which.Message.Should().Contain("Saved FillColor").And.Contain("#988");
    }

    [AvaloniaTheory]
    [InlineData("CurrentX = 100\nCurrentY = 200\nCls", "0|0")]
    [InlineData("PSet (300, 400)", "300|400")]
    [InlineData("PSet (300, 400)\nPSet Step(10, 20)", "310|420")]
    [InlineData("Line (100, 100)-(500, 700)", "500|700")]
    [InlineData("Line (100, 100)-(500, 700)\nLine -(900, 300)", "900|300")]
    [InlineData("CurrentX = 1000\nCurrentY = 1000\nLine Step(10, 10)-Step(100, 50)", "1110|1060")]
    [InlineData("CurrentX = 1000\nCurrentY = 1000\nLine Step(10, 10)-Step(100, 50)\nLine -Step(5, 5)", "1115|1065")]
    [InlineData("Line (100, 100)-(500, 700), , B", "500|700")]
    [InlineData("Line (100, 100)-(500, 700), vbRed, BF", "500|700")]
    [InlineData("Picture1.CurrentX = 50\nPicture1.CurrentY = 60\nPicture1.Cls", "0|0", "Picture1.")]
    [InlineData("Picture1.PSet (300, 400)", "300|400", "Picture1.")]
    [InlineData("Picture1.Line (10, 20)-(30, 40)", "30|40", "Picture1.")]
    public void Drawing_moves_the_current_position(string statement, string expected, string target = "") =>
        Check("AutoRedraw = True\n" + statement + "\nDebug.Print " + Position(target), expected + "|Single|Single");

    [AvaloniaTheory]
    [InlineData("7.4", "7")]
    [InlineData("7.5", "8")]
    [InlineData("7.6", "8")]
    [InlineData("0.49", "0")]
    [InlineData("0.5", "0")]
    [InlineData("1.25", "1")]
    [InlineData("1.5", "2")]
    [InlineData("2.5", "2")]
    [InlineData("-0.5", "0")]
    [InlineData("-1.5", "-2")]
    public void Current_coordinates_keep_whole_twips(string assigned, string expected) =>
        Check($"CurrentX = {assigned}\nCurrentY = {assigned}\nDebug.Print " + Position(), expected + "|" + expected + "|Single|Single");

    [AvaloniaTheory]
    [InlineData("0.37", "0.4")]
    [InlineData("0.5", "0.5333334")]
    [InlineData("1.5", "1.466667")]
    [InlineData("2.5", "2.533333")]
    [InlineData("0.0333", "0")]
    public void Pixel_coordinates_still_keep_whole_twips(string assigned, string expected) =>
        Check($"ScaleMode = vbPixels\nCurrentX = {assigned}\nCurrentY = {assigned}\nDebug.Print " + Position(),
            expected + "|" + expected + "|Single|Single");

    [AvaloniaFact]
    public void A_scale_change_keeps_the_current_physical_position() =>
        Check("CurrentX = 7\nCurrentY = 7\nScaleMode = vbPixels\nDebug.Print " + Position()
            + "\nScale (0, 0)-(100, 100)\nDebug.Print " + Position()
            + "\nScaleMode = vbTwips\nDebug.Print " + Position(),
            "0.4666667|0.4666667|Single|Single", "0.1495727|0.2190923|Single|Single", "7|7|Single|Single");

    [AvaloniaTheory]
    [InlineData("0", 0)]
    [InlineData("7", 0)]
    [InlineData("7.4", 0)]
    [InlineData("7.5", 1)]
    [InlineData("7.6", 1)]
    [InlineData("8", 1)]
    [InlineData("14", 1)]
    [InlineData("14.9", 1)]
    [InlineData("15", 1)]
    [InlineData("22", 1)]
    [InlineData("22.5", 1)]
    [InlineData("23", 2)]
    [InlineData("29.9", 2)]
    [InlineData("30", 2)]
    [InlineData("37.5", 3)]
    [InlineData("52.5", 3)]
    public void PSet_lights_the_pixel_nearest_the_kept_twips(string twips, int pixel) =>
        Check($"PSet ({twips}, {twips}), vbRed\nScaleMode = vbPixels\nDebug.Print CStr(Point({pixel}, {pixel}))", "255");

    [AvaloniaTheory]
    [InlineData("7.5", "")]
    [InlineData("22.5", "")]
    [InlineData("37.5", "")]
    [InlineData("52.5", "")]
    [InlineData("0.5", "Scale (0, 0)-(312, 213)")]
    [InlineData("1.5", "Scale (0, 0)-(312, 213)")]
    [InlineData("2.5", "Scale (0, 0)-(312, 213)")]
    public void Point_rounds_straight_to_pixels_at_halves(string coordinate, string scale) =>
        Check(scale + $"\nPSet ({coordinate}, {coordinate}), vbRed\nDebug.Print CStr(Point({coordinate}, {coordinate}))", "15790320");

    [AvaloniaTheory]
    [InlineData("0.4")]
    [InlineData("0.6")]
    [InlineData("1.4")]
    [InlineData("1.6")]
    public void Point_and_PSet_agree_away_from_halves(string coordinate) =>
        Check($"Scale (0, 0)-(312, 213)\nPSet ({coordinate}, {coordinate}), vbRed\nDebug.Print CStr(Point({coordinate}, {coordinate}))", "255");

    [AvaloniaTheory]
    [InlineData("0.5", 213)]
    [InlineData("0.6", 212)]
    [InlineData("1.5", 211)]
    public void Flipped_scale_lights_the_measured_row(string y, int row) =>
        Check($"Scale (0, 213)-(312, 0)\nPSet (0, {y}), vbRed\nScaleMode = vbPixels\nDebug.Print CStr(Point(0, {row}))", "255");

    [AvaloniaFact]
    public void Flipped_user_scale_rounding_and_Point_take_different_rows() =>
        Check("Scale (0, 100)-(10, 0)\nLine (0, 0)-(10, 100), vbRed\nPSet (5, 50), vbBlue\nDebug.Print CStr(CurrentY)\nDebug.Print CStr(Point(5, 50))",
            "49.98435", "255");

    [AvaloniaTheory]
    [InlineData(10, "..##...........")]
    [InlineData(11, "....##.........")]
    [InlineData(12, "......##.......")]
    [InlineData(13, "........##.....")]
    [InlineData(14, "..........##...")]
    [InlineData(15, "...............")]
    public void A_sloped_line_matches_the_measured_pixel_grid(int y, string expected) =>
        Check($"ScaleMode = vbPixels\nLine (10, 10)-(20, 15), vbRed\nDim x, row As String\nFor x = 8 To 22\nIf Point(x, {y}) = vbRed Then\nrow = row & \"#\"\nElse\nrow = row & \".\"\nEnd If\nNext\nDebug.Print row", expected);

    [AvaloniaTheory]
    [InlineData(9, "15790320")]
    [InlineData(10, "255")]
    [InlineData(19, "255")]
    [InlineData(20, "15790320")]
    public void A_line_leaves_out_its_last_pixel(int x, string expected) =>
        Check($"ScaleMode = vbPixels\nLine (10, 10)-(20, 10), vbRed\nDebug.Print CStr(Point({x}, 10))", expected);

    [AvaloniaTheory]
    [InlineData("Line (30, 30)-(40, 35), vbRed, B", 30, 30, "255")]
    [InlineData("Line (30, 30)-(40, 35), vbRed, B", 40, 30, "255")]
    [InlineData("Line (30, 30)-(40, 35), vbRed, B", 30, 35, "255")]
    [InlineData("Line (30, 30)-(40, 35), vbRed, B", 40, 35, "255")]
    [InlineData("Line (30, 30)-(40, 35), vbRed, B", 41, 35, "15790320")]
    [InlineData("Line (30, 30)-(40, 35), vbRed, B", 40, 36, "15790320")]
    [InlineData("Line (30, 30)-(40, 35), vbRed, B", 35, 32, "15790320")]
    [InlineData("Line (50, 50)-(60, 55), vbBlue, BF", 60, 55, "16711680")]
    [InlineData("Line (50, 50)-(60, 55), vbBlue, BF", 61, 55, "15790320")]
    [InlineData("Line (50, 50)-(60, 55), vbBlue, BF", 60, 56, "15790320")]
    [InlineData("FillStyle = 0\nFillColor = vbGreen\nLine (70, 50)-(80, 55), vbRed, B", 70, 50, "255")]
    [InlineData("FillStyle = 0\nFillColor = vbGreen\nLine (70, 50)-(80, 55), vbRed, B", 75, 52, "65280")]
    public void Boxes_include_the_far_corner_and_use_the_measured_fill(string drawing, int x, int y, string expected) =>
        Check($"ScaleMode = vbPixels\n{drawing}\nDebug.Print CStr(Point({x}, {y}))", expected);

    [AvaloniaTheory]
    [InlineData("PSet (5, 5)", "Point(5, 5)", "0")]
    [InlineData("Line (10, 10)-(20, 10)", "Point(10, 10)", "0")]
    [InlineData("ForeColor = vbBlue\nPSet (5, 5)", "Point(5, 5)", "16711680")]
    [InlineData("ForeColor = vbBlue\nLine (10, 10)-(20, 20), , B", "Point(10, 10)", "16711680")]
    [InlineData("PSet (7, 7), -1", "Point(7, 7)", "16777215")]
    [InlineData("PSet (8, 8), &H1000000", "Point(8, 8)", "0")]
    [InlineData("PSet (9, 9), &H80000005", "Point(9, 9)", "16777215")]
    public void Method_colours_use_ForeColor_or_the_low_RGB_bytes(string drawing, string point, string expected) =>
        Check($"ScaleMode = vbPixels\n{drawing}\nDebug.Print CStr({point}) & \" \" & TypeName({point})", expected + " Long");

    [AvaloniaFact]
    public void Cls_clears_to_BackColor_and_keeps_the_two_surfaces_independent() =>
        Check("ScaleMode = vbPixels\nPicture1.ScaleMode = vbPixels\nPSet (10, 10), vbRed\nPicture1.PSet (10, 10), vbBlue\n"
            + "Debug.Print CStr(Point(10, 10))\nDebug.Print CStr(Picture1.Point(10, 10))\nBackColor = vbGreen\nMe.Cls\n"
            + "Debug.Print CStr(Point(10, 10))\nDebug.Print CStr(Picture1.Point(10, 10))\nPicture1.Cls\nDebug.Print CStr(Picture1.Point(10, 10))",
            "255", "16711680", "65280", "16711680", "15790320");

    [AvaloniaTheory]
    [InlineData("DrawWidth = 0", "DrawWidth", "380", "1")]
    [InlineData("DrawWidth = -1", "DrawWidth", "380", "1")]
    [InlineData("DrawWidth = 32767", "DrawWidth", "0", "32767")]
    [InlineData("DrawWidth = 32768", "DrawWidth", "6", "1")]
    [InlineData("DrawStyle = 7", "DrawStyle", "380", "0")]
    [InlineData("DrawStyle = -1", "DrawStyle", "380", "0")]
    [InlineData("DrawMode = 0", "DrawMode", "380", "13")]
    [InlineData("DrawMode = 17", "DrawMode", "380", "13")]
    [InlineData("FillStyle = 8", "FillStyle", "380", "1")]
    [InlineData("ForeColor = -1", "ForeColor", "380", "-2147483630")]
    [InlineData("FillColor = -1", "FillColor", "380", "0")]
    public void Property_errors_keep_the_old_value(string assignment, string member, string error, string value) =>
        Check($"On Error Resume Next\n{assignment}\nDebug.Print CStr(Err.Number)\nDebug.Print CStr({member})", error, value);

    [AvaloniaTheory]
    [InlineData("CurrentX = 1E+30")]
    [InlineData("PSet (1E+30, 1E+30)")]
    [InlineData("Line (0, 0)-(1E+10, 1E+10)")]
    public void Overflow_leaves_both_current_coordinates_unchanged(string statement) =>
        Check("CurrentX = 100\nCurrentY = 200\nOn Error Resume Next\n" + statement + "\nDebug.Print CStr(Err.Number)\nDebug.Print " + Position(),
            "6", "100|200|Single|Single");

    [AvaloniaTheory]
    [InlineData("Point(-100, -100)", "-1")]
    [InlineData("Point(1E+7, 0)", "-1")]
    public void Point_returns_Long_minus_one_at_the_two_measured_outside_points(string point, string expected) =>
        Check($"Debug.Print CStr({point}) & \" \" & TypeName({point})", expected + " Long");

    [AvaloniaFact]
    public void Point_overflow_is_error_6() =>
        Check("On Error Resume Next\nDim p\np = Point(1E+30, 0)\nDebug.Print CStr(Err.Number)", "6");

    [AvaloniaFact]
    public void Point_can_read_the_measured_row_just_past_the_client_area() =>
        Check("AutoRedraw = True\nScale (0, 100)-(10, 0)\nLine (0, 0)-(10, 100), vbRed\nScaleMode = vbPixels\n"
            + "Debug.Print CStr(Point(0, 213))\nDebug.Print CStr(Point(312, 0))", "255", "15790320");

    [AvaloniaTheory]
    [InlineData("Debug.Print Cls")]
    [InlineData("Debug.Print Me.Cls")]
    [InlineData("Debug.Print Picture1.Cls")]
    [InlineData("Me.Cls 1")]
    [InlineData("Debug.Print Point(1)")]
    [InlineData("Debug.Print Picture1.Point(1, 2, 3)")]
    [InlineData("FontTransparent = False")]
    [InlineData("DrawWidth = 13\nPSet (10, 10)")]
    [InlineData("DrawWidth = 32767\nLine (10, 10)-(20, 20)")]
    [InlineData("DrawStyle = 1\nLine (10, 10)-(20, 20)")]
    [InlineData("DrawMode = 7\nPSet (10, 10)")]
    [InlineData("FillStyle = 2\nLine (10, 10)-(20, 20), , B")]
    [InlineData("Dim o As Object\nSet o = Me\no.Line (10, 10)-(20, 20)")]
    [InlineData("Dim o As Object\nSet o = Picture1\no.PSet (10, 10)")]
    [InlineData("Dim o\nSet o = Me\no.Circle (10, 10), 5")]
    public void Later_slices_and_unmeasured_late_binding_refuse_even_with_Resume_Next(string statement)
    {
        var run = RunLoad(Form, "On Error Resume Next\n" + statement + "\nDebug.Print \"continued\"");
        run.Errors.Should().ContainSingle().Which.Should().BeOfType<NotImplementedException>()
            .Which.Message.Should().Contain("#988");
        run.Printed.Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData(1, false)]
    [InlineData(1.5, false)]
    [InlineData(1, true)]
    [InlineData(1.5, true)]
    public void Surface_pixels_align_with_controls_and_client_edges_before_and_after_resize(double scaling, bool inPicture)
    {
        // #988 round 2: compare the actual Skia frame with control layout, rather than Point's backing store.
        const string text = """
            Begin VB.TextBox Text1
                Text = ""
                Left = 600
                Top = 900
                Width = 900
                Height = 360
            End
            """;
        var frm = Form.ReplaceLineEndings("\n");
        frm = inPicture
            ? frm.Replace("      Left = 2160", text + "\n      Left = 450").Replace("      Top = 1200", "      Top = 600")
            : frm.Replace("   Begin VB.PictureBox Picture1", text + "\n   Begin VB.PictureBox Picture1");
        var target = inPicture ? "Picture1." : "";
        var procedures = $"""
            Public Sub Draw()
                {target}AutoRedraw = True
                {target}ScaleMode = vbPixels
                {target}BackColor = vbWhite
                {target}Cls
                {target}Line (Text1.Left, Text1.Top)-(Text1.Left + Text1.Width - 1, Text1.Top + Text1.Height - 1), vbRed, B
                {target}Line (0, 0)-({target}ScaleWidth - 1, {target}ScaleHeight - 1), vbGreen, B
                Dim x As Integer
                For x = 10 To 29
                    If x Mod 2 = 0 Then
                        {target}Line (x, 30)-(x, 39), vbRed, BF
                    Else
                        {target}Line (x, 30)-(x, 39), vbBlue, BF
                    End If
                Next
            End Sub
            """;
        var run = RunLoad(frm, "", window =>
        {
            window.SetRenderScaling(scaling);
            window.UpdateLayout();
            var canvas = window.GetLogicalDescendants().OfType<Canvas>().First(c => c.Children.OfType<VBPictureBox>().Any());
            var picture = canvas.Children.OfType<VBPictureBox>().Single();
            var host = inPicture ? picture.ChildHost! : canvas;
            var box = host.Children.OfType<VBTextBox>().Single();
            if (!inPicture) picture.IsVisible = false;
            ((VBFormRuntime)window).ExecuteSub("Draw");
            var textOrigin = box.TranslatePoint(default, window)!.Value;
            var textBounds = new Rect(textOrigin, box.Bounds.Size);
            // Reveal the drawing underneath the opaque TextBox, keeping its laid-out frame as the reference.
            box.IsVisible = false;

            void CheckFrame(bool edges)
            {
                window.UpdateLayout();
                var context = ExecutionContext.Capture();
                Dispatcher.UIThread.RunJobs();
                ExecutionContext.Restore(context!);
                using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered frame");
                using var buffer = frame.Lock();
                Color At(int x, int y)
                {
                    var offset = y * buffer.RowBytes + x * 4;
                    byte Byte(int i) => System.Runtime.InteropServices.Marshal.ReadByte(buffer.Address, offset + i);
                    return buffer.Format == Avalonia.Platform.PixelFormat.Bgra8888
                        ? Color.FromRgb(Byte(2), Byte(1), Byte(0))
                        : Color.FromRgb(Byte(0), Byte(1), Byte(2));
                }
                var origin = host.TranslatePoint(default, window)!.Value;
                var client = new Rect(origin, host.Bounds.Size);
                void Outline(Color colour, Rect expected)
                {
                    var points = new List<Point>();
                    for (var y = (int)Math.Ceiling(client.Top * scaling); y < (int)(client.Bottom * scaling); y++)
                    for (var x = (int)Math.Ceiling(client.Left * scaling); x < (int)(client.Right * scaling); x++)
                        if (At(x, y) == colour && (colour != Colors.Red || y > (origin.Y + 45) * scaling))
                            points.Add(new Point(x, y));
                    points.Should().NotBeEmpty($"the {colour} outline must be visible");
                    points.Min(p => p.X).Should().BeApproximately(expected.Left * scaling, 1);
                    points.Min(p => p.Y).Should().BeApproximately(expected.Top * scaling, 1);
                    (points.Max(p => p.X) + 1).Should().BeApproximately(expected.Right * scaling, 1);
                    (points.Max(p => p.Y) + 1).Should().BeApproximately(expected.Bottom * scaling, 1);
                }
                Outline(Colors.Red, textBounds);
                if (edges) Outline(Colors.Lime, client);
                // Adjacent one-VB-pixel stripes must retain their colours without interpolated shades.
                for (var x = (int)((origin.X + 10) * scaling); x < (int)((origin.X + 30) * scaling); x++)
                    At(x, (int)((origin.Y + 35) * scaling)).Should().BeOneOf(Colors.Red, Colors.Blue);
            }

            CheckFrame(true);
            if (inPicture)
            {
                picture.Width += 20;
                picture.Height += 10;
            }
            else
            {
                window.Width += 20;
                window.Height += 10;
            }
            CheckFrame(false); // Retained pixels stay in place when the client grows.
            ((VBFormRuntime)window).ExecuteSub("Draw");
            CheckFrame(true); // A new outline reaches the resized client's edges.
        }, procedures);
        run.Errors.Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(1.5)]
    public void The_surface_renders_on_both_owners_and_under_windowless_and_windowed_controls(double scaling)
    {
        const string controls = """
            Begin VB.Label Label1
                Caption = ""
                BackColor = &H0000FFFF&
                BackStyle = 1
                Left = 450
                Top = 150
                Width = 300
                Height = 300
            End
            Begin VB.Shape Shape1
                FillColor = &H0000FF00&
                FillStyle = 0
                Left = 900
                Top = 150
                Width = 300
                Height = 300
            End
            Begin VB.TextBox Text1
                Text = ""
                BackColor = &H00FFFFFF&
                Left = 1350
                Top = 150
                Width = 300
                Height = 300
            End
            """;
        var frm = Form.ReplaceLineEndings("\n").Replace("   Begin VB.PictureBox Picture1", controls + "\n   Begin VB.PictureBox Picture1");
        var run = RunLoad(frm, "AutoRedraw = True\nScaleMode = vbPixels\nLine (0, 0)-(120, 40), vbRed, BF\n"
            + "Picture1.AutoRedraw = True\nPicture1.ScaleMode = vbPixels\nPicture1.Line (10, 10)-(30, 30), vbBlue, BF", window =>
        {
            window.SetRenderScaling(scaling);
            window.UpdateLayout();
            var canvas = window.GetLogicalDescendants().OfType<Canvas>().First(c => c.Children.OfType<VBPictureBox>().Any());
            var picture = canvas.Children.OfType<VBPictureBox>().Single();
            var context = System.Threading.ExecutionContext.Capture();
            Dispatcher.UIThread.RunJobs();
            System.Threading.ExecutionContext.Restore(context!);
            using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered frame");
            using var buffer = frame.Lock();
            Avalonia.Media.Color At(Control origin, int x, int y)
            {
                var point = origin.TranslatePoint(new Avalonia.Point(x, y), window)!.Value;
                var offset = (int)(point.Y * scaling) * buffer.RowBytes + (int)(point.X * scaling) * 4;
                byte Byte(int i) => System.Runtime.InteropServices.Marshal.ReadByte(buffer.Address, offset + i);
                return buffer.Format == Avalonia.Platform.PixelFormat.Bgra8888
                    ? Avalonia.Media.Color.FromRgb(Byte(2), Byte(1), Byte(0))
                    : Avalonia.Media.Color.FromRgb(Byte(0), Byte(1), Byte(2));
            }
            At(canvas, 10, 20).Should().Be(Avalonia.Media.Colors.Red);
            At(picture.ChildHost!, 20, 20).Should().Be(Avalonia.Media.Colors.Blue);
            At(canvas, 40, 20).Should().Be(Avalonia.Media.Colors.Yellow);
            At(canvas, 70, 20).Should().Be(Avalonia.Media.Colors.Lime);
            At(canvas, 100, 20).Should().Be(Avalonia.Media.Colors.White);
        });
        run.Errors.Should().BeEmpty();
    }
}
