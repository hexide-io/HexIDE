using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// Which of two overlapping controls a running form draws on top (#1497), read from a real Skia frame.
/// </summary>
/// <remarks>
/// Measured in VB6 at run time (docs/vb6-fidelity-oracle/how-a-running-form-stacks-its-controls.md, "How a running form stacks its controls"):
/// <list type="bullet">
/// <item>the first block in a container is drawn on top;</item>
/// <item>a windowless control (Label, Shape, Line, Image) is drawn under every windowed one, whatever their order;</item>
/// <item>among the windowless ones the first block is on top, whatever the class;</item>
/// <item>a control-array element loaded at run time goes under the controls already there.</item>
/// </list>
/// HexIDE drew the last block on top, and every Line under every other control.
/// </remarks>
public class RuntimeStackingTests : WindowTestFixture
{
    private static readonly Color Red = Color.FromRgb(255, 0, 0);
    private static readonly Color Yellow = Color.FromRgb(255, 255, 0);
    private static readonly Color Green = Color.FromRgb(0, 255, 0);
    private static readonly Color Blue = Color.FromRgb(0, 0, 255);

    // Pixels throughout: a twip is a fifteenth of one.
    private static string Box(string vbType, string name, string colour, int left, int top, int width, int height,
        string extra = "") =>
        $"   Begin {vbType} {name} \r\n" +
        (vbType == "VB.Label" ? "      Caption         =   \"\"\r\n" : "") +
        (vbType == "VB.Shape"
            ? $"      FillColor       =   {colour}\r\n      FillStyle       =   0  'Solid\r\n"
            : vbType == "VB.CommandButton" ? "" : $"      BackColor       =   {colour}\r\n") +
        extra +
        $"      Height          =   {height * 15}\r\n      Left            =   {left * 15}\r\n" +
        $"      Top             =   {top * 15}\r\n      Width           =   {width * 15}\r\n   End\r\n";

    private static string Line(string name, int y) =>
        $"   Begin VB.Line {name} \r\n      BorderColor     =   &H00FF0000&\r\n      BorderWidth     =   8\r\n" +
        $"      X1              =   0\r\n      X2              =   {140 * 15}\r\n" +
        $"      Y1              =   {y * 15}\r\n      Y2              =   {y * 15}\r\n   End\r\n";

    private static string Form(string controls) =>
        "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   Caption         =   \"Form1\"\r\n" +
        "   ClientHeight    =   4500\r\n   ClientWidth     =   6000\r\n" + controls + "End\r\nAttribute VB_Name = \"Form1\"\r\n";

    private static Color At(Window window, Canvas canvas, int x, int y)
    {
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame was rendered");
        using var buffer = frame.Lock();
        var origin = canvas.TranslatePoint(new Point(0, 0), window) ?? throw new InvalidOperationException("not in the window");
        var offset = ((int)origin.Y + y) * buffer.RowBytes + ((int)origin.X + x) * 4;
        byte Byte(int i) => Marshal.ReadByte(buffer.Address, offset + i);
        return buffer.Format == Avalonia.Platform.PixelFormat.Bgra8888
            ? Color.FromRgb(Byte(2), Byte(1), Byte(0))
            : Color.FromRgb(Byte(0), Byte(1), Byte(2));
    }

    private static (Window Window, Canvas Canvas) Run(string controls)
    {
        var (canvas, window, _, _) = ContainerRuntimeHarness.Laid(Form(controls));
        Dispatcher.UIThread.RunJobs();
        return (window, canvas);
    }

    [AvaloniaFact]
    public void The_first_of_two_overlapping_controls_is_drawn_on_top()
    {
        var (window, canvas) = Run(
            Box("VB.PictureBox", "PicRed", "&H000000FF&", 10, 10, 60, 40) +
            Box("VB.PictureBox", "PicBlue", "&H00FF0000&", 40, 30, 60, 40));

        At(window, canvas, 55, 40).Should().Be(Red);
    }

