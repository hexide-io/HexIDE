using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace HexIDE.IDE;

/// <summary>
/// Opens the log folder in the platform's own file manager, for Help → Open Log Folder (#3921). An interface of its own
/// rather than a bound <c>Func&lt;DirectoryInfo, Task&lt;bool&gt;&gt;</c>, which the composition would hand to any later
/// constructor asking for that general delegate type.
/// </summary>
public interface ILogFolderOpener
{
    /// <summary>Opens <paramref name="directory"/>. False when it doesn't exist, or the platform's opener is missing or failed.</summary>
    Task<bool> OpenAsync(DirectoryInfo directory);
}

/// <summary>
/// Avalonia's launcher, which passes the path as one argument to ShellExecute, <c>open</c> or <c>xdg-open</c>, so no
/// command line is built from a folder name the brand chooses.
/// </summary>
internal sealed class PlatformLogFolderOpener : ILogFolderOpener
{
    public Task<bool> OpenAsync(DirectoryInfo directory) =>
        TopLevel.GetTopLevel(Static.MainView)!.Launcher.LaunchDirectoryInfoAsync(directory);
}
