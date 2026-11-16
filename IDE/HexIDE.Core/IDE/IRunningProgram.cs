using System;
using System.Collections.Generic;

namespace HexIDE.IDE;

/// <summary>Questions the host can ask a running program, on the UI thread, without knowing its engine.</summary>
public interface IRunningProgram
{
    /// <summary>Whether this window is a running form, and its stable name when it has one.
    /// Name metadata alone does not make a window a running form; other objects return null.</summary>
    RunningForm? Form(object window);

    /// <summary>The activity behind a form or one of its program dialogs; null when no program is behind it.
    /// A null window asks for any running program.</summary>
    IProgramActivity? Activity(object? window);

    /// <summary>Text as the program prints it. Subscribers keep their own position or buffer.</summary>
    event Action<string>? Written;
}

public sealed record RunningForm(object Window, string? Name, bool IsRunning);

/// <summary>Read-only activity observations. Walk tokens are opaque identities, valid for this activity's lifetime.</summary>
public interface IProgramActivity
{
    bool IsIdle { get; }
    bool IsWaitingOnWindow { get; }
    IReadOnlyCollection<object> Live { get; }
    IReadOnlyCollection<object> Loops { get; }
    IReadOnlyCollection<object> AtDoEvents { get; }
    IReadOnlyList<string> Busy { get; }
    IReadOnlyList<string> WaitingHandlers { get; }
    IReadOnlyList<string> BusySince(IReadOnlyCollection<object> before);
    IReadOnlyList<string> FinishedSince(IReadOnlyCollection<object> before);
}

/// <summary>The host's installed program observer. An absent provider has no forms, activity or output.</summary>
public static class RunningProgram
{
    public static IRunningProgram Current { get; set; } = new EmptyRunningProgram();
}

/// <summary>An absent engine answers without discovering windows, waiting or emitting output.</summary>
public sealed class EmptyRunningProgram : IRunningProgram
{
    public RunningForm? Form(object window) => null;
    public IProgramActivity? Activity(object? window) => null;
    public event Action<string>? Written { add { } remove { } }
}
