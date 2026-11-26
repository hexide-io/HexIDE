using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

/// <summary>
/// A Line (#163), drawn pixel for pixel as VB6 draws it. Every rule below was measured on vb6.exe with <c>Point</c> on
/// a painted form, native and p-code agreeing (the oracle's "The Line control").
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The .frm gives the endpoints in the container's scale. VB6 holds them as whole twips, half to even, and a
/// program reads them back in the container's scale.</item>
/// <item>An endpoint is on the pixel nearest its twips: 3007 twips is row 200, and 3008 is row 201.</item>
/// <item>A one-pixel line is GDI's: every pixel from the first endpoint up to the second, the second left out, stepping
/// as a midpoint line does. Its BorderStyle dashes it, counting pixels from the first endpoint.</item>
/// <item>A wider line is solid whatever its BorderStyle, and covers both endpoints. Across it, it covers BorderWidth rows
/// centred on the endpoint's, the extra one above where the width is even: 2 covers the endpoint's row and the one
/// above, and 3 the one above and the one below. A vertical line of 3 is centred too; an even vertical one is taken to
/// put its extra column to the left, which is not measured.</item>
/// <item>Inside Solid draws as Solid, and Transparent draws nothing.</item>
/// <item>A line earlier in the .frm is drawn over one later in it.</item>
/// </list>
/// It covers its own bounding box, so it lays itself out on its container's canvas from its endpoints, and takes no
/// pointer input: a Line has no events, and a click on it is a click on what is under it.
/// </remarks>
public class VBLine : Control
{
    public static readonly StyledProperty<VBColor> BorderColorProperty = AvaloniaProperty.Register<VBLine, VBColor>(nameof(BorderColor));

    public static readonly StyledProperty<BorderStyles> BorderStyleProperty =
        AvaloniaProperty.Register<VBLine, BorderStyles>(nameof(BorderStyle), BorderStyles.Solid);

    public static readonly StyledProperty<int> BorderWidthProperty = AvaloniaProperty.Register<VBLine, int>(nameof(BorderWidth), 1);

    public static readonly StyledProperty<int> DrawModeProperty = AvaloniaProperty.Register<VBLine, int>(nameof(DrawMode), 13);

    /// <summary>X1, Y1, X2 and Y2, in the order the coordinate methods index them.</summary>
    public const int X1 = 0, Y1 = 1, X2 = 2, Y2 = 3;

    // As the .frm gave them, in the container's scale, until the container's scale is known.
    private readonly double[] declared;
    private readonly double[] twips = new double[4];

    static VBLine()
    {
        AffectsRender<VBLine>(BorderColorProperty, BorderStyleProperty, BorderWidthProperty);
        BorderWidthProperty.Changed.AddClassHandler<VBLine>((line, _) => line.Place());
    }

    public VBLine(double x1, double y1, double x2, double y2)
    {
        declared = [x1, y1, x2, y2];
        IsHitTestVisible = false;
    }

    public VBColor BorderColor
    {
        get => GetValue(BorderColorProperty);
        set => SetValue(BorderColorProperty, value);
    }

    public BorderStyles BorderStyle
    {
        get => GetValue(BorderStyleProperty);
        set => SetValue(BorderStyleProperty, value);
    }

    public int BorderWidth
    {
        get => GetValue(BorderWidthProperty);
        set => SetValue(BorderWidthProperty, value);
    }

    /// <summary>Held and read back. Every mode draws as Copy Pen, 13, which is a divergence where it is not 13.</summary>
    public int DrawMode
    {
        get => GetValue(DrawModeProperty);
        set => SetValue(DrawModeProperty, value);
    }

    /// <summary>How many twips one of the container's units is, across and down, once the line is placed.</summary>
    public (double X, double Y)? TwipsPerUnit { get; private set; }

    /// <summary>Converts the declared endpoints with the container's scale, and lays the line out.</summary>
    public void Resolve((double X, double Y) twipsPerUnit)
    {
        TwipsPerUnit = twipsPerUnit;
        for (var i = 0; i < 4; i++)
            twips[i] = WholeTwips(declared[i] * Factor(i));
        Place();
    }

    /// <summary>An endpoint coordinate as a program reads it: a Single, in the container's scale.</summary>
    /// <remarks>
    /// The twips divided by the scale's twips per unit, which on a user scale is a Single (see
    /// <see cref="Components.LineComponentClass.TwipsPerUnit"/>), and the quotient rounded to a Single.
    /// </remarks>
    public float Read(int coordinate) =>
        TwipsPerUnit is null ? (float)declared[coordinate] : (float)(Origin(coordinate) + twips[coordinate] / Factor(coordinate));

    /// <summary>An endpoint coordinate a program assigns, in the container's scale.</summary>
    public void Write(int coordinate, double value)
    {
        if (TwipsPerUnit is null)
            throw new InvalidOperationException("A Line is written to before it is placed");
        twips[coordinate] = WholeTwips((value - Origin(coordinate)) * Factor(coordinate));
        Place();
    }

    /// <summary>
    /// The endpoints in whole twips. A value too big for a Long is Overflow, as a control's Left is (measured for
    /// Left; for a Line's coordinates it is the same rule, not separately measured).
    /// </summary>
    internal static double WholeTwips(double value)
    {
        if (double.IsNaN(value) || value < int.MinValue - 0.5 || value >= int.MaxValue + 0.5)
            throw new VBRunTimeException(VBStandardError.Overflow);
        return Math.Round(value, MidpointRounding.ToEven);
    }

