using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Labs.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Utils;
using Serilog;

namespace HexIDE.Controls;

public class MDIWindow : ContentControl
{
    private Border? titleBar;

    public static readonly StyledProperty<IImage?> IconProperty = AvaloniaProperty.Register<MDIWindow, IImage?>("Icon");
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<MDIWindow, string>("Title");
    public static readonly StyledProperty<bool> CanCloseProperty = AvaloniaProperty.Register<MDIWindow, bool>("CanClose", true);

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public IImage? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public ICommand CloseCommand
    {
        get => GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public object? CloseCommandParameter
    {
        get => GetValue(CloseCommandParameterProperty);
        set => SetValue(CloseCommandParameterProperty, value);
    }

    public bool CanClose
    {
        get => GetValue(CanCloseProperty);
        set => SetValue(CanCloseProperty, value);
    }

    static MDIWindow()
    {
        FocusableProperty.OverrideDefaultValue<MDIWindow>(true);
    }

    public MDIWindow()
    {
        AddHandler(PointerPressedEvent, OnWindowPressed, RoutingStrategies.Tunnel);
        // Resize handlers on `this` so they fire for edge hits where MDIWindow is the
        // deepest hit-testable element (ClassicBorderDecorator's 3D border zone has no
        // background coverage and fails hit-test, causing events to source from MDIWindow
        // itself rather than the border — so border.AddHandler never fired).
        AddHandler(PointerPressedEvent, OnBorderDown, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnBorderReleased);
        AddHandler(PointerMovedEvent, OnBorderMoved);
        AddHandler(PointerEnteredEvent, OnBorderEnter);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        titleBar = e.NameScope.Get<Border>("PART_TitleBar");

        titleBar.AddHandler(PointerPressedEvent, OnTitleBarDown);
        titleBar.AddHandler(PointerMovedEvent, OnTitleBarMoved);

        var contentPresenter = e.NameScope.Get<ContentPresenter>("PART_ContentPresenter");
        contentPresenter.GetObservable(ContentPresenter.ChildProperty)
            .Subscribe(new ActionObserver<Control?>(control =>
            {
                var child = contentPresenter.Child;
                if (child != null)
                {
                    child.GetObservable(CommandManager.CommandBindingsProperty)
                        .Subscribe(new ActionObserver<IList<CommandBinding>?>(bindings =>
                        {
                            if (bindings != null)
                                CommandManager.SetCommandBindings(this, bindings);
                        }));
                }
            }));
    }

    // Win32 direct cursor override — Avalonia 12 doesn't call SetCursor during SetCapture
    // when the pointer moves outside the captured window's layout bounds, so WM_SETCURSOR
    // is never fired and the cursor reverts to Arrow. We drive it manually instead.
    [DllImport("user32.dll")] [SupportedOSPlatform("windows")]
    private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);
    [DllImport("user32.dll")] [SupportedOSPlatform("windows")]
    private static extern IntPtr SetCursor(IntPtr hCursor);

    private StandardCursorType _dragCursorType;

    private static void ForceWin32Cursor(StandardCursorType type)
    {
        if (!OperatingSystem.IsWindows()) return;
        int id = type switch
        {
            StandardCursorType.SizeWestEast                                          => 32644,
            StandardCursorType.SizeNorthSouth                                        => 32645,
            StandardCursorType.TopLeftCorner or StandardCursorType.BottomRightCorner => 32642,
            StandardCursorType.TopRightCorner or StandardCursorType.BottomLeftCorner => 32643,
            _                                                                        => 32512,
        };
        var h = LoadCursor(IntPtr.Zero, id);
        if (h != IntPtr.Zero) SetCursor(h);
    }

    private void OnBorderEnter(object? sender, PointerEventArgs e)
    {
        var pos = e.GetPosition(this);
        Log.Verbose("[MDIWindow] OnBorderEnter pos=({X:F0},{Y:F0}) IsResizing={R} source={S}",
            pos.X, pos.Y, IsResizing, e.Source?.GetType().Name);
        if (!IsResizing)
            SetResizeCursor(e);
    }

