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
public record AddinFileEventArgs(string FilePath, string FileName);
