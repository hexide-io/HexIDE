using System;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Serilog;

namespace HexIDE.Infrastructure;

internal sealed class ExitStop(
    IClassicDesktopStyleApplicationLifetime desktop,
    Action disposeFileWatcher,
    Func<Task> stop,
    Action shutdownLogging,
    TimeSpan timeout)
{
    // One wedged server's whole stop: shutdown 2 s, exit 2 s, then LaunchedServerGuard.ExitTimeout (2 s on
    // Windows) for the process to exit before it is ended, so 6 s, with 1 s of scheduling room. A server whose
    // exit is sent waits up to 1 s for its output to end (#4484) in place of the exit's 2 s.
    internal static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(7);
    private bool _stopping;
    private bool _stopped;

    internal void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (_stopped || e.Cancel) return;
        e.Cancel = true;
        if (_stopping) return;
        _stopping = true;
        _ = StopThenShutdownAsync();
    }

    private async Task StopThenShutdownAsync()
    {
        // Even a synchronous stop must retry after the first ShutdownRequested has returned.
        await Task.Yield();
        try
        {
            disposeFileWatcher();
            await stop().WaitAsync(timeout);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Language server stop failed during IDE exit (limit {Timeout}) — exiting", timeout);
        }
        finally
        {
            shutdownLogging();
            _stopped = true;
            desktop.TryShutdown();
        }
    }
}