    private void SetResizeCursor(PointerEventArgs e)
    {
        var position = e.GetPosition(this);
        var isLeft = position.X <= 5;
        var isRight = position.X >= Bounds.Width - 5;
        var isTop = position.Y <= 5;
        var isBottom = position.Y >= Bounds.Height - 5;

        var cursor = StandardCursorType.Arrow;

        if (MDIHostPanel.GetWindowState(this) == WindowState.Normal)
        {
            if (isLeft)
            {
                if (isTop)
                    cursor = StandardCursorType.TopLeftCorner;
                else if (isBottom)
                    cursor = StandardCursorType.BottomLeftCorner;
                else
                    cursor = StandardCursorType.SizeWestEast;
            }
            else if (isRight)
            {
                if (isTop)
                    cursor = StandardCursorType.TopRightCorner;
                else if (isBottom)
                    cursor = StandardCursorType.BottomRightCorner;
                else
                    cursor = StandardCursorType.SizeWestEast;
            }
            else if (isTop || isBottom)
            {
                cursor = StandardCursorType.SizeNorthSouth;
            }
        }

        Log.Verbose("[MDIWindow] SetResizeCursor pos=({X:F0},{Y:F0}) bounds=({W:F0}x{H:F0}) -> {Cursor}",
            position.X, position.Y, Bounds.Width, Bounds.Height, cursor);
        _dragCursorType = cursor;
        Cursor = new Cursor(cursor);
    }

    private void OnBorderReleased(object? sender, PointerReleasedEventArgs e)
    {
        var tl = TopLevel.GetTopLevel(this);
        Log.Verbose("[MDIWindow] OnBorderReleased topLevel={TL} resizing=({L},{R},{T},{B})",
            tl?.GetType().Name ?? "null", leftResize, rightResize, topResize, bottomResize);
        if (tl is not null)
            tl.Cursor = null;
        rightResize = false;
        bottomResize = false;
        leftResize = false;
        topResize = false;
        e.Pointer.Capture(null);
    }

    private bool rightResize;
    private bool bottomResize;
    private bool leftResize;
    private bool topResize;
    private Size initialSize;

    private bool IsResizing => leftResize || rightResize || topResize || bottomResize;

    private void OnBorderMoved(object? sender, PointerEventArgs e)
    {
        if (IsResizing)
        {
            ForceWin32Cursor(_dragCursorType);
            Log.Verbose("[MDIWindow] OnBorderMoved RESIZING pos=({X:F0},{Y:F0}) cursor={C}",
                e.GetPosition(this).X, e.GetPosition(this).Y, Cursor);
        }
        else
            SetResizeCursor(e);
        if (FindParentMDIHost() is not { } canvas)
            return;

        var position = e.GetPosition(canvas);

        if (rightResize)
        {
            MDIHostPanel.SetWindowSize(this, MDIHostPanel.GetWindowSize(this).WithWidth(Math.Max(MinWidth, initialSize.Width + (position.X - initialPress.X))));
        }
        if (bottomResize)
        {
            MDIHostPanel.SetWindowSize(this, MDIHostPanel.GetWindowSize(this).WithHeight(Math.Max(MinHeight, initialSize.Height + (position.Y - initialPress.Y))));
        }
        if (leftResize)
        {
            var destinationRight = initialPosition.X + initialSize.Width;
            MDIHostPanel.SetWindowLocation(this, MDIHostPanel.GetWindowLocation(this).WithX(Math.Min(initialPosition.X + (position.X - initialPress.X), destinationRight - MinWidth)));
            MDIHostPanel.SetWindowSize(this, MDIHostPanel.GetWindowSize(this).WithWidth(Math.Max(MinWidth, destinationRight - MDIHostPanel.GetWindowLocation(this).X)));
        }
        if (topResize)
        {
            var destinationBottom = initialPosition.Y + initialSize.Height;
            MDIHostPanel.SetWindowLocation(this, MDIHostPanel.GetWindowLocation(this).WithY(Math.Min(initialPosition.Y + (position.Y - initialPress.Y), destinationBottom - MinHeight)));
            MDIHostPanel.SetWindowSize(this, MDIHostPanel.GetWindowSize(this).WithHeight(Math.Max(MinHeight, destinationBottom - MDIHostPanel.GetWindowLocation(this).Y)));
        }
    }

