using System.IO;
using System.Reflection;
using System.Text;
using Avalonia.Headless.XUnit;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Language;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;
using StandInFormat = HexIDE.Tests.Projects.ProjectFileFormatSeamTests.StandInFormat;

namespace HexIDE.Tests.Projects;

/// <summary>
/// That a project's documents go through that project's own format and read-only regions, not the IDE's (#4053, #3411
/// slice 10a). Every service here is given a format and region services that throw when asked anything, so a site that
/// still reaches the injected ones for a project's document fails, rather than rendering through VB6's by accident.
/// </summary>
public sealed class ProjectFormatPerProjectTests : IDisposable
{
    private readonly string _dir = Path.Join(Path.GetTempPath(), "hexide-format-per-project-" + Guid.NewGuid().ToString("N"));
    private readonly List<ProjectDefinition> _loaded = [];
    private readonly IWindowManager _windows = Substitute.For<IWindowManager>();
    private readonly Vb6ProjectFileFormat _vb6 = new();
    private readonly RecordingReader _standInReader = new();
    private readonly RecordingWriter _standInWriter = new();
    private readonly StandInFormat _standIn = new() { CarriesName = false };
    private readonly ProjectDefinition _vbProject;
    private readonly ProjectDefinition _standInProject;

    public ProjectFormatPerProjectTests()
    {
        // Nothing is injected any more (#3411 slice 10b-3). What a project with no format of its own would get is the test
        // fixtures' fallback, so it throws here: anything that reached past a project's own format fails.
        ProjectFormats.TestFixturesOnlyFallback = new ThrowingFormat();
        Directory.CreateDirectory(_dir);
        _standIn.Regions = new FormatRegions(_standInReader, _standInWriter, new NoRules());
        _vbProject = Project("Vb", _vb6, "Public Sub Shown()\r\nAttribute Shown.VB_Description = \"kept\"\r\nEnd Sub\r\n");
        _standInProject = Project("Ledger", _standIn, "plain\r\n");
        _loaded.AddRange([_vbProject, _standInProject]);
    }

