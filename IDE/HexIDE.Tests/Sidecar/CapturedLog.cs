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
    private readonly object gate = new();
    private readonly List<(string Fragment, TaskCompletionSource Seen)> awaited = [];
    public List<string> Messages { get; } = [];

    public CapturedLog() =>
        Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(this).CreateLogger();

    public void Emit(LogEvent logEvent)
    {
        var message = logEvent.RenderMessage();
        lock (gate)
        {
            Messages.Add(message);
            foreach (var (fragment, seen) in awaited)
                if (message.Contains(fragment, StringComparison.Ordinal))
                    seen.TrySetResult();
        }
    }

    /// <summary>
    /// Completes once a message containing <paramref name="fragment"/> is logged, from whatever thread logs it: what a test
    /// awaits for a line logged off its own thread, rather than reading the messages until it appears.
    /// </summary>
    public Task Logged(string fragment)
    {
        var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (Messages.Any(message => message.Contains(fragment, StringComparison.Ordinal)))
                seen.SetResult();
            else
                awaited.Add((fragment, seen));
        }
        return seen.Task;
    }

    public void Dispose() => Log.Logger = previous;
}
