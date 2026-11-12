using System.Text.Json;
using HexIDE.Runtime.Serialization;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using HexIDE.Desktop.Server;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Tests.ViewModels;

namespace HexIDE.Tests.Infrastructure;

public sealed class SplitDocumentTabsTests
{
    private static Dock.Model.Controls.IDocumentDock Split(MainViewViewModel.DockFactory factory, IDockable tab)
    {
        var original = factory.DocumentDock!;
        var split = factory.CreateDocumentDock();
        split.VisibleDockables = factory.CreateList<IDockable>();
        factory.MoveDockable(original, split, tab, null);
        factory.SplitToDock(original, split, DockOperation.Bottom);
        split.ActiveDockable = tab;
        factory.SetFocusedDockable(split, tab);
        return split;
    }

    [AvaloniaFact]
    public async Task RepliesIncludeSplitAreaAndClosingNoteCountsAllAreas()
    {
        var (factory, _) = DockFactoryTests.CreateFactory();
        factory.InitLayout(factory.CreateLayout());
        using var service = new DocumentDockService(new DockFactoryReference { Value = factory }, new EventBus());
        service.TrackTabs();
        var first = new Document { Title = "Same" };
        var moved = new Document { Title = "Same" };
        factory.AddDockable(factory.DocumentDock!, first);
        factory.AddDockable(factory.DocumentDock!, moved);
        Split(factory, moved);
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([]);
        using var context = new IdeContext(projects, service, Substitute.For<ILspClient>(), null!,
            Substitute.For<IProjectRunnerService>(), null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, new Vb6ProjectFileFormat(), new Vb6ReadOnlyRegionReader());
        var tools = new HexIdeTools(context);

        var reply = JsonSerializer.SerializeToElement(await tools.GetDocumentTabsAsync(TestContext.Current.CancellationToken), ReplyJson.Options);
        var tabs = reply.GetProperty("tabs");
        tabs.GetArrayLength().Should().Be(2);
        tabs[0].GetProperty("area").GetInt32().Should().Be(0);
        tabs[1].GetProperty("area").GetInt32().Should().Be(1);
        tabs[1].GetProperty("isActive").GetBoolean().Should().BeTrue();
        var ambiguous = await tools.CloseDocumentTabAsync(title: "Same");
        ambiguous.Success.Should().BeFalse("duplicate titles remain ambiguous across areas");
        ambiguous.Error.Should().Contain("dump_visual_tree").And.Contain("interact");
        moved.Title = "Moved";
        var activated = await tools.ActivateDocumentTabAsync(title: "Moved");
        activated.Success.Should().BeTrue();
        activated.Note.Should().Contain("Moved").And.Contain("2 open");
        var closed = await tools.CloseDocumentTabAsync(title: "Moved");
        closed.Success.Should().BeTrue();
        closed.Note.Should().Contain("1 open");
        service.AllTabs.Should().Equal(first);
    }

    [AvaloniaFact]
    public void MovingAnEditorKeepsItOpenAndClosingInSplitCleansItUp()
    {
        var (factory, _) = DockFactoryTests.CreateFactory();
        factory.InitLayout(factory.CreateLayout());
        using var service = new DocumentDockService(new DockFactoryReference { Value = factory }, new EventBus());
        var editor = new ProjectDocumentViewModel(Substitute.For<ILocalizationService>()).Initialize(TestHelpers.CreateProject());
        service.OpenDocument(editor);
        factory.AddDockable(factory.DocumentDock!, new Document { Title = "Keep original area" });
        Split(factory, editor);

        service.OpenDocuments.Should().Contain(editor);
        service.RecentlyClosed.Entries.Should().BeEmpty("moving a tab is not closing it");
        service.TryActivate<ProjectDocumentViewModel>(t => t == editor).Should().BeTrue();
        service.ActiveDocument.Should().BeSameAs(editor);
        service.AreaOf(editor).Should().Be(1);
        service.CloseDocument(editor);
        service.OpenDocuments.Should().BeEmpty();
        service.RecentlyClosed.Entries.Should().ContainSingle();
    }

