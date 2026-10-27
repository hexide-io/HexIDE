using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

// The measured single top row; additional layouts remain separate work (#1089).
public sealed class VBTabStrip : Control
{
    internal TabStripModel Model { get; } = new();
    internal TabStripTabs Tabs { get; }
    internal Dictionary<string, Vb6Value> Values { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Style"] = new(0L), ["TabWidthStyle"] = new(0L), ["Placement"] = new(0L), ["TabStyle"] = new(0L),
        ["MousePointer"] = new(0L), ["OLEDropMode"] = new(0L), ["TabFixedWidth"] = new(0), ["TabFixedHeight"] = new(0),
        ["TabMinWidth"] = new(566.9291f), ["MultiRow"] = new(false), ["HotTracking"] = new(false), ["MultiSelect"] = new(false),
        ["Separators"] = new(false), ["ShowTips"] = new(true),
    };
    public VBTabStrip()
    {
        Tabs = new TabStripTabs(this);
        Focusable = true;
        Model.Changed += InvalidateVisual;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        // Keep failures on the form's event queue, never the render thread.
        if (DrawingRefusal is { } reason)
        {
            if (!drawingRefused && this.FindLogicalAncestorOfType<IFormView>() is { } form)
            {
                drawingRefused = true;
                form.Context.ExecuteControlInput($"{VBProps.GetName(this)}_Drawing",
                    () => Task.FromException(TabStripMembers.Refused(reason)));
            }
            return;
        }
        drawingRefused = false;
        using (context.PushClip(new Rect(Bounds.Size)))
        {
            var style = (long)Values["Style"].Value!;
            var face = VBColor.FromSystemColor(VbSystemColor.Btnface).ToBrush();
            var light = VBColor.FromSystemColor(VbSystemColor.Btnhighlight).ToBrush();
            var shadow = VBColor.FromSystemColor(VbSystemColor.Btnshadow).ToBrush();
            context.FillRectangle(face, new Rect(Bounds.Size));
            if (style == 0 && Bounds.Height > 22 && Bounds.Width > 4)
                Edge(new Rect(2, 20, Bounds.Width - 4, Bounds.Height - 22), false, true);
            for (var i = 0; i < Model.Tabs.Count; i++)
            {
                var tab = Model.Tabs[i];
                var rect = HeaderBounds(i);
                var selected = tab.Selected;
                if (style == 0 && selected)
                    rect = rect.Inflate(new Thickness(1, 2, 1, 0));
                if (style != 2 || selected)
                    Edge(rect, style != 0 && selected, style != 0);
                if (style == 2 && (bool)Values["Separators"].Value! && i > 0)
                    context.DrawLine(new Pen(shadow), new Point(rect.Left - 5, rect.Top + 2),
                        new Point(rect.Left - 5, rect.Bottom - 2));
                var caption = VBCaption.Parse(tab.Caption);
                var text = CaptionText(caption.Shown);
                if (caption.AccessKey is { } key)
                    text.SetTextDecorations(TextDecorations.Underline, key, 1);
                context.DrawText(text, new Point(rect.Left + (rect.Width - text.Width) / 2,
                    rect.Top + (rect.Height - text.Height) / 2));
            }

            void Edge(Rect rect, bool pressed, bool bottom)
            {
                context.FillRectangle(face, rect);
                var upper = new Pen(pressed ? shadow : light);
                var lower = new Pen(pressed ? light : shadow);
                context.DrawLine(upper, rect.TopLeft, rect.TopRight);
                context.DrawLine(upper, rect.TopLeft, rect.BottomLeft);
                context.DrawLine(lower, rect.TopRight, rect.BottomRight);
                if (bottom)
                    context.DrawLine(lower, rect.BottomLeft, rect.BottomRight);
            }
        }
    }

    private bool drawingRefused;
    private string? DrawingRefusal => (long)Values["TabStyle"].Value! != 0 ? "TabStyle drawing"
        : (long)Values["Placement"].Value! != 0 || (bool)Values["MultiRow"].Value!
            || (long)Values["TabWidthStyle"].Value! != 0 || (int)Values["TabFixedWidth"].Value! != 0
            || (int)Values["TabFixedHeight"].Value! != 0 ? "drawing layout"
        : Model.Tabs.Count > 0 && HeaderBounds(Model.Tabs.Count - 1).Right > Bounds.Width ? "overflow drawing" : null;

    private static FormattedText CaptionText(string caption) => new(caption,
        System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
        Typeface.Default, 11, Brushes.Black);

    // Drawing and hit testing share the row; dimensions beyond the oracle are not fidelity claims.
    private Rect HeaderBounds(int index)
    {
        var style = (long)Values["Style"].Value!;
        double left = style == 0 ? 2 : 0;
        for (var i = 0; i <= index; i++)
        {
            var text = CaptionText(VBCaption.Parse(Model.Tabs[i].Caption).Shown);
            var width = Math.Max(38, Math.Ceiling(text.Width) + 12);
            if (i == index)
                return new Rect(left, style == 0 ? 2 : 0, width, style == 0 ? 18 : 21);
            left += width + (style == 0 ? 0 : style == 1 ? 3 : 10);
        }
        return default;
    }

