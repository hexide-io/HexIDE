using System.Text.RegularExpressions;
using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.IO;
using HexIDE.Localization;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Utils;

namespace HexIDE.Projects;

/// <summary>A project type an add-in contributed, with the add-in that did.</summary>
/// <param name="Type">The type, its file names copied when it was contributed.</param>
/// <param name="AddinId">The add-in's manifest id, which stays the same.</param>
/// <param name="Origin">The add-in's title, for display.</param>
public sealed record ContributedProjectType(AddinProjectType Type, string AddinId, string Origin)
{
    private readonly CancellationTokenSource _withdrawn = new();

    /// <summary>Cancelled once the type is taken out, as when its add-in is unloaded; an open in progress is called off.</summary>
    public CancellationToken Withdrawn => _withdrawn.Token;

    internal void Withdraw() => _withdrawn.Cancel();
}

/// <summary>
/// The project types add-ins contribute (<see cref="IHexIdeHost.RegisterProjectType"/>), and the reading of a project file
/// of one into a <see cref="ProjectDefinition"/> whose membership the add-in's program owns.
/// </summary>
public sealed partial class ContributedProjects
{
    // HexIDE's own, which no add-in reads in its place.
    private static readonly string[] OwnPatterns = ["*.vbp", "*.vbg", "*.twinproj"];

    private readonly object _gate = new();
    private readonly List<ContributedProjectType> _types = [];

    /// <summary>Raised when a type is contributed or taken out.</summary>
    public event Action? TypesChanged;

    /// <summary>
    /// Reads a member's file as it is added. The file system's read, except under test, where the test decides when each
    /// read completes and so in what order the add-in's reports finish.
    /// </summary>
    internal Func<string, Task<byte[]>> ReadFile { get; set; } = path => File.ReadAllBytesAsync(path);

    /// <summary>Every contributed type, in the order they were contributed.</summary>
    public IReadOnlyList<ContributedProjectType> Types
    {
        get
        {
            lock (_gate) return [.. _types];
        }
    }

    /// <summary>Adds an add-in's project type.</summary>
    /// <returns>What takes it out again.</returns>
    internal IDisposable Register(AddinProjectType type, AddinOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(type.Title, nameof(type));
        ArgumentException.ThrowIfNullOrWhiteSpace(origin.Id);
        if (type.OpenAsync is null || type.FileNames is not { Count: > 0 })
            throw new ArgumentException("A project type needs the files it reads and what reads them.", nameof(type));
        // Copied, so the names checked below are the names read: a list the add-in changes later changes neither.
        IReadOnlyList<string> names = [.. type.FileNames];
        foreach (var name in names)
            if (!IsFileNameOrPattern(name))
                throw new ArgumentException($"'{name}' is neither a file name nor a pattern of the form '*.ext'.", nameof(type));

        var contributed = new ContributedProjectType(type with { FileNames = names }, origin.Id, origin.Title);
        lock (_gate)
        {
            var taken = names.FirstOrDefault(name =>
                OwnPatterns.Any(own => Overlaps(own, name)) || _types.Any(known => known.Type.FileNames.Any(other => Overlaps(other, name))));
            if (taken is not null)
                throw new InvalidOperationException($"{origin.Title} contributes a project type for '{taken}', which another project type reads already.");
            _types.Add(contributed);
        }
        TypesChanged?.Invoke();

        var handle = new ActionDisposable(() =>
        {
            bool removed;
            lock (_gate) removed = _types.Remove(contributed);
            if (!removed) return;
            contributed.Withdraw();
            TypesChanged?.Invoke();
        });
        // An add-in whose initialisation fails has its type taken out with its other contributions.
        AddinInitializationScope.Record(handle.Dispose);
        return handle;
    }

    /// <summary>The contributed type that reads this file, or null when none does.</summary>
    public ContributedProjectType? Find(string path)
    {
        var fileName = Path.GetFileName(path);
        lock (_gate)
            return _types.FirstOrDefault(known => known.Type.FileNames.Any(name => Matches(name, fileName)));
    }

    private static bool IsFileNameOrPattern(string name) =>
        !string.IsNullOrWhiteSpace(name) && name.IndexOfAny(Path.GetInvalidFileNameChars().Where(c => c != '*').ToArray()) < 0 &&
        (!name.Contains('*') || PatternForm().IsMatch(name));

    [GeneratedRegex(@"^\*\.[^*.]+$")]
    private static partial Regex PatternForm();

    private static bool Matches(string nameOrPattern, string fileName) =>
        nameOrPattern.StartsWith("*.", StringComparison.Ordinal)
            ? fileName.EndsWith(nameOrPattern[1..], StringComparison.OrdinalIgnoreCase)
              && fileName.Length > nameOrPattern.Length - 1
            : string.Equals(nameOrPattern, fileName, StringComparison.OrdinalIgnoreCase);

