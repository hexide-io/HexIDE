using System;
using Avalonia.Controls;

namespace HexIDE.Automation;

/// <summary>
/// What Avalonia's native Wayland backend, opt-in behind <c>--platform wayland</c>, can't tell a program. Wayland gives a
/// client no position for its own windows: at 12.1.4 the backend's <c>Position</c> is always 0,0, its <c>PointToScreen</c>
/// hands back the point it was given, and it reads neither of the numbers the compositor sends to say where a popup went
/// (#3575, #3576).
/// </summary>
internal static class NativeWayland
{
    /// <summary>Stands in for the backend in a headless test, which can't load it.</summary>
    internal static Func<TopLevel, bool>? ForTest;

    /// <summary>
    /// Whether <paramref name="topLevel"/> is on that backend. It is a check on the name of the platform object's type,
    /// because this project doesn't reference the backend: the two executables that offer the flag do.
    /// </summary>
    public static bool Hosts(TopLevel topLevel) =>
        ForTest?.Invoke(topLevel) ?? topLevel.PlatformImpl?.GetType().Namespace == "Avalonia.Wayland";

    /// <summary>Why a popup has no place the tools can give, in the words of a reply's sentence.</summary>
    internal const string NoPopupPlace =
        "native Wayland's compositor places a popup itself and Avalonia doesn't pass on where it went";
}
