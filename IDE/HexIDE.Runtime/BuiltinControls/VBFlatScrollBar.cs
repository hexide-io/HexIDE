using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

// VB6 SP6 native and p-code, MSCOMCT2.OCX: #1097 groups 1–2.
public sealed class VBFlatScrollBar : Control, ICSharpPropertyBag
{
    private int min, max = 32767, current, smallChange = 1, largeChange = 1;
    private int orientation = 1, appearance = 1, arrows;
    private long mousePointer;

    private readonly DispatcherTimer repeat = new();
    private int pressed;
    private bool dragging;
    private double hoverPosition = double.NaN;
    private double dragStart;
    private int dragValue;

    static VBFlatScrollBar() => AffectsRender<VBFlatScrollBar>(IsEnabledProperty);

    public VBFlatScrollBar()
    {
        repeat.Tick += (_, _) => { if (IsEffectivelyEnabled) Step(); else repeat.Stop(); };
    }

    private double Length => orientation == 1 ? Bounds.Width : Bounds.Height;
    private double Cross => orientation == 1 ? Bounds.Height : Bounds.Width;
    private double Button => Math.Min(17, Length / 2);
    private double Travel => Math.Max(0, Length - 2 * Button - 17);
    private double Thumb => Button + (max == min ? 0 : (current - (double)min) / (max - (double)min) * Travel);
    private double Axis(Point p) => orientation == 1 ? p.X : p.Y;
    private Rect Rectangle(double start, double length) => orientation == 1
        ? new Rect(start, 0, Math.Max(0, length), Math.Max(0, Cross - 1)) : new Rect(0, start, Math.Max(0, Cross - 1), Math.Max(0, length));

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEffectivelyEnabled || e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
        var position = Axis(e.GetPosition(this));
        if (position >= Button && position >= Thumb && position < Thumb + 17 && position < Length - Button)
        {
            dragging = true;
            dragStart = position;
            dragValue = current;
            this.ExecuteSub(FlatScrollBarComponentClass.ScrollEvent);
        }
        else
        {
            pressed = position < Button ? -1 : position >= Length - Button ? 1 : position < Thumb ? -2 : 2;
            if (pressed == -1 && arrows == 2 || pressed == 1 && arrows == 1) { pressed = 0; return; }
            Step();
            // Ten repeats plus the initial press give the measured eleven Changes in one second.
            if (Math.Abs(pressed) == 1)
            {
                repeat.Interval = TimeSpan.FromMilliseconds(100);
                repeat.Start();
            }
        }
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    private void Step()
    {
        if (pressed == 0) return;
        var delta = Math.Sign(pressed) * (Math.Abs(pressed) == 1 ? smallChange : largeChange) * (max >= min ? 1 : -1);
        TrySetProperty("Value", new Vb6Value(Math.Clamp(current + delta, Math.Min(min, max), Math.Max(min, max))));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        hoverPosition = Axis(e.GetPosition(this));
        if (dragging && Travel > 0)
        {
            var next = (int)Math.Clamp(dragValue + Math.Truncate((Axis(e.GetPosition(this)) - dragStart) / Travel * (max - (double)min)), Math.Min(min, max), Math.Max(min, max));
            if (next != current)
            {
                current = next;
                this.ExecuteSub(FlatScrollBarComponentClass.ScrollEvent);
            }
        }
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        hoverPosition = double.NaN;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        var changed = dragging;
        Stop();
        e.Pointer.Capture(null);
        if (changed) this.ExecuteSub(ScrollBarComponentClass.ChangeEvent);
    }

