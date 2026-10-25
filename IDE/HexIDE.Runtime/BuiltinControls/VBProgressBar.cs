using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Runtime.BuiltinControls;

/// <summary>
/// MSComctlLib's ProgressBar (#1082), drawn as VB6's is: the Windows progress bar without visual styles, which is how a VB6
/// program without a manifest shows it.
/// </summary>
/// <remarks>
/// Measured from vb6.exe's running ProgressBars, pixel by pixel (the oracle's "In-box controls: the ProgressBar"):
/// <list type="bullet">
/// <item>The frame: Appearance 1 is a 2 px sunken edge; BorderStyle 1 puts a 1 px ring of the button face outside it, or on a
/// flat bar a 1 px window-frame line. A flat bar with no border has no frame.</item>
/// <item>The fill area is what the frame leaves, less 1 px all round. The fill runs across it from the left, or up it from
/// the bottom when vertical, for its length times (Value - Min) / (Max - Min), rounded half up.</item>
/// <item>Smooth, that length is one bar. Standard, it is blocks ⌊2/3⌋ of the area's thickness long with 2 px between, as many
/// as the length needs, rounded up; across, the last is cut 1 px short of the area's end.</item>
/// <item>A disabled bar is drawn as an enabled one.</item>
/// </list>
/// </remarks>
public class VBProgressBar : Control
{
    public static readonly StyledProperty<float> VbMinProperty = AvaloniaProperty.Register<VBProgressBar, float>(nameof(VbMin));
    public static readonly StyledProperty<float> VbMaxProperty = AvaloniaProperty.Register<VBProgressBar, float>(nameof(VbMax), 100);
    public static readonly StyledProperty<float> VbValueProperty = AvaloniaProperty.Register<VBProgressBar, float>(nameof(VbValue));
    public static readonly StyledProperty<VBOrientation> OrientationProperty =
        AvaloniaProperty.Register<VBProgressBar, VBOrientation>(nameof(Orientation));
    public static readonly StyledProperty<VBScrolling> ScrollingProperty =
        AvaloniaProperty.Register<VBProgressBar, VBScrolling>(nameof(Scrolling));
    public static readonly StyledProperty<VBAppearance> AppearanceProperty =
        AvaloniaProperty.Register<VBProgressBar, VBAppearance>(nameof(Appearance), VBAppearance._3D);
    public static readonly StyledProperty<VBBorder> BorderStyleProperty =
        AvaloniaProperty.Register<VBProgressBar, VBBorder>(nameof(BorderStyle));
    public static readonly StyledProperty<VBOLEDropMode> OLEDropModeProperty =
        AvaloniaProperty.Register<VBProgressBar, VBOLEDropMode>(nameof(OLEDropMode));
    public static readonly StyledProperty<bool> ShowFullProperty = AvaloniaProperty.Register<VBProgressBar, bool>(nameof(ShowFull));

    static VBProgressBar()
    {
        AffectsRender<VBProgressBar>(VbMinProperty, VbMaxProperty, VbValueProperty, OrientationProperty, ScrollingProperty,
            AppearanceProperty, BorderStyleProperty, ShowFullProperty);
        AttachedEvents.AttachMouse<VBProgressBar>();
        AttachedEvents.AttachClick<VBProgressBar>(AttachedEvents.ClickRelease.OverTheControl);
    }

