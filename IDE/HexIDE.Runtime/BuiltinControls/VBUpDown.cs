using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

// VB6 SP6, native and p-code: oracle "In-box controls: the UpDown", groups 1–2 (#1093).
public sealed class VBUpDown : Control, ICSharpPropertyBag
{
    private int min, max = 10, current, increment = 1, alignment = 1, dropMode;
    private short acceleration;
    private bool wrap, autoBuddy, syncBuddy;
    private Control? buddy;
    private string? buddyProperty;
    private (double Left, double Top, double Width, double Height)? original;

    private readonly DispatcherTimer repeat = new();
    private bool? pressedUp;
    internal bool Horizontal { get; set; }

    static VBUpDown()
    {
        AffectsRender<VBUpDown>(IsEnabledProperty);
        ArrowNavigation.Attach<VBUpDown>(upOnly: true);
        AttachedEvents.AttachMouse<VBUpDown>();
        AttachedEvents.AttachFocusEvents<VBUpDown>();
        PointerPressedEvent.AddClassHandler<VBUpDown>((control, e) => control.Press(e));
        PointerReleasedEvent.AddClassHandler<VBUpDown>((control, e) => control.Release(e));
    }

    public VBUpDown()
    {
        Focusable = true;
        repeat.Tick += (_, _) =>
        {
            repeat.Interval = TimeSpan.FromSeconds(0.11);
            if (pressedUp is { } up && IsEffectivelyEnabled) Step(up);
            else repeat.Stop();
        };
    }