    // Whether some file is read by both: a name either one matches, or two patterns of one extension.
    private static bool Overlaps(string a, string b) =>
        Matches(a, b.TrimStart('*')) || Matches(b, a.TrimStart('*')) || string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the project for what the add-in read, its modules loaded from their files as a .vbp's are. A member that
    /// cannot be read, or whose name another member has, is left out and named in the returned problems.
    /// </summary>
    internal async Task<(ProjectDefinition Project, IReadOnlyList<string> Problems)> BuildAsync(
        string path, AddinProjectContent content, ContributedProjectType type, Vb6TextEncoding hostAnsi,
        Action<string, byte[]>? fileRead, ILocalizationService localization)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, content.Name)
        {
            ContributedBy = type.AddinId,
            ContributorTitle = type.Origin,
        };
        project.AbsolutePath = path;
        project.ItemLinesDirectory = Path.GetDirectoryName(path)!;
        project.ListedFolders = [.. content.Folders];
        project.SetReferences(content.References.Select(ToVbReference));

        var problems = new List<string>();
        foreach (var member in content.Members)
        {
            try
            {
                await AddMemberAsync(project, member, hostAnsi, fileRead, localization);
            }
            catch (MemberNameTakenException ex)
            {
                problems.Add(ex.Message);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                problems.Add($"{member.Path}: {ex.Message}");
            }
        }
        return (project, problems);
    }

    /// <summary>
    /// Adds a member as a .vbp's is loaded. One already in the project at the same path is not added again.
    /// </summary>
    /// <returns>The module or other file added, or null when the project had it already.</returns>
    /// <exception cref="MemberNameTakenException">
    /// Another member has its name, which VB6 refuses as <c>Name conflicts with existing module</c>: it is left out.
    /// </exception>
    internal async Task<object?> AddMemberAsync(ProjectDefinition project, AddinProjectMember member, Vb6TextEncoding hostAnsi,
        Action<string, byte[]>? fileRead, ILocalizationService localization)
    {
        if (!Path.IsPathFullyQualified(member.Path))
            throw new ArgumentException($"'{member.Path}' is not a full path.");
        if (MemberAt(project, member.Path) is not null)
            return null;
        if (member.Kind == AddinMemberKind.Other)
        {
            var document = new RelatedDocumentDefinition(project, Path.GetFileName(member.Path), member.Path);
            project.AddRelatedDocument(document);
            return document;
        }

        // A document module is kept as a class module with its header as it came: it is the host document's code,
        // for reading and editing, and nothing runs or instantiates it.
        var kind = member.Kind == AddinMemberKind.StandardModule ? ModuleKind.StandardModule : ModuleKind.ClassModule;
        var bytes = await ReadFile(member.Path);
        var read = Vb6TextFile.ReadDocument(bytes, null, hostAnsi);
        var name = NameOf(read.Text) ?? Path.GetFileNameWithoutExtension(member.Path);
        if (ProjectNaming.IsNameTaken(project, name))
            throw new MemberNameTakenException(string.Format(localization.GetString("Str.ContributedProject.NameTaken"),
                Path.GetFileName(member.Path), name, project.Name));
        fileRead?.Invoke(member.Path, bytes);
        var (header, body) = ModuleFileFormat.SplitHeader(read.Text, kind);
        var module = new ModuleDefinition(project, name, kind)
        {
            AbsolutePath = member.Path,
            TextEncoding = read.Encoding,
        };
        module.RecordOriginalHeader(header);
        module.UpdateCode(body);
        project.AddModule(module);
        return module;
    }

    /// <summary>The module or other file of <paramref name="project"/> at <paramref name="path"/>, or null.</summary>
    internal static object? MemberAt(ProjectDefinition project, string path) =>
        (object?)project.Modules.FirstOrDefault(module => module.AbsolutePath is { } at && HostFilePath.Equals(at, path))
        ?? project.RelatedDocuments.FirstOrDefault(document => document.AbsolutePath is { } at && HostFilePath.Equals(at, path));

    /// <summary>A member left out because another has its name; the message says which, for the user.</summary>
    internal sealed class MemberNameTakenException(string message) : Exception(message);

    // The module's Attribute VB_Name, which names it; the project file does not.
    private static string? NameOf(string source) => VbName().Match(source) is { Success: true } match ? match.Groups[1].Value : null;

    [GeneratedRegex(@"^\s*Attribute\s+VB_Name\s*=\s*""([^""]+)""", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex VbName();

    internal static VbReference ToVbReference(AddinProjectReference reference) => new(
        reference.Guid is { } guid ? "{" + guid.ToString().ToUpperInvariant() + "}" : "",
        reference.Major is { } major ? $"{major:x}.{reference.Minor ?? 0:x}" : "",
        0, reference.Path, reference.Name);
}
