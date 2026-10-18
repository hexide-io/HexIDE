using System.Diagnostics;

namespace HexIDE.Dap;

/// <summary>
/// Owns one adapter child process and its DAP connection. There is no handshake, retry, or restart policy.
/// Dispose only after any desired DAP disconnect exchange: disposal closes stdin, then kills an unresponsive
/// process tree after a grace period. Completion includes cleanup, exit status, and the bounded stderr tail.
/// </summary>
public sealed class DapProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly DapProcessOptions _options;
    private readonly TaskCompletionSource _stop = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _stderrStop = new();
    private readonly object _diagnosticsLock = new();
    private readonly Task _stderrReader;
    private string _stderr = "";
    private long _dropped;
    private Exception? _stderrFailure;

    private DapProcess(Process process, DapProcessOptions options)
    {
        (_process, _options) = (process, options);
        ProcessId = process.Id;
        Connection = new(process.StandardOutput.BaseStream, process.StandardInput.BaseStream, options.Connection);
        // Read chunks rather than lines: even an adapter writing a never-ending line has bounded retention.
        _stderrReader = ReadStandardErrorAsync();
        Completion = MonitorAsync();
    }

    public int ProcessId { get; }
    public DapConnection Connection { get; }
    public Task<DapProcessResult> Completion { get; }
    public string StandardErrorTail { get { lock (_diagnosticsLock) return _stderr; } }
    public long DroppedStandardErrorCharacters { get { lock (_diagnosticsLock) return _dropped; } }

    /// <summary>Starts a process and takes ownership. Invalid configuration or launch failure throws.</summary>
    public static DapProcess Start(DapProcessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var info = new ProcessStartInfo(options.FileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (options.WorkingDirectory is { } directory) info.WorkingDirectory = directory;
        foreach (var argument in options.Arguments)
        {
            ArgumentNullException.ThrowIfNull(argument);
            info.ArgumentList.Add(argument);
        }
        foreach (var (name, value) in options.Environment)
            if (value is null) info.Environment.Remove(name); else info.Environment[name] = value;

        var process = new Process { StartInfo = info };
        try
        {
            if (!process.Start()) throw new IOException("Could not start the debug adapter.");
            return new(process, options);
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* Start failed, or the process already exited. */ }
            finally { process.Dispose(); }
            throw;
        }
    }

    private async Task ReadStandardErrorAsync()
    {
        var buffer = new char[2048];
        try
        {
            while (true)
            {
                var count = await _process.StandardError.ReadAsync(buffer, _stderrStop.Token).ConfigureAwait(false);
                if (count == 0) return;
                lock (_diagnosticsLock)
                {
                    var combined = string.Concat(_stderr, buffer.AsSpan(0, count));
                    var discard = Math.Max(0, combined.Length - _options.StandardErrorTailCharacters);
                    _dropped += discard;
                    _stderr = combined[discard..];
                }
            }
        }
        catch (Exception ex)
        {
            if (!_stderrStop.IsCancellationRequested)
            {
                _stderrFailure = ex;
                _stop.TrySetResult(); // An undrained stderr pipe could otherwise stall the adapter.
            }
        }
    }

    private async Task<DapProcessResult> MonitorAsync()
    {
        Exception? cleanupFailure = null;
        var forced = false;
        int? exitCode = null;
        Exception? connectionFailure = null;
        try
        {
            var exited = _process.WaitForExitAsync();
            var first = await Task.WhenAny(exited, Connection.Completion, _stop.Task).ConfigureAwait(false);
            // Let already-written responses/events reach the connection before closing its stdout.
            // Descendants may inherit pipe handles, so process exit alone does not guarantee pipe EOF.
            if (first == exited)
            {
                try { await Connection.Completion.WaitAsync(_options.ShutdownTimeout).ConfigureAwait(false); }
                catch (Exception ex) { cleanupFailure ??= ex; }
            }
            try { await Connection.DisposeAsync().AsTask().WaitAsync(_options.ShutdownTimeout).ConfigureAwait(false); }
            catch (Exception ex) { cleanupFailure ??= ex; }

            try { await exited.WaitAsync(_options.ShutdownTimeout).ConfigureAwait(false); }
            catch (TimeoutException)
            {
                try
                {
                    if (!_process.HasExited)
                    {
                        try
                        {
                            _process.Kill(entireProcessTree: true);
                            forced = true;
                        }
                        catch (InvalidOperationException) when (_process.HasExited)
                        { /* The adapter exited between the check and Kill. */ }
                    }
                    await exited.WaitAsync(_options.ShutdownTimeout).ConfigureAwait(false);
                }
                catch (Exception ex) { cleanupFailure ??= ex; }
            }
            catch (Exception ex) { cleanupFailure ??= ex; }

            try { if (_process.HasExited) exitCode = _process.ExitCode; }
            catch (Exception ex) { cleanupFailure ??= ex; }
            try { await _stderrReader.WaitAsync(_options.ShutdownTimeout).ConfigureAwait(false); }
            catch (Exception ex) { cleanupFailure ??= ex; }
            if (Connection.Completion.IsCompletedSuccessfully) connectionFailure = Connection.Completion.Result;
        }
        finally
        {
            _stderrStop.Cancel();
            try { _process.StandardError.Dispose(); }
            catch (Exception ex) { cleanupFailure ??= ex; }
            try { _process.Dispose(); }
            catch (Exception ex) { cleanupFailure ??= ex; }
            // Cancellation has been observed before disposing the source; no read uses a disposed source.
            try { await _stderrReader.WaitAsync(_options.ShutdownTimeout).ConfigureAwait(false); }
            catch (Exception ex) { cleanupFailure ??= ex; }
            if (_stderrReader.IsCompleted) _stderrStop.Dispose();
        }
        return new(exitCode, forced, connectionFailure, cleanupFailure ?? _stderrFailure,
            StandardErrorTail, DroppedStandardErrorCharacters);
    }

    public async ValueTask DisposeAsync()
    {
        _stop.TrySetResult();
        await Completion.ConfigureAwait(false);
    }
}
