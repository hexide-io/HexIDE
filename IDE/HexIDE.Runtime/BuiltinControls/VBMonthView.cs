using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.LogicalTree;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;

namespace HexIDE.Runtime.BuiltinControls;

// Calendar drawing uses the measured structure; exact pixel metrics remain unmeasured.
public sealed class VBMonthView : Control, ICSharpPropertyBag, ICSharpProxy
{
    private readonly MonthViewState state = new();

    private TopLevel? inputRoot;
    private DateTime? pressedDate;
    private bool pressedButton;
    private bool secondClick;
    private bool pressedMultiSelect;

    static VBMonthView()
    {
        FocusableProperty.OverrideDefaultValue<VBMonthView>(true);
        AttachedEvents.AttachFocusEvents<VBMonthView>();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        inputRoot = TopLevel.GetTopLevel(this);
        // Avalonia focuses at the source during tunnelling; intercept earlier to put MouseDown first.
        inputRoot?.AddHandler(PointerPressedEvent, RootPressed, RoutingStrategies.Tunnel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        inputRoot?.RemoveHandler(PointerPressedEvent, RootPressed);
        inputRoot = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void RootPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, this)) return;
        try { Press(e); }
        catch (NotImplementedException error) when (this.FindLogicalAncestorOfType<VBFormRuntime>() is { } form)
        {
            e.Handled = true;
            form.Events.Raise("MonthView input", () => form.Context.DispatchInput(() => Task.FromException(error)));
        }
    }

    private void Press(PointerPressedEventArgs e)
    {
        var kind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        if (kind == PointerUpdateKind.RightButtonPressed)
        {
            AttachedEvents.RaiseMouse(this, e, ComponentBaseClass.MouseDownEvent);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }
        if (kind != PointerUpdateKind.LeftButtonPressed) return;
        var (date, button) = Target(e.GetPosition(this));
        if (date is null) return;
        var minimum = ((VbDate)Read("MinDate").Value!).Instant;
        var maximum = ((VbDate)Read("MaxDate").Value!).Instant;
        if (date < minimum || date > maximum || minimum.TimeOfDay != TimeSpan.Zero || maximum.TimeOfDay != TimeSpan.Zero
            || ((VbDate)Read("Value").Value!).Instant.TimeOfDay != TimeSpan.Zero)
            throw Pending("date input with timed values or outside date bounds");
        if ((bool)Read("MultiSelect").Value! && (button || e.KeyModifiers != KeyModifiers.None))
            throw Pending("MultiSelect input");
        var multiSelect = (bool)Read("MultiSelect").Value!;
        if (multiSelect && (date.Value.Year != state.Selection.Value.Instant.Year
            || date.Value.Month != state.Selection.Value.Instant.Month || e.ClickCount == 2))
            throw Pending("MultiSelect input");
        var previous = state.Selection;
        pressedMultiSelect = multiSelect;
        pressedDate = date;
        pressedButton = button;
        secondClick = e.ClickCount == 2 && !button;
        var doubleClick = secondClick;
        e.Pointer.Capture(this);
        e.Handled = true;
        Input(
            () =>
            {
                if (doubleClick) this.ExecuteSub(ComponentBaseClass.DblClickEvent);
                else AttachedEvents.RaiseMouse(this, e, ComponentBaseClass.MouseDownEvent);
            },
            () => { if (doubleClick) Raise(MonthViewComponentClass.DateDblClickEvent, new Vb6Value(VbDate.FromDateTime(date.Value))); },
            () => Focus(),
            () =>
            {
                if (!button && !multiSelect) return;
                SelectDate(date.Value);
                RaiseSelection(previous);
            },
            () => { if (button) RaiseDayBold(); });
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!pressedMultiSelect || pressedDate is not { } date || Target(e.GetPosition(this)).Date is not { } target || date == target)
            return;
        pressedDate = null;
        e.Pointer.Capture(null);
        e.Handled = true;
        Input(() => throw Pending("MultiSelect input"));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton == MouseButton.Right)
        {
            AttachedEvents.RaiseMouse(this, e, ComponentBaseClass.MouseUpEvent);
            this.ExecuteSub(ComponentBaseClass.ClickEvent);
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }
        if (e.InitialPressMouseButton != MouseButton.Left || pressedDate is not { } date) return;
        var button = pressedButton;
        var doubleClick = secondClick;
        var multiSelect = pressedMultiSelect;
        pressedDate = null;
        e.Pointer.Capture(null);
        e.Handled = true;
        var previous = state.Selection;
        Input(
            () => AttachedEvents.RaiseMouse(this, e, ComponentBaseClass.MouseUpEvent),
            () => { if (!doubleClick) this.ExecuteSub(ComponentBaseClass.ClickEvent); },
            () =>
            {
                if (button || multiSelect) return;
                var before = ((VbDate)Read("Value").Value!).Instant;
                var movedMonth = before.Year != date.Year || before.Month != date.Month;
                SelectDate(date);
                if (movedMonth) RaiseDayBold();
            },
            () => { if (!button && !multiSelect) RaiseSelection(previous); },
            () => { if (!button) Raise(MonthViewComponentClass.DateClickEvent, Read("SelStart")); });
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyCode(e.Key) is not { } code) return;
        e.Handled = true;
        Input(
            () => Raise(ComponentBaseClass.KeyDownEvent, new Vb6Value(code), new Vb6Value(Shift(e.KeyModifiers))),
            () =>
            {
                if (e.Key is Key.K or Key.Escape)
                {
                    if (e.KeyModifiers != KeyModifiers.None) throw Pending(ComponentBaseClass.KeyDownEvent.Name);
                    Raise(ComponentBaseClass.KeyPressEvent, new Vb6Value(e.Key == Key.K ? 107 : 27));
                    return;
                }
                if (e.Key == Key.Right && e.KeyModifiers == KeyModifiers.Shift && (bool)Read("MultiSelect").Value!) return;
                if (Convert.ToInt32(Read("MonthRows").Value) * Convert.ToInt32(Read("MonthColumns").Value) != 1
                    || (bool)Read("MultiSelect").Value!) throw Pending(ComponentBaseClass.KeyDownEvent.Name);
                var before = ((VbDate)Read("Value").Value!).Instant;
                if (before.TimeOfDay != TimeSpan.Zero) throw Pending(ComponentBaseClass.KeyDownEvent.Name);
                DateTime date;
                try
                {
                    date = (e.Key, e.KeyModifiers) switch
                    {
                        (Key.Right, KeyModifiers.None) => before.AddDays(1),
                        (Key.Down, KeyModifiers.None) => before.AddDays(7),
                        (Key.PageDown, KeyModifiers.None) => before.AddMonths(1),
                        (Key.PageUp, KeyModifiers.Control) => before.AddYears(-1),
                        (Key.Home, KeyModifiers.None) => new DateTime(before.Year, before.Month, 1),
                        (Key.End, KeyModifiers.None) => new DateTime(before.Year, before.Month, DateTime.DaysInMonth(before.Year, before.Month)),
                        _ => throw Pending(ComponentBaseClass.KeyDownEvent.Name),
                    };
                }
                catch (ArgumentOutOfRangeException) { throw Pending(ComponentBaseClass.KeyDownEvent.Name); }
                if (e.Key is Key.PageDown or Key.PageUp && date.Day != before.Day)
                    throw Pending(ComponentBaseClass.KeyDownEvent.Name);
                var minimum = ((VbDate)Read("MinDate").Value!).Instant;
                var maximum = ((VbDate)Read("MaxDate").Value!).Instant;
                if (minimum.TimeOfDay != TimeSpan.Zero || maximum.TimeOfDay != TimeSpan.Zero || date < minimum || date > maximum)
                    throw Pending(ComponentBaseClass.KeyDownEvent.Name);
                SelectDate(date);
                if (date.Year != before.Year || date.Month != before.Month) RaiseDayBold();
            },
            () =>
            {
                if (e.Key is Key.K or Key.Escape || e.KeyModifiers == KeyModifiers.Shift) return;
                RaiseSelection();
            });
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (KeyCode(e.Key) is not { } code) return;
        e.Handled = true;
        Input(() => Raise(ComponentBaseClass.KeyUpEvent, new Vb6Value(code), new Vb6Value(Shift(e.KeyModifiers))));
    }

    private static int Shift(KeyModifiers modifiers) =>
        (modifiers.HasFlag(KeyModifiers.Shift) ? 1 : 0) | (modifiers.HasFlag(KeyModifiers.Control) ? 2 : 0)
        | (modifiers.HasFlag(KeyModifiers.Alt) ? 4 : 0);

    private static int? KeyCode(Key key) => key switch
    {
        Key.Right => 39, Key.Down => 40, Key.PageDown => 34, Key.PageUp => 33,
        Key.Home => 36, Key.End => 35, Key.K => 75, Key.Escape => 27,
        _ => null,
    };

    private void SelectDate(DateTime date)
    {
        state.TrySetProperty("Value", new Vb6Value(VbDate.FromDateTime(date)));
        InvalidateVisual();
    }

    private void RaiseSelection((VbDate Value, VbDate Start, VbDate End)? previous = null)
    {
        foreach (var handler in ControlEvents.Handlers(this, this.FindLogicalAncestorOfType<IModuleExecutionRoot>(), MonthViewComponentClass.SelChangeEvent.Name))
        {
            Vb6Value[] args = [Read("SelStart"), Read("SelEnd"), new Vb6Value(0)];
            if (handler.Own && VBProps.GetIndex(this) is { } index) args = [new Vb6Value(index), .. args];
            handler.Root.ExecuteEventByRef(handler.Name, args, args.Length - 1, cancel =>
            {
                if (previous is { } selection && Convert.ToDouble(cancel.Value, CultureInfo.InvariantCulture) != 0)
                {
                    state.RestoreSelection(selection);
                    InvalidateVisual();
                }
            });
        }
    }

    private void RaiseDayBold()
    {
        var first = ((VbDate)state.VisibleDay(new Vb6Value(1)).Value!).Instant.Date;
        Raise(MonthViewComponentClass.GetDayBoldEvent, new Vb6Value(VbDate.FromDateTime(first)), new Vb6Value(42),
            new Vb6Value(new Vb6Value.ValueType(Vb6Value.ValueType.Boolean, true), [(0, 587)]));
    }

    private void Raise(EventClass eventClass, params Vb6Value[] args)
    {
        foreach (var handler in ControlEvents.Handlers(this, this.FindLogicalAncestorOfType<IModuleExecutionRoot>(), eventClass.Name))
            handler.Root.ExecuteSub(handler.Name, handler.Own && VBProps.GetIndex(this) is { } index
                ? [new Vb6Value(index), .. args] : args);
    }

    private void Input(params Action[] steps)
    {
        var pump = this.FindLogicalAncestorOfType<IModuleExecutionRoot>()?.Events;
        if (pump is null) { foreach (var step in steps) step(); return; }
        async Task Work()
        {
            foreach (var step in steps)
            {
                step();
                await pump.WaitForRaised();
            }
        }
        pump.Raise("MonthView input", () => this.FindLogicalAncestorOfType<VBFormRuntime>() is { } form
            ? form.Context.DispatchInput(Work) : Work());
    }

    private (DateTime? Date, bool Button) Target(Point point)
    {
        // Input is limited to the measured single-month layout in this slice.
        if (Convert.ToInt32(Read("MonthRows").Value) * Convert.ToInt32(Read("MonthColumns").Value) != 1)
            throw Pending("multiple-month input");
        var value = ((VbDate)Read("Value").Value!).Instant;
        var line = Bounds.Height / 8;
        if (line <= 0 || Bounds.Width <= 0 || !new Rect(Bounds.Size).Contains(point)) return (null, false);
        if (point.Y < line)
        {
            if (point.X >= line && point.X < Bounds.Width - line) return (null, false);
            var direction = point.X < line ? -1 : 1;
            if (value.TimeOfDay != TimeSpan.Zero || Convert.ToInt32(Read("ScrollRate").Value) != 0)
                throw Pending("timed or nondefault ScrollRate navigation");
            DateTime target;
            try { target = value.AddMonths(direction); }
            catch (ArgumentOutOfRangeException) { throw Pending("navigation at the calendar boundary"); }
            if (value.Day > DateTime.DaysInMonth(target.Year, target.Month))
                throw Pending("month navigation from a missing day");
            return (target.Date, true);
        }
        if (point.Y < 2 * line) return (null, false);
        var numbers = (bool)Read("ShowWeekNumbers").Value!;
        var cell = Bounds.Width / (numbers ? 8 : 7);
        var column = (int)(point.X / cell) - (numbers ? 1 : 0);
        if (column < 0 || column > 6) return (null, false);
        var first = new DateTime(value.Year, value.Month, 1);
        var start = first.AddDays(-((7 + (int)first.DayOfWeek - (Convert.ToInt32(Read("StartOfWeek").Value) - 1)) % 7));
        return (start.AddDays(((int)(point.Y / line) - 2) * 7 + column), false);
    }

    internal Vb6Value DefaultValue
    {
        get { state.TryGetProperty("Value", out var result); return result; }
    }

    public bool TryGetProperty(string name, out Vb6Value result)
    {
        RefusePending(name);
        return state.TryGetProperty(name, out result);
    }

    public bool TrySetProperty(string name, Vb6Value value)
    {
        RefusePending(name);
        var changed = state.TrySetProperty(name.StartsWith("VisibleDays(", StringComparison.OrdinalIgnoreCase) ? "VisibleDays" : name, value);
        if (changed) InvalidateVisual();
        return changed;
    }

    private Vb6Value Read(string name) { state.TryGetProperty(name, out var result); return result; }

    public override void Render(DrawingContext context)
    {
        var rows = Convert.ToInt32(Read("MonthRows").Value);
        var columns = Convert.ToInt32(Read("MonthColumns").Value);
        // Preserve the blank rendering for layouts whose first month is unmeasured.
        if (rows * columns is not (1 or 12)) return;
        var date = ((VbDate)Read("Value").Value!).Instant;
        if (date.Year == 9999 && (rows * columns == 12 || date.Month == 12)) return;
        var month = new DateTime(date.Year, rows * columns == 12 ? 1 : date.Month, 1);
        var start = ((VbDate)Read("SelStart").Value!).Instant.Date;
        var end = ((VbDate)Read("SelEnd").Value!).Instant.Date;
        var firstDay = Convert.ToInt32(Read("StartOfWeek").Value) - 1;
        var numbers = (bool)Read("ShowWeekNumbers").Value!;
        var width = Bounds.Width / columns;
        var height = Bounds.Height / rows;
        if (width <= 0 || height <= 0) return;
        var foreground = VBColor.FromSystemColor(VbSystemColor.Btntext).ToBrush();
        var grey = VBColor.FromSystemColor(VbSystemColor.Graytext).ToBrush();
        var face = VBColor.FromSystemColor(VbSystemColor.Btnface).ToBrush();
        var selection = VBColor.FromSystemColor(VbSystemColor.Highlight).ToBrush();
        context.FillRectangle(VBColor.FromSystemColor(VbSystemColor.Window).ToBrush(), new Rect(Bounds.Size));
        // Proportional layout is intentional: the oracle records no title or cell pixel metrics.
        void Text(string text, Rect cell, IBrush brush, bool bold = false)
        {
            var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, bold ? FontWeight.Bold : FontWeight.Normal),
                Math.Max(1, Math.Min(12, height / 12)), brush);
            context.DrawText(formatted, new Point(cell.Center.X - formatted.Width / 2, cell.Center.Y - formatted.Height / 2));
        }
        for (var m = 0; m < rows * columns; m++)
        {
            var current = month.AddMonths(m);
            var x = m % columns * width;
            var y = m / columns * height;
            var line = height / 8;
            var cellWidth = width / (numbers ? 8 : 7);
            context.FillRectangle(face, new Rect(x, y, width, line));
            if (rows * columns == 1)
            {
                Text("◀", new Rect(x, y, line, line), foreground);
                Text("▶", new Rect(x + width - line, y, line, line), foreground);
            }
            Text(current.ToString("Y", CultureInfo.CurrentCulture), new Rect(x, y, width, line), foreground, true);
            var grid = current.AddDays(-((7 + (int)current.DayOfWeek - firstDay) % 7));
            var inset = numbers ? cellWidth : 0;
            for (var day = 0; day < 7; day++)
                Text(CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)((firstDay + day) % 7)),
                    new Rect(x + inset + day * cellWidth, y + line, cellWidth, line), grey);
            context.DrawLine(new Pen(grey), new Point(x, y + 2 * line), new Point(x + width, y + 2 * line));
            for (var i = 0; i < 42; i++)
            {
                var shown = grid.AddDays(i);
                var cell = new Rect(x + inset + i % 7 * cellWidth, y + (2 + i / 7) * line, cellWidth, line);
                var selected = shown >= start && shown <= end;
                if (selected) context.FillRectangle(selection, cell);
                Text(shown.Day.ToString(CultureInfo.CurrentCulture), cell, selected
                    ? VBColor.FromSystemColor(VbSystemColor.Highlighttext).ToBrush()
                    : shown.Month == current.Month ? foreground : grey);
                if (numbers && i % 7 == 0)
                    Text(state.Week(shown).ToString(CultureInfo.CurrentCulture),
                        new Rect(x, cell.Y, cellWidth, line), grey);
            }
            if (numbers) context.DrawLine(new Pen(grey), new Point(x + inset, y + line), new Point(x + inset, y + height));
        }
    }

    internal Vb6Value VisibleDay(Vb6Value index) => state.VisibleDay(index);

    public void Call(string name, List<Vb6Value> arguments)
    {
        if (name.Equals("Refresh", StringComparison.OrdinalIgnoreCase) && arguments.Count == 0) { InvalidateVisual(); return; }
        throw Pending(name);
    }

    private static void RefusePending(string name)
    {
        // File autosizing and position snapping belong to C; the carried block's geometry is only a placeholder.
        if (name.ToLowerInvariant() is "width" or "height" or "left" or "top") throw Pending(name);
        if (MonthViewComponentClass.Instance.Member(name.Split('(')[0]) is { Implemented: false }) throw Pending(name);
    }

    internal static NotImplementedException Pending(string name) => new($"MonthView {name} is not implemented (#1095)");
}
