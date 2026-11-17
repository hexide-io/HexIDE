using HexIDE.IDE;
using HexIDE.Language;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests;

/// <summary>
/// A file format, VB6's unless a test names another, with stand-in read-only regions, for the seam tests. A project's regions
/// come from its format (#3411 slice 10b-3), so a test that drives them through a stand-in puts its project under this. A part
/// left out does nothing, as no rules or no writer did when they were injected. A copy of HexIDE.Tests' own, which this
/// project can't see.
/// </summary>
internal sealed class RegionStandInFormat(IReadOnlyRegionReader? reader = null, IReadOnlyRegionRules? rules = null,
    IReadOnlyRegionWriter? writer = null) : IProjectFileFormat
{
    private sealed class NoRules : IReadOnlyRegionRules;

    private sealed class NoWriter : IReadOnlyRegionWriter;

    /// <summary>The format every file is read and written with: VB6's, or the test's own.</summary>
    public IProjectFileFormat Inner { get; set; } = new Vb6ProjectFileFormat();

    public IReadOnlyRegionReader? Reader { get; set; } = reader;
    public IReadOnlyRegionRules? Rules { get; set; } = rules;
    public IReadOnlyRegionWriter? Writer { get; set; } = writer;

    public FormatRegions Regions => new(Reader ?? NoReadOnlyRegions.Instance, Writer ?? new NoWriter(), Rules ?? new NoRules());

    /// <summary>Puts <paramref name="project"/> under this format, and returns it.</summary>
    public ProjectDefinition On(ProjectDefinition project)
    {
        project.Format = this;
        return project;
    }

    public string FormExtension => Inner.FormExtension;
    public string MemberExtension(ModuleKind kind) => Inner.MemberExtension(kind);

    public RenderedFile RenderForm(FormDefinition form, string fileName) => Inner.RenderForm(form, fileName);
    public RenderedFile RenderModule(ModuleDefinition module, string fileName) => Inner.RenderModule(module, fileName);
    public string RenderDesignerHeader(FormDefinition form, string fileName) => Inner.RenderDesignerHeader(form, fileName);
    public string RenderProject(ProjectDefinition project, string projectPath, Func<object, string?>? pathOf = null,
        bool includeRelatedDocuments = true) => Inner.RenderProject(project, projectPath, pathOf, includeRelatedDocuments);
    public string RenderGroup(string groupPath, IReadOnlyList<ProjectDefinition> projects, ProjectDefinition? startupProject,
        IReadOnlyList<string>? unknownLines, Func<object, string?>? pathOf) =>
        Inner.RenderGroup(groupPath, projects, startupProject, unknownLines, pathOf);
    public IReadOnlyList<ComponentInstance> RootChildren(IReadOnlyList<ComponentInstance> components, ComponentInstance root) =>
        Inner.RootChildren(components, root);
    public Task<ProjectFileRead> ReadProjectAsync(string path, Vb6TextEncoding hostAnsi, Action<string, byte[]>? fileRead,
        IDeserializeErrorSink problems) => Inner.ReadProjectAsync(path, hostAnsi, fileRead, problems);
    public Task<ProjectFileRead> ReadProjectTextAsync(string path, string text, Vb6TextEncoding encoding, Vb6TextEncoding hostAnsi,
        IDeserializeErrorSink problems) => Inner.ReadProjectTextAsync(path, text, encoding, hostAnsi, problems);
    public SerializedProjectGroup ReadGroup(string text) => Inner.ReadGroup(text);
    public (string Header, string Body) SplitModule(string text, ModuleKind kind) => Inner.SplitModule(text, kind);
    public string CodePrefix(FormDefinition form) => Inner.CodePrefix(form);
    public string CodePrefix(ModuleDefinition module) => Inner.CodePrefix(module);
    public bool PrefixCarriesName(ModuleDefinition module) => Inner.PrefixCarriesName(module);
    public string CompanionExtension(string path) => Inner.CompanionExtension(path);
    public string MemberLine(ModuleKind kind, string name, string relativePath) => Inner.MemberLine(kind, name, relativePath);
    public bool IsCompanionCitation(string value) => Inner.IsCompanionCitation(value);
    public FormDefinition? ReadForm(ProjectDefinition project, string text, byte[]? companion, IDeserializeErrorSink problems) =>
        Inner.ReadForm(project, text, companion, problems);
    public FormDefinition? ReadDesignerPart(ProjectDefinition project, string text, byte[]? companion, IDeserializeErrorSink problems) =>
        Inner.ReadDesignerPart(project, text, companion, problems);
}