    private void Stop() { repeat.Stop(); pressed = 0; dragging = false; InvalidateVisual(); }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { Stop(); base.OnPointerCaptureLost(e); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { Stop(); base.OnDetachedFromVisualTree(e); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var face = VBColor.FromSystemColor(VbSystemColor.Btnface).ToBrush();
        for (var y = 0; y < Bounds.Height; y++)
            for (var x = 0; x < Bounds.Width; x++)
                context.FillRectangle((x + y) % 2 == 0 ? Brushes.White : face, new Rect(x, y, 1, 1));
        DrawBox(context, Rectangle(0, Button), appearance == 0 ? 2 : appearance == 2 && hoverPosition < Button ? 1 : 0, false);
        DrawBox(context, Rectangle(Length - Button, Button), appearance == 0 ? 2 : appearance == 2 && hoverPosition >= Length - Button ? 1 : 0, false);
        DrawArrow(context, Button / 2, -1, IsEffectivelyEnabled && arrows != 2);
        DrawArrow(context, Length - Button / 2, 1, IsEffectivelyEnabled && arrows != 1);
        if (IsEffectivelyEnabled) DrawBox(context, Rectangle(Thumb, Math.Min(17, Length - 2 * Button)), appearance == 0 ? 2 : appearance == 2 && hoverPosition >= Thumb && hoverPosition < Thumb + 17 ? 1 : 0, dragging);
    }

    private static void DrawBox(DrawingContext context, Rect rect, int edge, bool solid)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        context.FillRectangle(solid ? new SolidColorBrush(Color.FromRgb(160, 160, 160)) : VBColor.FromSystemColor(VbSystemColor.Btnface).ToBrush(), rect);
        if (solid) return;
        if (edge == 0)
        {
            context.DrawRectangle(new Pen(new SolidColorBrush(Color.FromRgb(160, 160, 160))), rect.Deflate(0.5));
            return;
        }
        for (var inset = 0; inset < edge; inset++)
        {
            var top = new Pen(new SolidColorBrush(edge == 2 && inset == 0 ? Color.FromRgb(227, 227, 227) : Colors.White));
            var bottom = new Pen(new SolidColorBrush(edge == 2 && inset == 0 ? Color.FromRgb(105, 105, 105) : Color.FromRgb(160, 160, 160)));
            var r = rect.Deflate(inset + 0.5);
            context.DrawLine(bottom, r.BottomLeft, r.BottomRight);
            context.DrawLine(bottom, r.TopRight, r.BottomRight);
            context.DrawLine(top, r.TopLeft, r.TopRight);
            context.DrawLine(top, r.TopLeft, r.BottomLeft);
        }
    }

