using System.Runtime.CompilerServices;
using HexIDE.IDE;

namespace HexIDE.Integration.Tests;

internal static class TemporaryProfile
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hexide-tests-{Environment.ProcessId}-{Guid.NewGuid():N}", "HexIDE");
        UserDataPath.RedirectTo(directory);
        Directory.CreateDirectory(directory);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(Path.GetDirectoryName(directory)!, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };
    }
}
