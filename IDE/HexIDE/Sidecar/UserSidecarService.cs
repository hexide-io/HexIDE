using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;
using Serilog;

namespace HexIDE.Sidecar;

/// <summary>
/// Keeps each project's bookmarks and breakpoints in its per-user sidecar, <c>&lt;project&gt;.user.hexproj</c> beside
/// the .vbp, which the user chooses to commit or ignore.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rewritten, never rebuilt.</b> A sidecar can hold what this build does not understand: a version newer than
/// its own, keys a later build added. A save keeps all of it and writes this build's marks around it, because a
/// newer build's content deleted by an older one is lost without anybody seeing it go (#466). What is kept is what
/// the file holds when it is rewritten, read again then, so content another build wrote while this one had the
/// project open survives too. This build's own marks are written from what it holds: for those, this session wins.
/// </para>
/// <para>
/// <b>Per project, never shared.</b> A .vbg loads several projects, two of them possibly of one name. Each has its
/// own waiting save, so a change in one cannot cancel another's. A change stays owed until it is written, so a
/// write that fails is made again when the project closes; and a project closed while a change is owed is written
/// first and cleared after, so its last change is kept and clearing it from memory never erases its file.
/// </para>
/// <para>
/// <b>Written synchronously.</b> A sidecar is a few hundred bytes, and the .vbp beside it is written the same way.
/// A write that no other write can interleave with is what makes a save on close, an explicit save during a
/// waiting one, and the single temporary file all safe without anything further.
/// </para>
/// </remarks>
public class UserSidecarService : IUserSidecarService
{
    /// <summary>The format this build writes: lines counted from the first line the code window showed.</summary>
    private const int CurrentVersion = 1;

    private readonly IBookmarkService bookmarkService;
    private readonly IBreakpointService breakpointService;
    private readonly IProjectManager projectManager;

    // By instance, not by name or path: a .vbg can hold two projects called the same thing, and a project not yet
    // saved has no path.
    private readonly Dictionary<ProjectDefinition, ProjectState> projects = new(ReferenceEqualityComparer.Instance);

    // Projects whose stores this service is filling or emptying, so the change events are its own and save nothing.
    private readonly HashSet<ProjectDefinition> quiet = new(ReferenceEqualityComparer.Instance);

    // The debounced saves not yet finished, so tests can wait for them; see SettledAsync.
    private readonly List<Task> scheduled = [];

