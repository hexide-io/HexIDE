using Serilog;

namespace HexIDE.Addins;

/// <summary>
/// Checks the revocation list again every hour while HexIDE runs, so a session left open for days still hears of a
/// revocation (#5577). The interval is fixed: a setting would only let something stretch it. Due-ness is measured against
/// the wall clock on a short tick rather than by one long timer, so a machine waking from sleep checks straight away, and a
/// clock that went backwards counts as due. A small random offset keeps installs from checking at the same minute.
/// </summary>
internal sealed class RevocationMonitor : IDisposable
{
    internal static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    internal static readonly TimeSpan MaxJitter = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);

    private readonly Action _check;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<TimeSpan> _jitter;
    private readonly CancellationTokenSource _stop = new();
    private DateTimeOffset _last;
    private TimeSpan _wait;
    private int _checking;

    /// <param name="check">Runs one check. Called off the UI thread, never twice at once.</param>
    /// <param name="now">The wall clock; tests pass their own.</param>
    /// <param name="jitter">The offset added to the next interval; tests pass their own.</param>
    public RevocationMonitor(Action check, Func<DateTimeOffset>? now = null, Func<TimeSpan>? jitter = null)
    {
        _check = check;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _jitter = jitter ?? (() => TimeSpan.FromSeconds(Random.Shared.Next((int)MaxJitter.TotalSeconds)));
        // Startup has just checked, so the first check here is an interval away.
        _last = _now();
        _wait = Interval + _jitter();
    }

    /// <summary>Runs a check when one is due, and says whether it did.</summary>
    internal bool TickOnce()
    {
        var now = _now();
        if (now >= _last && now - _last < _wait)
            return false;
        if (Interlocked.Exchange(ref _checking, 1) == 1)
            return false;
        try
        {
            _last = now;
            _wait = Interval + _jitter();
            _check();
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "RevocationMonitor: the revocation check failed — the active list stays");
            return true;
        }
        finally
        {
            Volatile.Write(ref _checking, 0);
        }
    }

    public void Start() => _ = Task.Run(async () =>
    {
        using var timer = new PeriodicTimer(Tick);
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token))
                TickOnce();
        }
        catch (OperationCanceledException) { }
    });

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }
}
