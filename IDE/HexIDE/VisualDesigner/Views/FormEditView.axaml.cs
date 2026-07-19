using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using HexIDE.Controls;
using HexIDE.Events;
using HexIDE.IDE;
using R3;
using Serilog;

namespace HexIDE.VisualDesigner;

public partial class FormEditView : UserControl
{
    public FormEditView()
    {
        InitializeComponent();
        NewControlSpawner.AddHandler(PointerPressedEvent, OnNewControlStarted);
        NewControlSpawner.AddHandler(PointerMovedEvent, OnNewControlMoving);
        NewControlSpawner.AddHandler(PointerReleasedEvent, OnNewPointerReleased);

        // DoubleTapped must be registered with handledEventsToo because ListBoxItem
        // (ControlItem) marks PointerPressed as handled, which prevents the gesture
        // recognizer from firing DoubleTapped through normal XAML routing.
        ControlsContainer.AddHandler(DoubleTappedEvent, VisualControl_OnDoubleTapped,
            Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);

        FormContainer.AddHandler(PointerMovedEvent, OnFormPointerMoved);
        FormContainer.AddHandler(PointerReleasedEvent, OnFormPointerReleased);
    }

    private double SnapToGrid(double x)
    {
        return SnapGridUtils.SnapToGrid(this, x);
    }

    private Point newControlInitialPress;
    private bool isSpawningNewControl;
    private IDisposable? selectedComponentSub;
    private IDisposable? settingsSub;

    // Rubber-band state
    private Point _rubberBandStart;
    private bool _isRubberBanding;

    // Guards against selection-change loops between VM and ControlsContainer
    private bool _syncingSelection;