    private TabStripModel.Tab? pressedTab;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Handled = true;
        pressedTab = null;
        var point = e.GetCurrentPoint(this);
        if (point.Properties.PointerUpdateKind is not (PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.RightButtonPressed))
            return;
        if ((long)Values["Placement"].Value! != 0 || (bool)Values["MultiRow"].Value!
            || (long)Values["TabWidthStyle"].Value! != 0 || (int)Values["TabFixedWidth"].Value! != 0
            || (int)Values["TabFixedHeight"].Value! != 0)
        {
            if (this.FindLogicalAncestorOfType<IFormView>() is { } form)
                form.Context.ExecuteControlInput($"{VBProps.GetName(this)}_MouseInput",
                    () => Task.FromException(TabStripMembers.Refused("mouse header layout")));
            return;
        }
        for (var i = 0; i < Model.Tabs.Count; i++)
            if (HeaderBounds(i).Contains(point.Position))
            {
                pressedTab = Model.Tabs[i];
                Focus();
                e.Pointer.Capture(this);
                DispatchMouse(e, pressedTab, true, e.ClickCount % 2 == 0,
                    point.Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed);
                break;
            }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Handled = true;
        if (pressedTab is { } tab)
            DispatchMouse(e, tab, false, false, e.InitialPressMouseButton == MouseButton.Right);
        pressedTab = null;
        e.Pointer.Capture(null);
    }

    private void DispatchMouse(PointerEventArgs e, TabStripModel.Tab tab, bool press, bool second, bool right)
    {
        if (this.FindLogicalAncestorOfType<IFormView>() is not { } form)
            return;
        var point = e.GetPosition(this);
        var shift = (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 1 : 0)
            | (e.KeyModifiers.HasFlag(KeyModifiers.Control) ? 2 : 0) | (e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? 4 : 0);
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        CallArg[] args = [new(new Vb6Value(right ? 2 : 1), null), new(new Vb6Value(shift), null),
            new(new Vb6Value(ControlPositions.MouseCoordinate(point.X, VBScaleMode.Twip, false)), null),
            new(new Vb6Value(ControlPositions.MouseCoordinate(point.Y, VBScaleMode.Twip, true)), null)];
        form.Context.ExecuteControlInput($"{VBProps.GetName(this)}_MouseInput",
            () => TabStripMembers.Mouse(this, tab, form.Context, press, second, right, control, args));
    }

    private TopLevel? keyRoot;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        keyRoot = TopLevel.GetTopLevel(this);
        keyRoot?.AddHandler(KeyDownEvent, OnMnemonic, RoutingStrategies.Tunnel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        keyRoot?.RemoveHandler(KeyDownEvent, OnMnemonic);
        keyRoot = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnMnemonic(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.KeyModifiers != KeyModifiers.Alt || !IsEffectivelyEnabled || !IsEffectivelyVisible)
            return;
        TabStripModel.Tab? match = null;
        foreach (var tab in Model.Tabs)
        {
            var caption = VBCaption.Parse(tab.Caption);
            if (caption.AccessKey != VBCaption.Parse(tab.Caption, VBCaption.KeyChoice.First).AccessKey
                || caption.AccessKey is not { } at || !string.Equals(caption.Shown[at].ToString(),
                    e.KeySymbol, StringComparison.OrdinalIgnoreCase))
                continue;
            // Duplicate mnemonics have not been measured for this control.
            if (match is not null)
                return;
            match = tab;
        }
        if (match is not null)
        {
            Focus();
            SelectFromKeyboard(match);
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.KeyModifiers != KeyModifiers.None || Model.SelectedItem is not { } selected)
            return;
        var index = e.Key switch
        {
            Key.Left => selected.Index - 1,
            Key.Right => selected.Index + 1,
            Key.Home => 1,
            Key.End => Model.Tabs.Count,
            _ => 0,
        };
        // End-point and no-selection navigation have not been measured.
        if (index < 1 || index > Model.Tabs.Count || index == selected.Index)
            return;
        SelectFromKeyboard(Model.Tabs[index - 1], arrowKey: e.Key is Key.Left or Key.Right);
        e.Handled = true;
    }

    private void SelectFromKeyboard(TabStripModel.Tab tab, bool arrowKey = false)
    {
        if (this.FindLogicalAncestorOfType<IFormView>() is { } form)
            form.Context.ExecuteControlInput($"{VBProps.GetName(this)}_KeyboardSelection",
                () => TabStripMembers.SelectFromKeyboard(this, tab, form.Context, arrowKey));
    }
}