    private readonly object gate = new();

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        // An empty kind is left out rather than written as null, so a sidecar holds only what it records.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public UserSidecarService(IBookmarkService bookmarkService, IBreakpointService breakpointService,
        IProjectManager projectManager)
    {
        this.bookmarkService = bookmarkService;
        this.breakpointService = breakpointService;
        this.projectManager = projectManager;
        // Both personal-state stores persist to the same per-user sidecar, on the same debounced save.
        bookmarkService.BookmarksChanged += OnSidecarStateChanged;
        breakpointService.BreakpointsChanged += OnSidecarStateChanged;
        // The stores are app-lifetime singletons; unloading a project must drop its entries so they can't bleed
        // into the next project opened under the same form/module names (its gutter, its run's breakpoints, or its
        // sidecar).
        projectManager.ProjectUnloaded += OnProjectUnloaded;
    }

    /// <summary>
    /// How long a change waits before it is written, so that a burst of changes is written once.
    /// </summary>
    /// <remarks>Replaced by tests, which complete it when they choose rather than waiting for it.</remarks>
    internal Func<CancellationToken, Task> Debounce { get; init; } = token => Task.Delay(500, token);

    /// <summary>How a sidecar is read when its project opens.</summary>
    /// <remarks>Replaced by tests, which hold the read open to close or change projects meanwhile.</remarks>
    internal Func<string, Task<string>> ReadText { get; init; } = path => File.ReadAllTextAsync(path);

    private sealed class ProjectState
    {
        /// <summary>The sidecar path this state describes: where it was read, or last written.</summary>
        public string? ReadFrom;

        /// <summary>
        /// The sidecar as last read or written at <see cref="ReadFrom"/>, or carried there from where the project
        /// was before. Null while nothing has been read.
        /// </summary>
        public UserSidecarData? Read;

        /// <summary>The sidecar at <see cref="ReadFrom"/> could not be read or applied when the project opened.</summary>
        public bool ReadFailed;

        /// <summary>Whether that refusal has been logged, so it is logged once rather than per change.</summary>
        public bool RefusalLogged;

        /// <summary>
        /// Whether the refusal of a sidecar that could be read at load and can no longer be read has been logged.
        /// </summary>
        public bool UnreadableLogged;

        /// <summary>The sidecar is being read, so the stores do not yet hold its marks and nothing may be written.</summary>
        public bool Loading;

        /// <summary>A change has been made, or a write attempted, that has not yet reached the file.</summary>
        public bool Owed;

        /// <summary>The debounce the latest change is waiting on, if one is.</summary>
        public CancellationTokenSource? Pending;
    }

    private ProjectState StateOf(ProjectDefinition project)
    {
        if (!projects.TryGetValue(project, out var state))
            projects[project] = state = new ProjectState();
        return state;
    }

    private void OnProjectUnloaded(ProjectDefinition project)
    {
        lock (gate)
        {
            // A change still owed is written now, while the stores still hold it: a project closed straight after a
            // change, which includes closing the IDE, must not lose that change.
            if (projects.TryGetValue(project, out var state) && state.Owed)
                Save(project);

            // Then this project's documents are cleared from the shared stores WITHOUT persisting the emptied state,
            // so removing it from memory never erases its on-disk sidecar.
            //
            // Scoped by project rather than by name: every key is a document of this project, so nothing another
            // loaded project owns can be reached from here even if it is called the same thing.
            quiet.Add(project);
            try
            {
                bookmarkService.ClearProject(project);
                breakpointService.ClearProject(project);
            }
            finally
            {
                quiet.Remove(project);
            }

            projects.Remove(project);
        }
    }

    public async Task LoadAsync(ProjectDefinition project)
    {
        var path = SidecarPath(project);
        if (path == null) return;

        // Registered before the read, even when there is no file: a sidecar that appears there later is this
        // project's own, and a project closed while its file is read can be told apart once the read completes.
        var state = new ProjectState { ReadFrom = path, Loading = File.Exists(path) };
        lock (gate)
            projects[project] = state;

        if (!state.Loading) return;

        UserSidecarData? data = null;
        Exception? failure = null;
        try
        {
            data = JsonSerializer.Deserialize<UserSidecarData>(await ReadText(path), s_jsonOptions);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        lock (gate)
        {
            // Closed while the file was read. The stores outlive the project, so nothing of it may reach them.
            if (!projects.TryGetValue(project, out var current) || !ReferenceEquals(current, state))
                return;

            state.Loading = false;
            state.Read = data;
            state.ReadFailed = data == null;
            if (data == null)
            {
                Log.Warning(failure, "Failed to load user sidecar from {Path}; it will not be rewritten", path);
            }
            else
            {
                quiet.Add(project);
                try
                {
                    Apply(project, data, path);
                }
                catch (Exception ex)
                {
                    // Some marks may be in the stores and some not, so a rewrite now would lose the rest.
                    state.ReadFailed = true;
                    Log.Warning(ex, "Failed to apply user sidecar from {Path}; it will not be rewritten", path);
                }
                finally
                {
                    quiet.Remove(project);
                }
            }

            // A change made while the file was read was held back, because the stores did not yet hold the file's
            // marks and a write then would have dropped them. It is written now that they do.
            if (state.Owed)
                Save(project);
        }
    }

    /// <summary>
    /// Puts the sidecar's marks into the stores. An entry goes to every document of the name it gives: a project
    /// loaded from a .vbp written elsewhere may hold a form and a module of one name, whose marks a save unions
    /// under that name, and an entry for a document the project does not have goes nowhere.
    /// </summary>
    private void Apply(ProjectDefinition project, UserSidecarData data, string path)
    {
        var byName = DocumentLookup.DocumentsOf(project).ToLookup(d => d.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var (document, lines) in Gather(byName, data.Bookmarks, first: 0, "bookmark", path))
            bookmarkService.SetBookmarks(document, lines);

        foreach (var (document, lines) in Gather(byName, data.Breakpoints, first: 1, "breakpoint", path))
            breakpointService.SetDocument(document, lines);
    }

    /// <summary>
    /// Each document's lines from every entry naming it, together. Two keys can name one document — a sidecar
    /// holding both spellings, or one keyed <c>vb6://form/Foo</c> and <c>vb6://module/Foo</c> — and setting each in
    /// turn would keep only the last.
    /// </summary>
    private static Dictionary<DocumentIdentity, List<int>> Gather(ILookup<string, DocumentIdentity> byName,
        Dictionary<string, List<int>>? entries, int first, string what, string path)
    {
        var gathered = new Dictionary<DocumentIdentity, List<int>>();
        if (entries == null) return gathered;
        foreach (var (key, lines) in entries)
        {
            foreach (var document in byName[NameIn(key)])
            {
                if (!gathered.TryGetValue(document, out var all))
                    gathered[document] = all = [];
                all.AddRange(WithinDocument(document, lines, first, what, path).Where(line => !all.Contains(line)));
            }
        }
        return gathered;
    }

    /// <summary>
    /// The <paramref name="lines"/> that are lines of <paramref name="document"/>; the rest are logged and dropped.
    /// </summary>
    /// <remarks>
    /// A mark on a line the document does not have is drawn nowhere and never hit, yet it came back on every load
    /// (#574). It could be there from before set_breakpoints and set_bookmarks refused such lines (#570), from a
    /// file shortened outside HexIDE, or from a hand-edited sidecar. Dropped here, it is gone from the next save.
    /// Lines are counted as those tools count them, in the code the editor numbers, which has no Attribute header;
    /// bookmarks from 0, breakpoints from 1.
    /// </remarks>
    private static IEnumerable<int> WithinDocument(
        DocumentIdentity document, IReadOnlyCollection<int> lines, int first, string what, string path)
    {
        var lineCount = (document.Module?.Code ?? document.Form?.Code ?? "").Split('\n').Length;
        var last = first + lineCount - 1;
        var outside = lines.Where(l => l < first || l > last).Distinct().ToList();
        if (outside.Count == 0)
            return lines;

        Log.Warning("Dropped {What}s on {Lines} from {Path}: {Document} has {Count} line(s), numbered {First}..{Last}",
            what, outside, path, document.Display, lineCount, first, last);
        return lines.Where(l => l >= first && l <= last).ToList();
    }

    /// <summary>
    /// The document name a sidecar key gives.
    /// </summary>
    /// <remarks>
    /// <b>Two spellings, told apart by the key itself.</b> A sidecar written before documents had identities
    /// keyed by the URI the IDE used internally — <c>vb6://form/Form1</c> — and one written since keys by the
    /// document's name alone, because the file already belongs to exactly one project. A VB6 name is a
    /// letter followed by letters, digits and underscores, so it can never look like a URI; no version step
    /// is needed to distinguish them, and none is spent on it. The <c>version</c> field records how the
    /// <em>lines</em> are counted, which is a separate question and not one this change moves.
    /// </remarks>
    private static string NameIn(string key)
    {
        if (key.StartsWith("vb6://", StringComparison.OrdinalIgnoreCase))
        {
            var slash = key.LastIndexOf('/');
            if (slash >= 0) return key[(slash + 1)..];
        }
        return key;
    }

    /// <summary>Writes the project's sidecar now, superseding any change still waiting to be written.</summary>
    /// <remarks>Completes before it returns; see the type's remarks for why the write is synchronous.</remarks>
    public Task SaveAsync(ProjectDefinition project)
    {
        lock (gate)
            Save(project);
        return Task.CompletedTask;
    }

    /// <summary>Writes every change still owed to any project's sidecar.</summary>
    /// <remarks>
    /// For closing the IDE without closing its projects first, which is what a forced close does: the save on a
    /// project's close is never reached, and a waiting debounce would not outlive the process.
    /// </remarks>
    public void FlushAll()
    {
        lock (gate)
            foreach (var project in projects.Where(entry => entry.Value.Owed).Select(entry => entry.Key).ToList())
                Save(project);
    }

    /// <summary>Writes the project's sidecar. Called with <see cref="gate"/> held.</summary>
    private void Save(ProjectDefinition project)
    {
        var state = StateOf(project);
        // Whatever was waiting is written now, with everything else.
        state.Pending?.Cancel();
        state.Pending = null;

        // While the sidecar is read the stores do not hold its marks yet, so a write would drop them. The change
        // stays owed, and the load writes it once the marks are in.
        if (state.Loading) return;

        // A project never saved has nowhere to write. What it owes stays owed, and goes when it is closed.
        var path = SidecarPath(project);
        if (path == null) return;

        try
        {
            // Saved somewhere else, or never opened from a file: the project takes what it read with it, and a
            // sidecar already at the new place is replaced, as the project file beside it is. Whether the old one
            // could be read says nothing about the new place. The new place becomes the project's own only once
            // it has been written, or found empty, so a write that fails there is not followed by a save that
            // takes the sidecar lying there for the project's own.
            var elsewhere = !string.Equals(state.ReadFrom, path, PathComparison);

            if (state.ReadFailed && !elsewhere)
            {
                if (!state.RefusalLogged)
                    Log.Warning("User sidecar {Path} is not rewritten, because it could not be read: its bookmarks and " +
                                "breakpoints are not being saved until it is repaired or removed and the project reopened",
                        path);
                state.RefusalLogged = true;
                return;
            }

            var exists = File.Exists(path);
            var onDisk = exists ? File.ReadAllText(path) : null;

            // At its own place, what is kept is what the file holds now: content another build wrote since the
            // project opened survives, and content deleted with the file stays deleted. Somewhere new, the
            // project's own content goes with it.
            var source = elsewhere ? state.Read : null;
            if (onDisk != null && !elsewhere)
            {
                source = TryRead(onDisk);
                if (source == null)
                {
                    if (!state.UnreadableLogged)
                        Log.Warning("User sidecar {Path} is not rewritten, because it can no longer be read", path);
                    state.UnreadableLogged = true;
                    return;
                }
                state.UnreadableLogged = false;
            }

            var bookmarks = Collect(project, bookmarkService.GetBookmarks);
            var breakpoints = Collect(project, breakpointService.GetBreakpoints);
            // Never lowered: a newer build's sidecar still holds that build's content after this one rewrites it,
            // and labelling it with this build's version would misdescribe it.
            var version = Math.Max(source?.Version ?? CurrentVersion, CurrentVersion);
            var extra = source?.Extra is { Count: > 0 } unrecognised ? unrecognised : null;

            // A sidecar is created only when there is something to record, and content this build does not
            // understand is something. One that exists is rewritten even when its last mark has gone, rather than
            // deleted: it may hold what this build does not understand, and it is a file the user may have committed.
            if (!exists && bookmarks.Count == 0 && breakpoints.Count == 0 && extra == null && version == CurrentVersion)
            {
                // Nothing lies here, so the place is the project's own from now on: a sidecar that appears here
                // later is kept as its own, as it is for a project opened where none was.
                Adopt(state, path, data: null);
                return;
            }

            var data = new UserSidecarData
            {
                Version = version,
                Bookmarks = bookmarks.Count > 0 ? bookmarks : null,
                Breakpoints = breakpoints.Count > 0 ? breakpoints : null,
                Extra = extra,
            };

            var json = JsonSerializer.Serialize(data, s_jsonOptions);
            if (json != onDisk)
            {
                var tempPath = path + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, path, overwrite: true);
                Log.Debug("UserSidecarService: Saved sidecar to {Path}", path);
            }

            Adopt(state, path, data);
        }
        catch (Exception ex)
        {
            // Owed, whether or not a change was: the next change, the project's close or the IDE's makes it again.
            // That includes a write to a new place, which no change asked for.
            state.Owed = true;
            Log.Warning(ex, "Failed to save user sidecar to {Path}", path);
        }
    }

    /// <summary>Records that the sidecar at <paramref name="path"/> now holds <paramref name="data"/>, and nothing is owed.</summary>
    private static void Adopt(ProjectState state, string path, UserSidecarData? data)
    {
        state.Read = data;
        state.ReadFrom = path;
        state.ReadFailed = false;
        state.RefusalLogged = false;
        state.Owed = false;
    }

    /// <summary>How sidecar paths compare: as the file system compares them, as other path checks here do.</summary>
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static UserSidecarData? TryRead(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<UserSidecarData>(json, s_jsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// A per-document line map for one store, keyed by the document's name within this project's own file. The
    /// project needs no naming inside it, because the sidecar belongs to one.
    /// </summary>
    /// <remarks>
    /// An entry naming a document the project does not have is not kept: it is written from what the stores hold,
    /// which has nothing for it. Keeping such entries for a document that is only absent for now belongs to the
    /// sidecar migration (#273 task 3.17), which has to tell one from a document renamed or removed this session.
    /// </remarks>
    private static Dictionary<string, List<int>> Collect(
        ProjectDefinition project, Func<DocumentIdentity, IReadOnlyList<int>> getLines)
    {
        var map = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in DocumentLookup.DocumentsOf(project))
        {
            var lines = getLines(document);
            if (lines.Count == 0) continue;

            // Unioned rather than overwritten. A name is unique within a project from now on, but a
            // project loaded from a .vbp written elsewhere may already hold a form and a module
            // sharing one — and writing the second over the first would silently lose its marks.
            if (map.TryGetValue(document.Name, out var existing))
                existing.AddRange(lines.Where(line => !existing.Contains(line)));
            else
                map[document.Name] = new List<int>(lines);
        }
        foreach (var lines in map.Values) lines.Sort();
        return map;
    }

    private void OnSidecarStateChanged(DocumentIdentity document)
    {
        // The document carries its project, so there is nothing to look up and nothing to guess: this used
        // to search every loaded project for one holding a form or module of the URI's name, and answered
        // with whichever project was found first when two of them held one.
        var project = document.Project;

        lock (gate)
        {
            if (quiet.Contains(project)) return;

            // This project's waiting change, and no other project's, is superseded by this one.
            var state = StateOf(project);
            state.Owed = true;
            state.Pending?.Cancel();
            var pending = state.Pending = new CancellationTokenSource();
            scheduled.RemoveAll(save => save.IsCompleted);
            scheduled.Add(SaveAfterDelayAsync(project, pending));
        }
    }

    /// <summary>Completes when every debounced save scheduled so far has finished, written or superseded.</summary>
    internal Task SettledAsync()
    {
        lock (gate)
            return Task.WhenAll(scheduled.ToArray());
    }

    private async Task SaveAfterDelayAsync(ProjectDefinition project, CancellationTokenSource pending)
    {
        try
        {
            await Debounce(pending.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lock (gate)
        {
            // Superseded by a later change, or already written by an explicit save or by the project closing.
            if (pending.IsCancellationRequested) return;
            Save(project);
        }
    }

    private static string? SidecarPath(ProjectDefinition project)
    {
        if (project.AbsolutePath == null) return null;
        var dir = Path.GetDirectoryName(project.AbsolutePath)!;
        var stem = Path.GetFileNameWithoutExtension(project.AbsolutePath);
        return Path.Combine(dir, stem + ".user.hexproj");
    }
}

internal class UserSidecarData
{
    /// <summary>
    /// How this sidecar is written. Read from the next change onwards, when lines start being counted from
    /// the top of the file rather than from the first line the code window showed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It records the <b>line base</b>, not how entries are keyed. The two key spellings — the old
    /// <c>vb6://form/Form1</c> and the document's bare name — are told apart by the key itself, since a VB6
    /// name cannot look like a URI, so re-keying costs no version step. See
    /// <c>UserSidecarService.NameIn</c>.
    /// </para>
    /// <para>
    /// A rewrite never lowers it: a sidecar a newer build wrote still holds that build's content afterwards. So the
    /// <c>bookmarks</c> and <c>breakpoints</c> keys are counted from the first line the code window showed whatever
    /// the version says: every build that writes those keys counts them that way.
    /// </para>
    /// </remarks>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("bookmarks")]
    public Dictionary<string, List<int>>? Bookmarks { get; set; }

    [JsonPropertyName("breakpoints")]
    public Dictionary<string, List<int>>? Breakpoints { get; set; }

    /// <summary>Everything this build does not recognise, written back as it was read.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