    [AvaloniaFact]
    public void FocusMovingToAToolKeepsTheLastFocusedAreasActiveTab()
    {
        var (factory, _) = DockFactoryTests.CreateFactory();
        factory.InitLayout(factory.CreateLayout());
        using var service = new DocumentDockService(new DockFactoryReference { Value = factory }, new EventBus());
        service.TrackTabs();
        var first = new Document { Title = "First" };
        var moved = new Document { Title = "Moved" };
        factory.AddDockable(factory.DocumentDock!, first);
        factory.AddDockable(factory.DocumentDock!, moved);
        factory.DocumentDock!.ActiveDockable = first;
        Split(factory, moved);
        service.ActiveTab.Should().BeSameAs(moved);

        // Focus goes to a tool pane, as a click in the Immediate window sends it: the split area was used last.
        var tool = Walk(factory.DocumentLayout!).OfType<Dock.Model.Controls.ITool>().First();
        factory.SetFocusedDockable((IDock)tool.Owner!, tool);

        service.ActiveTab.Should().BeSameAs(moved, "the split area held focus last, not the first area");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewDocumentOpensInLastFocusedArea(bool focusTool)
    {
        var (factory, _) = DockFactoryTests.CreateFactory();
        factory.InitLayout(factory.CreateLayout());
        using var service = new DocumentDockService(new DockFactoryReference { Value = factory }, new EventBus());
        service.TrackTabs();
        factory.AddDockable(factory.DocumentDock!, new Document { Title = "First" });
        var moved = new Document { Title = "Moved" };
        factory.AddDockable(factory.DocumentDock!, moved);
        Split(factory, moved);
        if (focusTool)
        {
            var tool = Walk(factory.DocumentLayout!).OfType<Dock.Model.Controls.ITool>().First();
            factory.SetFocusedDockable((IDock)tool.Owner!, tool);
        }

        var editor = new ProjectDocumentViewModel(Substitute.For<ILocalizationService>()).Initialize(TestHelpers.CreateProject());
        service.OpenDocument(editor);

        service.AreaOf(editor).Should().Be(1);
        service.ActiveTab.Should().BeSameAs(editor);
    }

    [AvaloniaFact]
    public void NewDocumentWithoutSplitOpensInFirstArea()
    {
        var (factory, _) = DockFactoryTests.CreateFactory();
        factory.InitLayout(factory.CreateLayout());
        using var service = new DocumentDockService(new DockFactoryReference { Value = factory }, new EventBus());
        var editor = new ProjectDocumentViewModel(Substitute.For<ILocalizationService>()).Initialize(TestHelpers.CreateProject());

        service.OpenDocument(editor);

        service.AreaOf(editor).Should().Be(0);
        service.ActiveTab.Should().BeSameAs(editor);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpeningRepliesReportArea(bool designer)
    {
        var project = TestHelpers.CreateProjectWithForm();
        var (factory, _) = DockFactoryTests.CreateFactory();
        factory.InitLayout(factory.CreateLayout());
        using var service = new DocumentDockService(new DockFactoryReference { Value = factory }, new EventBus());
        service.TrackTabs();
        factory.AddDockable(factory.DocumentDock!, new Document { Title = "First" });
        var moved = new Document { Title = "Moved" };
        factory.AddDockable(factory.DocumentDock!, moved);
        Split(factory, moved);
        var editor = new ProjectDocumentViewModel(Substitute.For<ILocalizationService>()).Initialize(project);
        var editors = Substitute.For<IEditorService>();
        editors.When(e => e.EditForm(project.Forms[0])).Do(_ => service.OpenDocument(editor));
        editors.When(e => e.EditCode(project.Forms[0])).Do(_ => service.OpenDocument(editor));
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([project]);
        using var context = new IdeContext(projects, service, Substitute.For<ILspClient>(), editors,
            Substitute.For<IProjectRunnerService>(), null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, new Vb6ProjectFileFormat(), new Vb6ReadOnlyRegionReader());
        var tools = new HexIdeTools(context);

        var result = designer ? await tools.ViewDesignerAsync(project.Forms[0].Name)
            : await tools.OpenFileAsync(project.Forms[0].Name);
        var reply = JsonSerializer.SerializeToElement(result, ReplyJson.Options);

        result.Success.Should().BeTrue();
        reply.GetProperty("area").GetInt32().Should().Be(1);
    }

    private static IEnumerable<IDockable> Walk(IDockable node)
    {
        yield return node;
        if (node is IDock { VisibleDockables: { } children })
            foreach (var child in children)
                foreach (var descendant in Walk(child))
                    yield return descendant;
    }

    [AvaloniaFact]
    public void SplitProjectTabIsSavedAndReopened()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-split-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var project = TestHelpers.CreateProject();
            project.AbsolutePath = Path.Combine(directory, "Project1.vbp");
            File.WriteAllText(project.AbsolutePath, "");
            var (factory, _) = DockFactoryTests.CreateFactory();
            factory.InitLayout(factory.CreateLayout());
            using var service = new DocumentDockService(new DockFactoryReference { Value = factory }, new EventBus());
            var loc = Substitute.For<ILocalizationService>();
            var editors = new EditorService(service, () => throw new InvalidOperationException(),
                () => throw new InvalidOperationException(), () => new ProjectDocumentViewModel(loc),
                () => throw new InvalidOperationException(), () => Substitute.For<IProjectManager>(), loc)
                { DocumentSessions = new DocumentSessionStore(Path.Combine(directory, "sessions")) };
            editors.EditProject(project);
            factory.AddDockable(factory.DocumentDock!, new Document { Title = "Keep original area" });
            Split(factory, service.OpenDocuments.Single());
            editors.SaveOpenDocuments(project);
            service.CloseAll();
            editors.RestoreOpenDocuments(project);
            service.OpenDocuments.Should().ContainSingle().Which.Should().BeOfType<ProjectDocumentViewModel>()
                .Which.Project.Should().BeSameAs(project);
            service.ActiveTab.Should().BeSameAs(service.OpenDocuments.Single());
            service.CloseAll();
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
