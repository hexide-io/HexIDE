using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using HexIDE.Bookmarks;
using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;
using Serilog;

namespace HexIDE.Sidecar;

public class UserSidecarService : IUserSidecarService
{
    private readonly IBookmarkService bookmarkService;
    private readonly IProjectManager projectManager;
    private CancellationTokenSource? _saveCts;
    private bool _loading;

    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    public UserSidecarService(IBookmarkService bookmarkService, IProjectManager projectManager)
    {
        this.bookmarkService = bookmarkService;
        this.projectManager = projectManager;
        bookmarkService.BookmarksChanged += OnBookmarksChanged;
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
            if (data?.Bookmarks == null) return;

            foreach (var (uri, lines) in data.Bookmarks)
                bookmarkService.SetBookmarks(uri, lines);
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
            var bookmarks = new Dictionary<string, List<int>>();

            foreach (var form in project.Forms)
            {
                var uri = $"vb6://form/{form.Name}";
                var lines = bookmarkService.GetBookmarks(uri);
                if (lines.Count > 0)
                    bookmarks[uri] = new List<int>(lines);
            }

            foreach (var module in project.Modules)
            {
                var uri = $"vb6://module/{module.Name}";
                var lines = bookmarkService.GetBookmarks(uri);
                if (lines.Count > 0)
                    bookmarks[uri] = new List<int>(lines);
            }

            var data = new UserSidecarData
            {
                Bookmarks = bookmarks.Count > 0 ? bookmarks : null
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

    private void OnBookmarksChanged(string uri)
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

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
