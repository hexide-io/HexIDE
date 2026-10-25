using System.Text.Json;
using System.Text.Json.Serialization;

namespace HexIDE.Lsp.Providers;

public sealed class ClangdCompilationDatabaseProvider : IWorkspaceArtifactProvider
{
    public string Name => "clangd";

    public string? Produce(WorkspaceArtifactContext context)
    {
        var arguments = context.Settings is { ValueKind: JsonValueKind.Object } settings
            && settings.TryGetProperty("arguments", out var configured)
            ? configured.EnumerateArray().Select(a => a.GetString()!).ToArray()
            : new[] { "clang" };
        var entries = context.Project.Files
            .Where(f => f.Kind == WorkspaceProjectFileKind.Carried
                && Path.GetExtension(f.HostPath).ToLowerInvariant() is ".c" or ".cc" or ".cpp" or ".cxx"
                    or ".h" or ".hh" or ".hpp" or ".hxx")
            .Select(f => new CompilationCommand(Path.GetDirectoryName(f.HostPath)!, f.HostPath,
                [.. arguments, f.HostPath])).ToArray();
        return JsonSerializer.Serialize(entries, CompilationDatabaseJsonContext.Default.CompilationCommandArray);
    }
}

internal sealed record CompilationCommand(
    [property: JsonPropertyName("directory")] string Directory,
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("arguments")] string[] Arguments);

[JsonSerializable(typeof(CompilationCommand[]))]
internal partial class CompilationDatabaseJsonContext : JsonSerializerContext;
