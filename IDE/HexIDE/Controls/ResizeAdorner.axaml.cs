using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.VisualTree;
using HexIDE.VisualDesigner;

namespace HexIDE.Controls;

public class ResizeAdorner : TemplatedControl
{
    public static readonly StyledProperty<bool> IsPrimaryProperty =
        AvaloniaProperty.Register<ResizeAdorner, bool>(nameof(IsPrimary), defaultValue: true);

    public bool IsPrimary
    {
        get => GetValue(IsPrimaryProperty);
        set => SetValue(IsPrimaryProperty, value);
    }

    static ResizeAdorner()
    {
        IsPrimaryProperty.Changed.AddClassHandler<ResizeAdorner>((adorner, _) =>
            adorner.PseudoClasses.Set(":secondary", !adorner.IsPrimary));
    }

    public static readonly AttachedProperty<ResizeXDirection> ResizeXDirectionProperty = AvaloniaProperty.RegisterAttached<ResizeAdorner, Control, ResizeXDirection>("ResizeXDirection");

    public static ResizeXDirection GetResizeXDirection(AvaloniaObject element) => element.GetValue(ResizeXDirectionProperty);
    public static void SetResizeXDirection(AvaloniaObject element, ResizeXDirection value) => element.SetValue(ResizeXDirectionProperty, value);

    public static readonly AttachedProperty<ResizeYDirection> ResizeYDirectionProperty = AvaloniaProperty.RegisterAttached<ResizeAdorner, Control, ResizeYDirection>("ResizeYDirection");
    public static ResizeYDirection GetResizeYDirection(AvaloniaObject element) => element.GetValue(ResizeYDirectionProperty);
    public static void SetResizeYDirection(AvaloniaObject element, ResizeYDirection value) => element.SetValue(ResizeYDirectionProperty, value);

    public static readonly StyledProperty<ResizeAdornerDirections> AllowedDirectionProperty =
        AvaloniaProperty.Register<ResizeAdorner, ResizeAdornerDirections>(nameof(AllowedDirection), ResizeAdornerDirections.All);

    public ResizeAdornerDirections AllowedDirection
    {
        get => GetValue(AllowedDirectionProperty);
        set => SetValue(AllowedDirectionProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        var topLeft = e.NameScope.Get<Control>("PART_TopLeft");
        var top = e.NameScope.Get<Control>("PART_Top");
        var topRight = e.NameScope.Get<Control>("PART_TopRight");
        var left = e.NameScope.Get<Control>("PART_Left");
        var right = e.NameScope.Get<Control>("PART_Right");
        var bottom = e.NameScope.Get<Control>("PART_Bottom");
        var bottomLeft = e.NameScope.Get<Control>("PART_BottomLeft");
        var bottomRight = e.NameScope.Get<Control>("PART_BottomRight");
        var moveGrip = e.NameScope.Find<Control>("PART_MoveGrip");

        var all = new Control[]{topLeft, top, topRight, left, right, bottom, bottomLeft, bottomRight};

        foreach (var control in all)
        {
            control.AddHandler(PointerPressedEvent, OnHandlePointerPressed);
            control.AddHandler(PointerMovedEvent, OnHandlePointerMoved);
            control.AddHandler(PointerReleasedEvent, OnHandlePointerReleased);
        }

        if (moveGrip != null)
        {
            moveGrip.AddHandler(PointerPressedEvent, OnHandlePointerPressed);
            moveGrip.AddHandler(PointerMovedEvent, OnHandlePointerMoved);
            moveGrip.AddHandler(PointerReleasedEvent, OnHandlePointerReleased);
        }
    }

    // Set by ControlsContainer to all other currently-selected ControlItems; drives group drag.
    public IReadOnlyList<ControlItem>? GroupDragParticipants { get; set; }

    public Action? OnDragStarted { get; set; }
    public Action? OnDragCompleted { get; set; }

    private bool isResizing = false;
    private Point initialPosition;
    private Rect originalBounds;
    private Point originalOrigin;
    private ResizeXDirection xDirection = ResizeXDirection.None;
    private ResizeYDirection yDirection = ResizeYDirection.None;
    private Visual? adornedElement;
    private Control? adornedElementChild;

    // Captured at drag start so position changes during drag don't affect the reference.
    private IReadOnlyList<ControlItem>? _groupDragParticipants;
    private Point[]? _participantOriginalPositions;

    private void OnHandlePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (isResizing)
            OnDragCompleted?.Invoke();
        isResizing = false;
        adornedElement = null;
        adornedElementChild = null;
        _groupDragParticipants = null;
        _participantOriginalPositions = null;
    }

