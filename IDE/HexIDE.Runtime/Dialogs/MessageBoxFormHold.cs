using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform;
using Avalonia.Threading;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Dialogs;

// Separate from Form.Enabled: answering a box must not overwrite the program's own setting (#834).
internal sealed class MessageBoxFormHold : IDisposable
{
    private static readonly AttachedProperty<int> CountProperty =
        AvaloniaProperty.RegisterAttached<Control, Control, int>("MessageBoxHoldCount");
    private static readonly AttachedProperty<State?> StateProperty =
        AvaloniaProperty.RegisterAttached<Control, Control, State?>("MessageBoxHoldState");

    // The reference assembly hides this native API; use the same local bridge as WindowManager (#1366).
    private static readonly Action<IWindowImpl, bool> setNativeEnabled = typeof(IWindowImpl)
        .GetMethod("SetEnabled")!.CreateDelegate<Action<IWindowImpl, bool>>();
    private static readonly AsyncLocal<MessageBoxFormHold?> current = new();

    private readonly List<State> states = [];
    private readonly MessageBoxFormHold? previous;
    private bool disposed;

    internal MessageBoxFormHold(IReadOnlyList<VbObject> forms) : this(forms, ApplyNativeEnabled) { }

    internal MessageBoxFormHold(IReadOnlyList<VbObject> forms, Action<Window, bool> nativeEnabled)
    {
        previous = current.Value;
        while (previous is { disposed: true })
            previous = previous.previous;
        current.Value = this;
        foreach (var form in forms)
            if (FormRoot.View(form.FormWindow) is Control root)
            {
                var state = root.GetValue(StateProperty);
                if (state is null)
                {
                    state = new State(root, form.FormWindow as Window, nativeEnabled);
                    root.SetValue(StateProperty, state);
                }
                states.Add(state);
                root.SetValue(CountProperty, root.GetValue(CountProperty) + 1);
            }
    }

    internal static bool IsHeld(Control root) => root.GetValue(CountProperty) > 0;

    // VB mouse bridges observe handled events too; they must respect the hold on their form root.
    internal static bool IsInputHeld(Control control) =>
        control.FindLogicalAncestorOfType<IFormView>() is Control root && IsHeld(root);

    // Hold input at the root without disabling its controls, which would grey them (#834 review).
    internal static void HoldInput(Control root)
    {
        foreach (var input in new RoutedEvent[] { InputElement.PointerPressedEvent, InputElement.PointerReleasedEvent,
                     InputElement.PointerMovedEvent, InputElement.PointerWheelChangedEvent, InputElement.KeyDownEvent,
                     InputElement.KeyUpEvent, InputElement.TextInputEvent })
            root.AddHandler(input, new EventHandler<RoutedEventArgs>((_, e) => e.Handled |= IsHeld(root)),
                RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    // The host chooses the owner before disabling native windows can change which window is active.
    internal static void EnterNative()
    {
        if (current.Value is { disposed: false } hold)
            foreach (var state in hold.states)
                state.HoldNative();
    }

    internal static void RefreshNative(Window window)
    {
        if (FormRoot.View(window) is Control root)
            root.GetValue(StateProperty)?.Refresh();
    }

    internal static void ApplyNativeEnabled(Window window, bool enabled)
    {
        if (window.PlatformImpl is { } platform)
            setNativeEnabled(platform, enabled);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (ReferenceEquals(current.Value, this))
            current.Value = previous;
        while (current.Value is { disposed: true } ended)
            current.Value = ended.previous;
        foreach (var state in states)
            state.Release();
    }

    private sealed class State(Control root, Window? window, Action<Window, bool> nativeEnabled)
    {
        private bool native;
        private bool queued;

        internal void HoldNative()
        {
            native = true;
            Refresh();
        }

        internal void Refresh()
        {
            if (!native || !ReferenceEquals(root.GetValue(StateProperty), this)
                || window is null || !ReferenceEquals(FormRoot.View(window), root))
                return;
            var enabled = !IsHeld(root) && window.IsEnabled;
            foreach (var child in window.OwnedWindows)
                if (child.IsDialog && child.PlatformImpl is not null)
                    enabled = false;
            nativeEnabled(window, enabled);
        }

        internal void Release()
        {
            root.SetValue(CountProperty, root.GetValue(CountProperty) - 1);
            Refresh();
            if (!native || window is null)
            {
                ClearIfReleased();
                return;
            }
            if (queued)
                return;
            queued = true;
            // ShowDialog completes inside Closed, before Avalonia removes the child and enables its owner again.
            // Reapply after that cleanup, before the next input. A closed form need not stay alive for this job.
            var weak = new WeakReference<State>(this);
            Dispatcher.UIThread.Post(() =>
            {
                if (weak.TryGetTarget(out var state))
                {
                    state.queued = false;
                    state.Refresh();
                    state.ClearIfReleased();
                }
            }, DispatcherPriority.Send);
        }

        private void ClearIfReleased()
        {
            if (ReferenceEquals(root.GetValue(StateProperty), this) && !IsHeld(root))
                root.ClearValue(StateProperty);
        }
    }
}