    public float VbMin { get => GetValue(VbMinProperty); set => SetValue(VbMinProperty, value); }
    public float VbMax { get => GetValue(VbMaxProperty); set => SetValue(VbMaxProperty, value); }
    public float VbValue { get => GetValue(VbValueProperty); set => SetValue(VbValueProperty, value); }
    public VBOrientation Orientation { get => GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
    public VBScrolling Scrolling { get => GetValue(ScrollingProperty); set => SetValue(ScrollingProperty, value); }
    public VBAppearance Appearance { get => GetValue(AppearanceProperty); set => SetValue(AppearanceProperty, value); }
    public VBBorder BorderStyle { get => GetValue(BorderStyleProperty); set => SetValue(BorderStyleProperty, value); }
    public VBOLEDropMode OLEDropMode { get => GetValue(OLEDropModeProperty); set => SetValue(OLEDropModeProperty, value); }

    /// <summary>Drawn full whatever its Value, as VB6's designer draws one (measured): Value is not saved, so the designer
    /// has none to draw.</summary>
    public bool ShowFull { get => GetValue(ShowFullProperty); set => SetValue(ShowFullProperty, value); }

    /// <summary>How many pixels of <paramref name="length"/> the fill covers.</summary>
    public static int FillLength(int length, float min, float max, float value)
    {
        if (length <= 0 || max <= min)
            return 0;
        var fraction = Math.Clamp(((double)value - min) / ((double)max - min), 0, 1);
        return (int)Math.Floor(length * fraction + 0.5);
    }

    /// <summary>The block length for a fill area <paramref name="thickness"/> across: two thirds of it, at least 1.</summary>
    public static int BlockLength(int thickness) => Math.Max(1, thickness * 2 / 3);

    private const int BlockGap = 2;

    public override void Render(DrawingContext context)
    {
        var width = (int)Math.Round(Bounds.Width);
        var height = (int)Math.Round(Bounds.Height);
        if (width <= 0 || height <= 0)
            return;
        void Fill(VbSystemColor colour, int x, int y, int w, int h)
        {
            if (w > 0 && h > 0)
                context.FillRectangle(VBColor.FromSystemColor(colour).ToBrush(), new Rect(x, y, w, h));
        }

        Fill(VbSystemColor.Btnface, 0, 0, width, height);

        var inset = 0;
        if (Appearance == VBAppearance._3D)
        {
            if (BorderStyle == VBBorder.FixedSingle)
                inset = 1;
            // The sunken edge: shadow then dark shadow at the top and left, light then highlight at the bottom and right.
            var (l, t, r, b) = (inset, inset, width - 1 - inset, height - 1 - inset);
            Fill(VbSystemColor.Btnshadow, l, t, r - l, 1);
            Fill(VbSystemColor.Btnshadow, l, t, 1, b - t);
            Fill(VbSystemColor._3DDKSHADOW, l + 1, t + 1, r - l - 2, 1);
            Fill(VbSystemColor._3DDKSHADOW, l + 1, t + 1, 1, b - t - 2);
            Fill(VbSystemColor._3DLIGHT, l + 1, b - 1, r - l - 1, 1);
            Fill(VbSystemColor._3DLIGHT, r - 1, t + 1, 1, b - t - 1);
            Fill(VbSystemColor.Btnhighlight, l, b, r - l + 1, 1);
            Fill(VbSystemColor.Btnhighlight, r, t, 1, b - t + 1);
            inset += 2;
        }
        else if (BorderStyle == VBBorder.FixedSingle)
        {
            Fill(VbSystemColor.Windowframe, 0, 0, width, 1);
            Fill(VbSystemColor.Windowframe, 0, height - 1, width, 1);
            Fill(VbSystemColor.Windowframe, 0, 0, 1, height);
            Fill(VbSystemColor.Windowframe, width - 1, 0, 1, height);
            inset = 1;
        }

        // The fill area: inside the frame, less 1 px all round.
        var areaLeft = inset + 1;
        var areaTop = inset + 1;
        var areaWidth = width - 2 * (inset + 1);
        var areaHeight = height - 2 * (inset + 1);
        if (areaWidth <= 0 || areaHeight <= 0)
            return;

        var vertical = Orientation == VBOrientation.Vertical;
        var length = vertical ? areaHeight : areaWidth;
        var thickness = vertical ? areaWidth : areaHeight;
        var filled = ShowFull ? length : FillLength(length, VbMin, VbMax, VbValue);
        if (filled <= 0)
            return;

        void Bar(int from, int extent)
        {
            if (vertical)
                Fill(VbSystemColor.Highlight, areaLeft, areaTop + areaHeight - from - extent, areaWidth, extent);
            else
                Fill(VbSystemColor.Highlight, areaLeft + from, areaTop, extent, areaHeight);
        }

        if (Scrolling == VBScrolling.Smooth)
        {
            Bar(0, filled);
            return;
        }

        var block = BlockLength(thickness);
        var step = block + BlockGap;
        var blocks = (filled + step - 1) / step;
        for (var i = 0; i < blocks; i++)
        {
            var from = i * step;
            // The last block is cut 1 px short of the area's end (measured across; taken to hold up the bar as well).
            var extent = Math.Min(block, length - 1 - from);
            if (extent <= 0)
                break;
            Bar(from, extent);
        }
    }
}