    private void OnHandlePointerMoved(object? sender, PointerEventArgs e)
    {
        if (isResizing && adornedElement != null)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                if (isResizing)
                    OnDragCompleted?.Invoke();
                isResizing = false;
                adornedElement = null;
                return;
            }
            var position = e.GetPosition(this.GetVisualParent());
            var diff = position - initialPosition;

            double newWidth = originalBounds.Width, newHeight = originalBounds.Height, newTop = originalOrigin.Y, newLeft = originalOrigin.X;

            // Everything below happens in the CONTAINER's space, not the canvas's. VB6 snaps to the grid it
            // draws inside the container, and container origins are not grid multiples — a child at 2100 twips
            // inside a container parked at -20000 twips snaps absolutely to 2120, which is off its own
            // container's 120-twip grid, so repeated drags never settle.
            var container = ContainerBoundsOfAdorned();

            double SnapToGrid(double x) => SnapGridUtils.SnapToGrid(this, x);

            double SnapX(double x) => SnapToGrid(x - container.X) + container.X;
            double SnapY(double y) => SnapToGrid(y - container.Y) + container.Y;

            // Clamp against the container's client edge, not canvas zero. Two bugs in one: pinning to absolute
            // 0 puts the control on the FORM's left edge — storing a relative Left of +20000 twips for a
            // container at -20000 — and where the container's origin is negative the old bounds were min > max,
            // which makes Math.Clamp THROW. A left- or top-edge resize of anything inside an off-screen
            // container crashed the designer; the four picOptions in Options Dialog.frm are exactly that.
            static double ClampTo(double value, double min, double max) => Math.Clamp(value, min, Math.Max(min, max));

            if (xDirection == ResizeXDirection.Right)
            {
                newWidth = SnapToGrid(originalBounds.Width + diff.X);
            }
            else if (xDirection == ResizeXDirection.Left)
            {
                var originalRight = originalOrigin.X + originalBounds.Width;
                newLeft = ClampTo(SnapX(originalOrigin.X + diff.X), container.X, originalRight);
                newWidth = originalRight - newLeft;
            }


            if (yDirection == ResizeYDirection.Bottom)
            {
                newHeight = SnapToGrid(originalBounds.Height + diff.Y);
            }
            else if (yDirection == ResizeYDirection.Top)
            {
                var originalBottom = originalOrigin.Y + originalBounds.Height;
                newTop = ClampTo(SnapY(originalOrigin.Y + diff.Y), container.Y, originalBottom);
                newHeight = originalBottom - newTop;
            }

            if (xDirection == ResizeXDirection.None && yDirection == ResizeYDirection.None)
            {
                newLeft = SnapX(originalOrigin.X + diff.X);
                newTop = SnapY(originalOrigin.Y + diff.Y);
            }

            if (adornedElementChild != null)
            {
                if (adornedElementChild.MinHeight != 0)
                    newHeight = Math.Max(adornedElementChild.MinHeight, newHeight);
                if (adornedElementChild.MaxHeight != 0)
                    newHeight = Math.Min(adornedElementChild.MaxHeight, newHeight);
                if (adornedElementChild.MinWidth != 0)
                    newWidth = Math.Max(adornedElementChild.MinWidth, newWidth);
                if (adornedElementChild.MaxWidth != 0)
                    newWidth = Math.Min(adornedElementChild.MaxWidth, newWidth);
            }

            adornedElement.SetCurrentValue(Canvas.TopProperty, newTop);
            adornedElement.SetCurrentValue(HeightProperty, Math.Max(1, newHeight));
            adornedElement.SetCurrentValue(Canvas.LeftProperty, newLeft);
            adornedElement.SetCurrentValue(WidthProperty, Math.Max(1, newWidth));

