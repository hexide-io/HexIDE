using Dock.Model.Core;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.ViewModels;

public sealed class DocumentSessionTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "hexide-document-session-" + Guid.NewGuid().ToString("N"));
    private readonly IDocumentDockService dock = Substitute.For<IDocumentDockService>();
    private readonly List<BaseEditorWindowViewModel> tabs = [];
    private readonly List<BaseEditorWindowViewModel> made = [];
    private readonly EventBus bus = new();
    private readonly IProjectService projects = Substitute.For<IProjectService>();
    private readonly ILocalizationService localization = Substitute.For<ILocalizationService>();
    private readonly EditorService editors;
    private readonly ProjectManager manager;
    private readonly DocumentSessionStore store;
    private IDockable? active;

    public DocumentSessionTests()
    {
        Directory.CreateDirectory(directory);
        store = new DocumentSessionStore(Path.Combine(directory, "sessions"));
        dock.AllTabs.Returns(_ => tabs.Cast<IDockable>().ToArray());
        dock.OpenDocuments.Returns(tabs);
        dock.ActiveTab.Returns(_ => active);
        dock.When(d => d.OpenDocument(Arg.Any<BaseEditorWindowViewModel>(), Arg.Any<bool>())).Do(c =>
        {
            var tab = c.Arg<BaseEditorWindowViewModel>();
            tabs.Add(tab);
            made.Add(tab);
            if (c.Arg<bool>()) active = tab;
        });
        dock.TryActivateAny(Arg.Any<Func<IDockable, bool>>()).Returns(c =>
        {
            var found = tabs.FirstOrDefault(t => c.Arg<Func<IDockable, bool>>()(t));
            if (found is null) return false;
            active = found;
            return true;
        });
        editors = new EditorService(dock, NewCode, () => new RelatedDocumentEditorViewModel(Substitute.For<ILspClient>(), Substitute.For<IWindowManager>(), localization),
            () => new ProjectDocumentViewModel(localization), NewDesigner, () => manager!, localization) { DocumentSessions = store };
        manager = new ProjectManager(editors, bus);
        bus.Subscribe<ProjectUnloadedEvent>(_ => { tabs.Clear(); active = null; });
    }

    private CodeEditorViewModel NewCode() => new(Substitute.For<IWindowManager>(), editors, projects, bus,
        Substitute.For<ILspClient>(), Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(),
        Substitute.For<IBookmarkService>(), Substitute.For<IBreakpointService>(),
        Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(), Substitute.For<IRunScope>(), localization);

    private FormEditViewModel NewDesigner() => new(null!, bus, projects, null!, null!,
        Substitute.For<ISettingsService>(), localization, new Vb6ProjectFileFormat());

    private ProjectDefinition Project(string name = "Test")
    {
        var project = TestHelpers.CreateProject(name);
        project.AbsolutePath = Path.Combine(directory, name + ".vbp");
        File.WriteAllText(project.AbsolutePath, "");
        var form = TestHelpers.CreateForm(project);
        form.AbsolutePath = Path.Combine(directory, "Form1.frm");
        File.WriteAllText(form.AbsolutePath, "");
        project.AddForm(form);
        var module = TestHelpers.CreateModule(project);
        module.AbsolutePath = Path.Combine(directory, "Module1.bas");
        File.WriteAllText(module.AbsolutePath, "");
        project.AddModule(module);
        return project;
    }

    [AvaloniaFact]
    public void ProjectCloseAndFreshOpenRestoreCodeAndDesignerInOrderAndActiveTab()
    {
        var project = Project();
        manager.AddProject(project);
        editors.EditCode(project.Modules[0]);
        editors.EditForm(project.Forms[0]);
        editors.EditCode(project.Forms[0]);
        active = tabs[0];
        manager.UnloadProject(project);
        // New model instances, as on a fresh process opening the same project.
        var reopened = Project();
        manager.AddProject(reopened);
        tabs.Should().BeEmpty("project loading has not established its baselines yet");
        bus.Publish(new ProjectOpenedEvent(reopened));
        tabs.Should().HaveCount(3);
        tabs[0].Should().BeOfType<CodeEditorViewModel>().Which.ModuleDefinition.Should().BeSameAs(reopened.Modules[0]);
        tabs[1].Should().BeOfType<FormEditViewModel>().Which.FormDefinition.Should().BeSameAs(reopened.Forms[0]);
        tabs[2].Should().BeOfType<CodeEditorViewModel>().Which.FormDefinition.Should().BeSameAs(reopened.Forms[0]);
        active.Should().BeSameAs(tabs[0]);
        new DocumentSessionStore(Path.Combine(directory, "sessions")).Load(project.AbsolutePath).Should().HaveCount(3);
    }

    [AvaloniaFact]
    public void CarriedFilesProjectViewAndUserControlDesignerAreRestored()
    {
        var project = Project();
        var carried = new RelatedDocumentDefinition(project, "notes.txt", Path.Combine(directory, "notes.txt"));
        File.WriteAllText(carried.AbsolutePath!, "notes");
        project.AddRelatedDocument(carried);
        var control = TestHelpers.CreateModule(project, "Control1", ModuleKind.UserControl);
        control.AbsolutePath = Path.Combine(directory, "Control1.ctl");
        File.WriteAllText(control.AbsolutePath, "");
        var form = TestHelpers.CreateForm(project, "Control1");
        form.AbsolutePath = control.AbsolutePath;
        control.UpdateFormPart(form);
        project.AddModule(control);
        editors.EditRelatedDocument(carried);
        editors.EditProject(project);
        editors.EditForm(form);
        editors.SaveOpenDocuments(project);
        tabs.Clear();
        editors.RestoreOpenDocuments(project);
        tabs.Should().HaveCount(3);
        tabs[0].Should().BeOfType<RelatedDocumentEditorViewModel>().Which.RelatedDocument.Should().BeSameAs(carried);
        tabs[1].Should().BeOfType<ProjectDocumentViewModel>().Which.Project.Should().BeSameAs(project);
        tabs[2].Should().BeOfType<FormEditViewModel>().Which.FormDefinition.Should().BeSameAs(form);
    }

    [AvaloniaFact]
    public void ExplicitlyClosedTabsAndOtherProjectsAreNotRecorded()
    {
        var project = Project();
        var other = Project("Other");
        manager.AddProject(project);
        editors.EditCode(project.Modules[0]);
        editors.EditForm(project.Forms[0]);
        editors.EditCode(other.Modules[0]);
        tabs.RemoveAt(0);
        editors.SaveOpenDocuments(project);
        var saved = store.Load(project.AbsolutePath);
        saved.Should().ContainSingle().Which.Kind.Should().Be("designer");
        tabs.Clear();
        editors.SaveOpenDocuments(project);
        store.Load(project.AbsolutePath).Should().BeEmpty();
    }

    [AvaloniaFact]
    public void MissingRemovedUnknownAndMalformedEntriesDoNotPreventRemainingTabs()
    {
        var project = Project();
        store.Save(project.AbsolutePath, [new("code", Path.Combine(directory, "gone.bas"), false),
            new("code", project.AbsolutePath!, false), new("future-kind", project.Forms[0].AbsolutePath!, false),
            null!, new("designer", project.Forms[0].AbsolutePath!, true)]);
        manager.AddProject(project);
        bus.Publish(new ProjectOpenedEvent(project));
        tabs.Should().ContainSingle().Which.Should().BeOfType<FormEditViewModel>();
        active.Should().BeSameAs(tabs[0]);
    }

    [Fact]
    public void RecoveryAndPathlessProjectsNeitherReadNorOverwriteSavedSessions()
    {
        var project = Project();
        store.Save(project.AbsolutePath, [new("code", project.Modules[0].AbsolutePath!, true)]);
        var recovery = new DocumentSessionStore(Path.Combine(directory, "sessions"), recoveryMode: true);
        recovery.Load(project.AbsolutePath).Should().BeEmpty();
        recovery.Save(project.AbsolutePath, []);
        store.Save(null, [new("code", "unsaved", true)]);
        store.Load(null).Should().BeEmpty();
        store.Load(project.AbsolutePath).Should().ContainSingle();
    }

    [Fact]
    public void UnreadableStateIsIgnoredAndProjectsHaveSeparateRecords()
    {
        var project = Project();
        var other = Project("Other");
        store.Save(project.AbsolutePath, [new("code", project.Modules[0].AbsolutePath!, false)]);
        store.Load(other.AbsolutePath).Should().BeEmpty();
        File.WriteAllText(Directory.GetFiles(Path.Combine(directory, "sessions")).Single(), "{broken");
        store.Load(project.AbsolutePath).Should().BeEmpty();
    }

    public void Dispose()
    {
        foreach (var tab in made) tab.Dispose();
        Directory.Delete(directory, recursive: true);
    }
}