    // A Line's ends add the container's ScaleLeft and ScaleTop, and a write takes them off (#5530, oracle PR #5534:
    // measured on Lines loaded in twips, pixels and inches, which once loaded are whole twips as a centimetre Line is).
    private double Origin(int coordinate) =>
        TwipsPerUnit is { X: AvaloniaInterop.RuntimeScale.TwipsPerCentimetre }
            && Parent is Control host && AvaloniaInterop.RuntimeScale.Of(host) is { } scale
            ? scale.Origin(coordinate % 2 != 0) : 0;

    private double Factor(int coordinate)
    {
        if (TwipsPerUnit is { X: AvaloniaInterop.RuntimeScale.TwipsPerCentimetre }
            && Parent is Control host && AvaloniaInterop.RuntimeScale.Of(host) is { } scale)
        {
            var factor = scale.TwipsPerUnit(coordinate % 2 != 0);
            // A Line divides by a Single user unit (#4693, oracle PR #5514).
            return VBProps.GetScaleMode(host) == VBScaleMode.User ? (float)factor : factor;
        }
        return coordinate % 2 == 0 ? TwipsPerUnit!.Value.X : TwipsPerUnit!.Value.Y;
    }

    // The pixel nearest a twips position. Twips are whole and a pixel is 15 of them, so no position is halfway.
    private static int Pixel(double twips) => (int)Math.Round(twips / VBScaleModeExtensions.PixelToTwips);

    private Rect LineBounds()
    {
        var bounds = LineControlBounds.BoundsFor(twips[X1], twips[Y1], twips[X2], twips[Y2], BorderWidth);
        return new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
    }

    private void Place()
    {
        if (TwipsPerUnit is null)
            return;
        var bounds = LineBounds();
        Canvas.SetLeft(this, bounds.X);
        Canvas.SetTop(this, bounds.Y);
        Width = bounds.Width;
        Height = bounds.Height;
        InvalidateVisual();
    }

    // GDI's pen styles at one pixel wide, as runs of pixels on and off from the first endpoint (measured).
    private static int[]? Dashes(BorderStyles style) => style switch
    {
        BorderStyles.Dash => [18, 6],
        BorderStyles.Dot => [3, 3],
        BorderStyles.DashDot => [9, 6, 3, 6],
        BorderStyles.DashDotDot => [9, 3, 3, 3, 3, 3],
        _ => null,
    };

    private static bool IsOn(int[]? dashes, int step)
    {
        if (dashes is null)
            return true;
        var period = 0;
        foreach (var run in dashes)
            period += run;
        var at = step % period;
        for (var i = 0; i < dashes.Length; i++)
        {
            if (at < dashes[i])
                return i % 2 == 0;
            at -= dashes[i];
        }
        return false;
    }

    public override void Render(DrawingContext context)
    {
        if (TwipsPerUnit is null || BorderStyle == BorderStyles.Transparent)
            return;
        var origin = LineBounds();
        int x1 = Pixel(twips[X1]) - (int)origin.X, y1 = Pixel(twips[Y1]) - (int)origin.Y;
        int x2 = Pixel(twips[X2]) - (int)origin.X, y2 = Pixel(twips[Y2]) - (int)origin.Y;
        if (x1 == x2 && y1 == y2)
            return;
        var brush = BorderColor.ToBrush();
        var width = BorderWidth;

        if (width <= 1)
        {
            var dashes = Dashes(BorderStyle);
            int dx = Math.Abs(x2 - x1), dy = Math.Abs(y2 - y1), sx = Math.Sign(x2 - x1), sy = Math.Sign(y2 - y1);
            // The midpoint line, stepping along the longer axis and the shorter one only past a half: (300,200) to
            // (310,205) covers 300 and 301 on row 200, then two to a row (measured). The second endpoint is left out.
            int major = Math.Max(dx, dy), minor = Math.Min(dx, dy);
            var decision = 2 * minor - major;
            int x = x1, y = y1;
            for (var step = 0; step < major; step++)
            {
                if (IsOn(dashes, step))
                    context.FillRectangle(brush, new Rect(x, y, 1, 1));
                if (decision > 0)
                {
                    if (dx >= dy) y += sy; else x += sx;
                    decision -= 2 * major;
                }
                decision += 2 * minor;
                if (dx >= dy) x += sx; else y += sy;
            }
            return;
        }

        // Wider: solid, both endpoints covered, and BorderWidth across, centred, the extra one above or to the left.
        var before = width / 2;
        if (y1 == y2)
            context.FillRectangle(brush, new Rect(Math.Min(x1, x2), y1 - before, Math.Abs(x2 - x1) + 1, width));
        else if (x1 == x2)
            context.FillRectangle(brush, new Rect(x1 - before, Math.Min(y1, y2), width, Math.Abs(y2 - y1) + 1));
        else
        {
            // A wide diagonal is GDI's geometric pen with round ends, which this approximates: not measured.
            var pen = new Pen(brush, width, lineCap: PenLineCap.Round);
            context.DrawLine(pen, new Point(x1 + 0.5, y1 + 0.5), new Point(x2 + 0.5, y2 + 0.5));
        }
    }
}
