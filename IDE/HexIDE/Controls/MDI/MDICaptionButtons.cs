using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HexIDE.Utils;

namespace HexIDE.Controls;

/// <summary>
/// Custom caption buttons (close / minimize / restore) for MDI windows.
/// Avalonia 12 removed CaptionButtons from the framework; this is a self-contained
/// TemplatedControl replacement that owns its own ControlTheme.
/// </summary>
public class MDICaptionButtons : TemplatedControl
{
    private IDisposable? _stateSubscription;

    private Button? _closeButton;
    private Button? _minimizeButton;
    private Button? _restoreButton;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (FindHost() is { } host)
            Attach(host);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Detach();
    }

    private void Attach(MDIWindow hostWindow)
    {
        if (_stateSubscription == null)
        {
            _stateSubscription = hostWindow.GetObservable(MDIHostPanel.WindowStateProperty)
                .Subscribe(new ActionObserver<WindowState>(x =>
                {
                    PseudoClasses.Set(":minimized",  x == WindowState.Minimized);
                    PseudoClasses.Set(":normal",     x == WindowState.Normal);
                    PseudoClasses.Set(":maximized",  x == WindowState.Maximized);
                    PseudoClasses.Set(":fullscreen", x == WindowState.FullScreen);
                }));

            if (_closeButton != null)
                _closeButton.IsEnabled = hostWindow.CanClose;
        }
    }

    private void Detach()
    {
        _stateSubscription?.Dispose();
        _stateSubscription = null;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_closeButton != null)   _closeButton.Click   -= OnCloseClick;
        if (_minimizeButton != null) _minimizeButton.Click -= OnMinimizeClick;
        if (_restoreButton != null)  _restoreButton.Click  -= OnRestoreClick;

        _closeButton    = e.NameScope.Find<Button>("PART_CloseButton");
        _minimizeButton = e.NameScope.Find<Button>("PART_MinimizeButton");
        _restoreButton  = e.NameScope.Find<Button>("PART_RestoreButton");

        if (_closeButton != null)
        {
            _closeButton.Click += OnCloseClick;
            if (FindHost() is { } host)
                _closeButton.IsEnabled = host.CanClose;
        }
        if (_minimizeButton != null) _minimizeButton.Click += OnMinimizeClick;
        if (_restoreButton  != null) _restoreButton.Click  += OnRestoreClick;
    }

    private void OnCloseClick(object?   sender, RoutedEventArgs e) => OnClose();
    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => OnMinimize();
    private void OnRestoreClick(object?  sender, RoutedEventArgs e) => OnRestore();

    private void OnClose()
    {
        if (FindHost() is { } window)
            window.CloseCommand?.Execute(window.CloseCommandParameter);
    }

    private void OnMinimize()
    {
        if (FindHost() is not { } window) return;

        if (MDIHostPanel.GetWindowState(window) == WindowState.Minimized)
        {
            MDIHostPanel.SetOldMinimizedWindowLocation(window, MDIHostPanel.GetWindowLocation(window));
            MDIHostPanel.SetWindowLocation(window, MDIHostPanel.GetOldWindowLocation(window));
            MDIHostPanel.SetWindowState(window, WindowState.Normal);
        }
        else
        {
            MDIHostPanel.SetOldWindowLocation(window, MDIHostPanel.GetWindowLocation(window));
            MDIHostPanel.SetWindowState(window, WindowState.Minimized);
            if (MDIHostPanel.GetOldMinimizedWindowLocation(window) is { } oldLoc)
                MDIHostPanel.SetWindowLocation(window, oldLoc);
            else if (this.FindAncestorOfType<MDIHostPanel>() is { } hostPanel)
                MDIHostPanel.SetWindowLocation(window, new Point(0, hostPanel.Bounds.Height - window.MinHeight));
        }
    }

    private void OnRestore()
    {
        if (FindHost() is not { } window) return;

        MDIHostPanel.SetWindowState(window,
            MDIHostPanel.GetWindowState(window) == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized);
    }

    private MDIWindow? FindHost() => this.FindAncestorOfType<MDIWindow>();
}
