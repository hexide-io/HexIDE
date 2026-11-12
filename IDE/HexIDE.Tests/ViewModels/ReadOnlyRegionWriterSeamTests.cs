using Avalonia.Headless.XUnit;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Language;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;

namespace HexIDE.Tests.ViewModels;

/// <summary>The IDE's own writes come from the writer, even when the text contains no VB6 (#3694).</summary>
public class ReadOnlyRegionWriterSeamTests : IDisposable
{
    private const string Notes = "title Old\r\n~ stale\r\nplain\r\n";
    private const string Saved = "title Old\r\n~ current\r\nplain\r\n";
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-writer-" + Guid.NewGuid().ToString("N"));
    private readonly List<CodeEditorViewModel> editors = [];
    private readonly List<ProjectDefinition> loaded = [];
    private readonly IWindowManager windows = Substitute.For<IWindowManager>();
    private readonly IProjectManager projects = Substitute.For<IProjectManager>();
    private readonly IProjectFileFormat format = Substitute.For<IProjectFileFormat>();
    private readonly NoteWriter writer = new();
    private readonly FileBaselineStore baselines = new();

    public ReadOnlyRegionWriterSeamTests()
    {
        Directory.CreateDirectory(dir);
        projects.LoadedProjects.Returns(_ => loaded);
        projects.When(p => p.AddProject(Arg.Any<ProjectDefinition>())).Do(c => loaded.Add(c.Arg<ProjectDefinition>()));
        format.CodePrefix(Arg.Any<FormDefinition>()).Returns(c => FormCodeText.Prefix(c.Arg<FormDefinition>()));
        format.CodePrefix(Arg.Any<ModuleDefinition>()).Returns(c => FormCodeText.Prefix(c.Arg<ModuleDefinition>()));
        format.RenderForm(Arg.Any<FormDefinition>(), Arg.Any<string>())
            .Returns(c => new RenderedFile(c.Arg<FormDefinition>().Code, null));
        format.RenderProject(Arg.Any<ProjectDefinition>(), Arg.Any<string>(), Arg.Any<Func<object, string?>?>(), Arg.Any<bool>())
            .Returns("a project\r\n");
        windows.ShowDialog(Arg.Any<IDialog>()).Returns(c =>
        {
            if (c.Arg<IDialog>() is SaveChangesViewModel prompt) prompt.Yes();
            return Task.FromResult(true);
        });
    }

