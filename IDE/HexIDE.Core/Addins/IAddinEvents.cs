namespace HexIDE.Addins;

public interface IAddinEvents
{
    event Action<AddinProjectEventArgs>? ProjectLoaded;
    event Action<AddinProjectEventArgs>? ProjectUnloaded;
    event Action<AddinFileEventArgs>? FileOpened;
    event Action<AddinFileEventArgs>? FileClosed;
    event Action? RunStarted;
    event Action? RunStopped;
}

public record AddinProjectEventArgs(string ProjectPath, string ProjectName);
/// <param name="FilePath">
/// The document's own file, or empty when it has none and when the tab is not a VB6 document at all. It used
/// to be the tab's title, which is neither a path nor stable: it is localized and carries the project name.
/// </param>
/// <param name="FileName">The document's own VB6 name, or the tab's title where there is no document.</param>
/// <param name="Project">The project holding the document, by name, or null where there is no document.</param>
public record AddinFileEventArgs(string FilePath, string FileName, string? Project = null);
