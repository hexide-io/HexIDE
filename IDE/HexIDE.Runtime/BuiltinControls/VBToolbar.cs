using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

// Measured in #1088, issuecomment-6428845889; visual sizing uses the host theme.
public sealed class VBToolbar : UserControl, ICSharpPropertyBag
{
    protected override Type StyleKeyOverride => typeof(UserControl);

    private readonly StackPanel strip = new() { Orientation = Orientation.Horizontal };

    public VBToolbar()
    {
        Content = strip;
        Background = Brushes.Transparent;
        AddHandler(PointerPressedEvent, Press, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, Release, RoutingStrategies.Tunnel);
        Styles.Add(new Style(x => x.Is<Button>().Class("flat").Not(y => y.Class(":pointerover")).Not(y => y.Class(":checked")).Not(y => y.Class(":pressed")))
        {
            Setters = { new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent) }
        });
    }

    private readonly Dictionary<Control, ToolbarItem> targets = [];
    private bool suppressRelease;

    private (ToolbarItem? Item, bool Arrow) Target(PointerEventArgs e)
    {
        for (var visual = e.Source as Visual; visual is not null && visual != this; visual = visual.GetVisualParent())
            if (visual is Control control && targets.TryGetValue(control, out var target))
            {
                target.TryGetProperty("Style", out var kind);
                // VB6's dropdown arrow strip is 13 px wide and spans the button's height (#2328).
                var arrow = Convert.ToInt64(kind.Value) == 5 &&
                    e.GetPosition(control).X >= control.Bounds.Width - 13;
                return (target, arrow);
            }
        return (null, false);
    }

    private void ItemEvent(EventClass kind, ToolbarItem item, Func<bool>? when = null)
    {
        var handlers = ControlEvents.Handlers(this, this.FindLogicalAncestorOfType<IModuleExecutionRoot>(), kind.Name)
            .Where(h => h.Root.Handles(h.Name)).ToList();
        bool? ready = null;
        bool Once() => ready ??= when?.Invoke() ?? true;
        if (handlers.Count == 0) { Once(); return; }
        foreach (var handler in handlers)
            handler.Root.ExecuteSubIf(handler.Name, [new Vb6Value(item)], Once);
    }

    private void Activate(ToolbarItem? item)
    {
        if (item is null) return;
        // Apply the value at the event's turn, after the preceding Click handler.
        ItemEvent(ToolbarComponentClass.ButtonClickEvent, item, () =>
        {
            item.TryGetProperty("Enabled", out var enabled);
            item.TryGetProperty("Style", out var kind);
            item.TryGetProperty("Value", out var value);
            var style = Convert.ToInt64(kind.Value);
            if (!(bool)enabled.Value! || style is 3 or 4 || style == 2 && Convert.ToInt64(value.Value) == 1) return false;
            if (style is 1 or 2) item.TrySetProperty("Value", new Vb6Value(style == 2 || Convert.ToInt64(value.Value) == 0 ? 1L : 0L));
            return true;
        });
    }

    private void Press(object? sender, PointerPressedEventArgs e)
    {
        var (item, arrow) = Target(e);
        var left = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
        suppressRelease = left && (arrow || e.ClickCount % 2 == 0);
        e.Handled = suppressRelease;
        if (left && e.ClickCount % 2 == 0) { Activate(item); return; }
        AttachedEvents.RaiseMouse(this, e, ComponentBaseClass.MouseDownEvent);
        if (!left || !arrow || item is null) return;
        ItemEvent(ToolbarComponentClass.ButtonDropDownEvent, item);
        item.TryGetProperty("ButtonMenus", out var menus);
        var flyout = new MenuFlyout();
        foreach (var menu in ((ToolbarItems)menus.Value!).Items)
        {
            if (!menu.IsVisible) continue;
            menu.TryGetProperty("Text", out var text);
            menu.TryGetProperty("Enabled", out var enabled);
            var entry = new MenuItem { Header = text.Value, IsEnabled = (bool)enabled.Value! };
            entry.Click += (_, _) => ItemEvent(ToolbarComponentClass.ButtonMenuClickEvent, menu);
            flyout.Items.Add(entry);
        }
        var anchor = targets.First(t => t.Value == item).Key;
        FlyoutBase.SetAttachedFlyout(anchor, flyout);
        flyout.ShowAt(anchor);
    }

    private void Release(object? sender, PointerReleasedEventArgs e)
    {
        e.Handled = suppressRelease;
        if (suppressRelease) { suppressRelease = false; return; }
        AttachedEvents.RaiseMouse(this, e, ComponentBaseClass.MouseUpEvent);
        this.ExecuteSub(ComponentBaseClass.ClickEvent);
        if (e.InitialPressMouseButton == MouseButton.Left) Activate(Target(e).Item);
        RefreshButtons();
    }

    internal void RefreshButtons()
    {
        strip.Children.Clear();
        targets.Clear();
        foreach (var item in Buttons.Items)
        {
            if (!item.IsVisible) continue;
            item.TryGetProperty("Style", out var kind);
            item.TryGetProperty("Caption", out var caption);
            item.TryGetProperty("Value", out var value);
            item.TryGetProperty("Enabled", out var enabled);
            item.TryGetProperty("ToolTipText", out var tip);
            Control visual;
            switch (Convert.ToInt64(kind.Value))
            {
                case 3:
                    visual = new Border { Width = 1, Background = Brushes.Gray, Margin = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Stretch };
                    break;
                case 4:
                    visual = new Border { Width = Math.Max(0, item.LayoutWidth / 15) };
                    break;
                case 1: case 2:
                    visual = new ToggleButton { Content = caption.Value, IsChecked = Convert.ToInt64(value.Value) == 1 };
                    break;
                case 5:
                    var face = new StackPanel { Orientation = Orientation.Horizontal };
                    face.Children.Add(new TextBlock { Text = (string)caption.Value! });
                    var arrow = new TextBlock { Text = "▾" };
                    face.Children.Add(arrow);
                    visual = Convert.ToInt64(value.Value) == 1
                        ? new ToggleButton { Content = face, IsChecked = true }
                        : new Button { Content = face };
                    break;
                default:
                    visual = Convert.ToInt64(value.Value) == 1
                        ? new ToggleButton { Content = caption.Value, IsChecked = true }
                        : new Button { Content = caption.Value };
                    break;
            }
            visual.IsEnabled = (bool)enabled.Value!;
            if (style == 1) visual.Classes.Add("flat");
            if (showTips && (string)tip.Value! != "") ToolTip.SetTip(visual, tip.Value);
            targets[visual] = item;
            strip.Children.Add(visual);
        }
    }

    private long appearance = 1, borderStyle, style, textAlignment, helpContextId, mousePointer, oleDropMode;
    private bool allowCustomize = true, showTips = true, wrappable = true;
    private string helpFile = "";
    private ToolbarItems? buttons;
    public ToolbarItems Buttons => buttons ??= new(this);
    internal bool HasHadButtons;
    internal int LoadedAlign = 1;

    internal bool TextBelow => textAlignment == 0;
    internal float ButtonWidth
    {
        get
        {
            if (!HasHadButtons) return 345.2604f;
            // Alpha is the measured caption; no general caption-sizing rule was measured.
            if (TextBelow && Buttons.Items.Any(i => i.IsMeasuredCaption) && Buttons.Items.All(i => i.HasMeasuredWidth))
                return 510.2362f;
            throw ToolbarItems.Unmeasured();
        }
    }
    internal float ButtonHeight
    {
        get
        {
            if (!TextBelow) return 329.9528f;
            if (!HasHadButtons) return 569.7643f;
            _ = ButtonWidth;
            return 540.2835f;
        }
    }

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        value = name.ToLowerInvariant() switch
        {
            "buttons" => new(Buttons),
            "appearance" => new(appearance),
            "borderstyle" => new(borderStyle),
            "style" => new(style),
            "textalignment" => new(textAlignment),
            "allowcustomize" => new(allowCustomize),
            "showtips" => new(showTips),
            "wrappable" => new(wrappable),
            "enabled" => new(IsEnabled),
            "buttonwidth" => new(ButtonWidth),
            "buttonheight" => new(ButtonHeight),
            "height" => new(HasHadButtons ? 630f : 660f),
            "align" => new(LoadedAlign),
            "helpcontextid" => new(helpContextId),
            "helpfile" => new(helpFile),
            "mousepointer" => new(mousePointer),
            "oledropmode" => new(oleDropMode),
            _ => default,
        };
        if (value != default)
            return true;
        RefusePending(name);
        if (name.Equals("Font", StringComparison.OrdinalIgnoreCase) || name.Equals("TabStop", StringComparison.OrdinalIgnoreCase))
            throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
        return false;
    }

    public bool TrySetProperty(string name, Vb6Value value)
    {
        var changed = SetProperty(name, value);
        if (changed) RefreshButtons();
        return changed;
    }

    private bool SetProperty(string name, Vb6Value value)
    {
        switch (name.ToLowerInvariant())
        {
            case "buttons": throw ToolbarItems.Error(383);
            case "style": style = Choice(value, 1); return true;
            case "textalignment": textAlignment = Choice(value, 1); return true;
            case "appearance": appearance = MeasuredChoice(value, 1); return true;
            case "borderstyle": borderStyle = MeasuredChoice(value, 0, 1); return true;
            case "helpcontextid": helpContextId = Long(value); return true;
            case "helpfile":
                if (value.Type != Vb6Value.ValueType.String)
                    throw UnmeasuredConversion();
                helpFile = (string)value.Value!;
                return true;
            case "mousepointer": mousePointer = MeasuredChoice(value, 0); return true;
            case "oledropmode": oleDropMode = MeasuredChoice(value, 0); return true;
            case "allowcustomize": allowCustomize = Boolean(value); return true;
            case "showtips": showTips = Boolean(value); return true;
            case "wrappable": wrappable = Boolean(value); return true;
            case "enabled": IsEnabled = Boolean(value); return true;
            // VB6 accepts these assignments without changing the size, even negative ButtonWidth (measured).
            case "buttonwidth": case "buttonheight": case "height":
                _ = Long(value);
                return true;
            case "align": throw new NotImplementedException("Setting the Toolbar's Align is not implemented (#1088)");
        }
        RefusePending(name);
        if (name.Equals("Font", StringComparison.OrdinalIgnoreCase) || name.Equals("TabStop", StringComparison.OrdinalIgnoreCase))
            throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
        return false;
    }

    private static long Long(Vb6Value value) =>
        value.Type == Vb6Value.ValueType.Integer || value.Type == Vb6Value.ValueType.Long
            ? Convert.ToInt64(value.Value) : throw UnmeasuredConversion();

    private static bool Boolean(Vb6Value value) =>
        value.Type == Vb6Value.ValueType.Boolean ? (bool)value.Value! : throw UnmeasuredConversion();

    private static NotImplementedException UnmeasuredConversion() =>
        new("Toolbar property conversions or enum values beyond the measurements are not implemented (#1088)");

    private static long MeasuredChoice(Vb6Value value, params long[] measured)
    {
        var number = Long(value);
        return measured.Contains(number) ? number : throw UnmeasuredConversion();
    }

    private static long Choice(Vb6Value value, int maximum)
    {
        var number = Long(value);
        return number >= 0 && number <= maximum ? number : throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
    }

    internal static void RefusePending(string name)
    {
        if (ToolbarComponentClass.Instance.Member(name) is { Implemented: false })
            throw new NotImplementedException($"The Toolbar's {name} is not implemented (#1088)");
    }
}
