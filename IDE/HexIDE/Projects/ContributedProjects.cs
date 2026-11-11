using System.Text.RegularExpressions;
using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.IO;
using HexIDE.Localization;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Utils;
using Serilog;

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

    /// <summary>
    /// What a project of this type offers: its forms written where its type can render them, and its own file, with
    /// membership and references where that file carries them, where its type has a writer. Never Components, Make or Run.
    /// </summary>
    public ProjectOffers Offers => ProjectOffers.Nothing with
    {
        SetsProperties = Type.ProjectFile is { } properties && properties.Carries.HasFlag(ProjectFileParts.Properties),
        SetsReferences = Type.ProjectFile is { } references && references.Carries.HasFlag(ProjectFileParts.References),
        WritesForms = Type.Forms is IFormFileFormat,
        WritesProjectFile = Type.ProjectFile is not null,
        ChangesMembership = Type.ProjectFile is { } writer && writer.Carries.HasFlag(ProjectFileParts.Members),
    };
}

/// <summary>
/// Renders a contributed project's own file through its type's writer, for as long as the type is there, and only for the
/// path the project was opened from (#3698). An add-in that has gone is never asked again, and one that throws has rendered
/// nothing.
/// </summary>
internal sealed class ContributedProjectFile(ContributedProjectType type, string openedFrom) : IProjectFileWriter
{
    /// <summary>The type whose writer this is, and so the add-in that writes this project's file.</summary>
    internal ContributedProjectType Type => type;

    /// <summary>The project file the type read, which is the only place it's written.</summary>
    internal string OpenedFrom { get; } = Path.GetFullPath(openedFrom);

    public ProjectFileParts Carries => type.Type.ProjectFile?.Carries ?? ProjectFileParts.None;

    public System.Text.Encoding Encoding => type.Type.ProjectFile?.Encoding ?? new System.Text.UTF8Encoding(false);

