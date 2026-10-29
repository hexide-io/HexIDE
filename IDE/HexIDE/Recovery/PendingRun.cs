using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using Serilog;

namespace HexIDE.Recovery;

/// <summary>
/// A run that ended without a clean exit and still holds unsaved work, claimed by this IDE so that no other offers it
/// at the same time (hexide-io/HexIDE#499 task 2.1).
/// </summary>
/// <remarks>
/// <para>
/// <b>A run is over when its process is, judged by id and start time together.</b> An id alone is reused, so a later
/// process given the same one must not pass for the run. The start time is compared to within a second, because Linux
/// derives it from the boot time and a tick count and need not give the same value twice.
/// </para>
/// <para>
/// <b>Doubt leaves a run alone.</b> A process whose start time can't be read is taken to be the run, still going.
/// Wrongly calling a run over would put two IDEs on one folder. Wrongly calling it alive only delays an offer.
/// </para>
/// <para>
/// <b>The claim is an exclusive handle on a file in the run's folder,</b> held while this IDE runs. A run another IDE has
/// already claimed is left to it. The handle is released before the folder is deleted, which Windows would otherwise
/// refuse.
/// </para>
/// </remarks>
internal sealed class PendingRun : IDisposable
{
    private const string ClaimName = "claim";

    // A folder with no run.json is a run between making its folder and writing its record, unless nothing has been
    // written in it for this long. Last write rather than creation: Linux can't set a birth time, which leaves creation
    // unmeasurable in a test, and a run writes its record within moments of making the folder anyway.
    private static readonly TimeSpan RecordGrace = TimeSpan.FromMinutes(1);

    private readonly List<RecoveryRun.ProjectRecord> projects;
    private RecoveryRun.RunRecord record;
    private FileStream? claim;

    private PendingRun(string folder, RecoveryRun.RunRecord record, FileStream claim)
    {
        Folder = folder;
        this.record = record;
        this.claim = claim;
        projects = [.. record.Projects];
    }

    /// <summary>The run's folder.</summary>
    public string Folder { get; }

    /// <summary>When the run's latest capture was written.</summary>
    public DateTime? SavedAt => record.SavedAt;

    /// <summary>The projects whose work is still waiting: neither restored nor discarded.</summary>
    public IReadOnlyList<RecoveryRun.ProjectRecord> Projects => projects;

    /// <summary>
    /// Every run under <paramref name="root"/> that is over and holds unsaved work, claimed for this IDE. A run that is
    /// over with nothing captured is deleted on the way, without asking.
    /// </summary>
    /// <param name="root">The recovery directory.</param>
    /// <param name="ownFolder">This IDE's own run, which is never offered.</param>
    /// <param name="isAlive">Whether the process of an id and start time is still running; <see cref="IsAlive"/>.</param>
    /// <param name="now">The current time, in UTC.</param>
    public static IReadOnlyList<PendingRun> Find(string root, string? ownFolder, Func<int, DateTime, bool> isAlive, DateTime now)
    {
        var found = new List<PendingRun>();
        if (!Directory.Exists(root))
            return found;

        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            if (ownFolder is not null && SameFolder(folder, ownFolder))
                continue;
            try
            {
                if (TryTake(folder, isAlive, now) is { } run)
                    found.Add(run);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                Log.Warning(ex, "Crash recovery could not read the run in {Folder}; it is left as it is", folder);
            }
        }
        return found;
    }

    private static PendingRun? TryTake(string folder, Func<int, DateTime, bool> isAlive, DateTime now)
    {
        if (!File.Exists(Path.Combine(folder, "run.json")))
        {
            if (now - Directory.GetLastWriteTimeUtc(folder) > RecordGrace && Claim(folder) is { } stale)
                DeleteClaimed(folder, stale);
            return null;
        }

        var record = RecoveryRun.Read(folder);
        // A layout this build doesn't know belongs to a newer HexIDE, which can still offer it: neither read nor deleted.
        if (record is null || record.Format != RecoveryRun.Format)
            return null;
        if (isAlive(record.ProcessId, record.ProcessStart))
            return null;
        if (Claim(folder) is not { } claim)
            return null;

        if (record.Capture is null || record.Projects.Count == 0)
        {
            DeleteClaimed(folder, claim);
            return null;
        }
        return new PendingRun(folder, record, claim);
    }

    /// <summary>
    /// Whether the process <paramref name="processId"/>, started at <paramref name="processStart"/>, is still running.
    /// </summary>
    public static bool IsAlive(int processId, DateTime processStart)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var started = process.StartTime.ToUniversalTime();
            if ((started - processStart.ToUniversalTime()).Duration() > TimeSpan.FromSeconds(1))
                return false;
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false; // no process has that id
        }
        catch (InvalidOperationException)
        {
            return false; // it ended while being looked at
        }
        catch (Exception ex) when (ex is Win32Exception or NotSupportedException)
        {
            return true; // it can't be read, so it is left alone
        }
    }

    /// <summary>
    /// <paramref name="project"/>'s work, with each file's text and companion read back from the run's folder.
    /// </summary>
    public UnsavedProject Read(RecoveryRun.ProjectRecord project) => new(
        project.Name, project.Path, project.ProjectFileHash, new Vb6TextEncoding(project.CodePage, project.Utf8Bom),
        project.ProjectFile is { } projectFile ? new UnsavedFile(Path.GetFileName(projectFile), ReadText(projectFile)) : null,
        [
            .. project.Documents.Select(document => new UnsavedDocument(
                document.Name, document.Kind, document.Path, new Vb6TextEncoding(document.CodePage, document.Utf8Bom),
                document.File is { } file
                    ? new UnsavedFile(Path.GetFileName(file), ReadText(file),
                        document.Companion is { } companion ? Path.GetFileName(companion) : null,
                        document.Companion is { } bytes ? File.ReadAllBytes(Inside(bytes)) : null)
                    : null,
                document.DiskHash)),
        ],
        project.RenderedAt,
        project.Scratch);

    /// <summary>
    /// <paramref name="project"/> has been restored or discarded. Once nothing in the run is left waiting, its folder goes.
    /// </summary>
    public void Resolve(RecoveryRun.ProjectRecord project)
    {
        if (!projects.Remove(project))
            return;
        if (projects.Count == 0)
        {
            if (claim is { } held)
            {
                claim = null;
                DeleteClaimed(Folder, held);
            }
            return;
        }
        record = record with { Projects = [.. projects] };
        RecoveryRun.WriteRecord(Folder, record);
    }

    /// <summary>Gives up the claim, leaving the run for the next IDE to offer.</summary>
    public void Dispose()
    {
        claim?.Dispose();
        claim = null;
    }

    private string ReadText(string relative) => File.ReadAllText(Inside(relative), Encoding.UTF8);

    // The record names files by paths relative to the folder; one that climbs out of it is not read.
    private string Inside(string relative)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Folder)) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(Folder, relative.Replace('/', Path.DirectorySeparatorChar)));
        return full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            ? full
            : throw new IOException($"The recovery record names '{relative}', which is outside its run's folder.");
    }

    private static FileStream? Claim(string folder)
    {
        try
        {
            return new FileStream(Path.Combine(folder, ClaimName), FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (IOException)
        {
            return null; // another IDE has it
        }
    }

    private static void DeleteClaimed(string folder, FileStream claim)
    {
        claim.Dispose();
        Directory.Delete(folder, recursive: true);
    }

    private static bool SameFolder(string a, string b) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
