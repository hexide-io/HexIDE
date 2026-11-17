using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Wayland;
using Serilog;

namespace HexIDE.Startup;

internal static class DisplayBackend
{
    internal static string Select(string? argument, string? environment) =>
        (argument ?? environment)?.ToLowerInvariant() switch
        {
            "auto" => "auto",
            "wayland" => "wayland",
            _ => "x11"
        };

    internal static int Run(string selection, Func<int> start, Func<string, string> formatFailure, Action<string> logFailure)
    {
        try
        {
            return HexIDE.Runtime.UiThread.Run(start);
        }
        catch (AvaloniaWaylandException ex) when (OperatingSystem.IsLinux() && selection == "wayland")
        {
            var message = formatFailure(ex.Message);
            Console.Error.WriteLine(message);
            logFailure(message);
            return 2;
        }
    }

    internal static AppBuilder UseDisplayBackend(this AppBuilder builder, string? argument = null, ILogger? logger = null)
    {
        var selection = Select(argument, Environment.GetEnvironmentVariable("HEXIDE_PLATFORM"));
        builder.UsePlatformDetect();
        if (OperatingSystem.IsLinux())
        {
            if (selection == "auto")
                builder.UseWaylandWithFallback();
            else if (selection == "wayland")
                builder.UseWayland();
        }

        return builder.AfterSetup(_ =>
        {
            // The first window identifies the actual backend after auto's fallback, and the IDE's
            // logger is configured by then (AfterSetup itself precedes app initialization).
            IDisposable? subscription = null;
            subscription = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                (logger ?? Log.Logger).Information("Display backend started: {Backend} (requested {Selection})",
                    window.PlatformImpl?.GetType().FullName, selection);
                subscription?.Dispose();
            });
        });
    }
}
