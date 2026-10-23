using System;
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
/// Measured in VB6 at run time (docs/vb6-fidelity-oracle.md, "How a running form stacks its controls"):
/// <list type="bullet">
/// <item>the first block in a container is drawn on top;</item>
/// <item>a windowless control (Label, Shape, Line, Image) is drawn under every windowed one, whatever their order;</item>
/// <item>among the windowless ones the first block is on top, whatever the class;</item>
/// <item>a control-array element loaded at run time goes under the controls already there.</item>
/// </list>
/// HexIDE drew the last block on top, and every Line under every other control.
/// </remarks>
public class RuntimeStackingTests
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
}
