using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Sidecar;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.Projects;

/// <summary>
/// What the IDE does with a project's files through a format that isn't VB6's (#3564). The stand-in's text is nothing a VB6
/// reader could take, so each assertion can only pass by way of the interface. The loading is still the real VB6 reader,
/// until the read half follows.
/// </summary>
public class ProjectFileFormatSeamTests : IDisposable
{
    private readonly string _dir = Path.Join(Path.GetTempPath(), "hexide-format-" + Guid.NewGuid().ToString("N"));
    private readonly List<ProjectDefinition> _loaded = [];
    private readonly IProjectManager _projects = Substitute.For<IProjectManager>();
    private readonly StandInFormat _format = new();
    private readonly string _vbp;
    private readonly string _frm;
    private readonly string _bas;

    public ProjectFileFormatSeamTests()
    {
        Directory.CreateDirectory(_dir);
        _vbp = Path.Join(_dir, "Test.vbp");
        _frm = Path.Join(_dir, "Form1.frm");
        _bas = Path.Join(_dir, "Module1.bas");
        File.WriteAllText(_vbp, "Type=Exe\r\nForm=Form1.frm\r\nModule=Module1; Module1.bas\r\nName=\"Test\"\r\n");
        File.WriteAllText(_frm, "VERSION 5.00\r\nBegin VB.Form Form1\r\n   Caption = \"Form1\"\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n");
        File.WriteAllText(_bas, "Attribute VB_Name = \"Module1\"\r\nOption Explicit\r\n");
        _projects.LoadedProjects.Returns(_ => _loaded);
        _projects.When(m => m.AddProject(Arg.Any<ProjectDefinition>()))
            .Do(call => _loaded.Add(call.Arg<ProjectDefinition>()));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private async Task<ProjectService> Opened(string? path = null)
    {
        var windows = Substitute.For<IWindowManager>();
        windows.ShowDialog(Arg.Any<IDialog>()).Returns(call =>
        {
            if (call.Arg<IDialog>() is SaveChangesViewModel prompt) prompt.Yes();
            return Task.FromResult(true);
        });
        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        var service = new ProjectService(() => throw new InvalidOperationException("no new-project dialog here"), windows,
            Substitute.For<IEventBus>(), _projects, Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), sidecar, new FileBaselineStore(), Substitute.For<ILocalizationService>(),
            Substitute.For<IHeaderRefresher>(), format: _format);
        await service.OpenProject(path ?? _vbp);
        return service;
    }

    [Fact]
    public async Task A_document_is_edited_exactly_when_the_format_renders_it_differently()
    {
        var service = await Opened();
        var project = _loaded.Single();

        service.HasUnsavedChanges(project.Forms.Single()).Should().BeFalse();
        service.HasUnsavedChanges(project.Modules.Single()).Should().BeFalse();

        _format.Stamp = "two";

        service.HasUnsavedChanges(project.Forms.Single()).Should().BeTrue();
        service.HasUnsavedChanges(project.Modules.Single()).Should().BeTrue();
    }

    [Fact]
    public async Task A_save_writes_what_the_format_rendered()
    {
        var service = await Opened();
        _format.Stamp = "two";

        await service.SaveProject(_loaded.Single(), saveAs: false);

        File.ReadAllText(_frm).Should().Be("form Form1 as Form1.frm: two");
        File.ReadAllText(_bas).Should().Be("module Module1 as Module1.bas: two");
        File.ReadAllText(_vbp).Should().Be("project Test: two");
    }

