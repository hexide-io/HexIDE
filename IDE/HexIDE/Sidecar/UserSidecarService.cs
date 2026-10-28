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
using HexIDE.Runtime.Serialization;
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
/// <para>
/// <b>Three formats, migrated as the project opens.</b> Version 1 counted from the old code window's first
/// line; version 2 from the file's top. Version 3 counts code marks from the end of the full read-only header,
/// so an unapplied external header change cannot move them on reopen (#884). Header marks remain file-relative.
/// Each format has its own keys, so older builds preserve newer marks without rewriting their numbering.
/// See <see cref="UserSidecarData"/> and <see cref="Apply"/>.
/// </para>
/// </remarks>
public class UserSidecarService : IUserSidecarService
{
    /// <summary>
    /// The format this build writes: code-relative marks and separate file-relative header marks.
    /// Versions 1 and 2 are migrated on open, identified by their keys rather than the version field.
    /// </summary>
    private const int CurrentVersion = 3;

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
        /// was before. Null while nothing has been read. After a migration it is what the file holds once the
        /// migration is written, whether or not that write has landed yet.
        /// </summary>
        public UserSidecarData? Read;

        /// <summary>
        /// The names whose marks this session keeps in the stores and writes from them: every document the project
        /// had when it opened, except one whose file was missing then. Compared ignoring case, as VB6 compares
        /// names. Null for a project that was not opened from a file, which owns every name.
        /// </summary>
        /// <remarks>
        /// Fixed when the project opens and never updated, because an entry for a name outside it is carried in the
        /// file untouched, and has to stay carried even if a document of that name is added or removed meanwhile.
        /// Asking the project as it is now would keep a renamed document's old entry beside its new one, bring back
        /// a mark removed while it was renamed, and give a later document of the old name marks nobody set in it.
        /// </remarks>
        public HashSet<string>? Owned;

        /// <summary>
        /// The version-1 entries the load moved into the stores, so a save does not write them back beside the marks
        /// they became. Null once a write at <see cref="ReadFrom"/> has landed, since the file no longer holds them.
        /// </summary>
        public Entries? Consumed;

        /// <summary>The version-2 entries moved at load, subtracted only once their rewrite lands.</summary>
        public Entries? ConsumedFile;

        /// <summary>The header entries this session last wrote, for the same carry rules as code entries.</summary>
        public Entries? WroteHeader;

        /// <summary>
        /// What the stores put under the version-3 code keys at the last write to <see cref="ReadFrom"/>, so that a later
        /// save can tell an entry this session wrote from one it is carrying. Null until something has been written.
        /// </summary>
        /// <remarks>
        /// A name outside <see cref="Owned"/> can still be one this session writes: a document renamed to it, or
        /// added under it, since the project opened. Without this, the next save would find that entry in the file,
        /// see a name the project did not own, and carry it — so a document renamed twice would leave its middle
        /// name's marks behind, and a mark removed from it would come back.
        /// </remarks>
        public Entries? Wrote;

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

