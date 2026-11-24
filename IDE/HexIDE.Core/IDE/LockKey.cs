namespace HexIDE.IDE;

/// <summary>The keys whose toggle a StatusBar's key panels show: Style 1 to 4.</summary>
public enum LockKey
{
    CapsLock,
    NumLock,
    Insert,
    ScrollLock,
}

/// <summary>Where a running program's reading of a <see cref="LockKey"/> comes from.</summary>
public enum LockKeySource
{
    /// <summary>Automation set it (DEBUG builds only).</summary>
    Set,
    /// <summary>The keyboard's own toggle, on Windows.</summary>
    Keyboard,
    /// <summary>The presses each window has seen, on a host with no toggle to read.</summary>
    Window,
}

