using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace HexIDE.Tests.Sidecar;

/// <summary>Collects what is logged while it is installed, and puts the previous logger back when disposed.</summary>
/// <remarks>
/// The sidecar reports what it drops or will not rewrite only in the log, so the log is what its tests read. Shared
/// by the sidecar's test classes; the suite does not run tests in parallel, so replacing the static logger is safe.
/// </remarks>
internal sealed class CapturedLog : ILogEventSink, IDisposable
{
    private readonly ILogger previous = Log.Logger;
    public List<string> Messages { get; } = [];

    public CapturedLog() =>
        Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(this).CreateLogger();

    public void Emit(LogEvent logEvent) => Messages.Add(logEvent.RenderMessage());

    public void Dispose() => Log.Logger = previous;
}
