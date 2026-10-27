using Avalonia.Controls.Primitives;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

public sealed partial class VBStatusBar : TemplatedControl
{
    public VBStatusBar() : this(null) { }

    internal VBStatusBar(Func<long, bool>? readKeyState)
    {
        this.readKeyState = readKeyState ?? KeyOn;
        Panels = new StatusBarPanels(this);
        Tag = "";
    }

    public StatusBarPanels Panels { get; }
    private long style;
    private string simpleText = "";
    public long Style { get => style; set { style = value; InvalidateVisual(); } }
    public string SimpleText { get => simpleText; set { simpleText = value; InvalidateVisual(); } }
    public bool ShowTips { get; set; } = true;
    public long MousePointer { get; set; }
    public long OLEDropMode { get; set; }
    internal double ContainerHeight { get; set; } = double.NaN;
    public short Align { get; set; } = 2;

    // Console measurements: #1087, issuecomment-6427372779. The left edge and each
    // separator use a pixel; the final 19 pixels belong to the grip. Extra spring
    // space is shared in whole pixels, above each panel's own HIMETRIC minimum.
    private double layoutWidth = double.NaN;

    internal void LayoutPanels(bool force = false)
    {
        var pixels = double.IsFinite(Width) ? Width : Bounds.Width;
        if (!force && pixels.Equals(layoutWidth)) return;
        var visible = Panels.Items.Where(p => p.visible).ToList();
        var springs = visible.Where(p => p.autoSize == 1).ToList();
        if (springs.Count == 0)
        {
            layoutWidth = pixels;
            return;
        }
        if (!double.IsFinite(pixels))
            throw new NotImplementedException("StatusBar spring layout needs a known width (#1087)");
        foreach (var panel in visible.Where(p => p.autoSize == 2))
        {
            if (panel.Text.Length != 0)
                throw new NotImplementedException("Nonempty Contents Width is deferred to #1087 part B");
            panel.width = panel.minWidth;
        }
        var used = visible.Sum(p => (p.autoSize == 1 ? p.minWidth : p.width) / 15.0);
        var extra = Math.Max(0, Math.Floor((pixels - 19 - visible.Count - used) / springs.Count));
        var extraTwips = Math.Round(extra * 2540 / 96) * 1440 / 2540;
        foreach (var panel in springs)
            panel.width = (float)(panel.minWidth + extraTwips);
        layoutWidth = pixels;
    }
}
