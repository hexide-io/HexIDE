using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Serilog;

namespace HexIDE.Recovery;

/// <summary>
/// Backs up this run's unsaved work while the IDE runs, so an exit nobody chose doesn't lose it (hexide-io/HexIDE#499).
/// </summary>
public interface IRecoveryService
{
    /// <summary>Makes this run's recovery folder and starts backing up unsaved work. Desktop only.</summary>
    void Start();

    /// <summary>
    /// The IDE is about to close without anyone deciding about its unsaved work, as on a terminate signal. Backs the
    /// work up now, and keeps the folder through the close so the next start can offer it.
    /// </summary>
    void KeepForRecovery();

    /// <summary>
    /// The IDE is closing. Stops backing up, and deletes this run's folder, unless <see cref="KeepForRecovery"/> was
    /// called: somebody answered for the unsaved work, or there was none.
    /// </summary>
    void EndRun();

    /// <summary>This run's own recovery folder, or null when recovery is off this session.</summary>
    string? RunFolder { get; }
}

/// <inheritdoc/>
/// <remarks>
/// <para>
/// <b>Read on the UI thread, written off it.</b> The model and every editor buffer belong to the UI thread, so a tick
/// renders the unsaved files there and hands the text to one background writer. A capture that arrives while a write
/// is under way replaces any still waiting: only the newest matters.
/// </para>
/// <para>
/// <b>A capture identical to the last one is not written,</b> so an idle IDE doesn't touch the disk every few seconds.
/// The tick still renders each document to find out, which is the dirty check the save prompt makes.
/// </para>
/// <para>
/// <b>Never fatal.</b> The backup is a safety net, and a fault in it must not take down the IDE it protects: a folder
/// that can't be made turns recovery off for the session, and a capture or write that fails is logged and retried at
/// the next tick.
/// </para>
/// </remarks>
public sealed class RecoveryService : IRecoveryService
{
    /// <summary>How often unsaved work is captured.</summary>
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    // How long a close waits for a write under way. A write that outlasts it leaves the folder, rather than racing it.
    private static readonly TimeSpan EndWait = TimeSpan.FromSeconds(5);

    private readonly IProjectService projects;
    private readonly IClock clock;
    private readonly Func<RecoveryRun> begin;
    private readonly Lock gate = new();

    private RecoveryRun? run;
    private DispatcherTimer? timer;
    private IReadOnlyList<UnsavedProject>? pending;
    private Task? writer;
    private string written = RecoveryRun.Fingerprint([]);
    private bool ended;
    private bool keep;

    public RecoveryService(IProjectService projects, IClock clock)
        : this(projects, clock, () => RecoveryRun.Begin(RecoveryPath.Directory, Environment.ProcessId, ProcessStart()))
    {
    }

    internal RecoveryService(IProjectService projects, IClock clock, Func<RecoveryRun> begin)
    {
        this.projects = projects;
        this.clock = clock;
        this.begin = begin;
    }

    /// <summary>This run's folder, or null when recovery is off.</summary>
    internal string? Folder => run?.Folder;

    public string? RunFolder => run?.Folder;

    public void Start()
    {
        if (!Begin())
            return;
        timer = new DispatcherTimer(Interval, DispatcherPriority.Background, (_, _) => Capture());
        timer.Start();
    }

    /// <summary><see cref="Start"/> without the timer: makes the run's folder, and says whether it could.</summary>
    internal bool Begin()
    {
        try
        {
            run = begin();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Warning(ex, "Crash recovery is off this session: its folder could not be made");
            return false;
        }
    }

    public void KeepForRecovery()
    {
        lock (gate)
            keep = true;
        Capture();
    }

    public void EndRun()
    {
        timer?.Stop();
        Task? draining;
        bool keeping;
        lock (gate)
        {
            ended = true;
            keeping = keep;
            draining = writer;
        }

        // Before the folder goes: a write still under way would make it again, as a run that never ended.
        if (draining is not null && !draining.Wait(EndWait))
        {
            Log.Warning("Crash recovery's last write outlasted the close, so its folder {Folder} is left", run?.Folder);
            return;
        }

        if (keeping || run is null)
            return;
        try
        {
            run.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Crash recovery could not delete {Folder} after a clean exit", run.Folder);
        }
    }

    /// <summary>
    /// Captures the unsaved work now and queues it to be written. On the UI thread. Returns the write, for a test to
    /// await.
    /// </summary>
    internal Task Capture()
    {
        if (run is null)
            return Task.CompletedTask;

        IReadOnlyList<UnsavedProject> captured;
        try
        {
            captured = projects.CaptureUnsavedWork();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Crash recovery could not capture unsaved work; trying again at the next tick");
            return Task.CompletedTask;
        }

        lock (gate)
        {
            // A close that is keeping the folder still takes the capture KeepForRecovery made just before it.
            if (ended && !keep)
                return Task.CompletedTask;
            pending = captured;
            return writer ??= Task.Run(WriteQueued);
        }
    }

    private void WriteQueued()
    {
        while (true)
        {
            IReadOnlyList<UnsavedProject> next;
            lock (gate)
            {
                if (pending is null)
                {
                    writer = null;
                    return;
                }
                next = pending;
                pending = null;
            }

            var fingerprint = RecoveryRun.Fingerprint(next);
            if (fingerprint == written)
                continue;
            try
            {
                run!.Write(next, clock.UtcNow);
                written = fingerprint;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Crash recovery could not write its backup to {Folder}", run!.Folder);
            }
        }
    }

    private static DateTime ProcessStart()
    {
        using var process = Process.GetCurrentProcess();
        return process.StartTime.ToUniversalTime();
    }
}
