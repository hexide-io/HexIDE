using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

public sealed class VBSlider : Control
{
    internal long Min, Max = 10, Value, SmallChange = 1, LargeChange = 5, TickFrequency = 1;
    internal long TickStyle, Orientation, TextPosition, BorderStyle, OLEDropMode;
    internal long SelectionStart, SelectionLength;
    internal bool SelectRange;
    internal string Text = "";

    private bool pressed, dragging;
    private Point pointer;
    private int pageDirection;
    private object? restingTip;
    private PlacementMode restingPlacement;
    private double restingOffset;
    private readonly DispatcherTimer repeat;

    static VBSlider()
    {
        AttachedEvents.AttachMouse<VBSlider>();
        AttachedEvents.AttachClick<VBSlider>(AttachedEvents.ClickRelease.OverTheControl);
        PointerPressedEvent.AddClassHandler<VBSlider>((s, e) => s.Press(e));
        PointerReleasedEvent.AddClassHandler<VBSlider>((s, e) => s.Release(e));
        PointerMovedEvent.AddClassHandler<VBSlider>((s, e) => s.Move(e));
    }

    public VBSlider()
    {
        Focusable = true;
        repeat = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        repeat.Tick += (_, _) => Page();
        DetachedFromVisualTree += (_, _) => CancelGesture();
    }

    private void Scroll() => this.ExecuteSub(SliderComponentClass.ScrollEvent);
    private void Change() => this.ExecuteSub(SliderComponentClass.ChangeEvent);
    private bool Vertical => Orientation == 1;
    private double Length => Vertical ? Bounds.Height : Bounds.Width;
    private double Cross => Vertical ? Bounds.Width : Bounds.Height;
    private double Position(long value) => 10 + (Max > Min ? (value - Min) * (Length - 20) / (Max - Min) : 0);
    private double Along(Point p) => Vertical ? p.Y : p.X;
    private Point Point(double along, double across) => Vertical ? new Point(across, along) : new Point(along, across);
    private Rect Thumb => Vertical ? new Rect(Cross / 2 - 9, Position(Value) - 6, 18, 12)
        : new Rect(Position(Value) - 6, Cross / 2 - 9, 12, 18);

    private void Press(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        if (Thumb.Contains(e.GetPosition(this)) && Text.Length != 0 && (Vertical || TextPosition != 0))
            throw new NotImplementedException("Slider drag tip placement is not implemented (#1091): unmeasured.");
        pressed = true;
        pointer = e.GetPosition(this);
        dragging = Thumb.Contains(pointer);
        e.Pointer.Capture(this);
        if (dragging)
        {
            Scroll();
            restingTip = ToolTip.GetTip(this);
            restingPlacement = ToolTip.GetPlacement(this);
            restingOffset = ToolTip.GetHorizontalOffset(this);
            ToolTip.SetTip(this, Text);
            ToolTip.SetPlacement(this, PlacementMode.Top);
            ToolTip.SetHorizontalOffset(this, Position(Value) - Bounds.Width / 2);
            ToolTip.SetIsOpen(this, Text.Length != 0);
        }
        else
        {
            pageDirection = Math.Sign(Along(pointer) - Position(Value));
            Page();
            repeat.Start();
        }
        e.Handled = true;
    }

    private void Page()
    {
        var distance = Along(pointer) - Position(Value);
        if (Math.Abs(distance) <= 6 || Math.Sign(distance) != pageDirection) return;
        SetValue(Value + pageDirection * LargeChange, false);
        Scroll();
    }

    private void Move(PointerEventArgs e)
    {
        pointer = e.GetPosition(this);
        if (!pressed || !dragging || Length <= 20) return;
        // A move far outside the perpendicular bounds leaves the thumb where it was (#1091 oracle).
        var across = Vertical ? pointer.X : pointer.Y;
        if (across < 0 || across > Cross) return;
        var target = Math.Clamp(Min + (long)Math.Round((Along(pointer) - 10) * (Max - Min) / (Length - 20)), Min, Max);
        while (Value != target)
        {
            SetValue(Value + Math.Sign(target - Value), false);
            Scroll();
        }
        ToolTip.SetHorizontalOffset(this, Position(Value) - Bounds.Width / 2);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        CancelGesture();
    }

    private void Release(PointerReleasedEventArgs e)
    {
        if (!pressed || e.InitialPressMouseButton != MouseButton.Left) return;
        var wasDragging = dragging;
        CancelGesture();
        e.Pointer.Capture(null);
        if (wasDragging) Scroll();
        Change();
        e.Handled = true;
    }

    private void CancelGesture()
    {
        repeat.Stop();
        if (dragging)
        {
            ToolTip.SetIsOpen(this, false);
            ToolTip.SetTip(this, restingTip);
            ToolTip.SetPlacement(this, restingPlacement);
            ToolTip.SetHorizontalOffset(this, restingOffset);
        }
        pressed = dragging = false;
    }

    private static int KeyCode(Key key) => key switch
    {
        Key.Left => 37, Key.Up => 38, Key.Right => 39, Key.Down => 40,
        Key.Home => 36, Key.End => 35, Key.PageUp => 33, Key.PageDown => 34, _ => 0,
    };

