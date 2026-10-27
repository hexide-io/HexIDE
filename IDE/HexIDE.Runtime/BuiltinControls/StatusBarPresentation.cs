using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

public sealed partial class VBStatusBar
{
    private readonly Func<long, bool> readKeyState;
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private TopLevel? keyboardRoot;
    private readonly HashSet<Key> toggledKeys = [];
    private readonly HashSet<Key> heldKeys = [];
    private bool pressing, secondPress;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        keyboardRoot = TopLevel.GetTopLevel(this);
        keyboardRoot?.AddHandler(KeyDownEvent, KeyPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        keyboardRoot?.AddHandler(KeyUpEvent, KeyReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        clock.Tick += Tick;
        clock.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        clock.Stop();
        clock.Tick -= Tick;
        keyboardRoot?.RemoveHandler(KeyDownEvent, KeyPressed);
        keyboardRoot?.RemoveHandler(KeyUpEvent, KeyReleased);
        keyboardRoot = null;
        heldKeys.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    private void Tick(object? sender, EventArgs e) => InvalidateVisual();

    private void KeyPressed(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.CapsLock or Key.NumLock or Key.Insert or Key.Scroll)) return;
        if (heldKeys.Add(e.Key) && !toggledKeys.Remove(e.Key)) toggledKeys.Add(e.Key);
        InvalidateVisual();
    }

    private void KeyReleased(object? sender, KeyEventArgs e) => heldKeys.Remove(e.Key);

    [LibraryImport("user32.dll")]
    private static partial short GetKeyState(int key);

    private bool KeyOn(long style)
    {
        if (style == 7) return false; // Kana stayed greyed on the measured keyboard.
        var (key, native) = style switch
        {
            1 => (Key.CapsLock, 0x14), 2 => (Key.NumLock, 0x90),
            3 => (Key.Insert, 0x2D), 4 => (Key.Scroll, 0x91), _ => (Key.None, 0),
        };
        // Avalonia exposes no initial lock state. Other hosts follow keys seen in this window;
        // Windows can also read toggles that were already on before the form opened.
        return OperatingSystem.IsWindows() ? (GetKeyState(native) & 1) != 0 : toggledKeys.Contains(key);
    }