    public void Dispose()
    {
        HexIDE.Tests.Infrastructure.TestFixturesOnlyFormat.Set();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best effort */ }
    }

    // A project as an open leaves it: its format set, its members where its file lists them.
    private ProjectDefinition Project(string name, IProjectFileFormat format, string code)
    {
        var folder = Path.Join(_dir, name);
        Directory.CreateDirectory(folder);
        var project = new ProjectDefinition(VBProjectType.EXE, name)
        {
            AbsolutePath = Path.Join(folder, name + ".vbp"),
            ItemLinesDirectory = folder,
            Format = format,
        };
        var form = new FormDefinition(project, FormComponentClass.Instance, "Form1") { AbsolutePath = Path.Join(folder, "Form1.frm") };
        project.AddForm(form);
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule) { AbsolutePath = Path.Join(folder, "Module1.bas") };
        module.UpdateCode(code);
        project.AddModule(module);
        return project;
    }

    private ProjectService Service()
    {
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns(_ => _loaded);
        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns("{0}");
        return new ProjectService(() => throw new InvalidOperationException("no new-project dialog here"),
            _windows, Substitute.For<IEventBus>(), projects, Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), sidecar, new FileBaselineStore(), localization,
            Substitute.For<IHeaderRefresher>(), new ComponentRegistry());
    }

    private HeaderRefresher Refresher(params BaseEditorWindowViewModel[] open)
    {
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns(open.ToList());
        return new HeaderRefresher(new EventBus(), dock, new BreakpointService(), new BookmarkService());
    }

    private CodeEditorViewModel Window() => new(_windows, Substitute.For<IEditorService>(), Substitute.For<IProjectService>(),
        new EventBus(), Substitute.For<ILspClient>(), Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(),
        new BookmarkService(), new BreakpointService(), Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
        Substitute.For<IRunScope>(), new LocalizationService());

    private static ModuleDefinition ModuleOf(ProjectDefinition project) => project.Modules.Single();

    [Fact]
    public async Task Each_project_is_saved_through_its_own_format()
    {
        var service = Service();

        await service.SaveProject(_vbProject, saveAs: false);
        await service.SaveProject(_standInProject, saveAs: false);

        File.ReadAllText(ModuleOf(_vbProject).AbsolutePath!).Should().StartWith("Attribute VB_Name = \"Module1\"")
            .And.Contain("Attribute Shown.VB_Description = \"kept\"");
        File.ReadAllText(_vbProject.AbsolutePath!).Should().Contain("Module=Module1; Module1.bas");
        File.ReadAllText(ModuleOf(_standInProject).AbsolutePath!).Should().Be("module Module1 as Module1.bas: one");
        File.ReadAllText(_standInProject.AbsolutePath!).Should().Be("project Ledger: one");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Each_project_s_module_is_compared_with_its_file_as_its_own_format_renders_it(bool standIn)
    {
        var project = standIn ? _standInProject : _vbProject;
        var module = ModuleOf(project);
        var baselines = new FileBaselineStore();
        baselines.Record(module.AbsolutePath!, project.Format!.RenderModule(module, "Module1.bas").Text);

        new DirtyDetector(baselines, Substitute.For<IProjectService>())
            .Classify(new WatchedFileTarget(module.AbsolutePath!, null, module, null, null))
            .Should().Be(ReloadDecision.CleanReload);
    }

    [Fact]
    public void Each_project_s_form_takes_the_header_its_own_format_renders()
    {
        using var refresher = Refresher();

        refresher.LayoutChanged(_vbProject.Forms.Single());
        refresher.LayoutChanged(_standInProject.Forms.Single());

        _vbProject.Forms.Single().DesignerText.Should().StartWith("VERSION 5.00");
        _standInProject.Forms.Single().DesignerText.Should().Be("header as Form1.frm: one");
    }

    [Fact]
    public void A_rename_rewrites_a_closed_module_through_its_own_project_s_writer()
    {
        using var refresher = Refresher();
        ModuleOf(_standInProject).Name = "Renamed";

        refresher.NameChanged(DocumentIdentity.For(ModuleOf(_standInProject)));

        _standInWriter.NameRewrites.Should().Equal("Renamed");
    }

    [AvaloniaFact]
    public void Each_code_window_shows_its_own_format_s_prefix_and_guards_its_own_regions()
    {
        using var vb = Window();
        using var ledger = Window();

        vb.Initialize(ModuleOf(_vbProject));
        ledger.Initialize(ModuleOf(_standInProject));

        vb.Document.Text.Should().StartWith(_vb6.CodePrefix(ModuleOf(_vbProject)));
        var attribute = vb.Document.Text.IndexOf("Attribute Shown.", StringComparison.Ordinal);
        vb.SnapshotReadOnlyRegions()(attribute, 1).Should().BeTrue("VB6's reader keeps a member's Attribute line");

        var prefix = _standIn.CodePrefix(ModuleOf(_standInProject));
        ledger.Document.Text.Should().Be(prefix + "plain\r\n");
        ledger.SnapshotReadOnlyRegions()(0, 1).Should().BeTrue("the stand-in's reader guards its own prefix");
        ledger.SnapshotReadOnlyRegions()(prefix.Length, 1).Should().BeFalse();
        _standInReader.Calls.Should().BePositive();
    }

    // ── A .vbp project given a writer, as 10b's VB6 registration will give it one ────────────────────────────────

    [AvaloniaFact]
    public async Task A_vbp_project_given_a_writer_keeps_its_type_and_startup_in_project_properties()
    {
        var service = Service();
        GiveWriter(_vbProject);
        bool? vb6Settings = null;
        _windows.ShowDialog(Arg.Any<ProjectPropertiesViewModel>()).Returns(call =>
        {
            vb6Settings = call.Arg<ProjectPropertiesViewModel>().CanSetVb6Properties;
            return Task.FromResult(false);
        });

        await service.EditProjectProperties(_vbProject);

        vb6Settings.Should().BeTrue("Project Type and Startup Object are the type's offer, not a question of the writer");
    }

    [AvaloniaFact]
    public async Task A_vbp_project_given_a_writer_is_still_checked_for_a_change_made_outside()
    {
        var service = Service();
        GiveWriter(_vbProject);
        await service.SaveProject(_vbProject, saveAs: false);
        _vbProject.Name = "Ours";
        File.AppendAllText(_vbProject.AbsolutePath!, "Title=\"Theirs\"\r\n");
        _windows.ShowDialog(Arg.Any<FileConflictViewModel>()).Returns(call =>
        {
            var conflict = call.Arg<FileConflictViewModel>();
            var result = true;
            conflict.CloseRequested += answer => result = answer;
            conflict.KeepAll();
            return Task.FromResult(result);
        });

        await service.SaveProject(_vbProject, saveAs: false);

        await _windows.Received(1).ShowDialog(Arg.Is<FileConflictViewModel>(c => c.Files.Single().Name == "Vb.vbp"));
        File.ReadAllText(_vbProject.AbsolutePath!).Should().Contain("Name=\"Ours\"").And.NotContain("Title=\"Theirs\"");
    }

    // ProjectFileWriter is init-only: a .vbp project has none yet, and 10b's registration is what will give it one.
    private void GiveWriter(ProjectDefinition project) =>
        typeof(ProjectDefinition).GetProperty(nameof(ProjectDefinition.ProjectFileWriter))!
            .SetValue(project, new Vb6Writer(_vb6));

    private sealed class Vb6Writer(Vb6ProjectFileFormat format) : IProjectFileWriter
    {
        public ProjectFileParts Carries => ProjectFileParts.Members | ProjectFileParts.References | ProjectFileParts.Properties;

        public Encoding Encoding => Encoding.Latin1;

        public string RenderProject(ProjectDefinition project, string path, Func<object, string?>? pathOf) =>
            format.RenderProject(project, path, pathOf);
    }

    // ── What every service is given, and must not ask about a project's document ─────────────────────────────────

    private static InvalidOperationException Asked(string member) =>
        new($"The IDE's injected {member} was asked about a document whose project has a format of its own (#4053).");

    private sealed class ThrowingFormat : IProjectFileFormat
    {
        public string FormExtension => throw Asked("form extension");
        public string MemberExtension(ModuleKind kind) => throw Asked("member extension");
        public FormatRegions? Regions => throw Asked("format's regions");
        public RenderedFile RenderForm(FormDefinition form, string fileName) => throw Asked("format");
        public RenderedFile RenderModule(ModuleDefinition module, string fileName) => throw Asked("format");
        public string RenderDesignerHeader(FormDefinition form, string fileName) => throw Asked("format");
        public string RenderProject(ProjectDefinition project, string projectPath, Func<object, string?>? pathOf = null,
            bool includeRelatedDocuments = true) => throw Asked("format");
        public string RenderGroup(string groupPath, IReadOnlyList<ProjectDefinition> projects, ProjectDefinition? startupProject,
            IReadOnlyList<string>? unknownLines, Func<object, string?>? pathOf) => throw Asked("format");
        public IReadOnlyList<ComponentInstance> RootChildren(IReadOnlyList<ComponentInstance> components, ComponentInstance root) =>
            throw Asked("format");
        public Task<ProjectFileRead> ReadProjectAsync(string path, Vb6TextEncoding hostAnsi, Action<string, byte[]>? fileRead,
            IDeserializeErrorSink problems) => throw Asked("format");
        public Task<ProjectFileRead> ReadProjectTextAsync(string path, string text, Vb6TextEncoding encoding,
            Vb6TextEncoding hostAnsi, IDeserializeErrorSink problems) => throw Asked("format");
        public SerializedProjectGroup ReadGroup(string text) => throw Asked("format");
        public FormDefinition? ReadForm(ProjectDefinition project, string text, byte[]? companion, IDeserializeErrorSink problems) =>
            throw Asked("format");
        public FormDefinition? ReadDesignerPart(ProjectDefinition project, string text, byte[]? companion,
            IDeserializeErrorSink problems) => throw Asked("format");
        public (string Header, string Body) SplitModule(string text, ModuleKind kind) => throw Asked("format");
        public string CodePrefix(FormDefinition form) => throw Asked("format");
        public string CodePrefix(ModuleDefinition module) => throw Asked("format");
        public bool PrefixCarriesName(ModuleDefinition module) => throw Asked("format");
        public string CompanionExtension(string path) => throw Asked("format");
        public string MemberLine(ModuleKind kind, string name, string relativePath) => throw Asked("format");
        public bool IsCompanionCitation(string value) => throw Asked("format");
    }

    private sealed class ThrowingReader : IReadOnlyRegionReader
    {
        public IReadOnlyList<TextRegion> Regions(string text, int prefixLength) => throw Asked("region reader");
        public IReadOnlyList<TextRegion> Regions(string text, int prefixLength, bool prefixIsEntireHeader) =>
            throw Asked("region reader");
        public IReadOnlyList<HangingRegion> HangingRegions(string text, int prefixLength) => throw Asked("region reader");
        public int CommentStart(string text, HangingRegion region) => throw Asked("region reader");
        public bool StartsHangingRegion(ReadOnlySpan<char> line) => throw Asked("region reader");
        public int? LineToHangFrom(string text, HangingRegion region, string inserted) => throw Asked("region reader");
    }

    private sealed class ThrowingWriter : IReadOnlyRegionWriter
    {
        public IReadOnlyList<TextChange> SaveRewrites(string text, int prefixLength) => throw Asked("region writer");
        public string? DescribeSaveRewrite(string text, TextChange change) => throw Asked("region writer");
        public TextChange? NameRewrite(string text, int prefixLength, string name, bool moduleLevel = false) =>
            throw Asked("region writer");
    }

    private sealed class ThrowingRules : IReadOnlyRegionRules
    {
        public ContentReplacement? Replace(string current, int prefixLength, string incoming) => throw Asked("region rules");
        public string? RangeEditRefusal(string before, string after, int prefixLength) => throw Asked("region rules");
        public string? DescribeRegions(string text, int prefixLength, IReadOnlyList<TextRegion> regions) =>
            throw Asked("region rules");
        public bool RenameMayWrite(string text, int prefixLength, IReadOnlyList<TextChange> changes, TextChange edit) =>
            throw Asked("region rules");
        public bool HeaderDeclares(string text, int prefixLength, string name) => throw Asked("region rules");
    }

    // ── The stand-in format's own regions ───────────────────────────────────────────────────────────────────────

    private sealed class RecordingReader : IReadOnlyRegionReader
    {
        public int Calls { get; private set; }

        // The prefix, which the stand-in ends at its first line break, and nothing else.
        public IReadOnlyList<TextRegion> Regions(string text, int prefixLength)
        {
            Calls++;
            return prefixLength > 0 ? [new TextRegion(0, prefixLength)] : [];
        }
    }

    private sealed class RecordingWriter : IReadOnlyRegionWriter
    {
        public List<string> NameRewrites { get; } = [];

        public TextChange? NameRewrite(string text, int prefixLength, string name, bool moduleLevel = false)
        {
            NameRewrites.Add(name);
            return null;
        }
    }

    private sealed class NoRules : IReadOnlyRegionRules;
}