    public void Dispose()
    {
        foreach (var editor in editors) editor.Dispose();
        Directory.Delete(dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Only the title and the tucked line are the IDE's; every proposed change lies inside them.</summary>
    private sealed class NoteReader : IReadOnlyRegionReader
    {
        public IReadOnlyList<TextRegion> Regions(string text, int prefixLength)
        {
            var end = text.IndexOf("plain", StringComparison.Ordinal);
            return end < 0 ? [] : [new TextRegion(0, end)];
        }
    }

    private sealed class NoteWriter : IReadOnlyRegionWriter
    {
        public string? DescribeSaveRewrite(string text, TextChange change) =>
            "The save refreshed the tucked note.";
        public bool RewriteOnSave { get; set; } = true;
        public List<(string Text, int Prefix)> Saves { get; } = [];

        public IReadOnlyList<TextChange> SaveRewrites(string text, int prefixLength)
        {
            Saves.Add((text, prefixLength));
            var at = text.IndexOf("stale", StringComparison.Ordinal);
            return RewriteOnSave && at >= 0 ? [new TextChange(at, 5, "current")] : [];
        }

        public TextChange? NameRewrite(string text, int prefixLength, string name)
        {
            var at = text.IndexOf("title ", prefixLength, StringComparison.Ordinal);
            if (at < 0) return null;
            at += 6;
            var end = text.IndexOf('\r', at);
            return text[at..end] == name ? null : new TextChange(at, end - at, name);
        }
    }

    private sealed class NoWriter : IReadOnlyRegionWriter;

    private ProjectDefinition Project() => new(VBProjectType.EXE, "P") { AbsolutePath = Path.Join(dir, "P.vbp") };

    private FormDefinition Form(ProjectDefinition project, string code = Notes)
    {
        var form = new FormDefinition(project, FormComponentClass.Instance, "Old") { AbsolutePath = Path.Join(dir, "Old.frm") };
        form.RecordDesignerText("");
        form.UpdateCode(code);
        project.AddForm(form);
        return form;
    }

    private ModuleDefinition Module(string code = Notes)
    {
        var module = new ModuleDefinition(Project(), "Old", ModuleKind.UserControl);
        module.RecordOriginalHeader("");
        module.UpdateCode(code);
        return module;
    }

    private CodeEditorViewModel Editor(ModuleDefinition module, IReadOnlyRegionWriter? provider)
    {
        var vm = new CodeEditorViewModel(
            windows, Substitute.For<IEditorService>(), Substitute.For<IProjectService>(), new EventBus(),
            Substitute.For<ILspClient>(), Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(),
            new BookmarkService(), new BreakpointService(), Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            Substitute.For<IRunScope>(), new LocalizationService(), new Vb6ProjectFileFormat(), new NoteReader(), regionWriter: provider);
        editors.Add(vm);
        return vm.Initialize(module);
    }

    private ProjectService Service(IReadOnlyRegionWriter provider, IProjectFileFormat? fileFormat = null)
    {
        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        return new ProjectService(provider, () => throw new InvalidOperationException(), windows, new EventBus(), projects,
            Substitute.For<IRecentProjectsService>(), Substitute.For<IReferenceLibraryService>(), sidecar, baselines,
            new LocalizationService(), Substitute.For<IHeaderRefresher>(), fileFormat ?? format, new ComponentRegistry());
    }

    private HeaderRefresher Refresher(IReadOnlyRegionWriter provider, params CodeEditorViewModel[] open)
    {
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns(open.Cast<BaseEditorWindowViewModel>().ToList());
        return new HeaderRefresher(provider, new EventBus(), dock, new BreakpointService(), new BookmarkService(), format);
    }

    [Fact]
    public void AChangedSaveWritesTheProvidersChangesAndReportsThoseSameChanges()
    {
        var form = Form(Project());
        form.RecordDesignerText("a header\r\n");
        var report = new SaveRewriteReport();

        Service(writer).SerializeFormToFile(form, form.AbsolutePath!, rewrites: report).Should().BeTrue();

        form.Code.Should().Be(Saved);
        File.ReadAllText(form.AbsolutePath!).Should().Be(Saved);
        writer.Saves.Should().ContainSingle().Which.Should().Be(("a header\r\n" + Notes, "a header\r\n".Length));
        report.Note.Should().Be(" The save refreshed the tucked note.");
    }

    private sealed class SilentWriter : IReadOnlyRegionWriter
    {
        public IReadOnlyList<TextChange> SaveRewrites(string text, int prefixLength) =>
            [new TextChange(text.IndexOf("stale", StringComparison.Ordinal), 5, "current")];
    }

    [Fact]
    public void AWriterWithoutWordsSavesWithoutAddingANote()
    {
        var form = Form(Project());
        var report = new SaveRewriteReport();
        Service(new SilentWriter()).SerializeFormToFile(form, form.AbsolutePath!, rewrites: report).Should().BeTrue();
        form.Code.Should().Be(Saved);
        report.Note.Should().BeEmpty();
    }

    [Fact]
    public void RewriteNotesKeepTheirSpacingAndAreNotRepeated()
    {
        var report = new SaveRewriteReport();
        TextChange[] changes = [new(0, 5, "current"), new(6, 5, "current")];
        report.Record("stale stale", changes, writer);
        report.Record("stale stale", changes, writer);
        report.Note.Should().Be(" The save refreshed the tucked note.");
    }

    [Fact]
    public async Task AnUnchangedProjectSaveAtItsOwnPathLeavesTheProvidersRewritesAlone()
    {
        var project = Project();
        loaded.Add(project);
        var form = Form(project);
        writer.RewriteOnSave = false;
        var service = Service(writer);
        await service.SaveProject(project, saveAs: false);
        service.HasUnsavedChanges(form).Should().BeFalse();
        writer.RewriteOnSave = true;
        writer.Saves.Clear();

        await service.SaveProject(project, saveAs: false);

        form.Code.Should().Be(Notes);
        File.ReadAllText(form.AbsolutePath!).Should().Be(Notes);
        writer.Saves.Should().ContainSingle();
    }

    [AvaloniaFact]
    public void TheWindowAdoptsASaveAsAnIdeWriteAndUndoReassertsIt()
    {
        var module = Module();
        var editor = Editor(module, writer);
        editor.Document.Insert(editor.Document.TextLength, "typed\r\n");
        editor.AdoptRewrittenQualifiers(Saved + "typed\r\n");
        editor.Document.Text.Should().Be(Saved + "typed\r\n");

        editor.UndoRequested(() => editor.Document.UndoStack.Undo());

        editor.Document.Text.Should().Be(Saved, "Undo skips the IDE's write, removes typing, and reasserts the rewrite");
    }

    [AvaloniaFact]
    public void ARenameUsesTheWriterInTheWindowAndUndoSkipsIt()
    {
        var module = Module();
        var editor = Editor(module, writer);
        editor.Document.Insert(editor.Document.TextLength, "typed\r\n");
        module.Name = "NewName";
        using var refresher = Refresher(writer, editor);

        refresher.NameChanged(DocumentIdentity.For(module));

        editor.Document.Text.Should().Be(Notes.Replace("Old", "NewName") + "typed\r\n");
        editor.UndoRequested(() => editor.Document.UndoStack.Undo());
        editor.Document.Text.Should().Be(Notes.Replace("Old", "NewName"));
    }

    [Fact]
    public void ARenameUsesTheWriterInAClosedDocumentsCode()
    {
        var module = Module();
        module.Name = "NewName";
        using var refresher = Refresher(writer);

        refresher.NameChanged(DocumentIdentity.For(module));

        module.Code.Should().Be(Notes.Replace("Old", "NewName"));
    }

    [Fact]
    public async Task LoadingReconcilesTheProvidersNameLineAndGivesTheNotice()
    {
        var project = Project();
        var form = Form(project, Notes.Replace("Old", "Different"));
        File.WriteAllText(project.AbsolutePath!, "a project\r\n");
        format.ReadProjectAsync(Arg.Any<string>(), Arg.Any<Vb6TextEncoding>(), Arg.Any<Action<string, byte[]>?>(), Arg.Any<IDeserializeErrorSink>())
            .Returns(new ProjectFileRead(project, [], [], File.ReadAllBytes(project.AbsolutePath!)));

        await Service(writer).OpenProject(project.AbsolutePath!);

        form.Code.Should().Be(Notes);
        windows.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IWindowManager.MessageBox))
            .Select(c => (string)c.GetArguments()[0]!).Should().ContainSingle()
            .Which.Should().Contain("Conflicting names").And.Contain("Old");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoWriterAndDefaultWriterLeaveVb6TextAloneInTheWindow(bool defaults)
    {
        const string code = "Attribute VB_Name = \"Old\"\r\nPublic Sub Current()\r\nAttribute Stale.VB_Description = \"x\"\r\nEnd Sub\r\n";
        var editor = Editor(Module(code), defaults ? new NoWriter() : null);
        editor.RetargetVbName("NewName");
        editor.AdoptRewrittenQualifiers(code.Replace("Stale.", "Current."));
        editor.Document.Text.Should().Be(code);
    }

    [Fact]
    public void DefaultWriterLeavesAChangedVb6FormsStaleQualifierAndNameAlone()
    {
        const string code = "Attribute VB_Name = \"Different\"\r\nPublic Sub Current()\r\nAttribute Stale.VB_Description = \"x\"\r\nEnd Sub\r\n";
        new Vb6ReadOnlyRegionWriter().SaveRewrites(code, 0).Should().NotBeEmpty();
        var form = Form(Project(), code);
        var provider = new NoWriter();
        var report = new SaveRewriteReport();
        Service(provider, new Vb6ProjectFileFormat()).SerializeFormToFile(form, form.AbsolutePath!, rewrites: report).Should().BeTrue();
        using var refresher = Refresher(provider);
        refresher.NameChanged(DocumentIdentity.For(form));
        form.Code.Should().Be(code);
        File.ReadAllText(form.AbsolutePath!).Should().Contain("Attribute Stale.VB_Description");
        report.Note.Should().BeEmpty();
    }
}
