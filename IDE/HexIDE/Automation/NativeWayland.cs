using System;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

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

    internal static Func<TopLevel, bool>? PlacedForTest = null;

    // PopupImpl's position remains default until xdg_popup.configure (#4627). The platform property is internal.
    private static readonly PropertyInfo? PlatformPosition = typeof(IWindowBaseImpl)
        .GetProperty("Position", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    public static bool Placed(TopLevel popup) => !Hosts(popup)
        || (PlacedForTest?.Invoke(popup) ?? popup.PlatformImpl is IWindowBaseImpl platform
            && PlatformPosition?.GetValue(platform) is PixelPoint position && position != default);

    /// <summary>
    /// Whether <paramref name="topLevel"/> is on that backend. It is a check on the name of the platform object's type,
    /// because this project doesn't reference the backend: the two executables that offer the flag do.
    /// </summary>
    public static bool Hosts(TopLevel topLevel) =>
        ForTest?.Invoke(topLevel) ?? topLevel.PlatformImpl?.GetType().Namespace == "Avalonia.Wayland";
}