    private void OnNewPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (isSpawningNewControl && NewControlSpawnerPhantom.IsVisible)
        {
            if (DataContext is FormEditViewModel vm)
            {
                Log.Debug("FormEditView: Spawning control at ({Left},{Top} {Width}x{Height})",
                    Canvas.GetLeft(NewControlSpawnerPhantom), Canvas.GetTop(NewControlSpawnerPhantom),
                    NewControlSpawnerPhantom.Width, NewControlSpawnerPhantom.Height);
                vm.SpawnControl(new Rect(Canvas.GetLeft(NewControlSpawnerPhantom),
                    Canvas.GetTop(NewControlSpawnerPhantom), NewControlSpawnerPhantom.Width,
                    NewControlSpawnerPhantom.Height));
                if (ControlsContainer.ContainerFromIndex(ControlsContainer.SelectedIndex) is { } control)
                {
                    control.Focus();
                }
            }
            NewControlSpawnerPhantom.IsVisible = false;
        }
        isSpawningNewControl = false;
    }

    private void OnNewControlMoving(object? sender, PointerEventArgs e)
    {
        if (isSpawningNewControl)
        {
            var point = e.GetCurrentPoint(NewControlSpawner).Position;
            double left = SnapToGrid(Math.Min(point.X, newControlInitialPress.X));
            double top = SnapToGrid(Math.Min(point.Y, newControlInitialPress.Y));
            double right = SnapToGrid(Math.Max(point.X, newControlInitialPress.X));
            double bottom = SnapToGrid(Math.Max(point.Y, newControlInitialPress.Y));
            Canvas.SetLeft(NewControlSpawnerPhantom, left);
            Canvas.SetTop(NewControlSpawnerPhantom, top);
            NewControlSpawnerPhantom.Width = Math.Max(1, right - left);
            NewControlSpawnerPhantom.Height = Math.Max(1, bottom - top);
            NewControlSpawnerPhantom.IsVisible = true;
        }
    }

    private void OnNewControlStarted(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(NewControlSpawner);
        if (point.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
        {
            isSpawningNewControl = true;
            newControlInitialPress = point.Position;
        }
    }

    // PointerPressed on the form canvas background (fires only when not handled by a child control).
    private void OnFormPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled)
            return;

        if (DataContext is not FormEditViewModel vm)
            return;

        if (!vm.ToolsBoxToolViewModel.IsCursorSelected)
            return;

        if (e.GetCurrentPoint(FormContainer).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
            return;

        _rubberBandStart = e.GetPosition(ControlsContainer);
        _isRubberBanding = false;
        e.Pointer.Capture(FormContainer);
    }

    private void OnFormPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer.Captured, FormContainer))
            return;

        if (!e.GetCurrentPoint(FormContainer).Properties.IsLeftButtonPressed)
        {
            CancelRubberBand();
            return;
        }

        var pos = e.GetPosition(ControlsContainer);
        if (!_isRubberBanding &&
            (Math.Abs(pos.X - _rubberBandStart.X) > 3 || Math.Abs(pos.Y - _rubberBandStart.Y) > 3))
        {
            _isRubberBanding = true;
        }

        if (_isRubberBanding)
            UpdateRubberBandRect(pos);
    }

    private void OnFormPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer.Captured, FormContainer))
            return;

        e.Pointer.Capture(null);

        if (_isRubberBanding)
        {
            var pos = e.GetPosition(ControlsContainer);
            CommitRubberBand(pos, (e.KeyModifiers & KeyModifiers.Control) != 0);
            HideRubberBandRect();
        }
        else
        {
            // Plain click on empty canvas — select the form (clears control selection).
            if (DataContext is FormEditViewModel vm)
                vm.SelectedComponent = vm.Form;
        }

        _isRubberBanding = false;
    }

    private void UpdateRubberBandRect(Point current)
    {
        var left   = Math.Min(_rubberBandStart.X, current.X);
        var top    = Math.Min(_rubberBandStart.Y, current.Y);
        var width  = Math.Abs(current.X - _rubberBandStart.X);
        var height = Math.Abs(current.Y - _rubberBandStart.Y);

        Canvas.SetLeft(RubberBandRect, left);
        Canvas.SetTop(RubberBandRect, top);
        RubberBandRect.Width  = Math.Max(1, width);
        RubberBandRect.Height = Math.Max(1, height);
        RubberBandRect.IsVisible = true;
    }

    private void HideRubberBandRect() => RubberBandRect.IsVisible = false;

    private void CancelRubberBand()
    {
        HideRubberBandRect();
        _isRubberBanding = false;
    }

    private void CommitRubberBand(Point end, bool addToExisting)
    {
        if (DataContext is not FormEditViewModel vm) return;

        var rubberRect = new Rect(
            Math.Min(_rubberBandStart.X, end.X),
            Math.Min(_rubberBandStart.Y, end.Y),
            Math.Abs(end.X - _rubberBandStart.X),
            Math.Abs(end.Y - _rubberBandStart.Y));

        var matched = new List<int>();
        for (int i = 0; i < ControlsContainer.Items.Count; i++)
        {
            if (ControlsContainer.Items[i] is ComponentInstanceViewModel item)
            {
                var itemRect = new Rect(item.Left, item.Top, item.Width, item.Height);
                if (rubberRect.Intersects(itemRect))
                    matched.Add(i);
            }
        }

        if (matched.Count == 0)
        {
            if (!addToExisting)
                vm.SelectedComponent = vm.Form;
            return;
        }

        _syncingSelection = true;
        try
        {
            ControlsContainer.Selection.BeginBatchUpdate();
            if (!addToExisting)
                ControlsContainer.Selection.Clear();
            foreach (var i in matched)
                ControlsContainer.Selection.Select(i);
            ControlsContainer.Selection.EndBatchUpdate();
        }
        finally
        {
            _syncingSelection = false;
        }

        // Update VM primary to first matched item.
        if (ControlsContainer.SelectedItem is ComponentInstanceViewModel primary)
        {
            _syncingSelection = true;
            try { vm.SelectedComponent = primary; }
            finally { _syncingSelection = false; }
        }
        vm.SetSelectedComponents(ControlsContainer.SelectedItems?.OfType<ComponentInstanceViewModel>() ?? []);
    }

    // Fired by ControlsContainer when the user changes selection interactively.
    private void OnControlsContainerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection) return;
        if (DataContext is not FormEditViewModel vm) return;

        _syncingSelection = true;
        try
        {
            vm.SelectedComponent = ControlsContainer.SelectedItem as ComponentInstanceViewModel ?? vm.Form;
            vm.SetSelectedComponents(ControlsContainer.SelectedItems?.OfType<ComponentInstanceViewModel>() ?? []);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void VisualControl_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        Log.Debug("FormEditView: DoubleTapped fired, sender={Sender}, e.Source={Source}, e.Source.DataContext={DC}",
            sender?.GetType().Name, (e.Source as Control)?.GetType().Name, (e.Source as Control)?.DataContext?.GetType().Name);

        // Walk up from e.Source to find the ComponentInstanceViewModel.
        // The source may be the ControlItem itself, the ControlRenderer, or a child Image.
        ComponentInstanceViewModel? vm = null;
        var current = e.Source as Control;
        while (current != null && current != this)
        {
            if (current.DataContext is ComponentInstanceViewModel found)
            {
                vm = found;
                break;
            }
            current = current.Parent as Control;
        }

        if (vm == null)
        {
            Log.Debug("FormEditView: DoubleTapped — no ComponentInstanceViewModel found in visual tree, ignoring");
            return;
        }

        if (DataContext is not FormEditViewModel rootVm)
        {
            Log.Warning("FormEditView: DoubleTapped — DataContext is not FormEditViewModel");
            return;
        }

        var eventClass = vm.Instance.BaseClass.Events.FirstOrDefault();
        Log.Debug("FormEditView: DoubleTapped on {ControlName} ({ControlType}), default event={Event}",
            vm.Name, vm.Instance.BaseClass.Name, eventClass?.Name ?? "(none)");

        if (eventClass == null)
            rootVm.RequestCode(null);
        else
            rootVm.RequestCode($"{vm.Name}_{eventClass.Name}");
        e.Handled = true;
    }

    private void OnFormDoubleTapped(object? sender, TappedEventArgs e)
    {
        Log.Debug("FormEditView: Form background DoubleTapped");
        if (DataContext is not FormEditViewModel rootVm)
            return;

        rootVm.RequestCode($"Form_Load");
        e.Handled = true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        selectedComponentSub?.Dispose();
        settingsSub?.Dispose();
        if (DataContext is FormEditViewModel vm)
        {
            selectedComponentSub = vm.ObservePropertyChanged(x => x.SelectedComponent)
                .Subscribe(selectedComponent =>
                {
                    // Form adorner: show resize handles on the form border when form itself is selected.
                    if (selectedComponent == vm.Form)
                    {
                        var adorner = new ResizeAdorner { AllowedDirection = ResizeAdornerDirections.All & ~(ResizeAdornerDirections.Left | ResizeAdornerDirections.Top) };
                        adorner.OnDragStarted = () => vm.BeginDrag([vm.Form]);
                        adorner.OnDragCompleted = () => vm.EndDrag();
                        AdornerLayer.SetAdorner(FormContainer, adorner);
                    }
                    else
                    {
                        AdornerLayer.SetAdorner(FormContainer, null);
                    }

                    // Sync ControlsContainer to the VM-driven single selection.
                    if (!_syncingSelection)
                        ControlsContainer.SetSingleSelection(selectedComponent);
                });

            ControlsContainer.BeginDragCallback = vm.BeginDrag;
            ControlsContainer.EndDragCallback = vm.EndDrag;

            ApplyGridSettings(vm.Settings);
            vm.Settings.PropertyChanged += OnSettingsPropertyChanged;
            settingsSub = Disposable.Create(() =>
            {
                vm.Settings.PropertyChanged -= OnSettingsPropertyChanged;
                ControlsContainer.BeginDragCallback = null;
                ControlsContainer.EndDragCallback = null;
            });
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        selectedComponentSub?.Dispose();
        settingsSub?.Dispose();
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ISettingsService.ShowGrid) or nameof(ISettingsService.GridWidth)
            or nameof(ISettingsService.GridHeight) or nameof(ISettingsService.AlignToGrid))
        {
            if (DataContext is FormEditViewModel vm)
                ApplyGridSettings(vm.Settings);
        }
    }

    private void ApplyGridSettings(ISettingsService settings)
    {
        SnapGridUtils.SetSnapToGrid(this, settings.AlignToGrid);
        SnapGridUtils.SetGridSize(this, settings.GridWidth);

        if (settings.ShowGrid)
        {
            var w = settings.GridWidth;
            var h = settings.GridHeight;
            var sizeStr = $"0,0,{w},{h}";
            GridPatternBorder.Background = new VisualBrush
            {
                SourceRect = RelativeRect.Parse(sizeStr),
                DestinationRect = RelativeRect.Parse(sizeStr),
                TileMode = TileMode.Tile,
                Stretch = Stretch.UniformToFill,
                Visual = BuildGridDotCanvas(w, h)
            };
        }
        else
        {
            GridPatternBorder.Background = Brushes.Transparent;
        }
    }

    private static Canvas BuildGridDotCanvas(int w, int h)
    {
        var canvas = new Canvas { Width = w, Height = h, Background = Brushes.Transparent };
        var dot = new Rectangle
        {
            Width = 1, Height = 1,
            Fill = (IBrush)Application.Current!.FindResource("FormGridPatternBrush")!
        };
        canvas.Children.Add(dot);
        return canvas;
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Source is Control c)
        {
            if (DataContext is FormEditViewModel viewModel)
            {
                viewModel.EventBus.Publish(new TextInputForPropertyEvent(e.Text));
            }
        }
    }
}
