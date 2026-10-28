using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime;

public class VBFormRuntime : Window, IModuleExecutionRoot, IFormView
{

    private VBWindowContext windowContext;

    public VBWindowContext Context => windowContext;

    public EventPump Events => windowContext.Events;
    
    public VBFormRuntime()
    {
        windowContext = new VBWindowContext(new StandaloneStandardLib(this)) { Window = this };
        // Held by a modal form, the window takes no input of its own (#822): what reaches it is stopped on the way down, and
        // activating it brings the modal form forward instead.
        foreach (var input in new RoutedEvent[] { KeyDownEvent, KeyUpEvent, TextInputEvent, PointerPressedEvent, PointerReleasedEvent, PointerWheelChangedEvent })
            AddHandler(input, new EventHandler<RoutedEventArgs>((_, e) => e.Handled |= heldBy is not null), RoutingStrategies.Tunnel);
        // Activating a form tells the program, which raises Activate and Deactivate (#994). A held form is never active: Windows
        // doesn't activate a disabled window, and activating it brings the modal form forward.
        Activated += (_, _) =>
        {
            if (heldBy is not null)
                heldBy.ActivateForm();
            else
                windowContext.Program?.Activated(windowContext);
        };
    }

    private IFormView? heldBy;

    public IFormView? HeldBy
    {
        get => heldBy;
        set
        {
            heldBy = value;
            // Its controls stop taking hits, which disabling them would do too, greying each one (#822).
            if (Content is InputElement content)
                content.IsHitTestVisible = value is null;
        }
    }

    public void ActivateForm() => Activate();

    // Form_Load is not raised here: the program raises Initialize and Load before the window is shown (#822, #994).

    // VB size setters raise inside their scope; Move holds both Width and Height together (#1569, #2389).
    private int resizingOnce;
    private bool resizePending;

    /// <summary>
    /// Holds Resize back until the returned scope ends, and then raises it once if the size changed: a form's Move sets
    /// Width and Height together, and VB6 raises one Resize for the two, with both new (measured, #1569).
    /// </summary>
    internal IDisposable ResizeOnce()
    {
        resizingOnce++;
        return new ResizeScope(this);
    }

    private sealed class ResizeScope(VBFormRuntime form) : IDisposable
    {
        private bool done;

        public void Dispose()
        {
            if (done)
                return;
            done = true;
            if (--form.resizingOnce == 0 && form.resizePending)
            {
                form.resizePending = false;
                form.windowContext.WindowResized(sizeSet: true);
            }
        }
    }

    // Whether the window system has sized the window since it was first shown. Until then ClientSize is still the size the
    // window was made at, the platform's default, and the size it is being shown at is its Width and Height (#994).
    private bool sized;

    /// <inheritdoc/>
    public (Size Size, WindowState State) Extent
    {
        get
        {
            if (WindowState == WindowState.Minimized)
                return (default, WindowState.Minimized);
            var size = sized || double.IsNaN(Width) || double.IsNaN(Height) ? ClientSize : new Size(Width, Height);
            return (new Size(Math.Round(size.Width * RenderScaling), Math.Round(size.Height * RenderScaling)), WindowState);
        }
    }

    private bool windowResizePending;

    // State and client-size notifications describe one window change (#2389). Wait for layout to settle, but drain
    // before the next input, so a border drag still raises Resize for each move rather than only at its end.
    private void QueueWindowResize()
    {
        if (windowResizePending)
            return;
        windowResizePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            windowResizePending = false;
            windowContext.WindowResized(paintGrowth: true);
        }, DispatcherPriority.Loaded);
    }

    protected override void OnResized(WindowResizedEventArgs e)
    {
        base.OnResized(e);
        sized |= IsVisible;
        QueueWindowResize();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // Shown, Resize the first time since the form loaded (#994). Win32 doesn't report a client resize when minimizing,
        // where VB6 raises Resize with WindowState already 1, so the state is watched too.
        if (change.Property == IsVisibleProperty && IsVisible)
            windowContext.Shown();
        else if (change.Property == WidthProperty || change.Property == HeightProperty)
        {
            // Use the requested size until layout reports it, and raise inside the setter, even in Load (#1570).
            sized = false;
            if (resizingOnce > 0)
                resizePending = true;
            else if (IsVisible)
                QueueWindowResize();
            else
                windowContext.WindowResized(sizeSet: true);
        }
        else if (change.Property == WindowStateProperty)
        {
            if (WindowState == WindowState.Minimized)
                windowContext.WindowResized();
            else
                QueueWindowResize();
        }
    }

    public void ExecuteKeyboardEvent(string name, IReadOnlyList<Vb6Value> args, int writable, Action<int> completed) =>
        windowContext.ExecuteKeyboardEvent(name, args, writable, completed);

    public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null)
    {
        windowContext.ExecuteSub(name, args);
    }

    public System.Threading.Tasks.Task ExecuteEditEventAsync(string name, IList<Vb6Value> args) =>
        windowContext.ExecuteEditEventAsync(name, args);

    public void ExecuteSubIf(string name, IReadOnlyList<Vb6Value>? args, Func<bool> when) =>
        windowContext.ExecuteSubIf(name, args, when);

    public bool Handles(string name) => windowContext.Handles(name);

    public void ExecuteEvent(string name, Func<IReadOnlyList<Vb6Value>> args, EventCoalescing coalescing = EventCoalescing.None) =>
        windowContext.ExecuteEvent(name, args, coalescing);

    public void ExecuteTimer(string name, IReadOnlyList<Vb6Value>? args = null) => windowContext.ExecuteTimer(name, args);

    public Task<bool> UnloadAsync(int mode) => windowContext.UnloadAsync(mode);

    public void SetContent(Control content) => Content = content;

    private bool positioned;
    internal bool CenterOnShow { get; set; }

    // A disabled form is shown without being activated, so the control that had the focus keeps it (measured, #1101).

    public void ShowForm()
    {
        if (!positioned && CenterOnShow)
        {
            AvaloniaInterop.FormPosition.Center(this);
            WindowStartupLocation = WindowStartupLocation.Manual;
        }
        positioned = true;
        ShowActivated = IsEnabled;
        Show();
    }

    public void HideForm() => Hide();

    public bool IsShown => IsVisible;

    public void CloseForm() => Close();

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // The close box asks the program first, as VB6's does: QueryUnload with UnloadMode 0, then Unload, and either may
        // cancel. The window closes once the form has unloaded (#973). A close from code, End, Stop or the IDE shutting
        // down is not the program's to refuse.
        if (e.CloseReason == WindowCloseReason.WindowClosing && !e.IsProgrammatic && windowContext.UnloadFromCloseBox())
            e.Cancel = true;
        base.OnClosing(e);
    }
}
