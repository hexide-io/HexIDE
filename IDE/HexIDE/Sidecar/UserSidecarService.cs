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

public class UserSidecarService : IUserSidecarService
{
    private readonly IBookmarkService bookmarkService;
    private readonly IBreakpointService breakpointService;
    private readonly IProjectManager projectManager;
    private CancellationTokenSource? _saveCts;
    private bool _loading;

    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    public UserSidecarService(IBookmarkService bookmarkService, IBreakpointService breakpointService,
        IProjectManager projectManager)
    {
        this.bookmarkService = bookmarkService;
        this.breakpointService = breakpointService;
        this.projectManager = projectManager;
        // Both personal-state stores persist to the same per-user sidecar (<project>.user.hexproj, beside the .vbp
        // — the user chooses to commit or ignore it), on the same debounced save.
        bookmarkService.BookmarksChanged += OnSidecarStateChanged;
        breakpointService.BreakpointsChanged += OnSidecarStateChanged;
        // The stores are app-lifetime singletons; unloading a project must drop its entries so they can't bleed
        // into the next project opened under the same form/module names (its gutter, its run's breakpoints, or its
        // sidecar).
        projectManager.ProjectUnloaded += OnProjectUnloaded;
    }

    private void OnProjectUnloaded(ProjectDefinition project)
    {
        // Clear this project's documents from the shared stores WITHOUT persisting the emptied state: suppress the
        // change-driven save (_loading) and cancel any pending debounce, so removing it from memory never erases
        // the project's on-disk sidecar.
        //
        // Scoped by project rather than by name: every key is a document of this project, so nothing another
        // loaded project owns can be reached from here even if it is called the same thing.
        _saveCts?.Cancel();
        _loading = true;
        try
        {
            bookmarkService.ClearProject(project);
            breakpointService.ClearProject(project);
        }
        finally
        {
            _loading = false;
        }
    }

    public async Task LoadAsync(ProjectDefinition project)
    {
        var path = SidecarPath(project);
        if (path == null || !File.Exists(path)) return;

        _loading = true;
        try
        {
            var json = await File.ReadAllTextAsync(path);
            var data = JsonSerializer.Deserialize<UserSidecarData>(json, s_jsonOptions);
            if (data == null) return;

            var byName = DocumentLookup.DocumentsOf(project)
                .ToDictionary(d => d.Name, d => d, StringComparer.OrdinalIgnoreCase);

            if (data.Bookmarks != null)
                foreach (var (key, lines) in data.Bookmarks)
                    if (Resolve(byName, key) is { } document)
                        bookmarkService.SetBookmarks(document, WithinDocument(document, lines, first: 0, "bookmark", path));

            if (data.Breakpoints != null)
                foreach (var (key, lines) in data.Breakpoints)
                    if (Resolve(byName, key) is { } document)
                        breakpointService.SetDocument(document, WithinDocument(document, lines, first: 1, "breakpoint", path));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load user sidecar from {Path}", path);
        }
        finally
        {
            _loading = false;
        }
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
    /// The document a sidecar key names, or null when this project has no such document.
    /// </summary>
    /// <remarks>
    /// <b>Two spellings, told apart by the key itself.</b> A sidecar written before documents had identities
    /// keyed by the URI the IDE used internally — <c>vb6://form/Form1</c> — and one written since keys by the
    /// document's name alone, because the file already belongs to exactly one project. A VB6 name is a
    /// letter followed by letters, digits and underscores, so it can never look like a URI; no version step
    /// is needed to distinguish them, and none is spent on it. The <c>version</c> field records how the
    /// <em>lines</em> are counted, which is a separate question and not one this change moves.
    /// </remarks>
    private static DocumentIdentity? Resolve(IReadOnlyDictionary<string, DocumentIdentity> byName, string key)
    {
        var name = key;
        if (key.StartsWith("vb6://", StringComparison.OrdinalIgnoreCase))
        {
            var slash = key.LastIndexOf('/');
            if (slash >= 0) name = key[(slash + 1)..];
        }
        return byName.TryGetValue(name, out var document) ? document : null;
    }

    public async Task SaveAsync(ProjectDefinition project)
    {
        var path = SidecarPath(project);
        if (path == null) return;

        try
        {
            // A per-document line map for one store, keyed by the document's name within this project's own
            // file. The project needs no naming inside it, because the sidecar belongs to one.
            Dictionary<string, List<int>> Collect(Func<DocumentIdentity, IReadOnlyList<int>> getLines)
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

            var bookmarks = Collect(bookmarkService.GetBookmarks);
            var breakpoints = Collect(breakpointService.GetBreakpoints);

            var data = new UserSidecarData
            {
                Bookmarks = bookmarks.Count > 0 ? bookmarks : null,
                Breakpoints = breakpoints.Count > 0 ? breakpoints : null
            };

            var json = JsonSerializer.Serialize(data, s_jsonOptions);
            var tempPath = path + ".tmp";
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, path, overwrite: true);

            Log.Debug("UserSidecarService: Saved sidecar to {Path}", path);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to save user sidecar to {Path}", path);
        }
    }

    private void OnSidecarStateChanged(DocumentIdentity document)
    {
        if (_loading) return;

        // The document carries its project, so there is nothing to look up and nothing to guess: this used
        // to search every loaded project for one holding a form or module of the URI's name, and answered
        // with whichever project was found first when two of them held one.
        var project = document.Project;

        _saveCts?.Cancel();
        _saveCts?.Dispose();
        _saveCts = new CancellationTokenSource();
        var token = _saveCts.Token;
        _ = SaveAfterDelayAsync(project, token);
    }

    private async Task SaveAfterDelayAsync(ProjectDefinition project, CancellationToken token)
    {
        try
        {
            await Task.Delay(500, token);
            await SaveAsync(project);
        }
        catch (OperationCanceledException) { }
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
    /// It records the <b>line base</b>, not how entries are keyed. The two key spellings — the old
    /// <c>vb6://form/Form1</c> and the document's bare name — are told apart by the key itself, since a VB6
    /// name cannot look like a URI, so re-keying costs no version step. See
    /// <c>UserSidecarService.Resolve</c>.
    /// </remarks>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("bookmarks")]
    public Dictionary<string, List<int>>? Bookmarks { get; set; }

    [JsonPropertyName("breakpoints")]
    public Dictionary<string, List<int>>? Breakpoints { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