    internal string DisplayText(StatusBarPanel panel, DateTime now) => panel.Style switch
    {
        5 => now.ToString("t", VbLocale.Current),
        6 => now.ToString("d", VbLocale.Current),
        _ => panel.Text,
    };

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        LayoutPanels();
        context.FillRectangle(VBColor.FromSystemColor(VbSystemColor.Btnface).ToBrush(), new Rect(Bounds.Size));
        using var clip = context.PushClip(new Rect(Bounds.Size));
        var now = DateTime.Now;
        if (Style == 1)
            DrawText(context, SimpleText, new Rect(1, 1, Math.Max(0, Bounds.Width - 20), Math.Max(0, Bounds.Height - 2)), 0, false);
        else
        {
            double x = 1;
            foreach (var panel in Panels.Items.Where(p => p.visible))
            {
                var width = Math.Floor(panel.CurrentWidth() / 15.0);
                var rect = new Rect(x, 1, width, Math.Max(0, Bounds.Height - 2));
                DrawBorder(context, rect, panel.Bevel);
                // Exact text inset/clipping and border metrics are unmeasured rendering (#1087).
                if (width > 6 && rect.Height > 4)
                {
                    var content = rect.Deflate(new Thickness(3, 2));
                    if (panel.DrawingPicture?.Bitmap is { } bitmap)
                    {
                        // Exact picture inset and spacing are unmeasured rendering, as for text.
                        using (context.PushClip(content))
                            context.DrawImage(bitmap, new Rect(bitmap.Size),
                                new Rect(content.X, content.Y + (content.Height - bitmap.Size.Height) / 2,
                                    bitmap.Size.Width, bitmap.Size.Height));
                        var used = Math.Min(content.Width, bitmap.Size.Width + 3);
                        content = new Rect(content.X + used, content.Y, content.Width - used, content.Height);
                    }
                    DrawText(context, DisplayText(panel, now), content, panel.Alignment,
                        !IsEnabled || !panel.Enabled || (panel.Style is >= 1 and <= 4 or 7 && !readKeyState(panel.Style)));
                }
                x += width + 1;
            }
        }
        // The grip retains its 19 px reservation even in Simple style.
        for (var i = 0; i < 3; i++)
        {
            var end = new Point(Bounds.Width - 3, Bounds.Height - 5 - i * 4);
            var start = new Point(Bounds.Width - 5 - i * 4, Bounds.Height - 3);
            context.DrawLine(new Pen(VBColor.FromSystemColor(VbSystemColor.Btnshadow).ToBrush()), start, end);
            context.DrawLine(new Pen(VBColor.FromSystemColor(VbSystemColor.Btnhighlight).ToBrush()),
                start + new Vector(1, 0), end + new Vector(1, 0));
        }
    }

    private void DrawText(DrawingContext context, string text, Rect rect, long alignment, bool grey)
    {
        if (text.Length == 0 || rect.Width <= 0 || rect.Height <= 0) return;
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize,
            VBColor.FromSystemColor(grey ? VbSystemColor.Graytext : VbSystemColor.Btntext).ToBrush());
        var x = alignment switch { 1 => (rect.Width - formatted.Width) / 2, 2 => rect.Width - formatted.Width, _ => 0 };
        using var clip = context.PushClip(rect);
        context.DrawText(formatted, new Point(rect.X + x, rect.Y + (rect.Height - formatted.Height) / 2));
    }

    private static void DrawBorder(DrawingContext context, Rect rect, long bevel)
    {
        if (bevel == 0 || rect.Width < 2 || rect.Height < 2) return;
        var shadow = VBColor.FromSystemColor(VbSystemColor.Btnshadow).ToBrush();
        var light = VBColor.FromSystemColor(VbSystemColor.Btnhighlight).ToBrush();
        var first = new Pen(bevel == 1 ? shadow : light);
        var last = new Pen(bevel == 1 ? light : shadow);
        context.DrawLine(first, rect.TopLeft, rect.TopRight);
        context.DrawLine(first, rect.TopLeft, rect.BottomLeft);
        context.DrawLine(last, rect.BottomLeft, rect.BottomRight);
        context.DrawLine(last, rect.TopRight, rect.BottomRight);
    }

    private bool OverBar(Point point) => point.X >= 0 && point.X < Bounds.Width - 19 && point.Y >= 0 && point.Y < Bounds.Height;

    private StatusBarPanel? PanelAt(Point point)
    {
        if (Style == 1 || !OverBar(point)) return null;
        LayoutPanels();
        double x = 1;
        foreach (var panel in Panels.Items.Where(p => p.visible))
        {
            var width = Math.Floor(panel.CurrentWidth() / 15.0);
            if (point.X >= x && point.X < x + width) return panel;
            x += width + 1;
        }
        return null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!OverBar(e.GetPosition(this)) || !AttachedEvents.OwnsThePointerEvent(this, e.Source)
            || e.GetCurrentPoint(this).Properties.PointerUpdateKind is not
                (PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.RightButtonPressed or PointerUpdateKind.MiddleButtonPressed)) return;
        pressing = true;
        secondPress = e.ClickCount % 2 == 0;
        e.Pointer.Capture(this);
        if (secondPress) this.ExecuteSub(ComponentBaseClass.DblClickEvent);
        else AttachedEvents.RaiseMouse(this, e, ComponentBaseClass.MouseDownEvent);
        if (PanelAt(e.GetPosition(this)) is { } panel)
            RaisePanel(secondPress ? StatusBarComponentClass.PanelDblClickEvent : StatusBarComponentClass.PanelClickEvent, panel);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!pressing) return;
        pressing = false;
        AttachedEvents.RaiseMouse(this, e, ComponentBaseClass.MouseUpEvent);
        if (!secondPress && OverBar(e.GetPosition(this))) this.ExecuteSub(ComponentBaseClass.ClickEvent);
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (OverBar(e.GetPosition(this))) AttachedEvents.RaiseMouse(this, e, ComponentBaseClass.MouseMoveEvent);
    }

    private void RaisePanel(EventClass eventClass, StatusBarPanel panel)
    {
        foreach (var handler in ControlEvents.Handlers(this, this.FindLogicalAncestorOfType<IModuleExecutionRoot>(), eventClass.Name))
            handler.Root.ExecuteSub(handler.Name, handler.Own && VBProps.GetIndex(this) is { } index
                ? [new Vb6Value(index), new Vb6Value(panel)] : [new Vb6Value(panel)]);
    }
}
