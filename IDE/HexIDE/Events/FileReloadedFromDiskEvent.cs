using HexIDE.IDE;

namespace HexIDE.Events;

/// <summary>
/// Published after the file watcher reloads a component file from disk (silently or after a conflict
/// resolution). Lets interested views — Project Explorer, Object Browser, etc. — refresh.
/// </summary>
public sealed class FileReloadedFromDiskEvent(string absolutePath) : IEvent
{
    public string AbsolutePath { get; } = absolutePath;
}
