#if DEBUG
using Avalonia.Threading;

namespace HexIDE.Automation;

/// <summary>
/// What the main window's status bar shows of the automation server (#2451): that it is listening, on which port and
/// for which session, when the last call ran, which calls from other sessions it refused, and whether a call is running.
/// </summary>
/// <remarks>
/// <para>
/// <b>DEBUG only, like the server it describes.</b> A distributed build has no server, so it has nothing to show and
/// carries no hidden section.
/// </para>
/// <para>
/// The server reports to this from its own threads, and every change is made on the UI thread, where the section reads
/// it. One instance, <see cref="Current"/>, as there is one server: the server lives in HexIDE.Desktop and the status
/// bar here, and neither builds the other.
/// </para>
/// </remarks>
public sealed class AutomationServerStatus
{
    public static AutomationServerStatus Current { get; } = new();

    /// <summary>How long the activity cue stays on at least, so a call too short to see still shows.</summary>
    internal static TimeSpan BusyAtLeast = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// How long the cue stays on after the last call of a burst ends, so a run of calls shows one cue, not a flash per call.
    /// </summary>
    internal static TimeSpan BusyTail = TimeSpan.FromMilliseconds(300);

    private int _inFlight;
    private DateTimeOffset _busySince;
    private DispatcherTimer? _idle;

    /// <summary>Raised on the UI thread after anything here changes.</summary>
    public event Action? Changed;

    /// <summary>The port the server listens on, or null until it is listening.</summary>
    public int? Port { get; private set; }

    /// <summary>The agent session the IDE was started for with <c>--session</c>, or null when it serves any caller.</summary>
    public string? Session { get; private set; }

    /// <summary>
    /// The directory this IDE was built in, with the user's profile replaced, as the session refusal names it: the hover
    /// tool hands the tip's text back to agents, which paste it into public issues.
    /// </summary>
    public string? BuildDirectory { get; private set; }

    public string? Endpoint => Port is { } port ? $"http://localhost:{port}/mcp" : null;

    /// <summary>Whether a call is running, or one ended too recently for its cue to have gone.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>The tool the running call is to, or the last one that ran while the cue was on.</summary>
    public string? RunningTool { get; private set; }

    public string? LastTool { get; private set; }

    public DateTimeOffset? LastCallAt { get; private set; }

    public int RefusedCount { get; private set; }

    /// <summary>The session the last refused call named, or null when it named none.</summary>
    public string? LastRefusedFrom { get; private set; }

    /// <summary>The server is listening: reported once its port is bound, not when it starts to bind.</summary>
    public void Listening(int port, string? session, string buildDirectory) => Post(() =>
    {
        Port = port;
        Session = string.IsNullOrWhiteSpace(session) ? null : session.Trim();
        BuildDirectory = buildDirectory;
    });

    public void CallStarted(string tool) => Post(() =>
    {
        if (_inFlight++ == 0 && !IsBusy)
            _busySince = DateTimeOffset.Now;
        _idle?.Stop();
        IsBusy = true;
        RunningTool = tool;
    });

    public void CallEnded() => Post(() =>
    {
        _inFlight = Math.Max(0, _inFlight - 1);
        LastTool = RunningTool;
        LastCallAt = DateTimeOffset.Now;
        if (_inFlight > 0)
            return;
        var shown = DateTimeOffset.Now - _busySince;
        var hold = BusyAtLeast - shown > BusyTail ? BusyAtLeast - shown : BusyTail;
        if (hold <= TimeSpan.Zero)
        {
            IsBusy = false;
            return;
        }
        _idle ??= new DispatcherTimer();
        _idle.Interval = hold;
        _idle.Tick -= OnIdle;
        _idle.Tick += OnIdle;
        _idle.Start();
    });

    /// <summary>A call from another session, or from none, was refused (#1306).</summary>
    public void Refused(string? from) => Post(() =>
    {
        RefusedCount++;
        LastRefusedFrom = string.IsNullOrWhiteSpace(from) ? null : from.Trim();
    });

    private void OnIdle(object? sender, EventArgs e)
    {
        _idle?.Stop();
        if (_inFlight > 0)
            return;
        IsBusy = false;
        Changed?.Invoke();
    }

    private void Post(Action change) => Dispatcher.UIThread.Post(() =>
    {
        change();
        Changed?.Invoke();
    });

    /// <summary>Back to how a server that has not started reads, for tests.</summary>
    internal void Reset()
    {
        _idle?.Stop();
        _inFlight = 0;
        Port = null;
        Session = BuildDirectory = null;
        IsBusy = false;
        RunningTool = LastTool = LastRefusedFrom = null;
        LastCallAt = null;
        RefusedCount = 0;
    }
}
#endif