    [Fact]
    public async Task A_save_writes_the_companion_the_format_rendered()
    {
        var service = await Opened();
        _format.Companion = [1, 2, 3];

        await service.SaveProject(_loaded.Single(), saveAs: false);

        File.ReadAllBytes(Path.ChangeExtension(_frm, ".frx")).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task A_group_file_is_what_the_format_rendered()
    {
        var vbg = Path.Join(_dir, "Group.vbg");
        File.WriteAllText(vbg, "VBGROUP 5.0\r\nProject=Test.vbp\r\nStartupProject=Test.vbp\r\n");
        var service = await Opened(vbg);

        await service.SaveAllProjects(saveAs: false);

        File.ReadAllText(vbg).Should().Be("group of 1: one");
    }

    [Fact]
    public async Task The_code_page_check_is_of_the_text_the_format_rendered()
    {
        var service = await Opened();
        var form = _loaded.Single().Forms.Single();
        form.TextEncoding = new Vb6TextEncoding(1252);
        var before = File.ReadAllText(_frm);
        _format.Stamp = "こ";

        Func<Task> save = () => service.SaveForm(form, saveAs: false, interactive: false);

        await save.Should().ThrowAsync<SourceEncodingRefusal>();
        File.ReadAllText(_frm).Should().Be(before);
    }

    [Fact]
    public async Task Crash_recovery_keeps_what_the_format_rendered()
    {
        var service = await Opened();
        _format.Stamp = "two";

        var captured = service.CaptureUnsavedWork().Single();

        captured.ProjectFile!.Text.Should().Be("project Test: two");
        captured.Documents.Should().Contain(document => document.Content!.Text == "form Form1 as Form1.frm: two");
        captured.Documents.Should().Contain(document => document.Content!.Text == "module Module1 as Module1.bas: two");
    }

    [Fact]
    public async Task A_designer_change_takes_the_header_the_format_rendered()
    {
        await Opened();
        var form = _loaded.Single().Forms.Single();
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns(new List<BaseEditorWindowViewModel>());
        using var refresher = new HeaderRefresher(new EventBus(), dock, new BreakpointService(), new BookmarkService(), _format);

        refresher.LayoutChanged(form);

        form.DesignerText.Should().Be("header as Form1.frm: one");
    }

    [Theory]
    [InlineData("one", ReloadDecision.CleanReload)]
    [InlineData("two", ReloadDecision.Conflict)]
    public void An_unopened_module_is_compared_with_the_file_as_the_format_renders_it(string stamp, ReloadDecision expected)
    {
        var module = new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "Module1", ModuleKind.StandardModule);
        var baseline = new FileBaselineStore();
        baseline.Record(_bas, "module Module1 as Module1.bas: one");
        _format.Stamp = stamp;

        new DirtyDetector(baseline, Substitute.For<IProjectService>(), _format)
            .Classify(new WatchedFileTarget(_bas, null, module, null, null))
            .Should().Be(expected);
    }

    [AvaloniaFact]
    public async Task A_language_servers_project_text_is_what_the_format_rendered()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Test") { AbsolutePath = _vbp };
        _projects.LoadedProjects.Returns([project]);
        var workspace = new ProjectLspWorkspace(() => _projects, _format);

        var text = await workspace.Projects.Single().ProjectText!(TestContext.Current.CancellationToken);

        text.Should().Be("project Test: one");
        workspace.Dispose();
    }

    [Fact]
    public void Only_the_format_names_the_writers()
    {
        var root = RepoRoot();
        var writers = new Regex(@"\b(FormSerializer|ProjectSerializer|GroupSerializer)\b|\bModuleFileFormat\s*\.\s*ToFileContent\b");
        var comments = new Regex(@"//[^\n]*|/\*.*?\*/", RegexOptions.Singleline);

        var offenders = new[] { "HexIDE", "HexIDE.Core", "HexIDE.Lsp", "HexIDE.Desktop", "HexIDE.Browser" }
            .SelectMany(project => Directory.EnumerateFiles(Path.Join(root, "IDE", project), "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => Path.GetFileName(file) != "Vb6ProjectFileFormat.cs")
            .Where(file => writers.IsMatch(comments.Replace(File.ReadAllText(file), "")))
            .Select(file => Path.GetRelativePath(root, file))
            .ToList();

        offenders.Should().BeEmpty("the IDE reaches a project's file formats through IProjectFileFormat");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "IDE"))
                                && Directory.Exists(Path.Combine(dir.FullName, "LspServer"))))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repository root from the test output.");
    }

    private sealed class StandInFormat : IProjectFileFormat
    {
        public string Stamp { get; set; } = "one";
        public byte[]? Companion { get; set; }

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
    }
}

/// <summary>That the application's own composition gives each part the one format it binds (#3564).</summary>
public class ProjectFileFormatCompositionTests : CompositionTestBase
{
    private static object? Field(object owner, string name) =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner);

    [AvaloniaFact]
    public void Everything_that_renders_gets_the_same_format()
    {
        var composition = Composition;
        _ = composition.Root;
        var format = Field(composition.ProjectService, "format");

        format.Should().BeOfType<Vb6ProjectFileFormat>();
        Field(Field(composition.ProjectService, "headerRefresher")!, "format").Should().BeSameAs(format);
        Field(composition.LspWorkspace, "_format").Should().BeSameAs(format);
        Field(Field(composition.FileWatcherService, "dirtyDetector")!, "_format").Should().BeSameAs(format);
        Field(((Func<FormEditViewModel>)Field(composition.EditorService, "formEditViewModelFactory")!)(), "format").Should().BeSameAs(format);
    }
}
