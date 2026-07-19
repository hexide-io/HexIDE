using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Headless;

namespace HexIDE.Tests;

/// <summary>
/// One-time Avalonia headless initialization for tests that depend on
/// Avalonia infrastructure (e.g. AssetLoader for avares:// URIs).
/// </summary>
internal static class AvaloniaTestSetup
{
    private static bool s_initialized;
    private static readonly object s_lock = new();

    internal static void EnsureInitialized()
    {
        if (s_initialized) return;
        lock (s_lock)
        {
            if (s_initialized) return;
            AppBuilder.Configure<Application>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithoutStarting();
            s_initialized = true;
        }
    }
}
