using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Localization;
using HexIDE.Recovery;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.Projects;

/// <summary>
/// What the IDE does with a project's files through a format that isn't VB6's (#3564). None of the files here is anything a
/// VB6 reader could take, and the stand-in's renders are nothing a VB6 writer would produce, so each assertion can only pass
/// by way of the interface.
/// </summary>
public class ProjectFileFormatSeamTests : IDisposable
{
    private readonly string _dir = Path.Join(Path.GetTempPath(), "hexide-format-" + Guid.NewGuid().ToString("N"));
    private readonly List<ProjectDefinition> _loaded = [];
    private readonly IProjectManager _projects = Substitute.For<IProjectManager>();
    private readonly IWindowManager _windows = Substitute.For<IWindowManager>();
    private readonly FileBaselineStore _baselines = new();
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
        File.WriteAllText(_vbp, "not a project file");
        File.WriteAllText(_frm, "not a form");
        File.WriteAllText(_bas, "not a module");
        _projects.LoadedProjects.Returns(_ => _loaded);
        _projects.When(m => m.AddProject(Arg.Any<ProjectDefinition>()))
            .Do(call => _loaded.Add(call.Arg<ProjectDefinition>()));
        _windows.ShowDialog(Arg.Any<IDialog>()).Returns(call =>
        {
            if (call.Arg<IDialog>() is SaveChangesViewModel prompt) prompt.Yes();
            return Task.FromResult(true);
        });
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private ProjectService Service()
    {
        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns("{0}");
        return new ProjectService(() => throw new InvalidOperationException("no new-project dialog here"), _windows,
            Substitute.For<IEventBus>(), _projects, Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), sidecar, _baselines, localization,
            Substitute.For<IHeaderRefresher>(), format: _format);
    }

    private async Task<ProjectService> Opened(string? path = null)
    {
        var service = Service();
        await service.OpenProject(path ?? _vbp);
        return service;
    }

    private List<string> Messages() => _windows.ReceivedCalls()
        .Where(call => call.GetMethodInfo().Name == nameof(IWindowManager.MessageBox))
        .Select(call => (string)call.GetArguments()[0]!)
        .ToList();

    [Fact]
    public async Task A_project_is_what_the_format_read()
    {
        _format.Skipped = ["Doc1.dob"];

        await Opened();

        var project = _loaded.Single();
        project.Forms.Single().Name.Should().Be("Form1");
        project.Modules.Single().Name.Should().Be("Module1");
        Messages().Should().Contain(message => message.Contains("Doc1.dob"));
        _baselines.TryGet(_frm).Should().NotBeNull("what the read saw is what a later change is compared with");
        _baselines.TryGet(_vbp).Should().NotBeNull();
    }

    [Fact]
    public async Task A_group_is_what_the_format_read()
    {
        var vbg = Path.Join(_dir, "Group.vbg");
        File.WriteAllText(vbg, "not a group");
        _format.Group = new("Test.vbp", ["Test.vbp"], ["a line nothing models"]);

        await Opened(vbg);

        _loaded.Should().ContainSingle();
        _projects.CurrentGroup!.UnknownLines.Should().Equal("a line nothing models");
    }

    [Fact]
    public async Task A_form_reloads_from_its_text_and_companion_through_the_format()
    {
        var service = await Opened();
        var form = _loaded.Single().Forms.Single();
        File.WriteAllText(_frm, "changed elsewhere");
        File.WriteAllBytes(Path.ChangeExtension(_frm, ".frx"), [7, 8]);

        (await service.ReloadFormFromDisk(form)).Should().BeTrue();

        form.Code.Should().Be("code of changed elsewhere");
        _format.FormsRead.Single().Companion.Should().Equal(7, 8);
    }

    [Fact]
    public async Task A_module_reloads_through_the_format()
    {
        var service = await Opened();
        var module = _loaded.Single().Modules.Single();
        File.WriteAllText(_bas, "changed elsewhere");

        (await service.ReloadModuleFromDisk(module)).Should().BeTrue();

        module.OriginalHeader.Should().Be("header of changed elsewhere");
        module.Code.Should().Be("body of changed elsewhere");
    }

    [Fact]
    public async Task A_designer_module_reloads_with_the_companion_the_format_names()
    {
        var service = await Opened();
        var project = _loaded.Single();
        var path = Path.Join(_dir, "Ctl1.ctl");
        File.WriteAllText(path, "changed elsewhere");
        File.WriteAllBytes(Path.ChangeExtension(path, ".standin"), [3]);
        var module = new ModuleDefinition(project, "Ctl1", ModuleKind.UserControl) { AbsolutePath = path };
        project.AddModule(module);

        (await service.ReloadModuleFromDisk(module)).Should().BeTrue();

        _format.DesignerPartsRead.Single().Companion.Should().Equal(3);
        module.Code.Should().Be("code of changed elsewhere");
    }

    [Fact]
    public async Task An_existing_form_is_added_through_the_format()
    {
        var service = await Opened();
        var project = _loaded.Single();
        var path = Path.Join(_dir, "Form2.frm");
        File.WriteAllText(path, "another");
        _format.FormName = "Form2";

        await service.AddExistingForm(project, path);

        project.Forms.Should().Contain(form => form.Name == "Form2" && form.Code == "code of another");
    }

    [Fact]
    public async Task An_existing_module_is_added_through_the_format()
    {
        var service = await Opened();
        var project = _loaded.Single();
        var path = Path.Join(_dir, "Module2.bas");
        File.WriteAllText(path, "another");

        await service.AddExistingModule(project, path, ModuleKind.StandardModule);

        project.Modules.Should().Contain(module => module.Name == "Module2" && module.Code == "body of another");
    }

    [Fact]
    public async Task Recovered_work_goes_back_through_the_format()
    {
        var service = await Opened();
        var project = _loaded.Single();

        service.ApplyRecovered(project.Forms.Single(), new UnsavedFile("Form1.frm", "recovered", "Form1.standin", [5])).Should().BeTrue();
        service.ApplyRecovered(project.Modules.Single(), new UnsavedFile("Module1.bas", "recovered")).Should().BeTrue();

        project.Forms.Single().Code.Should().Be("code of recovered");
        _format.FormsRead.Single().Companion.Should().Equal(5);
        project.Modules.Single().Code.Should().Be("body of recovered");
    }

    [Fact]
    public async Task A_recovered_form_with_no_place_in_the_project_is_added_through_the_format()
    {
        var service = await Opened();
        var project = _loaded.Single();
        _format.FormName = "Form2";

        var added = service.AddRecovered(project,
            new UnsavedDocument("Form2", "Form", null, Vb6TextEncoding.Ansi, new UnsavedFile("Form2.frm", "recovered")));

        added.Should().BeOfType<FormDefinition>().Which.Code.Should().Be("code of recovered");
    }

    [Fact]
    public async Task An_unsaved_project_comes_back_through_the_format()
    {
        var service = Service();
        var renderedAt = Path.Join(Path.GetTempPath(), "hexide-format-restore-" + Guid.NewGuid().ToString("N"), "Test.vbp");

        var project = await service.RestoreUnsavedProject(new UnsavedProject("Test", null, null, Vb6TextEncoding.Ansi,
            new UnsavedFile("Test.vbp", "not a project file"), [], renderedAt));

        project!.Forms.Single().Name.Should().Be("Form1");
        project.AbsolutePath.Should().BeNull();
    }

    [Fact]
    public void A_raw_property_shows_as_binary_when_the_format_says_it_cites_the_companion()
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.Tool.Properties.Binary").Returns("(Binary)");
        _format.Citation = "P = cites it";

        new RawPropertyViewModel(new("P", ["P = cites it"]), localization, _format).Value.Should().Be("(Binary)");
        new RawPropertyViewModel(new("P", ["P = plain"]), localization, _format).Value.Should().Be("plain");
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
    public async Task A_save_writes_the_companion_the_format_rendered_where_the_format_says()
    {
        var service = await Opened();
        _format.Companion = [1, 2, 3];

        await service.SaveProject(_loaded.Single(), saveAs: false);

        File.ReadAllBytes(Path.ChangeExtension(_frm, ".standin")).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task A_group_file_is_what_the_format_rendered()
    {
        var vbg = Path.Join(_dir, "Group.vbg");
        File.WriteAllText(vbg, "not a group");
        _format.Group = new("Test.vbp", ["Test.vbp"], []);
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
    public void Only_the_format_names_the_readers_and_writers()
    {
        var root = RepoRoot();
        var names = new Regex(@"\b(FormSerializer|ProjectSerializer|GroupSerializer|FormDeserializer|FrxDeserializer|ProjectDeserializer|GroupDeserializer|ProjectLoader)\b"
                              + @"|\bModuleFileFormat\s*\.\s*(ToFileContent|SplitHeader)\b");
        var comments = new Regex(@"//[^\n]*|/\*.*?\*/", RegexOptions.Singleline);
        // A contributed project's module is split by name until the add-in type reads modules itself: #3411's slice 7.
        var allowed = new[] { "Vb6ProjectFileFormat.cs", "ContributedProjects.cs" };

        var offenders = new[] { "HexIDE", "HexIDE.Core", "HexIDE.Lsp", "HexIDE.Desktop", "HexIDE.Browser" }
            .SelectMany(project => Directory.EnumerateFiles(Path.Join(root, "IDE", project), "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !allowed.Contains(Path.GetFileName(file)))
            .Where(file => names.IsMatch(comments.Replace(File.ReadAllText(file), "")))
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
        public string FormName { get; set; } = "Form1";
        public string? Citation { get; set; }
        public IReadOnlyList<string> Skipped { get; set; } = [];
        public SerializedProjectGroup Group { get; set; } = new(null, [], []);
        public List<(string Text, byte[]? Companion)> FormsRead { get; } = [];
        public List<(string Text, byte[]? Companion)> DesignerPartsRead { get; } = [];

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