    /// <summary>One format's entries of a sidecar, by kind: a document's key mapped to its lines.</summary>
    private sealed record Entries(Dictionary<string, List<int>> Bookmarks, Dictionary<string, List<int>> Breakpoints)
    {
        public bool IsEmpty => Bookmarks.Count == 0 && Breakpoints.Count == 0;
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
        // The names it owns are taken now, while the project is as it opened, for the same reason.
        var owned = OwnedAtOpen(project);
        var state = new ProjectState { ReadFrom = path, Loading = File.Exists(path), Owned = owned };
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
                    var (migrated, consumed, consumedFile) = Apply(project, data, path, owned);
                    state.Read = migrated;
                    if (!consumed.IsEmpty || !consumedFile.IsEmpty)
                    {
                        // Written once, now, so the next opening finds the marks where they are rather than migrating
                        // them again. A change made while the file was read is written by the same save.
                        state.Consumed = consumed;
                        state.ConsumedFile = consumedFile;
                        state.Owed = true;
                        Log.Information(
                            "Migrated user sidecar {Path}: {Bookmarks} bookmark and {Breakpoints} breakpoint entries " +
                            "now use header-relative code numbering", path,
                            consumed.Bookmarks.Count + consumedFile.Bookmarks.Count,
                            consumed.Breakpoints.Count + consumedFile.Breakpoints.Count);
                    }
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
            // marks and a write then would have dropped them. It is written now that they do, and so is a migration.
            if (state.Owed)
                Save(project);
        }
    }

    /// <summary>
    /// The names of the documents the project has as it opens, except any whose file is missing: the names whose
    /// entries this session applies, and writes from the stores.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A member whose file is missing is preserved rather than loaded. Its entries can neither be moved nor
    /// checked against its lines, so they are carried untouched for an opening that finds the file to apply.
    /// A document's file may also disappear after loading, before the sidecar is read.
    /// </para>
    /// <para>
    /// A document with no file yet is not missing one: the code window shows it whole, header and all, so its
    /// lines can be counted. Where two documents share a name, as a project written elsewhere can, the name is
    /// carried if either cannot be measured, because the entry is one entry and cannot be half applied.
    /// </para>
    /// </remarks>
    private static HashSet<string> OwnedAtOpen(ProjectDefinition project)
    {
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in DocumentLookup.DocumentsOf(project))
            (document.AbsolutePath is { } file && !File.Exists(file) ? missing : owned).Add(document.Name);
        foreach (var line in project.PreservedItemLines)
        {
            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            var kind = line[..eq].Trim();
            if (!kind.Equals(SerializedProject.ModuleKey, StringComparison.OrdinalIgnoreCase)
                && !kind.Equals(SerializedProject.ClassKey, StringComparison.OrdinalIgnoreCase)
                && !kind.Equals(SerializedProject.UserControlKey, StringComparison.OrdinalIgnoreCase)
                && !kind.Equals(SerializedProject.PropertyPageKey, StringComparison.OrdinalIgnoreCase))
                continue;
            var value = line[(eq + 1)..].Trim();
            var semi = value.IndexOf(';');
            missing.Add(semi >= 0 ? value[..semi].Trim() : SerializedProject.FileNameWithoutExtensionOf(value));
        }
        owned.ExceptWith(missing);
        return owned;
    }

    /// <summary>
    /// Puts all three formats into the stores in file numbering, and records the older entries consumed.
    /// A save writes the gathered marks in version 3, keeping unplaceable entries in their original format.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The version-1 keys are migrated whatever <c>version</c> says.</b> A build with the #466 fix and without
    /// this migration keeps a version-2 file's keys and its version, and writes its own marks beside them under
    /// the old keys, so those keys are always counted the old way. An entry naming a document the project owns
    /// (<see cref="OwnedAtOpen"/>) is checked against the lines its document's code had, which is what the old
    /// numbering counted, then moved down by the number of lines the code window now shows above that code:
    /// <see cref="FormCodeText.Prefix(DocumentIdentity)"/>. That is the header of a <c>.bas</c> or <c>.cls</c>,
    /// and only the designer block of a form, UserControl or PropertyPage, whose old window already began at its
    /// <c>Attribute</c> lines: shifting by the whole header would put every mark on a form five lines too low.
    /// It is re-keyed to the document's name and leaves the old keys. Bookmarks stay 0-based and breakpoints
    /// 1-based, as the stores hold them; the bookmark tools count from 1 and convert at their edge (#571).
    /// </para>
    /// <para>
    /// <b>An entry for a name the project does not own is left exactly as it is</b>, under whichever key it was
    /// found. Guessing its shift would misplace its marks and dropping it would lose them; a later opening that
    /// can measure it migrates it then.
    /// </para>
    /// <para>
    /// <b>All formats' marks for one document are gathered into one set before it is stored</b>, because a
    /// store's setter replaces: a version-2 file can hold version-1 entries an older build wrote beside it, and
    /// setting each in turn would keep only the second.
    /// </para>
    /// <para>
    /// <b>A mark on a read-only line is dropped and logged</b>, except a file-relative header mark (versions 2 and 3).
    /// Nothing in the IDE sets one there since #273 task 3.12, but a mark is a bare line number, so one already set can come to sit there: an
    /// edit that adds or removes lines above it, a line below it coming to describe a member, or a reload
    /// (debugger-vb6-divergences D24). The save writes what the stores hold, so such a mark reaches the file and is
    /// dropped here at the next open; so is a migrated one (the old window showed a form's leading <c>Attribute</c>
    /// lines and a member's as editable code), and one written by a build without the refusal. A breakpoint there
    /// would never be hit, and a bookmark there would mark a line the IDE writes.
    /// </para>
    /// </remarks>
    private (UserSidecarData Migrated, Entries Consumed, Entries ConsumedFile) Apply(
        ProjectDefinition project, UserSidecarData data, string path, HashSet<string> owned)
    {
        var byName = DocumentLookup.DocumentsOf(project).ToLookup(d => d.Name, StringComparer.OrdinalIgnoreCase);
        var bookmarks = new Dictionary<DocumentIdentity, List<int>>();
        var breakpoints = new Dictionary<DocumentIdentity, List<int>>();

        var (oldBookmarks, consumedBookmarks) =
            Migrate(byName, owned, data.Bookmarks, first: 0, "bookmark", path, bookmarks);
        var (oldBreakpoints, consumedBreakpoints) =
            Migrate(byName, owned, data.Breakpoints, first: 1, "breakpoint", path, breakpoints);
        Gather(byName, owned, data.FileBookmarks, first: 0, "bookmark", path, bookmarks);
        GatherAnchored(byName, owned, data.CodeBookmarks, 0, "bookmark", path, bookmarks);
        Gather(byName, owned, data.HeaderBookmarks, 0, "bookmark", path, bookmarks);
        Gather(byName, owned, data.FileBreakpoints, first: 1, "breakpoint", path, breakpoints);
        GatherAnchored(byName, owned, data.CodeBreakpoints, 1, "breakpoint", path, breakpoints);
        Gather(byName, owned, data.HeaderBreakpoints, 1, "breakpoint", path, breakpoints);

        foreach (var (document, lines) in bookmarks)
            bookmarkService.SetBookmarks(document, lines);
        foreach (var (document, lines) in breakpoints)
            breakpointService.SetDocument(document, lines);

        var migrated = new UserSidecarData
        {
            Version = data.Version,
            FileBookmarks = data.FileBookmarks,
            FileBreakpoints = data.FileBreakpoints,
            Bookmarks = oldBookmarks.Count > 0 ? oldBookmarks : null,
            Breakpoints = oldBreakpoints.Count > 0 ? oldBreakpoints : null,
            CodeBookmarks = data.CodeBookmarks,
            CodeBreakpoints = data.CodeBreakpoints,
            HeaderBookmarks = data.HeaderBookmarks,
            HeaderBreakpoints = data.HeaderBreakpoints,
            Extra = data.Extra,
        };
        return (migrated, new Entries(consumedBookmarks, consumedBreakpoints),
            new Entries(OwnedEntries(data.FileBookmarks, owned), OwnedEntries(data.FileBreakpoints, owned)));
    }

    private static Dictionary<string, List<int>> OwnedEntries(Dictionary<string, List<int>>? entries,
        HashSet<string> owned) => entries?.Where(entry => owned.Contains(NameIn(entry.Key)))
        .ToDictionary(entry => entry.Key, entry => entry.Value) ?? [];

    /// <summary>Version 3 code lines are counted from the end of the full read-only header.</summary>
    private static void GatherAnchored(ILookup<string, DocumentIdentity> byName, HashSet<string> owned,
        Dictionary<string, List<int>>? entries, int first, string what, string path,
        Dictionary<DocumentIdentity, List<int>> into)
    {
        if (entries == null) return;
        foreach (var (key, lines) in entries)
        {
            RequireLines(key, lines);
            if (!owned.Contains(NameIn(key)))
            {
                LogCarried(key, what, path);
                continue;
            }

            foreach (var document in byName[NameIn(key)])
            {
                var shift = CodeWindowText.Of(document).HeaderLineCount;
                // Check before adding, so a negative or overflowing offset cannot become a valid file line.
                var invalid = lines.Where(line => line < first || (long)line + shift > int.MaxValue).ToList();
                if (invalid.Count > 0)
                    Log.Warning("Dropped {What:l}s on {Lines} from {Path}: invalid code offsets in {Document}",
                        what, invalid, path, document.Display);
                var valid = lines.Where(line => line >= first && (long)line + shift <= int.MaxValue)
                    .Select(line => line + shift).ToList();
                AddTo(into, document, OffReadOnlyLines(document,
                    WithinDocument(document, valid, first, what, path), first, what, path));
            }
        }
    }

    /// <summary>File-relative header marks survive; member Attribute lines still cannot be marked.</summary>
    private static void AddFileLines(Dictionary<DocumentIdentity, List<int>> into, DocumentIdentity document,
        IReadOnlyCollection<int> lines, int first, string what, string path)
    {
        var valid = WithinDocument(document, lines, first, what, path);
        var count = CodeWindowText.Of(document).HeaderLineCount;
        AddTo(into, document, valid.Where(line => line - first < count));
        AddTo(into, document, OffReadOnlyLines(document,
            valid.Where(line => line - first >= count).ToList(), first, what, path));
    }

    /// <summary>
    /// Moves one kind's version-1 entries into <paramref name="into"/>, in the file's numbering, and answers the
    /// entries left for a later opening and the entries moved.
    /// </summary>
    private static (Dictionary<string, List<int>> Left, Dictionary<string, List<int>> Moved) Migrate(
        ILookup<string, DocumentIdentity> byName, HashSet<string> owned, Dictionary<string, List<int>>? entries,
        int first, string what, string path, Dictionary<DocumentIdentity, List<int>> into)
    {
        var left = new Dictionary<string, List<int>>();
        var moved = new Dictionary<string, List<int>>();
        if (entries == null) return (left, moved);

        foreach (var (key, lines) in entries)
        {
            RequireLines(key, lines);
            var name = NameIn(key);
            if (!owned.Contains(name))
            {
                left[key] = lines;
                LogCarried(key, what, path);
                continue;
            }

            foreach (var document in byName[name])
            {
                var shift = FormCodeText.LineBreaks(FormCodeText.Prefix(document));
                var moving = WithinOldCode(document, lines, first, what, path).Select(line => line + shift);
                AddTo(into, document, OffReadOnlyLines(document, moving.ToList(), first, what, path));
            }
            moved[key] = lines;
        }
        return (left, moved);
    }

    /// <summary>
    /// Adds one kind's file-relative entries (version 2, or version 3 header marks) to <paramref name="into"/>:
    /// each document's lines from every entry naming it, together. Two keys can name one document — a sidecar holding both spellings, or one keyed
    /// <c>vb6://form/Foo</c> and <c>vb6://module/Foo</c> — and setting each in turn would keep only the last.
    /// </summary>
    /// <remarks>
    /// An entry for a name the project does not own is not applied, and is not lost: a save takes it from the file
    /// as the file holds it then. See <see cref="Carry"/>.
    /// </remarks>
    private static void Gather(ILookup<string, DocumentIdentity> byName, HashSet<string> owned,
        Dictionary<string, List<int>>? entries, int first, string what, string path,
        Dictionary<DocumentIdentity, List<int>> into)
    {
        if (entries == null) return;
        foreach (var (key, lines) in entries)
        {
            RequireLines(key, lines);
            if (!owned.Contains(NameIn(key)))
            {
                LogCarried(key, what, path);
                continue;
            }

            foreach (var document in byName[NameIn(key)])
                AddFileLines(into, document, lines, first, what, path);
        }
    }

    /// <summary>
    /// A key whose lines are <c>null</c> cannot be applied. Refused rather than skipped: some marks would reach the
    /// stores and some not, and a rewrite would then keep only those that did.
    /// </summary>
    private static void RequireLines(string key, List<int>? lines)
    {
        if (lines is null)
            throw new InvalidDataException($"The sidecar entry '{key}' has no lines.");
    }

    private static void AddTo(Dictionary<DocumentIdentity, List<int>> into, DocumentIdentity document,
        IEnumerable<int> lines)
    {
        if (!into.TryGetValue(document, out var all))
            into[document] = all = [];
        all.AddRange(lines.Where(line => !all.Contains(line)));
    }

    private static void LogCarried(string key, string what, string path) =>
        Log.Information("Kept the {What:l}s for {Key} in {Path} as they are: the project has no document of that " +
                        "name whose file could be found when it opened, so their lines cannot be placed", what, key, path);

    /// <summary>
    /// The <paramref name="lines"/> of a version-1 entry that were lines of the document's code when the entry was
    /// written; the rest are logged and dropped.
    /// </summary>
    /// <remarks>
    /// Checked in the numbering they were written in, before they move, as every build before the migration checked
    /// them (#574): bookmarks from 0 and breakpoints from 1, over the document's code without its header. Checked
    /// after the move instead, a line before the first, which was never a line of the old window, would be moved
    /// into the header and judged as a line of it.
    /// </remarks>
    private static IEnumerable<int> WithinOldCode(
        DocumentIdentity document, IReadOnlyCollection<int> lines, int first, string what, string path)
    {
        var code = new CodeWindowText(document.Module?.Code ?? document.Form!.Code, PrefixLength: 0);
        var (kept, dropped) = MarkLineRules.Within(code, lines, first);
        if (dropped.Count > 0)
            Log.Warning("Dropped {What:l}s on {Lines} from {Path}: they were written before lines counted from the top of " +
                        "the file, when {Document} had {Count} line(s) of code, numbered {First}..{Last}",
                what, dropped.Distinct(), path, document.Display, code.LineCount, first, first + code.LineCount - 1);
        return kept;
    }

    /// <summary>
    /// The <paramref name="lines"/> that are lines of <paramref name="document"/>; the rest are logged and dropped.
    /// </summary>
    /// <remarks>
    /// A mark on a line the document does not have is drawn nowhere and never hit, yet it came back on every load
    /// (#574). It could be there from before set_breakpoints and set_bookmarks refused such lines (#570), from a
    /// file shortened outside HexIDE, or from a hand-edited sidecar. Dropped here, it is gone from the next save.
    /// <para>
    /// Lines are counted as those tools count them, from the top of the file with the header included, which is
    /// how the stores hold them; bookmarks from 0, breakpoints from 1. Counting the code section, as this did
    /// until #273 task 3.12, dropped every mark on a form's last lines of code: the code window shows a form's
    /// designer block above its code, so its last line is that many lines further down than the code's.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<int> WithinDocument(
        DocumentIdentity document, IReadOnlyCollection<int> lines, int first, string what, string path)
    {
        var text = CodeWindowText.Of(document);
        var (kept, dropped) = MarkLineRules.Within(text, lines, first);
        if (dropped.Count > 0)
            Log.Warning("Dropped {What:l}s on {Lines} from {Path}: {Document} has {Count} line(s), numbered {First}..{Last}",
                what, dropped.Distinct(), path, document.Display, text.LineCount, first, first + text.LineCount - 1);
        return kept;
    }

    /// <summary>
    /// The <paramref name="lines"/>, numbered from <paramref name="first"/>, that are not on a read-only line of
    /// <paramref name="document"/>; the rest are logged and dropped. See <see cref="Apply"/> for why.
    /// </summary>
    private static IEnumerable<int> OffReadOnlyLines(
        DocumentIdentity document, IReadOnlyList<int> lines, int first, string what, string path)
    {
        if (lines.Count == 0) return lines;

        var text = CodeWindowText.Of(document);
        var regions = text.Regions;
        var dropped = lines.Where(line => ReadOnlyRegions.IsReadOnlyLine(text.Text, regions, line - first + 1))
            .Distinct().ToList();
        if (dropped.Count == 0) return lines;

        Log.Warning("Dropped {What:l}s on {Lines} from {Path}: in {Document} those are read-only lines, of its header " +
                    "or of the Attribute lines that describe a member, where nothing can be marked",
            what, dropped, path, document.Display);
        return lines.Where(line => !dropped.Contains(line));
    }

    /// <summary>
    /// The document name a sidecar key gives.
    /// </summary>
    /// <remarks>
    /// <b>Two spellings, told apart by the key itself.</b> A sidecar written before documents had identities
    /// keyed by the URI the IDE used internally — <c>vb6://form/Form1</c> — and one written since keys by the
    /// document's name alone, because the file already belongs to exactly one project. A VB6 name is a
    /// letter followed by letters, digits and underscores, so it can never look like a URI; no version step
    /// is needed to distinguish them, and none was spent on it. The <c>version</c> field, and which keys an
    /// entry is under, record how the <em>lines</em> are counted, which is a separate question.
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

            var bookmarks = Collect(project, bookmarkService.GetBookmarks, 0, header: false);
            var breakpoints = Collect(project, breakpointService.GetBreakpoints, 1, header: false);
            var headerBookmarks = Collect(project, bookmarkService.GetBookmarks, 0, header: true);
            var headerBreakpoints = Collect(project, breakpointService.GetBreakpoints, 1, header: true);
            var oursHeader = new Entries(Copy(headerBookmarks), Copy(headerBreakpoints));
            var ours = new Entries(Copy(bookmarks), Copy(breakpoints));
            Carry(source?.CodeBookmarks, state.Owned, state.Wrote?.Bookmarks, bookmarks);
            Carry(source?.HeaderBookmarks, state.Owned, state.WroteHeader?.Bookmarks, headerBookmarks);
            Carry(source?.CodeBreakpoints, state.Owned, state.Wrote?.Breakpoints, breakpoints);
            Carry(source?.HeaderBreakpoints, state.Owned, state.WroteHeader?.Breakpoints, headerBreakpoints);
            var fileBookmarks = Unconsumed(source?.FileBookmarks, state.ConsumedFile?.Bookmarks);
            var fileBreakpoints = Unconsumed(source?.FileBreakpoints, state.ConsumedFile?.Breakpoints);
            var oldBookmarks = Unconsumed(source?.Bookmarks, state.Consumed?.Bookmarks);
            var oldBreakpoints = Unconsumed(source?.Breakpoints, state.Consumed?.Breakpoints);
            // Never lowered: a newer build's sidecar still holds that build's content after this one rewrites it,
            // and labelling it with this build's version would misdescribe it.
            var version = Math.Max(source?.Version ?? CurrentVersion, CurrentVersion);
            var extra = source?.Extra is { Count: > 0 } unrecognised ? unrecognised : null;

            // A sidecar is created only when there is something to record, and content this build does not
            // understand is something, as are the entries it keeps without understanding where they go. One that
            // exists is rewritten even when its last mark has gone, rather than deleted: it may hold what this build
            // does not understand, and it is a file the user may have committed.
            if (!exists && bookmarks.Count == 0 && breakpoints.Count == 0 && oldBookmarks == null &&
                oldBreakpoints == null && fileBookmarks == null && fileBreakpoints == null &&
                headerBookmarks.Count == 0 && headerBreakpoints.Count == 0 && extra == null && version == CurrentVersion)
            {
                // Nothing lies here, so the place is the project's own from now on: a sidecar that appears here
                // later is kept as its own, as it is for a project opened where none was.
                Adopt(state, path, data: null, ours, oursHeader);
                return;
            }

            var data = new UserSidecarData
            {
                Version = version,
                CodeBookmarks = bookmarks.Count > 0 ? bookmarks : null,
                CodeBreakpoints = breakpoints.Count > 0 ? breakpoints : null,
                HeaderBookmarks = headerBookmarks.Count > 0 ? headerBookmarks : null,
                HeaderBreakpoints = headerBreakpoints.Count > 0 ? headerBreakpoints : null,
                FileBookmarks = fileBookmarks,
                FileBreakpoints = fileBreakpoints,
                Bookmarks = oldBookmarks,
                Breakpoints = oldBreakpoints,
                Extra = extra,
            };

            var json = JsonSerializer.Serialize(data, s_jsonOptions);
            if (json != onDisk)
            {
                AtomicFileWriter.WriteAllText(path, json);
                Log.Debug("UserSidecarService: Saved sidecar to {Path}", path);
            }

            Adopt(state, path, data, ours, oursHeader);
        }
        catch (Exception ex)
        {
            // Owed, whether or not a change was: the next change, the project's close or the IDE's makes it again.
            // That includes a write to a new place, which no change asked for.
            state.Owed = true;
            Log.Warning(ex, "Failed to save user sidecar to {Path}", path);
        }
    }

    /// <summary>
    /// Adds to <paramref name="map"/> the version-3 entries of <paramref name="source"/> that name a document this
    /// session does not own, as the file holds them, less whatever this session itself wrote there
    /// (<paramref name="wrote"/>). Where the stores have marks under the same name — a document of that name added
    /// since the project opened, or one whose file is missing — the two are joined, as the next opening would join
    /// them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Taken from the file each time rather than kept from the load, as everything else this build does not apply is:
    /// a sidecar the developer deleted while the project was open must not come back holding them, and an entry
    /// another build wrote there since is kept.
    /// </para>
    /// <para>
    /// <b>Less what this session wrote</b>, because a name the project did not own when it opened can still be one
    /// it writes — a document renamed to it — and that entry is this session's to drop, not one to carry. A line
    /// that is in both is taken as this session's: it is the one that can say it is gone.
    /// </para>
    /// </remarks>
    private static void Carry(Dictionary<string, List<int>>? source, HashSet<string>? owned,
        Dictionary<string, List<int>>? wrote, Dictionary<string, List<int>> map)
    {
        if (source == null || owned == null) return;
        foreach (var (key, lines) in source)
        {
            if (owned.Contains(NameIn(key))) continue;

            // Unchanged where this session wrote nothing under the key, so the entry is written back as it was read.
            var carried = wrote != null && wrote.TryGetValue(key, out var own) && lines != null
                ? lines.Where(line => !own.Contains(line)).ToList()
                : lines;
            if (carried is { Count: 0 }) continue;

            if (map.TryGetValue(key, out var existing))
            {
                existing.AddRange((carried ?? []).Where(line => !existing.Contains(line)));
                existing.Sort();
            }
            else
            {
                map[key] = carried!;
            }
        }
    }

    private static Dictionary<string, List<int>> Copy(Dictionary<string, List<int>> map) =>
        map.ToDictionary(entry => entry.Key, entry => new List<int>(entry.Value), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A file's earlier-format entries less those the load migrated into the stores, or null when none are left. This
    /// build never writes these keys from its own marks: they are content it carries, as it carries a newer
    /// build's, so that a build without the migration keeps what it wrote there for a later opening to migrate.
    /// </summary>
    /// <remarks>
    /// It matters until a write lands, and the first write it matters to is the migration's own: that save reads the
    /// file it is about to replace, which still holds every entry the load moved. After a migration whose write
    /// failed, because another program held the file, it goes on mattering until one succeeds. An entry is
    /// subtracted only when it is still exactly what the load moved, so that whatever an older build wrote under that
    /// key since is kept for the next opening to migrate.
    /// </remarks>
    private static Dictionary<string, List<int>>? Unconsumed(
        Dictionary<string, List<int>>? onFile, Dictionary<string, List<int>>? consumed)
    {
        if (onFile == null) return null;
        var left = new Dictionary<string, List<int>>();
        foreach (var (key, lines) in onFile)
        {
            if (consumed != null && consumed.TryGetValue(key, out var moved) && lines != null &&
                moved.SequenceEqual(lines))
                continue;
            left[key] = lines!;
        }
        return left.Count > 0 ? left : null;
    }

    /// <summary>
    /// Records that the sidecar at <paramref name="path"/> now holds <paramref name="data"/>, of which the stores
    /// wrote <paramref name="ours"/>, and nothing is owed.
    /// </summary>
    private static void Adopt(ProjectState state, string path, UserSidecarData? data, Entries ours, Entries oursHeader)
    {
        state.Read = data;
        state.ReadFrom = path;
        state.ReadFailed = false;
        state.RefusalLogged = false;
        state.Owed = false;
        state.Wrote = ours;
        state.WroteHeader = oursHeader;
        // The file written no longer holds what the load migrated, so nothing remains to be subtracted from it.
        state.Consumed = null;
        state.ConsumedFile = null;
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
    /// Only what the stores hold. An entry for a document the project did not own when it opened is added by
    /// <see cref="Carry"/>, from the file; one for a document it did own and no longer has — renamed or removed this
    /// session — is not written, because the stores have nothing for it.
    /// </remarks>
    private static Dictionary<string, List<int>> Collect(
        ProjectDefinition project, Func<DocumentIdentity, IReadOnlyList<int>> getLines, int first, bool header)
    {
        var map = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in DocumentLookup.DocumentsOf(project))
        {
            var shift = CodeWindowText.Of(document).HeaderLineCount;
            var lines = getLines(document).Where(line => (line - first < shift) == header)
                .Select(line => header ? line : line - shift).ToList();
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

/// <summary>A project's sidecar as it is written: three formats' marks, its version, and whatever else it holds.</summary>
internal class UserSidecarData
{
    /// <summary>
    /// The highest format anything in this sidecar is written in. 1, or no version, counts from the old code
    /// window; 2 from the file's top; 3 from the header's end for code and the file's top for header marks.
    /// A rewrite never lowers it: a sidecar a newer build wrote still holds that build's content afterwards.
    /// </summary>
    /// <remarks>
    /// It is read, but it does not decide how a key's lines are counted: the key does. A build with the #466 fix and
    /// without the migration keeps a version-2 file's version and writes version-1 lines under the version-1 keys
    /// beside it, so a file labelled 2 can hold both. Nor does it record how entries are keyed: the two spellings,
    /// the old <c>vb6://form/Form1</c> and the document's bare name, are told apart by the key itself. See
    /// <c>UserSidecarService.NameIn</c>.
    /// </remarks>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>Version 3 bookmarks, 0-based from the first line after the full read-only header.</summary>
    [JsonPropertyName("codeBookmarks")]
    public Dictionary<string, List<int>>? CodeBookmarks { get; set; }

    /// <summary>Version 3 breakpoints, 1-based from the first line after the full read-only header.</summary>
    [JsonPropertyName("codeBreakpoints")]
    public Dictionary<string, List<int>>? CodeBreakpoints { get; set; }

    /// <summary>Version 3 header bookmarks, 0-based from the top of the file.</summary>
    [JsonPropertyName("headerBookmarks")]
    public Dictionary<string, List<int>>? HeaderBookmarks { get; set; }

    /// <summary>Version 3 header breakpoints, 1-based from the top of the file.</summary>
    [JsonPropertyName("headerBreakpoints")]
    public Dictionary<string, List<int>>? HeaderBreakpoints { get; set; }

    /// <summary>Bookmarks by document, 0-based, counted from the top of the file. Written by version 2.</summary>
    [JsonPropertyName("fileBookmarks")]
    public Dictionary<string, List<int>>? FileBookmarks { get; set; }

    /// <summary>Breakpoints by document, 1-based, counted from the top of the file. Written by version 2.</summary>
    [JsonPropertyName("fileBreakpoints")]
    public Dictionary<string, List<int>>? FileBreakpoints { get; set; }

    /// <summary>
    /// Bookmarks by document, 0-based, counted from the first line the code window showed: version 1's numbering,
    /// in every file whatever its <see cref="Version"/>. Migrated on load; this build writes back only the entries
    /// it could not migrate, and never its own marks.
    /// </summary>
    [JsonPropertyName("bookmarks")]
    public Dictionary<string, List<int>>? Bookmarks { get; set; }

    /// <summary>Breakpoints by document, 1-based, in version 1's numbering. See <see cref="Bookmarks"/>.</summary>
    [JsonPropertyName("breakpoints")]
    public Dictionary<string, List<int>>? Breakpoints { get; set; }

    /// <summary>Everything this build does not recognise, written back as it was read.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