    private void OnBorderDown(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
            return;

        if (!GatherInitialMeasure(e))
            return;

        if (MDIHostPanel.GetWindowState(this) != WindowState.Normal)
            return;

        var position = e.GetPosition(this);
        leftResize = position.X <= 5;
        rightResize = position.X >= Bounds.Width - 5;
        topResize = position.Y <= 5;
        bottomResize = position.Y >= Bounds.Height - 5;

        if (leftResize || rightResize || topResize || bottomResize)
        {
            SetResizeCursor(e);
            e.Pointer.Capture(this);
            e.Handled = true;
            var tl = TopLevel.GetTopLevel(this);
            ForceWin32Cursor(_dragCursorType);
            // Post a second call so it wins after Avalonia's own post-press cursor reset
            // (SetCapture or ActivateMDIForm can trigger a WM_SETCURSOR that reverts to Arrow
            // after our synchronous call returns).
            var savedCursor = _dragCursorType;
            Dispatcher.UIThread.Post(() => { if (IsResizing) ForceWin32Cursor(savedCursor); });
            Log.Verbose("[MDIWindow] OnBorderDown RESIZE START l={L} r={R} t={T} b={B} cursor={C} topLevel={TL}",
                leftResize, rightResize, topResize, bottomResize, Cursor, tl?.GetType().Name ?? "null");
            if (tl is not null)
                tl.Cursor = Cursor;
        }
        else
        {
            Log.Verbose("[MDIWindow] OnBorderDown no-resize pos=({X:F0},{Y:F0}) bounds=({W:F0}x{H:F0})",
                position.X, position.Y, Bounds.Width, Bounds.Height);
        }
    }

    private Point initialPosition;
    private Point initialPress;
    public static readonly StyledProperty<object?> CloseCommandParameterProperty = AvaloniaProperty.Register<MDIWindow, object?>("CloseCommandParameter");
    public static readonly StyledProperty<ICommand> CloseCommandProperty = AvaloniaProperty.Register<MDIWindow, ICommand>("CloseCommand");

    private MDIHostPanel? FindParentMDIHost() => this.FindAncestorOfType<MDIHostPanel>();

    private void OnTitleBarMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(e.Source, titleBar))
            return;
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed)
        {
            if (FindParentMDIHost() is { } mdiHost)
            {
                var curPosition = e.GetPosition(mdiHost);
                var diff = curPosition - initialPress;
                MDIHostPanel.SetWindowLocation(this, new Point(
                    Math.Clamp(initialPosition.X + diff.X, -Bounds.Width + 50, mdiHost.Bounds.Width - 50),
                    Math.Clamp(initialPosition.Y + diff.Y, -Bounds.Height + 50, mdiHost.Bounds.Height - 50)
                    ));
            }
        }
    }

    private void OnTitleBarDown(object? sender, PointerPressedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, titleBar))
            return;
        var point = e.GetCurrentPoint(this);
        if (point.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
        {
            if (e.ClickCount >= 2)
            {
                var state = MDIHostPanel.GetWindowState(this);
                MDIHostPanel.SetWindowState(this,
                    state == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
                e.Handled = true;
                return;
            }
            GatherInitialMeasure(e);
        }
    }

    private void OnWindowPressed(object? sender, PointerPressedEventArgs e)
    {
        this.ActivateMDIForm();
    }

    private bool GatherInitialMeasure(PointerEventArgs e)
    {
        if (FindParentMDIHost() is { } canvas)
        {
            initialPosition = MDIHostPanel.GetWindowLocation(this);
            initialPress = e.GetPosition(canvas);
            initialSize = MDIHostPanel.GetWindowSize(this);
            return true;
        }

        return false;
    }
}
