using System.Runtime.InteropServices;
using HexIDE.IDE;

namespace HexIDE.Desktop;

/// <summary>
/// Gives a branded build's windows the identity its installed shortcut carries, so a running window groups with its
/// pinned shortcut. Stock declares none, and goes on grouping as it always has.
/// </summary>
internal static partial class WindowsTaskbarIdentity
{
    public static void Apply(DistributionIdentity distribution)
    {
        // Cosmetic if it fails, so a failure doesn't stop the IDE starting.
        if (OperatingSystem.IsWindows() && distribution.AppUserModelId is { } id)
            _ = SetCurrentProcessExplicitAppUserModelID(id);
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetCurrentProcessExplicitAppUserModelID(string id);
}
