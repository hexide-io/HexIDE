using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;

namespace HexIDE.IDE;

/// <summary>The interpreter's running-program questions. Moves to the provider with the engine at split step 2b.</summary>
public sealed class InterpreterRunningProgram : IRunningProgram
{
    public InterpreterRunningProgram() => _ = opened;

    public RunningForm? Form(object window) => window is AvaloniaObject form
        && (window is IModuleExecutionRoot || window is Window && VBProps.GetName(form) is not null)
        ? new RunningForm(window, VBProps.GetName(form), window is IModuleExecutionRoot) : null;

    public IProgramActivity? Activity(object? window)
    {
        if (window is null)
            return PumpOf(OpenWindows().FirstOrDefault(w => w is IModuleExecutionRoot { Events: not null }));
        if (window is IModuleExecutionRoot)
            return PumpOf(window);
        if (window is not Window box || !ProgramDialogs.IsProgramBox(box.Content))
            return null;
        for (var at = box.Owner as Window; at is not null; at = at.Owner as Window)
            if (at is IModuleExecutionRoot { Events: not null })
                return PumpOf(at);
        return Activity(null);
    }

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
        window is IModuleExecutionRoot { Events: { } pump } ? pump : null;

    public event Action<string>? Written
    {
        add => VBDebugConsole.Written += value;
        remove => VBDebugConsole.Written -= value;
    }
}