    [AvaloniaFact]
    public void The_first_of_two_overlapping_controls_inside_a_container_is_drawn_on_top()
    {
        var (window, canvas) = Run(
            "   Begin VB.PictureBox Holder \r\n      BorderStyle     =   0  'None\r\n" +
            "      Height          =   1500\r\n      Left            =   1500\r\n      Top             =   150\r\n" +
            "      Width           =   1500\r\n" +
            Box("VB.PictureBox", "PicGreen", "&H0000FF00&", 5, 5, 50, 40).Replace("\r\n   ", "\r\n      ") +
            Box("VB.PictureBox", "PicYellow", "&H0000FFFF&", 30, 25, 50, 40).Replace("\r\n   ", "\r\n      ") +
            "   End\r\n");

        // The holder is at 100, 10 and has no border, so its contents' places are 100, 10 on.
        At(window, canvas, 100 + 42, 10 + 35).Should().Be(Green);
    }

    [AvaloniaFact]
    public void A_Label_written_before_a_CommandButton_is_drawn_under_it()
    {
        var (window, canvas) = Run(
            Box("VB.Label", "LabelA", "&H000000FF&", 10, 110, 100, 40) +
            Box("VB.CommandButton", "CmdA", "", 40, 125, 81, 33));

        var face = At(window, canvas, 115, 150);
        At(window, canvas, 60, 140).Should().Be(face, "the button covers the Label where they overlap")
            .And.NotBe(Red);
    }

    [AvaloniaFact]
    public void A_Label_and_a_Line_stack_in_file_order()
    {
        var (window, canvas) = Run(
            Box("VB.Label", "LabelB", "&H0000FFFF&", 10, 170, 100, 30) + Line("LineB", 185) +
            Line("LineC", 235) + Box("VB.Label", "LabelC", "&H0000FFFF&", 10, 220, 100, 30));

        At(window, canvas, 130, 185).Should().Be(Blue, "the Line is drawn where nothing covers it");
        At(window, canvas, 60, 185).Should().Be(Yellow, "LabelB comes first, so it covers LineB");
        At(window, canvas, 60, 235).Should().Be(Blue, "LineC comes first, so it covers LabelC");
    }

    [AvaloniaFact]
    public void A_Shape_and_a_Label_stack_in_file_order()
    {
        var (window, canvas) = Run(
            Box("VB.Shape", "ShapeD", "&H0000FF00&", 150, 110, 100, 40) +
            Box("VB.Label", "LabelD", "&H0000FFFF&", 170, 125, 70, 40) +
            Box("VB.Label", "LabelE", "&H0000FFFF&", 150, 180, 70, 40) +
            Box("VB.Shape", "ShapeE", "&H0000FF00&", 170, 195, 100, 40));

        At(window, canvas, 200, 140).Should().Be(Green, "ShapeD comes first, so it covers LabelD");
        At(window, canvas, 200, 210).Should().Be(Yellow, "LabelE comes first, so it covers ShapeE");
    }

    [AvaloniaFact]
    public async Task A_control_array_element_loaded_at_run_time_is_drawn_under_the_controls_already_there()
    {
        var (canvas, window, ctx, env) = ContainerRuntimeHarness.Laid(Form(
            Box("VB.PictureBox", "PicGreen", "&H0000FF00&", 300, 60, 60, 40) +
            Box("VB.PictureBox", "PicArr", "&H000000FF&", 300, 10, 60, 40, "      Index           =   0\r\n")));

        await ContainerRuntimeHarness.Run(ctx, env, """
            Load PicArr(1)
            PicArr(1).BackColor = &HFF0000
            PicArr(1).Move 4950, 450
            PicArr(1).Visible = True

            """);
        Dispatcher.UIThread.RunJobs();

        At(window, canvas, 375, 55).Should().Be(Blue, "the loaded element is drawn where nothing covers it");
        At(window, canvas, 345, 40).Should().Be(Red, "PicArr(0) covers it");
        At(window, canvas, 345, 65).Should().Be(Green, "and so does PicGreen");
    }

    // ── ZOrder (#1503) ───────────────────────────────────────────────────────────────────────────────────

