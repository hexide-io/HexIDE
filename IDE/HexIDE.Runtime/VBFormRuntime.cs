using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
        Activated += (_, _) => heldBy?.ActivateForm();
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

    private bool minimizedResizeRaised;

    protected override void OnResized(WindowResizedEventArgs e)
    {
        base.OnResized(e);
        // Minimize is dispatched by the state change, including on backends that also resize it.
        if (WindowState == WindowState.Minimized)
        {
            if (minimizedResizeRaised)
                return;
            minimizedResizeRaised = true;
        }
        windowContext.ExecuteSub("Form_Resize");
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // Win32 deliberately does not report a client resize when minimizing. VB6 does raise Resize,
        // with WindowState already 1. Observe the window so the minimize button and code agree.
        // Before Show, setting the initial state must not raise a form event.
        if (change.Property == WindowStateProperty)
        {
            if (WindowState != WindowState.Minimized)
                minimizedResizeRaised = false;
            else if (IsVisible && !minimizedResizeRaised)
            {
                minimizedResizeRaised = true;
                windowContext.ExecuteSub("Form_Resize");
            }
        }
    }

    public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null)
    {
        windowContext.ExecuteSub(name, args);
    }

    public void ExecuteSubIf(string name, IReadOnlyList<Vb6Value>? args, Func<bool> when) =>
        windowContext.ExecuteSubIf(name, args, when);

    public bool Handles(string name) => windowContext.Handles(name);

    public void ExecuteEvent(string name, Func<IReadOnlyList<Vb6Value>> args, EventCoalescing coalescing = EventCoalescing.None) =>
        windowContext.ExecuteEvent(name, args, coalescing);

    public void ExecuteTimer(string name, IReadOnlyList<Vb6Value>? args = null) => windowContext.ExecuteTimer(name, args);

    public Task<bool> UnloadAsync(int mode) => windowContext.UnloadAsync(mode);

    public void SetContent(Control content) => Content = content;

    public void ShowForm() => Show();

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
