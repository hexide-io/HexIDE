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
        // Clear this project's URIs from the shared stores WITHOUT persisting the emptied state: suppress the
        // change-driven save (_loading) and cancel any pending debounce, so removing it from memory never erases
        // the project's on-disk sidecar.
        _saveCts?.Cancel();
        _loading = true;
        try
        {
            foreach (var uri in ProjectUris(project))
            {
                bookmarkService.SetBookmarks(uri, Array.Empty<int>());
                breakpointService.ClearDocument(uri);
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private static IEnumerable<string> ProjectUris(ProjectDefinition project) =>
        project.Forms.Select(f => $"vb6://form/{f.Name}")
            .Concat(project.Modules.Select(m => $"vb6://module/{m.Name}"));

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

            if (data.Bookmarks != null)
                foreach (var (uri, lines) in data.Bookmarks)
                    bookmarkService.SetBookmarks(uri, lines);

            if (data.Breakpoints != null)
                foreach (var (uri, lines) in data.Breakpoints)
                    breakpointService.SetDocument(uri, lines);
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

    public async Task SaveAsync(ProjectDefinition project)
    {
        var path = SidecarPath(project);
        if (path == null) return;

        try
        {
            // Collect a per-document line map (keyed by the same vb6:// URIs) from a service.
            Dictionary<string, List<int>> Collect(Func<string, IReadOnlyList<int>> getLines)
            {
                var map = new Dictionary<string, List<int>>();
                foreach (var uri in project.Forms.Select(f => $"vb6://form/{f.Name}")
                             .Concat(project.Modules.Select(m => $"vb6://module/{m.Name}")))
                {
                    var lines = getLines(uri);
                    if (lines.Count > 0)
                        map[uri] = new List<int>(lines);
                }
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

    private void OnSidecarStateChanged(string uri)
    {
        if (_loading) return;

        var project = FindProjectForUri(uri);
        if (project == null) return;

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

    private ProjectDefinition? FindProjectForUri(string uri)
    {
        foreach (var project in projectManager.LoadedProjects)
        {
            if (uri.StartsWith("vb6://form/", StringComparison.Ordinal))
            {
                var name = uri["vb6://form/".Length..];
                if (project.Forms.Any(f => f.Name == name))
                    return project;
            }
            else if (uri.StartsWith("vb6://module/", StringComparison.Ordinal))
            {
                var name = uri["vb6://module/".Length..];
                if (project.Modules.Any(m => m.Name == name))
                    return project;
            }
        }
        return null;
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
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("bookmarks")]
    public Dictionary<string, List<int>>? Bookmarks { get; set; }

    [JsonPropertyName("breakpoints")]
    public Dictionary<string, List<int>>? Breakpoints { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
