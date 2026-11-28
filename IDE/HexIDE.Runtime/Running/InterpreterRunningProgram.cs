using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Dialogs;
using HexIDE.IDE;

namespace HexIDE.Runtime.Running;

/// <summary>The interpreter's running-program questions. Moves to the provider with the engine at split step 2b.</summary>
public sealed class InterpreterRunningProgram : IRunningProgram
{
    public InterpreterRunningProgram() => _ = opened;

    // A single-view host window holds its form's execution root as content (#4068), so ask FormRoot, not the window's type.
    public RunningForm? Form(object window) => window is AvaloniaObject form
        && (FormRoot.Execution(window) is not null || window is Window && VBProps.GetName(form) is not null)
        ? new RunningForm(window, VBProps.GetName(form), FormRoot.Execution(window) is not null) : null;

    public IReadOnlyList<object> ShownWindows(object window) =>
        FormRoot.View(window)?.Context.Program?.ShownWindows() ?? [];

    public IProgramActivity? Activity(object? window)
    {
        if (window is null)
            return PumpOf(OpenWindows().FirstOrDefault(w => FormRoot.Execution(w) is { Events: not null }));
        if (FormRoot.Execution(window) is not null)
            return PumpOf(window);
        if (window is not Window box || !ProgramDialogs.IsProgramBox(box.Content))
            return null;
        for (var at = box.Owner as Window; at is not null; at = at.Owner as Window)
            if (FormRoot.Execution(at) is { Events: not null })
                return PumpOf(at);
        return Activity(null);
    }

    public string? ControlName(object element) => element is AvaloniaObject control ? VBProps.GetName(control) : null;

    public bool IsMenuItem(object element) => element is VBMenu;

    public bool NeverShows(object element) => element is VBTimer or VBCommonDialog;

    public (bool? On, LockKeySource Source) ReadLockKey(LockKey key)
    {
#if DEBUG
        if (LockKeys.Override(key) is { } set)
            return (set, LockKeySource.Set);
#endif
        return LockKeys.Keyboard(key) is { } keyboard
            ? (keyboard, LockKeySource.Keyboard) : (null, LockKeySource.Window);
    }

    public void SetLockKey(LockKey key, bool? on)
    {
#if DEBUG
        LockKeys.SetOverride(key, on);
#endif
    }

    public ProgramPointer? PointerOf(object cursor) => VBCursorTypeExtensions.PointerOf(cursor as Cursor) is { } pointer
        ? new(pointer, pointer.ToStandardCursorType().ToString()) : null;

    public bool CanNameFont(string? family) => VBFontExtensions.IsNameable(family);

    private static readonly HashSet<Window> opened = Track();

    private static HashSet<Window> Track()
    {
        var windows = new HashSet<Window>();
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => windows.Add(window));
        Window.WindowClosedEvent.AddClassHandler<Window>((window, _) => windows.Remove(window));
        return windows;
    }

    private static IEnumerable<Window> OpenWindows() =>
        ((Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
            ?.Windows ?? []).Concat(opened).Distinct().Where(w => w.IsVisible);

    private static IProgramActivity? PumpOf(object? window) =>
        FormRoot.Execution(window) is { Events: { } pump } ? pump : null;

    public event Action<string>? Written
    {
        add => VBDebugConsole.Written += value;
        remove => VBDebugConsole.Written -= value;
    }
}
