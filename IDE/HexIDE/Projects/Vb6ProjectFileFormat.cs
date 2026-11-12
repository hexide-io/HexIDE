using HexIDE.IDE;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Projects;

/// <summary>
/// VB6's files: <c>.vbp</c>, <c>.vbg</c>, <c>.frm</c>, <c>.ctl</c>, <c>.pag</c>, <c>.bas</c> and <c>.cls</c>, and the
/// companion binaries that go with the designer files. The one place in the IDE that names the readers and writers (#3564).
/// </summary>
public sealed class Vb6ProjectFileFormat : IProjectFileFormat
{
    /// <inheritdoc/>
    public RenderedFile RenderForm(FormDefinition form, string fileName)
    {
        var (text, companion) = new FormSerializer().Serialize(form, fileName);
        return new(text, companion);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A UserControl or PropertyPage is a designer file with a companion. A <c>.bas</c> or <c>.cls</c> gets the canonical
    /// VB6 header (<see cref="ModuleFileFormat"/>) in front of its body, so vb6.exe can load it.
    /// </remarks>
    public RenderedFile RenderModule(ModuleDefinition module, string fileName)
    {
        if (module.Kind is ModuleKind.UserControl or ModuleKind.PropertyPage && module.FormPart is { } designerPart)
        {
            var (text, companion) = new FormSerializer().Serialize(designerPart, module.Code, fileName);
            return new(text, companion);
        }
        return new(ModuleFileFormat.ToFileContent(module.Code, module.Name, module.Kind, module.OriginalHeader), null);
    }

    /// <inheritdoc/>
    public string RenderDesignerHeader(FormDefinition form, string fileName) =>
        new FormSerializer().SerializeDesignerText(form, fileName);

    /// <inheritdoc/>
    public string RenderProject(ProjectDefinition project, string projectPath, Func<object, string?>? pathOf = null,
        bool includeRelatedDocuments = true) =>
        new ProjectSerializer().Serialize(project, projectPath, pathOf, includeRelatedDocuments);

    /// <inheritdoc/>
    public string RenderGroup(string groupPath, IReadOnlyList<ProjectDefinition> projects, ProjectDefinition? startupProject,
        IReadOnlyList<string>? unknownLines, Func<object, string?>? pathOf) =>
        new GroupSerializer().Serialize(groupPath, projects, startupProject, unknownLines, pathOf);

    /// <inheritdoc/>
    public IReadOnlyList<ComponentInstance> RootChildren(IReadOnlyList<ComponentInstance> components, ComponentInstance root) =>
        FormSerializer.RootChildren(components, root);

    /// <inheritdoc/>
    public async Task<ProjectFileRead> ReadProjectAsync(string path, Vb6TextEncoding hostAnsi, Action<string, byte[]>? fileRead,
        IDeserializeErrorSink problems)
    {
        var read = await new ProjectLoader { HostAnsi = hostAnsi, FileRead = fileRead }.LoadAsync(path, problems);
        return new(read.Project, read.Serialized.SkippedUserDocumentPaths, read.ReadByteForByte, read.ProjectFileBytes);
    }

    /// <inheritdoc/>
    public SerializedProjectGroup ReadGroup(string text) => new GroupDeserializer().Deserialize(text);

    /// <inheritdoc/>
    /// <remarks>A form can hold the project's UserControls, so they are known to the read. A designer part's read does not.</remarks>
    public FormDefinition? ReadForm(ProjectDefinition project, string text, byte[]? companion, IDeserializeErrorSink problems) =>
        new FormDeserializer().Deserialize(project, text, problems, Records(companion, text), ProjectLoader.BuildUserControlRegistry(project));

    /// <inheritdoc/>
    public FormDefinition? ReadDesignerPart(ProjectDefinition project, string text, byte[]? companion, IDeserializeErrorSink problems) =>
        new FormDeserializer().Deserialize(project, text, problems, Records(companion, text));

    // The designer file's cited offsets are what partition the companion into records.
    private static IReadOnlyDictionary<int, byte[]>? Records(byte[]? companion, string text) =>
        companion is null ? null : FrxDeserializer.Read(companion, text);

    /// <inheritdoc/>
    public (string Header, string Body) SplitModule(string text, ModuleKind kind) => ModuleFileFormat.SplitHeader(text, kind);

    /// <inheritdoc/>
    public string CodePrefix(FormDefinition form) => FormCodeText.Prefix(form);

    /// <inheritdoc/>
    public string CodePrefix(ModuleDefinition module) => FormCodeText.Prefix(module);

    /// <inheritdoc/>
    public bool PrefixCarriesName(ModuleDefinition module) => ModuleFileFormat.HandlesHeader(module.Kind);

    /// <inheritdoc/>
    public string CompanionExtension(string path) => ProjectLoader.CompanionBinaryExtension(path);

    /// <inheritdoc/>
    public string MemberLine(ModuleKind kind, string name, string relativePath) => ProjectLoader.ItemLineFor(kind, name, relativePath);

    /// <inheritdoc/>
    public bool IsCompanionCitation(string value) => FrxDeserializer.IsFrxReference(value);
}
