using Avalonia;
using HexIDE.IDE;

namespace HexIDE.Desktop;

/// <summary>
/// Gives a branded build's windows the class its menu entry names, so a Linux desktop groups a running window with
/// that entry. Stock passes no options and keeps Avalonia's default, the name of its entry assembly.
/// </summary>
internal static class LinuxWindowIdentity
{
    public static AppBuilder Apply(AppBuilder builder, DistributionIdentity distribution)
        => Options(distribution) is { } options ? builder.With(options) : builder;

    internal static X11PlatformOptions? Options(DistributionIdentity distribution)
        => distribution.WindowClass is { } windowClass ? new X11PlatformOptions { WmClass = windowClass } : null;
}
