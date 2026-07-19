using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using HexIDE.VisualDesigner;
using Serilog;

namespace HexIDE.Controls;

public class ControlItem : ListBoxItem
{
    public static readonly StyledProperty<bool> IsPrimaryProperty =
        AvaloniaProperty.Register<ControlItem, bool>(nameof(IsPrimary), defaultValue: true);

    public bool IsPrimary
    {
        get => GetValue(IsPrimaryProperty);
        set => SetValue(IsPrimaryProperty, value);
    }

    static ControlItem()
    {
        IsSelectedProperty.Changed.AddClassHandler<ControlItem>((item, e) =>
        {
            if (item.IsSelected)
            {
                var adorner = new ResizeAdorner { IsPrimary = item.IsPrimary };
                AdornerLayer.SetAdorner(item, adorner);
            }
            else
            {
                AdornerLayer.SetAdorner(item, null);
            }
        });

        IsPrimaryProperty.Changed.AddClassHandler<ControlItem>((item, _) =>
        {
            if (AdornerLayer.GetAdorner(item) is ResizeAdorner adorner)
                adorner.IsPrimary = item.IsPrimary;
        });
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var clickCount = e.ClickCount;
        var isCtrlClick = (e.KeyModifiers & KeyModifiers.Control) != 0;
        var isLeftPress = e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed;

        // When this item is already selected and the user makes a plain left press,
        // mark the event handled before bubbling so that ControlsContainer's
        // ContainerPointerPressed handler does not deselect the other controls.
        // This is what allows dragging any selected control to move all of them.
        if (isLeftPress && IsSelected && !isCtrlClick)
            e.Handled = true;

        base.OnPointerPressed(e);

        if (clickCount >= 2)
        {
            Log.Debug("ControlItem: Raising DoubleTapped (click count={ClickCount})", clickCount);
            RaiseEvent(new TappedEventArgs(DoubleTappedEvent, e));
            return;
        }

        bool isLocked = (DataContext as ComponentInstanceViewModel)?.Owner.FormDefinition?.LockControls == true;
        if (!isCtrlClick && IsSelected && !isLocked && AdornerLayer.GetAdorner(this) is ResizeAdorner adorner)
            adorner.StartDrag(e);
    }
}
