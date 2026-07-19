using System;

namespace HexIDE.IDE;

/// <summary>
/// Watches loaded projects' component files for external changes and reloads them (silently when the
/// IDE has no unsaved edits). Runs autonomously once constructed; the only public surface is
/// <see cref="IDisposable.Dispose"/>, called on IDE shutdown to release the OS file watchers.
/// </summary>
public interface IFileWatcherService : IDisposable
{
}