            // Move all group drag participants by the same snapped delta as the primary.
            if (xDirection == ResizeXDirection.None && yDirection == ResizeYDirection.None &&
                _groupDragParticipants != null && _participantOriginalPositions != null)
            {
                double deltaX = newLeft - originalOrigin.X;
                double deltaY = newTop - originalOrigin.Y;
                for (int i = 0; i < _groupDragParticipants.Count; i++)
                {
                    _groupDragParticipants[i].SetCurrentValue(Canvas.LeftProperty, _participantOriginalPositions[i].X + deltaX);
                    _groupDragParticipants[i].SetCurrentValue(Canvas.TopProperty, _participantOriginalPositions[i].Y + deltaY);
                }
            }
        }
    }

    /// <summary>
    /// The client rectangle of whatever contains the control being dragged, in canvas space — read from the
    /// view-model, which is the one place the accumulated origin is computed.
    ///
    /// The fallback is the old behaviour: origin zero and no far edge, for a drag on something that is not a
    /// designed component.
    /// </summary>
    private Rect ContainerBoundsOfAdorned()
    {
        if (AdornerLayer.GetAdornedElement(this) is ControlItem item &&
            item.DataContext is ComponentInstanceViewModel vm)
            return vm.ContainerBounds;
        return new Rect(0, 0, double.MaxValue, double.MaxValue);
    }

    private bool IsAdornedElementLocked()
    {
        var ae = AdornerLayer.GetAdornedElement(this);
        return ae is ControlItem ci &&
               (ci.DataContext as ComponentInstanceViewModel)?.Owner.FormDefinition?.LockControls == true;
    }

    private void OnHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsAdornedElementLocked())
            return;

        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
        {
            adornedElement = AdornerLayer.GetAdornedElement(this);
            adornedElementChild = null;
            if (adornedElement != null && e.Source is Control control)
            {
                if (adornedElement is ControlItem controlItem)
                    adornedElementChild = controlItem.Presenter?.Child;

                isResizing = true;
                OnDragStarted?.Invoke();
                initialPosition = e.GetPosition(this.GetVisualParent());
                originalBounds = adornedElement.Bounds;
                originalOrigin = new Point(double.IsNaN(Canvas.GetLeft(adornedElement)) ? 0 : Canvas.GetLeft(adornedElement),
                    double.IsNaN(Canvas.GetTop(adornedElement)) ? 0 : Canvas.GetTop(adornedElement));
                xDirection = GetResizeXDirection(control);
                yDirection = GetResizeYDirection(control);
                e.Pointer.Capture(this);

                // Snapshot participant positions for group drag.
                var participants = GroupDragParticipants;
                if (participants != null && participants.Count > 0)
                {
                    _groupDragParticipants = participants;
                    _participantOriginalPositions = new Point[participants.Count];
                    for (int i = 0; i < participants.Count; i++)
                    {
                        var p = participants[i];
                        _participantOriginalPositions[i] = new Point(
                            double.IsNaN(Canvas.GetLeft(p)) ? 0 : Canvas.GetLeft(p),
                            double.IsNaN(Canvas.GetTop(p)) ? 0 : Canvas.GetTop(p));
                    }
                }
            }
        }
    }

    public void StartDrag(PointerPressedEventArgs e)
    {
        OnHandlePointerPressed(null, e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        OnHandlePointerMoved(null, e);
    }
}

public enum ResizeXDirection
{
    None,
    Right,
    Left
}

public enum ResizeYDirection
{
    None,
    Bottom,
    Top
}

[Flags]
public enum ResizeAdornerDirections
{
    S = 1,
    N = 2,
    W = 4,
    E = 8,
    SE = 16,
    NE = 32,
    SW = 64,
    NW = 128,
    Left = W | SW | NW,
    Top = N | NE | NW,
    Right = E | SE | NE,
    Bottom = S | SW | SE,
    All = Left | Top | Right | Bottom
}

public class HasResizeAdornerDirectionConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ResizeAdornerDirections dirs && parameter is ResizeAdornerDirections dir)
            return (dirs & dir) == dir;
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}