    private static async Task<(Window Window, Canvas Canvas, string Printed)> Ordered(string controls, string code)
    {
        var (canvas, window, ctx, env) = ContainerRuntimeHarness.Laid(Form(controls));
        var printed = await ContainerRuntimeHarness.Run(ctx, env, code + "\r\n");
        Dispatcher.UIThread.RunJobs();
        return (window, canvas, string.Join(" ", printed.Select(v => v.Value?.ToString())));
    }

    [AvaloniaFact]
    public async Task ZOrder_0_puts_a_control_over_the_others_and_ZOrder_1_under_them()
    {
        var (window, canvas, _) = await Ordered(
            Box("VB.PictureBox", "PicRed", "&H000000FF&", 10, 10, 60, 40) +
            Box("VB.PictureBox", "PicBlue", "&H00FF0000&", 40, 30, 60, 40) +
            Box("VB.PictureBox", "PicGreen", "&H0000FF00&", 150, 10, 60, 40) +
            Box("VB.PictureBox", "PicYellow", "&H0000FFFF&", 180, 30, 60, 40),
            "PicBlue.ZOrder\r\nPicGreen.ZOrder 1");

        At(window, canvas, 55, 40).Should().Be(Blue, "ZOrder with no argument brings PicBlue to the front");
        At(window, canvas, 195, 40).Should().Be(Yellow, "ZOrder 1 sends PicGreen to the back");
    }

    [AvaloniaFact]
    public async Task ZOrder_never_takes_a_control_out_of_its_plane()
    {
        var (window, canvas, _) = await Ordered(
            Box("VB.Label", "LabelA", "&H000000FF&", 10, 110, 100, 40) +
            Box("VB.CommandButton", "CmdA", "", 40, 125, 81, 33) +
            Box("VB.PictureBox", "PicOrange", "&H000080FF&", 150, 110, 60, 40) +
            Box("VB.Label", "LabelK", "&H0000FFFF&", 180, 125, 60, 40),
            "LabelA.ZOrder 0\r\nPicOrange.ZOrder 1");

        At(window, canvas, 60, 140).Should().Be(At(window, canvas, 115, 150), "a Label brought to the front stays under the button");
        At(window, canvas, 195, 140).Should().Be(Color.FromRgb(255, 128, 0), "a PictureBox sent to the back stays over the Label");
    }

    [AvaloniaFact]
    public async Task ZOrder_moves_a_windowless_control_among_the_windowless_ones()
    {
        var (window, canvas, _) = await Ordered(
            Box("VB.Label", "LabelE", "&H0000FFFF&", 150, 180, 70, 40) +
            Box("VB.Shape", "ShapeE", "&H0000FF00&", 170, 195, 100, 40) +
            Line("LineF", 235) + Box("VB.Label", "LabelF", "&H0000FFFF&", 10, 220, 100, 30),
            "ShapeE.ZOrder 0\r\nLineF.ZOrder 1");

        At(window, canvas, 200, 210).Should().Be(Green, "ShapeE is brought over LabelE");
        At(window, canvas, 60, 235).Should().Be(Yellow, "LineF is sent under LabelF");
    }

    [AvaloniaFact]
    public async Task ZOrder_moves_a_container_with_its_contents_and_an_array_element_alone()
    {
        var (window, canvas, _) = await Ordered(
            Box("VB.PictureBox", "PicCyan", "&H00FFFF00&", 10, 100, 60, 40) +
            "   Begin VB.PictureBox Holder \r\n      BorderStyle     =   0  'None\r\n" +
            "      Height          =   1200\r\n      Left            =   450\r\n      Top             =   1650\r\n" +
            "      Width           =   1500\r\n" +
            Box("VB.PictureBox", "PicIn", "&H00FF00FF&", 0, 0, 50, 40).Replace("\r\n   ", "\r\n      ") +
            "   End\r\n" +
            Box("VB.PictureBox", "PicArr", "&H000000FF&", 250, 10, 60, 40, "      Index           =   0\r\n") +
            Box("VB.PictureBox", "PicArr", "&H00FF0000&", 280, 30, 60, 40, "      Index           =   1\r\n"),
            "Holder.ZOrder\r\nPicArr(1).ZOrder");

        // The holder is at 30, 110, so PicIn covers 30 to 80 and 110 to 150, over PicCyan from 30 to 70 and 110 to 140.
        At(window, canvas, 50, 125).Should().Be(Color.FromRgb(255, 0, 255), "the holder came to the front with PicIn in it");
        At(window, canvas, 295, 40).Should().Be(Blue, "PicArr(1) came over PicArr(0)");
    }