    private void Press(PointerPressedEventArgs e)
    {
        if (!IsEffectivelyEnabled || e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
        Focus();
        var point = e.GetPosition(this);
        pressedUp = Horizontal ? point.X >= Bounds.Width / 2 : point.Y < Bounds.Height / 2;
        e.Pointer.Capture(this);
        Step(pressedUp.Value);
        repeat.Interval = TimeSpan.FromSeconds(0.53);
        repeat.Start();
        InvalidateVisual();
    }

    private void Release(PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left || pressedUp is not { } up) return;
        repeat.Stop();
        pressedUp = null;
        e.Pointer.Capture(null);
        InvalidateVisual();
        this.ExecuteSub(up ? UpDownComponentClass.UpClickEvent : UpDownComponentClass.DownClickEvent);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        repeat.Stop();
        pressedUp = null;
        InvalidateVisual();
        base.OnPointerCaptureLost(e);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        repeat.Stop();
        pressedUp = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void Step(bool up)
    {
        if (increment == 0) return;
        if (syncBuddy && buddy is not null)
        {
            var property = buddyProperty ?? DefaultProperty(buddy);
            if (!property.Equals("Text", StringComparison.OrdinalIgnoreCase)
                && !property.Equals("Caption", StringComparison.OrdinalIgnoreCase)
                && !property.Equals("Tag", StringComparison.OrdinalIgnoreCase))
                throw Unimplemented("input from buddy property " + property);
            var value = Read(buddy, property);
            try { current = Long(value); }
            catch (VBRunTimeException) { current = 0; }
            current = Math.Clamp(current, Math.Min(min, max), Math.Max(min, max));
        }
        long next;
        if (wrap && current == (up ? max : min)) next = up ? min : max;
        else
        {
            var remainder = Math.Abs((long)current) % increment;
            var raises = up == (max >= min);
            next = raises ? (long)current + increment + (remainder == 0 ? 0 : increment - remainder)
                : (long)current - increment - remainder;
        }
        SetValue((int)Math.Clamp(next, Math.Min(min, max), Math.Max(min, max)));
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        DrawButton(context, Horizontal ? new Rect(0, 0, Bounds.Width / 2, Bounds.Height)
            : new Rect(0, 0, Bounds.Width, Bounds.Height / 2), !Horizontal);
        DrawButton(context, Horizontal ? new Rect(Bounds.Width / 2, 0, Bounds.Width / 2, Bounds.Height)
            : new Rect(0, Bounds.Height / 2, Bounds.Width, Bounds.Height / 2), Horizontal);
    }

    private void DrawButton(DrawingContext context, Rect rect, bool up)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        var down = pressedUp == up;
        var face = VBColor.FromSystemColor(VbSystemColor.Btnface).ToBrush();
        var shadow = VBColor.FromSystemColor(VbSystemColor.Btnshadow).ToBrush();
        var light = VBColor.FromSystemColor(VbSystemColor.Btnhighlight).ToBrush();
        context.FillRectangle(face, rect);
        context.DrawRectangle(new Pen(shadow), rect.Deflate(0.5));
        context.DrawLine(new Pen(down ? shadow : light), rect.TopLeft + new Vector(1, 1), rect.TopRight + new Vector(-1, 1));
        context.DrawLine(new Pen(down ? shadow : light), rect.TopLeft + new Vector(1, 1), rect.BottomLeft + new Vector(1, -1));
        var center = rect.Center + (down ? new Vector(1, 1) : default);
        var size = Horizontal && buddy is not null ? 0.5 : Math.Max(0.5, Math.Min(3, (Math.Min(rect.Width, rect.Height) - 5) / 2));
        var direction = up ? -1 : 1;
        if (Horizontal) direction = -direction;
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(center + (Horizontal ? new Vector(direction * size, 0) : new Vector(0, direction * size)), true);
            path.LineTo(center + (Horizontal ? new Vector(-direction * size, -size) : new Vector(-size, -direction * size)));
            path.LineTo(center + (Horizontal ? new Vector(-direction * size, size) : new Vector(size, -direction * size)));
            path.EndFigure(true);
        }
        context.DrawGeometry(IsEffectivelyEnabled ? Brushes.Black : shadow, IsEffectivelyEnabled ? null : new Pen(light), geometry);
    }

    public int Value => current;

    internal void RememberDesignBounds() =>
        original = (Canvas.GetLeft(this), Canvas.GetTop(this), Width, Height);

    internal void LoadProperties(List<(string Property, Vb6Value Value)> properties)
    {
        foreach (var (name, value) in properties)
            switch (name.ToLowerInvariant())
            {
                case "min": min = Long(value); break;
                case "max": max = Long(value); break;
                case "value": current = Long(value); break;
                case "increment": increment = Long(value); break;
                case "alignment": alignment = Long(value); break;
                case "wrap": wrap = Long(value) != 0; break;
                case "enabled": IsEnabled = Long(value) != 0; break;
                case "oledropmode": dropMode = Long(value); break;
                case "orientation":
                    if (Long(value) is not (0 or 1)) throw Unimplemented(name);
                    break;
                case "left" or "top" or "width" or "height" or "tabindex" or "visible":
                case "buddycontrol" or "buddydispid" or "buddyproperty" or "autobuddy" or "syncbuddy":
                case "origleft" or "origtop" or "origright" or "origbottom": break;
                default: throw Unimplemented(name);
            }
        // Missing Orig* lines restore the OCX's measured 30 by 30 twip rectangle.
        double Twips(string name, int fallback = 0) => properties.FirstOrDefault(p => p.Property == name).Value is { Value: not null } v
            ? Long(v) / 15.0 : fallback / 15.0;
        var left = Twips("OrigLeft");
        var top = Twips("OrigTop");
        if (properties.Any(p => p.Property == "BuddyControl"))
            original = (left, top, Twips("OrigRight", 30) - left, Twips("OrigBottom", 30) - top);
        else RememberDesignBounds();
    }

    internal void LoadBuddy(List<(string Property, Vb6Value Value)> properties, FormControls controls)
    {
        var name = properties.FirstOrDefault(p => p.Property == "BuddyControl").Value.Value as string;
        if (string.IsNullOrEmpty(name)) return;
        buddy = controls.All.FirstOrDefault(c => string.Equals(VBProps.GetName(c), name, StringComparison.OrdinalIgnoreCase));
        if (buddy is null) return;
        autoBuddy = properties.Any(p => p.Property == "AutoBuddy" && Long(p.Value) != 0);
        syncBuddy = properties.Any(p => p.Property == "SyncBuddy" && Long(p.Value) != 0);
        var dispatch = properties.FirstOrDefault(p => p.Property == "BuddyProperty").Value;
        if (dispatch.Type == Vb6Value.ValueType.String && dispatch.Value as string != "Tag")
            throw Unimplemented("BuddyProperty");
        buddyProperty = dispatch.Type == Vb6Value.ValueType.String || dispatch.Value is null ? "(Invalid)"
            : Long(dispatch) switch { 0 => null, 65540 => "Left", _ => throw Unimplemented("BuddyProperty") };
    }

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        value = name.ToLowerInvariant() switch
        {
            "value" => new Vb6Value((long)current), "min" => new Vb6Value((long)min), "max" => new Vb6Value((long)max),
            "increment" => new Vb6Value((long)increment), "wrap" => new Vb6Value(wrap),
            "alignment" => new Vb6Value((long)alignment), "orientation" => new Vb6Value(Horizontal ? 1L : 0L),
            "autobuddy" => new Vb6Value(autoBuddy), "syncbuddy" => new Vb6Value(syncBuddy),
            "buddycontrol" => new Vb6Value(buddy is null ? "" : VBProps.GetName(buddy) ?? ""),
            "buddyproperty" => new Vb6Value(""), "enabled" => new Vb6Value(IsEnabled),
            "oledropmode" => new Vb6Value((long)dropMode), "acceleration" => new Vb6Value(acceleration),
            "hwnd" or "oledrag" or "aboutbox" => throw Unimplemented(name),
            _ => default,
        };
        return name.ToLowerInvariant() is "value" or "min" or "max" or "increment" or "wrap" or "alignment" or "orientation"
            or "autobuddy" or "syncbuddy" or "buddycontrol" or "buddyproperty" or "enabled" or "oledropmode" or "acceleration";
    }

    public bool TrySetProperty(string name, Vb6Value value)
    {
        switch (name.ToLowerInvariant())
        {
            case "value": SetValue(Long(value)); break;
            case "min": min = Long(value); Clamp(); break;
            case "max": max = Long(value); Clamp(); break;
            case "increment":
                var step = Long(value);
                increment = step >= 0 ? step : throw Invalid();
                break;
            case "wrap": wrap = Boolean(value); break;
            case "enabled": IsEnabled = Boolean(value); break;
            case "orientation": throw new VBRunTimeException(new VBStandardError(382, "Property cannot be set at run time"));
            case "alignment":
                var side = Long(value);
                if (side is not (0 or 1)) throw Invalid();
                Detach();
                alignment = side;
                Attach();
                break;
            case "oledropmode":
                var mode = Long(value);
                dropMode = mode is 0 or 1 ? mode : throw Invalid();
                break;
            case "acceleration":
                Integral(value);
                acceleration = Convert.ToInt16(VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Integer, null).Value);
                break;
            case "buddycontrol": Bind(Resolve(value)); autoBuddy = false; break;
            case "buddyproperty":
                RequireBuddy();
                if (value.Type != Vb6Value.ValueType.String) throw Unimplemented("BuddyProperty conversion");
                var property = (string)value.Value!;
                if (property.Length == 0 || !CanWrite(buddy!, property)) throw Invalid();
                if (!property.Equals("Text", StringComparison.OrdinalIgnoreCase)
                    && !property.Equals("Caption", StringComparison.OrdinalIgnoreCase)
                    && !property.Equals("Tag", StringComparison.OrdinalIgnoreCase))
                    throw Unimplemented("BuddyProperty " + property);
                buddyProperty = property;
                syncBuddy = true;
                break;
            case "syncbuddy": RequireBuddy(); syncBuddy = Boolean(value); break;
            case "autobuddy":
                var automatic = Boolean(value);
                if (automatic)
                {
                    if (!FormControls.TryOf(this, out var form)) throw Unimplemented("AutoBuddy without a form");
                    var previous = form.All.FirstOrDefault(c => TabOrder.HasTabIndex(c) && TabOrder.Get(c) == TabOrder.Get(this) - 1);
                    if (previous is null) throw Unimplemented("AutoBuddy without a preceding control");
                    Bind(previous);
                }
                autoBuddy = automatic;
                break;
            case "hwnd": throw new VBRunTimeException(VBStandardError.WrongNumberOfArgumentsOrInvalidPropertyAssignment);
            case "oledrag" or "aboutbox": throw Unimplemented(name);
            default: return false;
        }
        return true;
    }

    private void SetValue(int number)
    {
        if (number < Math.Min(min, max) || number > Math.Max(min, max)) throw Invalid();
        current = number;
        if (syncBuddy && buddy is not null)
        {
            if (buddyProperty == "(Invalid)") throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
            var property = buddyProperty ?? DefaultProperty(buddy);
            Write(buddy, property, new Vb6Value((long)number));
            // Measured for UpDown's Caption synchronization, before its own Change (#1093).
            if (buddy is VBLabel && property.Equals("Caption", StringComparison.OrdinalIgnoreCase))
                buddy.ExecuteSub(UpDownComponentClass.ChangeEvent);
        }
        this.ExecuteSub(UpDownComponentClass.ChangeEvent);
    }

    private void Clamp()
    {
        var number = Math.Clamp(current, Math.Min(min, max), Math.Max(min, max));
        if (number != current)
        {
            current = number;
            this.ExecuteSub(UpDownComponentClass.ChangeEvent);
        }
    }

    private Control? Resolve(Vb6Value value)
    {
        if (value.Value is null && value.Type == Vb6Value.ValueType.Object) return null;
        if (!FormControls.TryOf(this, out var form)) throw Unimplemented("BuddyControl without a form");
        if (value.Value is Control control && form.All.Contains(control)) return control;
        if (value.Type == Vb6Value.ValueType.String)
        {
            var name = (string)value.Value!;
            if (name.Length == 0) return null;
            return form.All.FirstOrDefault(c => string.Equals(VBProps.GetName(c), name, StringComparison.OrdinalIgnoreCase)) ?? throw Invalid();
        }
        throw Invalid();
    }

    private void Bind(Control? control)
    {
        if (ReferenceEquals(buddy, control)) return;
        if (control is not null && control.Width < Width)
            throw Unimplemented("binding a buddy narrower than the UpDown");
        original ??= (Canvas.GetLeft(this), Canvas.GetTop(this), Width, Height);
        Detach();
        buddy = control;
        if (buddy is null)
        {
            var rect = original.Value;
            Canvas.SetLeft(this, rect.Left);
            Canvas.SetTop(this, rect.Top);
            Width = rect.Width;
            Height = rect.Height;
        }
        else Attach();
    }

    private void Detach()
    {
        if (buddy is null) return;
        buddy.Width += Width;
        if (alignment == 0) Canvas.SetLeft(buddy, Canvas.GetLeft(buddy) - Width);
    }

    private void Attach()
    {
        if (buddy is null) return;
        if (Horizontal) Width = 255.0 / 15;
        buddy.Width -= Width;
        Canvas.SetTop(this, Canvas.GetTop(buddy));
        Height = buddy.Height;
        if (alignment == 1)
            Canvas.SetLeft(this, Canvas.GetLeft(buddy) + buddy.Width + 1.0 / 15);
        else
        {
            Canvas.SetLeft(this, Canvas.GetLeft(buddy));
            Canvas.SetLeft(buddy, Canvas.GetLeft(buddy) + Width);
        }
    }

    private void RequireBuddy()
    {
        if (buddy is null) throw new VBRunTimeException(new VBStandardError(35754, "BuddyControl property must be set first"));
    }

    private static string DefaultProperty(Control control) => control switch
    {
        VBTextBox => "Text", VBLabel => "Caption", _ => throw Unimplemented("buddy default property"),
    };

    private static bool CanWrite(Control control, string name) =>
        VBProperties.PropertiesByName.TryGetValue(name, out var properties)
        && properties.Any(p => AvaloniaInteroperability.TryGet(control, p, out _));

    private static Vb6Value Read(Control control, string name)
    {
        foreach (var property in VBProperties.PropertiesByName.GetValueOrDefault(name, []))
            if (AvaloniaInteroperability.TryGet(control, property, out var value)) return value;
        throw Unimplemented("buddy property " + name);
    }

    private static void Write(Control control, string name, Vb6Value value)
    {
        foreach (var property in VBProperties.PropertiesByName.GetValueOrDefault(name, []))
            if (AvaloniaInteroperability.TrySet(control, property, value)) return;
        throw Unimplemented("buddy property " + name);
    }

    private static void Integral(Vb6Value value)
    {
        if (value.Type != Vb6Value.ValueType.Integer && value.Type != Vb6Value.ValueType.Long) throw Unimplemented("property conversion");
    }

    private static int Long(Vb6Value value)
    {
        if (value.Type != Vb6Value.ValueType.Integer && value.Type != Vb6Value.ValueType.Long && value.Type != Vb6Value.ValueType.Single
            && value.Type != Vb6Value.ValueType.Double && value.Type != Vb6Value.ValueType.String) throw Unimplemented("numeric conversion");
        return Convert.ToInt32(VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Long, null).Value);
    }

    private static bool Boolean(Vb6Value value)
    {
        if (value.Type != Vb6Value.ValueType.Boolean && value.Type != Vb6Value.ValueType.Integer && value.Type != Vb6Value.ValueType.Long)
            throw Unimplemented("Boolean conversion");
        return (bool)VB6BuiltIns.CBool(value).Value!;
    }

    private static VBRunTimeException Invalid() => new(VBStandardError.InvalidPropertyValue);
    private static NotImplementedException Unimplemented(string member) => new($"UpDown {member} is not implemented (#1093)");
}
