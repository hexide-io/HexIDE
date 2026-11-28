using HexIDE.Runtime.Components;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.Projects;

/// <summary>A custom project file format that makes tests pass only through <see cref="IProjectFileFormat"/> (#3564).</summary>
internal sealed class StandInFormat : IProjectFileFormat
{
    public string FormExtension { get; set; } = "frm";
    public string? ClassExtension { get; set; }
    public string MemberExtension(ModuleKind kind) => kind == ModuleKind.ClassModule && ClassExtension is not null
        ? ClassExtension : new Vb6ProjectFileFormat().MemberExtension(kind);
    public FormatRegions? Regions { get; set; }

    public string CodePrefix(FormDefinition form) => $"prefix of {form.Name}: {Stamp}\r\n";
    public string CodePrefix(ModuleDefinition module) => $"prefix of {module.Name}: {Stamp}\r\n";
    public bool CarriesName { get; set; } = true;
    public bool PrefixCarriesName(ModuleDefinition module) => CarriesName;

    public string Stamp { get; set; } = "one";
    public byte[]? Companion { get; set; }
    public string FormName { get; set; } = "Form1";
    public string? Citation { get; set; }
    public IReadOnlyList<string> Skipped { get; set; } = [];
    public SerializedProjectGroup Group { get; set; } = new(null, [], []);
    public List<(string Text, byte[]? Companion)> FormsRead { get; } = [];
    public List<(string Text, byte[]? Companion)> DesignerPartsRead { get; } = [];
    public string? ProposedText { get; private set; }

    public RenderedFile RenderForm(FormDefinition form, string fileName) =>
        new($"form {form.Name} as {fileName}: {Stamp}", Companion);

    public RenderedFile RenderModule(ModuleDefinition module, string fileName) =>
        new($"module {module.Name} as {fileName}: {Stamp}", null);

    public string RenderDesignerHeader(FormDefinition form, string fileName) => $"header as {fileName}: {Stamp}";

    public string RenderProject(ProjectDefinition project, string projectPath, Func<object, string?>? pathOf = null,
        bool includeRelatedDocuments = true) => $"project {project.Name}: {Stamp}";

    public string RenderGroup(string groupPath, IReadOnlyList<ProjectDefinition> projects, ProjectDefinition? startupProject,
        IReadOnlyList<string>? unknownLines, Func<object, string?>? pathOf) => $"group of {projects.Count}: {Stamp}";

    public IReadOnlyList<ComponentInstance> RootChildren(IReadOnlyList<ComponentInstance> components, ComponentInstance root) => [];

    public Task<ProjectFileRead> ReadProjectAsync(string path, Vb6TextEncoding hostAnsi, Action<string, byte[]>? fileRead,
        IDeserializeErrorSink problems)
    {
        var directory = Path.GetDirectoryName(path)!;
        var project = new ProjectDefinition(VBProjectType.EXE, "Test") { AbsolutePath = path, ItemLinesDirectory = directory };
        var form = new FormDefinition(project, FormComponentClass.Instance, "Form1") { AbsolutePath = Path.Join(directory, "Form1.frm") };
        project.AddForm(form);
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule) { AbsolutePath = Path.Join(directory, "Module1.bas") };
        project.AddModule(module);
        foreach (var member in new[] { form.AbsolutePath!, module.AbsolutePath! }.Where(File.Exists))
            fileRead?.Invoke(member, File.ReadAllBytes(member));
        return Task.FromResult(new ProjectFileRead(project, Skipped, [], File.ReadAllBytes(path)));
    }

    public Task<ProjectFileRead> ReadProjectTextAsync(string path, string text, Vb6TextEncoding encoding,
        Vb6TextEncoding hostAnsi, IDeserializeErrorSink problems)
    {
        ProposedText = text;
        return Task.FromResult(new ProjectFileRead(new(VBProjectType.EXE, "Proposed"), [], [],
            Vb6TextFile.EncodeDocument(path, text, encoding)));
    }

    public SerializedProjectGroup ReadGroup(string text) => Group;

    public FormDefinition? ReadForm(ProjectDefinition project, string text, byte[]? companion, IDeserializeErrorSink problems)
    {
        FormsRead.Add((text, companion));
        return Read(project, text);
    }

    public FormDefinition? ReadDesignerPart(ProjectDefinition project, string text, byte[]? companion, IDeserializeErrorSink problems)
    {
        DesignerPartsRead.Add((text, companion));
        return Read(project, text);
    }

    private FormDefinition Read(ProjectDefinition project, string text)
    {
        var form = new FormDefinition(project, FormComponentClass.Instance, FormName);
        form.UpdateCode("code of " + text);
        return form;
    }

    public (string Header, string Body) SplitModule(string text, ModuleKind kind) => ("header of " + text, "body of " + text);

    public string CompanionExtension(string path) => ".standin";

    public string MemberLine(ModuleKind kind, string name, string relativePath) => $"member {name}";

    public bool IsCompanionCitation(string value) => value == Citation;
}
