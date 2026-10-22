namespace HexIDE.Dap;

/// <summary>An explicit command, launched directly without shell interpretation or executable discovery.</summary>
public sealed record DapProcessOptions(string FileName)
{
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public string? WorkingDirectory { get; init; }
    /// <summary>Overrides inherited environment variables; null removes a variable.</summary>
    public IReadOnlyDictionary<string, string?> Environment { get; init; } = new Dictionary<string, string?>();
    public DapConnectionOptions Connection { get; init; } = new();
    public DapCapture? Capture { get; init; }
    /// <summary>Bound for each cleanup stage: graceful exit, forced exit, and pipe draining.</summary>
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(2);
    public int StandardErrorTailCharacters { get; init; } = 16 * 1024;

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(FileName);
        ArgumentNullException.ThrowIfNull(Arguments);
        ArgumentNullException.ThrowIfNull(Environment);
        ArgumentNullException.ThrowIfNull(Connection);
        Connection.Validate();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(StandardErrorTailCharacters);
        if (ShutdownTimeout <= TimeSpan.Zero || ShutdownTimeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(ShutdownTimeout));
    }
}

/// <summary>Final process diagnostics. A nonzero exit is retained even if the DAP stream ended first.</summary>
public sealed record DapProcessResult(int? ExitCode, bool ForcedTermination, Exception? ConnectionFailure,
    Exception? CleanupFailure, string StandardErrorTail, long DroppedStandardErrorCharacters);
