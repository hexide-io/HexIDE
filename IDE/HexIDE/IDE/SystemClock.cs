using System;

namespace HexIDE.IDE;

/// <summary>The real wall-clock <see cref="IClock"/> used in production.</summary>
public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
