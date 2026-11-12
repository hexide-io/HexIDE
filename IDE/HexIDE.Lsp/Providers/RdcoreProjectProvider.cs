using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HexIDE.Lsp.Providers;

/// <summary>
/// The <c>.rdproj</c> RDCore's language server reads from the workspace root, without which it analyses nothing (#455).
/// </summary>
/// <remarks>
/// <para>
/// Measured against RDCore <c>653244e2</c>. A module is analysed only when it is listed. RDCore resolves a listed path
/// against the root by the host's own path rules, so each is written as the host computes it relative to the root, and a
/// module in a subfolder, or beside the project's folder, is analysed like any other. Forms and user controls are listed as
/// modules because RDCore reads a module's kind from its header, not its extension.
/// </para>
/// <para>
/// <c>OtherFiles</c> stays empty: RDCore parses every entry there as code, so listing the project file or a carried file
/// only adds syntax errors to its log. VBA needs no reference, since RDCore always loads it, and a reference it has no
/// description for is reported in its log and otherwise ignored. Keys are PascalCase because RDCore's reader is
/// case-sensitive: a camelCase file loads as an empty project.
/// </para>
/// <para>
/// <c>Version</c> names the file's format, not a server's version. RDCore reads and writes format <c>1.0</c>, and from
/// <c>6c1ed90d</c> on it opens a file of any version. A build older than that refuses a version newer than its own,
/// <c>0.1.0</c>, so settings can stamp that for one.
/// </para>
/// <para>
/// <c>SourceProject</c> names the <c>.vbp</c> a VB6 project was made from, as RDCore's own import writes it. RDCore has
/// said it will take VB6 from it when the client names no language, which <c>3f095c2d</c> does not yet do (#3317). Until
/// then, a VB6 workspace names the language in its entry's <c>initializationOptions</c>.
/// </para>
/// <para>
/// RDCore's specification (RD-VBAL 2.2.3) has <c>SourceProject</c> relative to the folder of the <c>.rdproj</c>. RDCore
/// reads its <c>.rdproj</c> only at the workspace root, so that folder is
/// <see cref="WorkspaceArtifactContext.WorkspaceRoot"/>; a provider is not told where its file goes. The path is computed
/// between two host paths and written with <c>/</c>, so it means the same on every host: the <c>.rdproj</c> is RDCore's
/// JSON, not a VB6 file, so a VB6 project file's backslashes don't apply. A <c>.vbp</c> on another drive has no relative
/// path, so nothing is written. A project defined by anything other than a <c>.vbp</c>, or not yet saved, has no
/// <c>SourceProject</c>.
/// </para>
/// </remarks>
public sealed class RdcoreProjectProvider : IWorkspaceArtifactProvider
{
    private const string DefaultVersion = "1.0";

    public string Name => "rdcore";

    public string? Produce(WorkspaceArtifactContext context)
    {
        var modules = context.Project.Files
            .Where(f => f.Kind is WorkspaceProjectFileKind.Module or WorkspaceProjectFileKind.Class
                or WorkspaceProjectFileKind.Form or WorkspaceProjectFileKind.UserControl)
            .Select(f => new RdcoreFile(Path.GetRelativePath(context.WorkspaceRoot, f.HostPath))).ToArray();
        if (modules.Length == 0) return null;
        var version = context.Settings is { ValueKind: JsonValueKind.Object } settings
            && settings.TryGetProperty("version", out var configured) && configured.ValueKind == JsonValueKind.String
            ? configured.GetString()! : DefaultVersion;
        // RDCore resolves a reference by the library's own name. The service passes a reader only once there is consent.
        var references = context.Project.References
            .Select(r => (Name: context.LibraryNames?.NameOf(r) is { Length: > 0 } read ? read : r.Name, Reference: r))
            .Where(n => n.Name.Length > 0)
            .Select(n => new RdcoreReference(n.Name, n.Reference.HostPath, n.Reference.Guid, n.Reference.Major,
                n.Reference.Minor)).ToArray();
        var constants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in (context.Project.CompilationArguments ?? new Dictionary<string, short>())
            .OrderBy(a => a.Key, StringComparer.OrdinalIgnoreCase))
            constants[name] = value.ToString(CultureInfo.InvariantCulture);
        return JsonSerializer.Serialize(
            new RdcoreProjectFile(version, [],
                new(context.Project.Name, references, modules, [], [], SourceProjectOf(context), constants)),
            RdcoreProjectJsonContext.Default.RdcoreProjectFile);
    }

    private static string? SourceProjectOf(WorkspaceArtifactContext context)
    {
        if (context.Project.DefinitionPath is not { } definition
            || !definition.EndsWith(".vbp", StringComparison.OrdinalIgnoreCase))
            return null;
        // RDCore reads its .rdproj only at the workspace root, so the root is the .rdproj's folder.
        var rdprojFolder = context.WorkspaceRoot;
        var relative = Path.GetRelativePath(rdprojFolder, definition);
        return Path.IsPathRooted(relative) ? null : relative.Replace(Path.DirectorySeparatorChar, '/');
    }
}

internal sealed record RdcoreProjectFile(string Version, string[] Configuration, RdcoreProjectInfo ProjectInfo);

internal sealed record RdcoreProjectInfo(string Name, RdcoreReference[] References, RdcoreFile[] Modules,
    RdcoreFile[] OtherFiles, string[] Folders,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SourceProject,
    Dictionary<string, string> PrecompilerConstants);

internal sealed record RdcoreReference(string Name, string? AbsolutePath, Guid? Guid, int? Major, int? Minor,
    string? TypeLibInfoPath = null, bool IsUnremovable = false);

internal sealed record RdcoreFile(string RelativeUri);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(RdcoreProjectFile))]
internal partial class RdcoreProjectJsonContext : JsonSerializerContext;