    public string RenderProject(ProjectDefinition project, string path, Func<object, string?>? pathOf)
    {
        if (type.Withdrawn.IsCancellationRequested)
            throw Refused(project.Name, "it's no longer loaded");
        if (type.Type.ProjectFile is not { } writer)
            throw Refused(project.Name, "its project type has nothing that writes its project file");
        if (!IsWhereItLives(path))
            throw Refused(project.Name, "HexIDE writes its project file only where it was opened");
        try
        {
            return writer.RenderProject(project, path, pathOf);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "{Origin} could not render {Project}'s project file", type.Origin, project.Name);
            throw new RenderRefusal(project.Name, type.Origin, ex.Message, ex.Message);
        }
    }

    /// <summary>Whether <paramref name="path"/> is the project file the type read.</summary>
    internal bool IsWhereItLives(string path)
    {
        try
        {
            return HostFilePath.Equals(Path.GetFullPath(path), OpenedFrom);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    internal RenderRefusal Refused(string project, string reason)
    {
        Log.Warning("Not writing {Project}'s project file: {Origin} can't render it, as {Reason}", project, type.Origin, reason);
        return new RenderRefusal(project, type.Origin, reason);
    }
}

/// <summary>
/// Reads and renders a contributed project's forms through its type's format, for as long as the type is there. An add-in
/// that has gone is never asked again. One that throws, or gives back a form with no root, has failed to read that form and
/// nothing else, and one that throws while rendering has rendered nothing.
/// </summary>
/// <remarks>
/// <para>
/// Where the type only reads, every form it reads is marked read-only, whichever of open, reload and crash recovery asked, as
/// a reload takes its verdict from the read. Nothing else can write it: rendering what an add-in read as a VB6 file would
/// change the file silently.
/// </para>
/// <para>
/// A render is checked before anything is written: it ends with the code it was given, which is how the code window finds
/// the header. A companion goes beside its file, and never on the file itself, another of the project's members or the
/// project's own file, so a format that names one wrongly can't write over anything the project holds. That holds when a
/// companion is read too.
/// </para>
/// </remarks>
internal sealed class ContributedFormFormat(ContributedProjectType type) : IFormFileFormat
{
    /// <summary>The type whose format this is, and so the add-in that reads and writes these forms.</summary>
    internal ContributedProjectType Type => type;

    /// <summary>The project whose forms these are, whose files a companion may never be. Set once it is built.</summary>
    internal ProjectDefinition? Project { get; set; }

    public FormDefinition? ReadForm(ProjectDefinition project, string text, byte[]? companion, IDeserializeErrorSink problems) =>
        Read(problems, forms => forms.ReadForm(project, text, companion, problems));

    public FormDefinition? ReadDesignerPart(ProjectDefinition project, string text, byte[]? companion, IDeserializeErrorSink problems) =>
        Read(problems, forms => forms.ReadDesignerPart(project, text, companion, problems));

    private FormDefinition? Read(IDeserializeErrorSink problems, Func<IFormFileReader, FormDefinition?> read)
    {
        if (type.Withdrawn.IsCancellationRequested)
        {
            problems.LogError($"{type.Origin} is no longer loaded, so nothing can read this form.");
            return null;
        }
        if (type.Type.Forms is not { } forms)
        {
            problems.LogError($"{type.Origin} lists this as a form, but its project type has nothing that reads forms.");
            return null;
        }

        FormDefinition? form;
        try
        {
            form = read(forms);
            // The name is the root's, and every view of the form asks for it.
            _ = form?.Name;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "{Origin} could not read a form", type.Origin);
            problems.LogError($"{type.Origin}: {ex.Message}");
            return null;
        }

        if (forms is not IFormFileFormat)
            form?.MarkUnfaithfulToSave(UnfaithfulSaveCause.ReadByAddin, $"{type.Origin} read this form, and its project type has nothing that writes one");
        return form;
    }

    public RenderedFile RenderForm(FormDefinition form, string fileName) =>
        Render(form.Name, form.Code, forms => forms.RenderForm(form, fileName));

    public RenderedFile RenderDesignerFile(ModuleDefinition module, string fileName) =>
        Render(module.Name, module.Code, forms => forms.RenderDesignerFile(module, fileName));

    public string RenderDesignerHeader(FormDefinition form, string fileName) =>
        Ask(form.Name, forms => forms.RenderDesignerHeader(form, fileName));

    private RenderedFile Render(string document, string code, Func<IFormFileFormat, RenderedFile> render)
    {
        var rendered = Ask(document, render);
        if (rendered.Text is not { } text || !text.EndsWith(code, StringComparison.Ordinal))
            throw Refused(document, "what it rendered doesn't end with the code it was given, so the header can't be told from it");
        // Where a companion may go is checked with the file's whole path, which only the IDE's side has: ProjectService.
        return rendered;
    }

    // Never asks a type that has gone: turning the add-in off, or withdrawing its consent, is what stops it running.
    private T Ask<T>(string document, Func<IFormFileFormat, T> ask)
    {
        if (type.Withdrawn.IsCancellationRequested)
            throw Refused(document, "it's no longer loaded");
        if (type.Type.Forms is not IFormFileFormat format)
            throw Refused(document, "its project type has nothing that writes forms");
        try
        {
            return ask(format);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "{Origin} could not render {Document}", type.Origin, document);
            throw new RenderRefusal(document, type.Origin, ex.Message, ex.Message);
        }
    }

    internal RenderRefusal Refused(string document, string reason)
    {
        Log.Warning("Not writing {Document}: {Origin} can't render it, as {Reason}", document, type.Origin, reason);
        return new RenderRefusal(document, type.Origin, reason);
    }

    public string? CompanionPath(string path)
    {
        if (type.Withdrawn.IsCancellationRequested)
            return null;
        string? companion;
        try
        {
            companion = type.Type.Forms?.CompanionPath(path);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "{Origin} could not name a form's companion", type.Origin);
            return null;
        }
        if (companion is null || IsBeside(path, companion) && !IsTheProjects(companion))
            return companion;
        Log.Warning("{Origin} names {Companion} as the companion of {Path}, which isn't beside it or is one of the project's own "
                    + "files, so it's neither read nor written", type.Origin, companion, path);
        return null;
    }

    // In the same folder as the file, and not the file itself.
    private static bool IsBeside(string path, string companion)
    {
        try
        {
            var (file, other) = (Path.GetFullPath(path), Path.GetFullPath(companion));
            return !HostFilePath.Equals(file, other)
                   && HostFilePath.Equals(Path.GetDirectoryName(file) ?? "", Path.GetDirectoryName(other) ?? "");
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    // The project's own file, or any member it holds, whatever its kind: a companion written there would overwrite it.
    private bool IsTheProjects(string companion)
    {
        if (Project is not { } project)
            return false;
        string full;
        try
        {
            full = Path.GetFullPath(companion);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return true;
        }
        return project.Forms.Select(form => form.AbsolutePath)
            .Concat(project.Modules.Select(module => module.AbsolutePath))
            .Concat(project.RelatedDocuments.Select(document => document.AbsolutePath))
            .Append(project.AbsolutePath)
            .OfType<string>()
            .Any(own => HostFilePath.Equals(Path.GetFullPath(own), full));
    }
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

    /// <summary>Raised once a type has been taken out, on the thread that took it out, before <see cref="TypesChanged"/>.</summary>
    public event Action<ContributedProjectType>? TypeWithdrawn;

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
            TypeWithdrawn?.Invoke(contributed);
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
        var format = new ContributedFormFormat(type);
        var project = new ProjectDefinition(VBProjectType.EXE, content.Name)
        {
            ContributedBy = type.AddinId,
            Offers = type.Offers,
            ContributorTitle = type.Origin,
            FormFormat = format,
            ProjectFileWriter = type.Type.ProjectFile is not null ? new ContributedProjectFile(type, path) : null,
        };
        // Its file is written in what its writer names. Without one it's never written, so its encoding says nothing.
        if (type.Type.ProjectFile is { } writer)
            project.TextEncoding = EncodingOf(writer.Encoding);
        format.Project = project;
        project.AbsolutePath = path;
        project.ItemLinesDirectory = Path.GetDirectoryName(path)!;
        project.ListedFolders = [.. content.Folders];
        project.SetReferences(content.References.Select(ToVbReference));

        var problems = new List<string>();
        // UserControls first, as a .vbp's are: a form can hold them, so its read has to find them there. In order otherwise.
        foreach (var member in content.Members.OrderBy(member => member.Kind != AddinMemberKind.UserControl))
        {
            try
            {
                await AddMemberAsync(project, member, hostAnsi, fileRead, localization, problems);
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
    /// <param name="readProblems">Told what a form that did load had wrong with it.</param>
    /// <returns>The module, form or other file added, or null when the project had it already.</returns>
    /// <exception cref="MemberNameTakenException">
    /// Another member has its name, which VB6 refuses as <c>Name conflicts with existing module</c>: it is left out.
    /// </exception>
    internal async Task<object?> AddMemberAsync(ProjectDefinition project, AddinProjectMember member, Vb6TextEncoding hostAnsi,
        Action<string, byte[]>? fileRead, ILocalizationService localization, ICollection<string>? readProblems = null)
    {
        if (!Path.IsPathFullyQualified(member.Path))
            throw new ArgumentException($"'{member.Path}' is not a full path.");
        // A member's save would write over the project's own file.
        if (IsTheProjectFile(project, member.Path))
            throw new ArgumentException($"'{member.Path}' is the project's own file, so it can't be one of its members.");
        if (MemberAt(project, member.Path) is not null)
            return null;
        if (member.Kind == AddinMemberKind.Other)
        {
            var document = new RelatedDocumentDefinition(project, Path.GetFileName(member.Path), member.Path);
            project.AddRelatedDocument(document);
            return document;
        }
        if (member.Kind is AddinMemberKind.Form or AddinMemberKind.UserControl or AddinMemberKind.PropertyPage)
            return await AddDesignerFileAsync(project, member, hostAnsi, fileRead, localization, readProblems);

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

    // Reads the text before the companion, as a .vbp's forms are: the text says how the companion divides.
    private async Task<object> AddDesignerFileAsync(ProjectDefinition project, AddinProjectMember member, Vb6TextEncoding hostAnsi,
        Action<string, byte[]>? fileRead, ILocalizationService localization, ICollection<string>? readProblems)
    {
        var path = member.Path;
        // Set on every project a contributed type opened, the only ones that reach here.
        var reader = project.FormFormat!;
        var bytes = await ReadFile(path);
        var read = Vb6TextFile.ReadDocument(bytes, null, hostAnsi);
        var companionPath = reader.CompanionPath(path);
        var companion = companionPath is not null && File.Exists(companionPath) ? await ReadFile(companionPath) : null;

        var problems = new ProjectService.DeserializeErrorSink(localization);
        var designer = member.Kind == AddinMemberKind.Form
            ? reader.ReadForm(project, read.Text, companion, problems)
            : reader.ReadDesignerPart(project, read.Text, companion, problems);
        if (designer is null)
            throw new ArgumentException(problems.Errors.Count > 0 ? string.Join(" ", problems.Errors) : "It couldn't be read as a form.");
        if (ProjectNaming.IsNameTaken(project, designer.Name))
            throw new MemberNameTakenException(string.Format(localization.GetString("Str.ContributedProject.NameTaken"),
                Path.GetFileName(path), designer.Name, project.Name));

        fileRead?.Invoke(path, bytes);
        if (companion is not null)
            fileRead?.Invoke(companionPath!, companion);
        foreach (var problem in problems.Errors)
            readProblems?.Add($"{path}: {problem}");
        designer.AbsolutePath = path;
        if (member.Kind == AddinMemberKind.Form)
        {
            designer.TextEncoding = read.Encoding;
            project.AddForm(designer);
            return designer;
        }

        var kind = member.Kind == AddinMemberKind.UserControl ? ModuleKind.UserControl : ModuleKind.PropertyPage;
        var module = new ModuleDefinition(project, designer.Name, kind) { AbsolutePath = path, TextEncoding = read.Encoding };
        module.UpdateFormPart(designer);
        module.UpdateCode(designer.Code);
        project.AddModule(module);
        return module;
    }

    /// <summary>Whether <paramref name="path"/> is <paramref name="project"/>'s own file, which no member may be.</summary>
    internal static bool IsTheProjectFile(ProjectDefinition project, string path)
    {
        if (project.AbsolutePath is not { } own)
            return false;
        try
        {
            return HostFilePath.Equals(Path.GetFullPath(path), Path.GetFullPath(own));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return true;
        }
    }

    // The encoding an add-in's writer names, as the IDE's own files name theirs: its code page, and a BOM where UTF-8 has one.
    private static Vb6TextEncoding EncodingOf(System.Text.Encoding encoding) =>
        new(encoding.CodePage, encoding.CodePage == 65001 && encoding.GetPreamble().Length > 0);

    /// <summary>The module, form or other file of <paramref name="project"/> at <paramref name="path"/>, or null.</summary>
    internal static object? MemberAt(ProjectDefinition project, string path) =>
        (object?)project.Modules.FirstOrDefault(module => module.AbsolutePath is { } at && HostFilePath.Equals(at, path))
        ?? (object?)project.Forms.FirstOrDefault(form => form.AbsolutePath is { } at && HostFilePath.Equals(at, path))
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
