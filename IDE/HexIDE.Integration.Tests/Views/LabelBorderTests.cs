using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A Label's border (#1077), read pixel by pixel from a real Skia frame of the hosted form and compared with what vb6.exe
/// drew for the same Labels, which <c>docs/vb6-fidelity-oracle.md</c> records under "A Label's border". Fixed Single with
/// Appearance 3D is a sunken edge two pixels deep: shadow over dark shadow along the top and left, highlight under light
/// along the bottom and right. Flat is one pixel of window frame. The BackColor fills what is left.
/// </summary>
public class LabelBorderTests : WindowTestFixture
{
    // The probe's Labels: 20 pixels square, 300 twips, and grey or red.
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Form1"
           Begin VB.Label L3DPair
              BackColor       =   &H00C0C0C0&
              BorderStyle     =   1  'Fixed Single
              Height          =   300
              Index           =   1
              Left            =   900
              Top             =   1500
              Width           =   300
           End
           Begin VB.Label L3DPair
              BackColor       =   &H00C0C0C0&
              BorderStyle     =   1  'Fixed Single
              Height          =   300
              Index           =   0
              Left            =   600
              Top             =   1500
              Width           =   300
           End
           Begin VB.Label LNone
              BackColor       =   &H00C0C0C0&
              Height          =   300
              Left            =   3300
              Top             =   600
              Width           =   300
           End
           Begin VB.Label L3DRed
              BackColor       =   &H000000FF&
              BorderStyle     =   1  'Fixed Single
              Height          =   300
              Left            =   2400
              Top             =   600
              Width           =   300
           End
           Begin VB.Label LFlat
              Appearance      =   0  'Flat
              BackColor       =   &H00C0C0C0&
              BorderStyle     =   1  'Fixed Single
              Height          =   300
              Left            =   1500
              Top             =   600
              Width           =   300
           End
           Begin VB.Label L3D
              BackColor       =   &H00C0C0C0&
              BorderStyle     =   1  'Fixed Single
              Height          =   300
              Left            =   600
              Top             =   600
              Width           =   300
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private static readonly Color Grey = Color.FromRgb(0xC0, 0xC0, 0xC0);
    private static readonly Color Red = Color.FromRgb(0xFF, 0, 0);

    // What the four 3D shades and the window frame are here: VB6's system colours as the form's resources resolve them.
    private static Window? host;

    private static Color System(VbSystemColor index) =>
        host!.TryFindResource(index.GetKey(), out var found) && found is ISolidColorBrush brush
            ? brush.Color
            : throw new InvalidOperationException($"system colour {index} does not resolve");

    private static Color Shadow => System(VbSystemColor.Btnshadow);
    private static Color DarkShadow => System(VbSystemColor._3DDKSHADOW);
    private static Color Light => System(VbSystemColor._3DLIGHT);
    private static Color Highlight => System(VbSystemColor.Btnhighlight);
    private static Color Frame => System(VbSystemColor.Windowframe);

    private static (Canvas Canvas, Window Window) Laid()
    {
        var (canvas, window, _, _) = ContainerRuntimeHarness.Laid(Form);
        host = window;
        return (canvas, window);
    }

    /// <summary>The colours of one row or column of a label, from its first pixel to its last, in a real Skia frame.</summary>
    private static Color[] Line(Window window, VBLabel label, bool row, int at)
    {
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame was rendered");
        using var buffer = frame.Lock();
        var origin = label.TranslatePoint(new Point(0, 0), window) ?? throw new InvalidOperationException("not in the window");
        var length = (int)(row ? label.Bounds.Width : label.Bounds.Height);
        var colours = new Color[length];
        for (var i = 0; i < length; i++)
        {
            var x = (int)origin.X + (row ? i : at);
            var y = (int)origin.Y + (row ? at : i);
            var offset = y * buffer.RowBytes + x * 4;
            byte At(int k) => Marshal.ReadByte(buffer.Address, offset + k);
            colours[i] = buffer.Format == Avalonia.Platform.PixelFormat.Bgra8888
                ? Color.FromRgb(At(2), At(1), At(0))
                : Color.FromRgb(At(0), At(1), At(2));
        }
        return colours;
    }

