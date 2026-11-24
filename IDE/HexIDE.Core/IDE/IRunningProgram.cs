using System;
using System.Collections.Generic;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.IDE;

/// <summary>Questions the host can ask a running program, on the UI thread, without knowing its engine.</summary>
public interface IRunningProgram
{
    /// <summary>Whether this object is part of a running program, and its stable name when it has one. That is a running
    /// form's window, or a UserControl's root on one (<see cref="RunningForm.IsRunning"/> true), or a window carrying a
    /// form's name and no program (false). A walk up from a control to a running ancestor relies on the UserControl case.
    /// Other objects return null.</summary>
    RunningForm? Form(object window);

    /// <summary>The activity behind a form or one of its program dialogs; null when no program is behind it.
    /// A null window asks for any running program.</summary>
    IProgramActivity? Activity(object? window);

    /// <summary>Shown, non-minimised form windows of this window's program, least recently activated first.</summary>
    IReadOnlyList<object> ShownWindows(object window);

    /// <summary>A program control's own name, or null for anything else.</summary>
    string? ControlName(object element);

    /// <summary>Whether this is a menu item a program created.</summary>
    bool IsMenuItem(object element);

    /// <summary>Whether this is a program control that never shows at run time.</summary>
    bool NeverShows(object element);

    /// <summary>A lock key's reading and where it comes from; null when each window follows its own presses.</summary>
    (bool? On, LockKeySource Source) ReadLockKey(LockKey key);

    /// <summary>Sets a lock key's reading, or returns it to the keyboard with null, in DEBUG builds only.</summary>
    void SetLockKey(LockKey key, bool? on);

    /// <summary>The MousePointer a program's cursor was made from, and what it shows, or null for any other cursor.</summary>
    ProgramPointer? PointerOf(object cursor);

    /// <summary>Whether a program's FontName could name this family here.</summary>
    bool CanNameFont(string? family);

    /// <summary>Text as the program prints it. Subscribers keep their own position or buffer.</summary>
    event Action<string>? Written;
}

public sealed record RunningForm(object Window, string? Name, bool IsRunning);

public sealed record ProgramPointer(VBCursorType Pointer, string Shows);

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
    public IReadOnlyList<object> ShownWindows(object window) => [];
    public RunningForm? Form(object window) => null;
    public IProgramActivity? Activity(object? window) => null;
    public string? ControlName(object element) => null;
    public bool IsMenuItem(object element) => false;
    public bool NeverShows(object element) => false;
    public (bool? On, LockKeySource Source) ReadLockKey(LockKey key) => (null, LockKeySource.Window);
    public void SetLockKey(LockKey key, bool? on) { }
    public ProgramPointer? PointerOf(object cursor) => null;
    public bool CanNameFont(string? family) => false;
    public event Action<string>? Written { add { } remove { } }
}