    private void KeyEvent(KeyEventArgs e, EventClass kind)
    {
        var shift = (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 1 : 0)
            | (e.KeyModifiers.HasFlag(KeyModifiers.Control) ? 2 : 0) | (e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? 4 : 0);
        foreach (var handler in ControlEvents.Handlers(this, this.FindLogicalAncestorOfType<IModuleExecutionRoot>(), kind.Name))
        {
            var args = new System.Collections.Generic.List<Vb6Value>();
            if (handler.Own && VBProps.GetIndex(this) is { } index) args.Add(new Vb6Value(index));
            args.Add(new Vb6Value(KeyCode(e.Key)));
            args.Add(new Vb6Value(shift));
            handler.Root.ExecuteSub(handler.Name, args);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyCode(e.Key) == 0) return;
        KeyEvent(e, ComponentBaseClass.KeyDownEvent);
        SetValue(e.Key switch
        {
            Key.Home => Min, Key.End => Max,
            Key.Left or Key.Up => Value - SmallChange,
            Key.Right or Key.Down => Value + SmallChange,
            Key.PageUp => Value - LargeChange, _ => Value + LargeChange,
        }, false);
        if (e.Key is Key.Home or Key.End) Change(); else Scroll();
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (KeyCode(e.Key) == 0) return;
        KeyEvent(e, ComponentBaseClass.KeyUpEvent);
        Change();
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        SetValue(Value - (long)Math.Sign(e.Delta.Y) * SmallChange, false);
        Scroll();
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.LightGray, new Rect(Bounds.Size));
        var dark = new Pen(Brushes.Gray);
        var channelWidth = SelectRange ? 8 : 3;
        Rect Band(double from, double to) => Vertical
            ? new Rect(Cross / 2 - channelWidth / 2.0, from, channelWidth, Math.Max(0, to - from))
            : new Rect(from, Cross / 2 - channelWidth / 2.0, Math.Max(0, to - from), channelWidth);
        context.DrawRectangle(SelectRange ? Brushes.White : Brushes.Gray, dark, Band(10, Length - 10));
        if (SelectRange && SelectionStart >= Min && SelectionLength > 0)
            context.FillRectangle(Brushes.Blue, Band(Position(SelectionStart), Position(SelectionStart + SelectionLength)));
        void Triangle(Point a, Point b, Point c, IBrush brush)
        {
            var geometry = new StreamGeometry();
            using (var g = geometry.Open()) { g.BeginFigure(a, true); g.LineTo(b); g.LineTo(c); g.EndFigure(true); }
            context.DrawGeometry(brush, dark, geometry);
        }
        void Tick(long value)
        {
            foreach (var side in TickStyle == 2 ? new[] { -1, 1 } : new[] { TickStyle == 1 ? -1 : 1 })
            {
                var pos = Position(value);
                context.DrawLine(dark, Point(pos, Cross / 2 + side * 12), Point(pos, Cross / 2 + side * 16));
            }
        }
        if (TickStyle != 3)
        {
            Tick(Min);
            // Dense ranges need only one stroke per drawable position, rather than billions of overlapping ticks.
            if (TickFrequency > 0 && Max > Min)
            {
                var stride = Math.Max(1L, (long)Math.Ceiling(GetNumTicks / Math.Max(1, Length))) * TickFrequency;
                for (var tick = Min + stride; tick < Max; tick += stride) Tick(tick);
            }
            Tick(Max);
            if (SelectRange && SelectionStart >= Min && SelectionLength > 0)
                foreach (var end in new[] { SelectionStart, SelectionStart + SelectionLength })
                    foreach (var side in TickStyle == 2 ? new[] { -1, 1 } : new[] { TickStyle == 1 ? -1 : 1 })
                        Triangle(Point(Position(end), Cross / 2 + side * 10),
                            Point(Position(end) - 3, Cross / 2 + side * 15),
                            Point(Position(end) + 3, Cross / 2 + side * 15), Brushes.Blue);
        }
        context.DrawRectangle(Brushes.LightGray, dark, Thumb);
        if (TickStyle != 2)
        {
            var side = TickStyle == 1 ? -1 : 1;
            Triangle(Point(Position(Value) - 6, Cross / 2 + side * 5), Point(Position(Value) + 6, Cross / 2 + side * 5),
                Point(Position(Value), Cross / 2 + side * 10), Brushes.LightGray);
        }
        if (!IsEffectivelyEnabled)
            for (var x = Thumb.Left + 1; x < Thumb.Right; x += 2)
                for (var y = Thumb.Top + 1; y < Thumb.Bottom; y += 2)
                    context.FillRectangle(Brushes.Gray, new Rect(x, y, 1, 1));
        if (BorderStyle == 1) context.DrawRectangle(null, dark, new Rect(Bounds.Size).Deflate(0.5));
        if (IsFocused) context.DrawRectangle(null, new Pen(Brushes.Black, 1, DashStyle.Dot), new Rect(Bounds.Size).Deflate(2));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        InvalidateVisual();
    }

    internal long SelStart => SelectRange ? SelectionStart : Value;
    internal long SelLength => SelectRange ? SelectionLength : 0;
    internal long GetNumTicks => Max <= Min || TickFrequency == 0 ? 2 : (Max - Min - 1) / TickFrequency + 2;

    internal void SetValue(long value, bool change)
    {
        var clamped = System.Math.Max(Min, System.Math.Min(Max, value));
        if (Value == clamped)
            return;
        Value = clamped;
        InvalidateVisual();
        if (change)
            this.ExecuteSub(SliderComponentClass.ChangeEvent);
    }

    internal void SetRange(long number, bool minimum)
    {
        if (minimum ? number >= Max : number <= Min)
            throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
        if (minimum)
            Min = number;
        else
        {
            Max = number;
            if (SelectRange)
                SelectionLength = System.Math.Min(SelectionLength, Max - SelectionStart);
        }
        SetValue(Value, false);
    }
}