    private static Color[] Run(Color first, Color second, int middle, Color fill, Color last, Color end)
    {
        var line = new Color[2 + middle + 2];
        line[0] = first;
        line[1] = second;
        for (var i = 0; i < middle; i++)
            line[2 + i] = fill;
        line[2 + middle] = last;
        line[3 + middle] = end;
        return line;
    }

    private static VBLabel Label(Canvas canvas, string name, int? index = null) =>
        canvas.Children.OfType<VBLabel>().Single(l => ContainerRuntimeHarness.NameOf(l) == name
                                                      && (index is null || VBProps.GetIndex(l) == index));

    [AvaloniaFact]
    public void A_3D_Fixed_Single_label_draws_a_sunken_edge_two_pixels_deep()
    {
        var (canvas, window) = Laid();
        var label = Label(canvas, "L3D");

        // The first row: shadow all along, but for the highlight that the right edge brings down to it.
        Line(window, label, row: true, at: 0).Should().Equal(Enumerable.Repeat(Shadow, 19).Append(Highlight));
        // The second: shadow, then dark shadow, with light and highlight at the right.
        Line(window, label, row: true, at: 1).Should().Equal(Run(Shadow, DarkShadow, 16, DarkShadow, Light, Highlight));
        // Through the middle, across and down: the BackColor between the two edges.
        Line(window, label, row: true, at: 10).Should().Equal(Run(Shadow, DarkShadow, 16, Grey, Light, Highlight));
        Line(window, label, row: false, at: 10).Should().Equal(Run(Shadow, DarkShadow, 16, Grey, Light, Highlight));
        // The last two rows: light, then highlight, each from the shadow at the left edge.
        Line(window, label, row: true, at: 18).Should().Equal(new[] { Shadow }.Concat(Enumerable.Repeat(Light, 18)).Append(Highlight));
        Line(window, label, row: true, at: 19).Should().Equal(Enumerable.Repeat(Highlight, 20));
    }

    [AvaloniaFact]
    public void The_edge_does_not_take_its_colours_from_the_BackColor()
    {
        var (canvas, window) = Laid();

        Line(window, Label(canvas, "L3DRed"), row: true, at: 10).Should().Equal(Run(Shadow, DarkShadow, 16, Red, Light, Highlight));
    }

    [AvaloniaFact]
    public void A_Flat_Fixed_Single_label_draws_one_pixel_of_window_frame()
    {
        var (canvas, window) = Laid();
        var label = Label(canvas, "LFlat");

        Line(window, label, row: true, at: 0).Should().Equal(Enumerable.Repeat(Frame, 20));
        var middle = new[] { Frame }.Concat(Enumerable.Repeat(Grey, 18)).Append(Frame).ToArray();
        Line(window, label, row: true, at: 10).Should().Equal(middle);
        Line(window, label, row: false, at: 10).Should().Equal(middle);
        Line(window, label, row: true, at: 19).Should().Equal(Enumerable.Repeat(Frame, 20));
    }

    [AvaloniaFact]
    public void A_label_with_no_border_is_its_BackColor_to_the_edge()
    {
        var (canvas, window) = Laid();

        Line(window, Label(canvas, "LNone"), row: true, at: 0).Should().Equal(Enumerable.Repeat(Grey, 20));
    }

    [AvaloniaFact]
    public void Two_cells_side_by_side_read_as_a_grid()
    {
        // Minesweeper's and Battleship's field: the left cell's highlight meets the right cell's shadow.
        var (canvas, window) = Laid();

        Line(window, Label(canvas, "L3DPair", 0), row: true, at: 10)[19].Should().Be(Highlight);
        Line(window, Label(canvas, "L3DPair", 1), row: true, at: 10)[0].Should().Be(Shadow);
    }
}
