using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Serilog;

namespace HexIDE.IDE;

public sealed record OpenDocumentState(string Kind, string Path, bool Active);

/// <summary>Project-specific open tabs, kept outside the project's source files.</summary>
public sealed class DocumentSessionStore
{
    private readonly string? directory;
    private readonly bool recoveryMode;

    public DocumentSessionStore() => recoveryMode = Static.IsRecoveryMode;

    internal DocumentSessionStore(string directory, bool recoveryMode = false)
    {
        this.directory = directory;
        this.recoveryMode = recoveryMode;
    }

    private string FileFor(string projectPath)
    {
        var key = Path.GetFullPath(projectPath);
        if (OperatingSystem.IsWindows()) key = key.ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(directory ?? Path.Combine(UserDataPath.Directory, "document-sessions"), hash + ".json");
    }

    public IReadOnlyList<OpenDocumentState> Load(string? projectPath)
    {
        if (recoveryMode || string.IsNullOrEmpty(projectPath)) return [];
        try
        {
            var file = FileFor(projectPath);
            return File.Exists(file) ? JsonSerializer.Deserialize<OpenDocumentState[]>(File.ReadAllText(file)) ?? [] : [];
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not load open documents for {Project}", projectPath);
            return [];
        }
    }

    public void Save(string? projectPath, IReadOnlyList<OpenDocumentState> documents)
    {
        if (recoveryMode || string.IsNullOrEmpty(projectPath)) return;
        try
        {
            var file = FileFor(projectPath);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            AtomicFileWriter.WriteAllText(file, JsonSerializer.Serialize(documents));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not save open documents for {Project}", projectPath);
        }
    }
}
