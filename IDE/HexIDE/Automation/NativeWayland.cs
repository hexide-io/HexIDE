using System;
using Avalonia.Controls;

namespace HexIDE.Automation;

/// <summary>
/// What Avalonia's native Wayland backend, opt-in behind <c>--platform wayland</c>, can't tell a program. Wayland gives a
/// client no position for its own windows (#3576). Popups report where the compositor placed them since Avalonia 12.1.5
/// (AvaloniaUI/Avalonia#22291), including screen-coordinate conversion.
/// </summary>
internal static class NativeWayland
{
    /// <summary>Stands in for the backend in a headless test, which can't load it.</summary>
    internal static Func<TopLevel, bool>? ForTest = null;

    /// <summary>
    /// Whether <paramref name="topLevel"/> is on that backend. It is a check on the name of the platform object's type,
    /// because this project doesn't reference the backend: the two executables that offer the flag do.
    /// </summary>
    public static bool Hosts(TopLevel topLevel) =>
        ForTest?.Invoke(topLevel) ?? topLevel.PlatformImpl?.GetType().Namespace == "Avalonia.Wayland";
}
