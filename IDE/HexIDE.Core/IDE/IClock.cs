using System;

namespace HexIDE.IDE;

/// <summary>
/// Abstracts the system clock so time-dependent logic (e.g. the file-watcher
/// <c>ChangeCoalescer</c>'s debounce window) can be unit-tested with virtual time.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