    [AvaloniaTheory]
    [InlineData("2", "5")]
    [InlineData("-1", "5")]
    [InlineData("0.6", "0")]
    [InlineData("\"1\"", "0")]
    [InlineData("\"x\"", "13")]
    [InlineData("32768", "6")]
    [InlineData("1.5", "5")]
    [InlineData("-0.5", "0")]
    [InlineData("Null", "13")]
    [InlineData("Empty", "0")]
    public async Task ZOrders_position_is_an_Integer_that_must_be_0_or_1(string argument, string error)
    {
        var (_, _, printed) = await Ordered(Box("VB.PictureBox", "Pic1", "&H00808080&", 10, 10, 40, 30),
            $"On Error Resume Next\r\nPic1.ZOrder {argument}\r\nDebug.Print Err.Number");

        printed.Should().Be(error);
    }

    // ── a long run of restacks ───────────────────────────────────────────────────────────────────────────

    // An animation that brings two controls forward in turn, frame after frame. Each placement used to go one past the
    // plane's extreme, so the two climbed by one a call, and after about a million crossed into the other plane's band:
    // two Labels became windowed, drawn over the buttons. The plane's numbers now stay those of its controls.
    [AvaloniaTheory]
    [InlineData("0")]
    [InlineData("1")]
    public async Task Controls_restacked_in_turn_keep_the_numbers_of_their_plane(string position)
    {
        async Task<(int[] Labels, int[] Pictures)> After(int rounds)
        {
            var (_, canvas, _) = await Ordered(
                Box("VB.Label", "LabelA", "&H000000FF&", 10, 10, 60, 40) +
                Box("VB.Label", "LabelB", "&H0000FF00&", 40, 30, 60, 40) +
                Box("VB.PictureBox", "PicA", "&H00FF0000&", 150, 10, 60, 40) +
                Box("VB.PictureBox", "PicB", "&H0000FFFF&", 180, 30, 60, 40),
                $"Dim i As Long\r\nFor i = 1 To {rounds}\r\nLabelA.ZOrder {position}: LabelB.ZOrder {position}\r\n" +
                $"PicA.ZOrder {position}: PicB.ZOrder {position}\r\nNext");
            return (canvas.Children.OfType<HexIDE.Runtime.BuiltinControls.VBLabel>().Select(c => c.ZIndex).ToArray(),
                canvas.Children.OfType<HexIDE.Runtime.BuiltinControls.VBPictureBox>().Select(c => c.ZIndex).ToArray());
        }

        var once = await After(1);
        var many = await After(500);

        many.Labels.Should().Equal(once.Labels, "the Labels end as they did after one round, on the same numbers");
        many.Pictures.Should().Equal(once.Pictures);
        many.Labels.Max().Should().BeLessThan(many.Pictures.Min(), "a Label stays under every windowed control");
    }

    // The plane is the control's, not read back from its number: a Label whose number was pushed among the windowed
    // controls' is still a Label, and its next restack puts it back under them.
    [AvaloniaFact]
    public async Task A_controls_plane_does_not_come_from_its_number()
    {
        var (window, canvas, _) = await Ordered(
            Box("VB.Label", "LabelA", "&H000000FF&", 10, 110, 100, 40) +
            Box("VB.CommandButton", "CmdA", "", 40, 125, 81, 33), "");
        var label = canvas.Children.OfType<HexIDE.Runtime.BuiltinControls.VBLabel>().Single();
        label.ZIndex = 10;

        HexIDE.Runtime.ControlStacking.IsWindowless(label).Should().BeTrue();
        HexIDE.Runtime.ControlStacking.PlaceOver(label, HexIDE.Runtime.ControlStacking.IsWindowless(label), canvas);
        Dispatcher.UIThread.RunJobs();

        At(window, canvas, 60, 140).Should().Be(At(window, canvas, 115, 150), "the Label went back under the button");
    }
}
