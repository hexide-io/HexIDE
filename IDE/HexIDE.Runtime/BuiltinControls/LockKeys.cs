using System.Runtime.InteropServices;
using Avalonia.Input;

namespace HexIDE.Runtime.BuiltinControls;

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

/// <summary>
/// The toggle state of the lock keys, as a running program's StatusBar reads it.
/// </summary>
/// <remarks>
/// <para>
/// On Windows that is the keyboard's own toggle, which a person's press changes. Avalonia exposes no lock state, so
/// other hosts follow the presses a window has seen, and that part stays with the StatusBar that sees them.
/// </para>
/// <para>
/// <b>In a DEBUG build, automation can set a key's state</b>, and a StatusBar prefers that to the keyboard's (#2296). A
/// key panel's "on" state could not otherwise be live-checked on Windows: <c>press_key</c> raises Avalonia's key events
/// and never touches the operating system's keyboard state, and toggling the real keyboard would change the machine
/// for everything else on it. A shipped build has nothing that could set one, as the automation server it exists for
/// compiles out.
/// </para>
/// </remarks>
public static partial class LockKeys
{
    /// <summary>The key a StatusBar panel of <paramref name="style"/> shows, or null for any other style.</summary>
    public static LockKey? OfPanelStyle(long style) => style switch
    {
        1 => LockKey.CapsLock,
        2 => LockKey.NumLock,
        3 => LockKey.Insert,
        4 => LockKey.ScrollLock,
        _ => null,
    };

    /// <summary>Avalonia's name for <paramref name="key"/>, as a window's key events carry it.</summary>
    public static Key AvaloniaKey(LockKey key) => key switch
    {
        LockKey.CapsLock => Key.CapsLock,
        LockKey.NumLock => Key.NumLock,
        LockKey.Insert => Key.Insert,
        _ => Key.Scroll,
    };

    private static int VirtualKey(LockKey key) => key switch
    {
        LockKey.CapsLock => 0x14,
        LockKey.NumLock => 0x90,
        LockKey.Insert => 0x2D,
        _ => 0x91,
    };

    [LibraryImport("user32.dll")]
    private static partial short GetKeyState(int key);

    /// <summary>
    /// The keyboard's own toggle for <paramref name="key"/> on Windows, or null on a host that offers none to read.
    /// </summary>
    public static bool? Keyboard(LockKey key) => OperatingSystem.IsWindows() ? (GetKeyState(VirtualKey(key)) & 1) != 0 : null;

#if DEBUG
    private static readonly Lock Gate = new();
    private static readonly Dictionary<LockKey, bool> Overrides = [];

    /// <summary>Raised after a key's state is set or cleared, so a StatusBar on screen repaints at once.</summary>
    public static event Action? Changed;

    /// <summary>The state automation set for <paramref name="key"/>, or null when it set none.</summary>
    public static bool? Override(LockKey key)
    {
        lock (Gate) return Overrides.TryGetValue(key, out var on) ? on : null;
    }

    /// <summary>Sets <paramref name="key"/>'s state as a running program reads it, or with null returns it to the keyboard.</summary>
    public static void SetOverride(LockKey key, bool? on)
    {
        lock (Gate)
        {
            if (on is { } state)
                Overrides[key] = state;
            else
                Overrides.Remove(key);
        }
        Changed?.Invoke();
    }
#endif
}