    private void DrawArrow(DrawingContext context, double position, int direction, bool enabled)
    {
        var center = orientation == 1 ? new Point(position, Cross / 2) : new Point(Cross / 2, position);
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            Vector Offset(double along, double across) => orientation == 1 ? new Vector(along, across) : new Vector(across, along);
            path.BeginFigure(center + Offset(direction * 2, 0), true);
            path.LineTo(center + Offset(-direction * 2, -3));
            path.LineTo(center + Offset(-direction * 2, 3));
            path.EndFigure(true);
        }
        context.DrawGeometry(enabled ? Brushes.Black : new SolidColorBrush(Color.FromRgb(160, 160, 160)), null, geometry);
    }

    public int Value => current;

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        value = name.ToLowerInvariant() switch
        {
            "value" => new Vb6Value(current),
            "_value" => current == 0 ? new Vb6Value(0) : throw Unimplemented("_Value after Value changes"),
            "min" => new Vb6Value(min), "max" => new Vb6Value(max),
            "smallchange" => new Vb6Value(smallChange), "largechange" => new Vb6Value(largeChange),
            "orientation" => new Vb6Value((long)orientation), "appearance" => new Vb6Value((long)appearance),
            "arrows" => new Vb6Value((long)arrows), "mousepointer" => new Vb6Value(mousePointer),
            "enabled" => new Vb6Value(IsEnabled),
            "hwnd" or "mouseicon" => throw Unimplemented(name),
            "tabstop" or "causesvalidation" => throw Missing(name),
            _ => default,
        };
        return name.ToLowerInvariant() is "value" or "_value" or "min" or "max" or "smallchange" or "largechange"
            or "orientation" or "appearance" or "arrows" or "mousepointer" or "enabled";
    }

    public bool TrySetProperty(string name, Vb6Value value)
    {
        switch (name.ToLowerInvariant())
        {
            case "value":
                var number = Integer(value, conversion: true);
                if (number < Math.Min(min, max) || number > Math.Max(min, max)) throw Invalid();
                if (current != number)
                {
                    current = number;
                    this.ExecuteSub(ScrollBarComponentClass.ChangeEvent);
                }
                break;
            case "min": min = Integer(value); Clamp(); break;
            case "max": max = Integer(value); Clamp(); break;
            case "smallchange": smallChange = Positive(value); break;
            case "largechange": largeChange = Positive(value); break;
            case "orientation":
                var direction = Enum(value, 1);
                if (orientation != direction)
                {
                    (Width, Height) = (Height, Width);
                    orientation = direction;
                }
                break;
            case "appearance": appearance = Enum(value, 2); break;
            case "arrows": arrows = Enum(value, 2); break;
            case "mousepointer":
                var pointer = Integral(value);
                if (pointer == -1) throw Invalid();
                if (pointer is < 0 or > 3) throw Unimplemented("MousePointer outside the measured range 0–3");
                mousePointer = pointer;
                break;
            case "enabled":
                if (value.Type != Vb6Value.ValueType.Boolean && value.Type != Vb6Value.ValueType.Integer && value.Type != Vb6Value.ValueType.Long)
                    throw Unimplemented("Enabled conversion");
                IsEnabled = Convert.ToInt64(value.Value) != 0;
                break;
            case "hwnd": throw new VBRunTimeException(new VBStandardError(383, "Set not supported (read-only property)"));
            case "tabstop" or "causesvalidation": throw Missing(name);
            case "_value" or "mouseicon": throw Unimplemented(name);
            default: return false;
        }
        InvalidateVisual();
        return true;
    }

    internal void LoadProperties(List<(string Property, Vb6Value Value)> properties)
    {
        foreach (var (property, value) in properties.OrderBy(p => p.Property.Equals("Value", StringComparison.OrdinalIgnoreCase) ? 1 : 0))
        {
            var name = property.ToLowerInvariant();
            if (name is "orientation" or "appearance" or "arrows")
            {
                var low = Convert.ToInt32(value.Value) & 0xffff;
                if (name == "orientation") orientation = low <= 1 ? low : throw Invalid();
                else if (name == "appearance") appearance = low <= 2 ? low : throw Invalid();
                else arrows = low <= 2 ? low : throw Invalid();
            }
            else if (name == "mousepointer")
            {
                // The saved probe reads 11; runtime assignments beyond 0–3 remain unmeasured.
                var pointer = Convert.ToInt64(value.Value);
                if (pointer is not (0 or 1 or 2 or 3 or 11)) throw Unimplemented("saved MousePointer");
                mousePointer = pointer;
            }
            else if (name is "min" or "max" or "smallchange" or "largechange" or "value" or "enabled")
                TrySetProperty(name, new Vb6Value(Convert.ToInt32(value.Value)));
            else if (name is not ("left" or "top" or "width" or "height" or "tabindex" or "visible" or "tag" or "tooltiptext"))
                throw Unimplemented("saved " + property);
        }
    }

    internal static void RefuseEarlyBound(Vb6Value reference, string method)
    {
        if (reference.Value is VBFlatScrollBar && method.Equals("SetFocus", StringComparison.OrdinalIgnoreCase)
            && reference.DeclaredAs is { } declared && (declared.Equals("MSComCtl2.FlatScrollBar", StringComparison.OrdinalIgnoreCase)
                || declared.Equals("FlatScrollBar", StringComparison.OrdinalIgnoreCase)))
            throw new VBMethodOrDataMemberNotFoundException(method, reference.Type);
    }

    private void Clamp() => current = Math.Clamp(current, Math.Min(min, max), Math.Max(min, max));
    private static int Integer(Vb6Value value, bool conversion = false)
    {
        if (!conversion && (value.Type != Vb6Value.ValueType.Integer && value.Type != Vb6Value.ValueType.Long))
            throw Unimplemented("numeric conversion");
        if (value.Type != Vb6Value.ValueType.Integer && value.Type != Vb6Value.ValueType.Long && value.Type != Vb6Value.ValueType.Single
            && value.Type != Vb6Value.ValueType.Double && value.Type != Vb6Value.ValueType.String) throw Unimplemented("numeric conversion");
        return Convert.ToInt32(VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Integer, null).Value);
    }
    private static int Positive(Vb6Value value)
    {
        var number = Integer(value);
        return number > 0 ? number : throw Invalid();
    }
    private static long Integral(Vb6Value value)
    {
        if (value.Type != Vb6Value.ValueType.Integer && value.Type != Vb6Value.ValueType.Long)
            throw Unimplemented("numeric conversion");
        return Convert.ToInt64(value.Value);
    }
    private static int Enum(Vb6Value value, int maximum)
    {
        var number = Integral(value);
        return number >= 0 && number <= maximum ? (int)number : throw Invalid();
    }
    private static VBRunTimeException Invalid() => new(VBStandardError.InvalidPropertyValue);
    private static VBRunTimeException Missing(string name) => new(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod, name);
    private static NotImplementedException Unimplemented(string name) => new($"FlatScrollBar {name} is not implemented (#1097)");
}
