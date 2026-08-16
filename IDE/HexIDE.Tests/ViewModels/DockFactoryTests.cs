using System.Linq;
using System.Runtime.CompilerServices;
using HexIDE;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Tools;
using HexIDE.Tools.ObjectBrowser;
using HexIDE.Tools.TranslationEditor;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.ViewModels;

public class DockFactoryTests
{
    private static (MainViewViewModel.DockFactory factory, ImmediateToolViewModel immediate) CreateFactory()
    {
        var docDock = Substitute.For<IDocumentDockService>();
        var eventBus = Substitute.For<IEventBus>();
        var windowManager = Substitute.For<IWindowManager>();
        var projectManager = Substitute.For<IProjectManager>();
        var projectService = Substitute.For<IProjectService>();
        var editorService = Substitute.For<IEditorService>();
        var loc = Substitute.For<ILocalizationService>();
        loc.ActiveLanguage.Returns("en");

        // ToolBoxToolViewModel loads Avalonia bitmaps in its ctor; bypass since the tree never renders.
        var toolBox = (ToolBoxToolViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ToolBoxToolViewModel));
        var properties = new PropertiesToolViewModel(docDock, eventBus, windowManager, loc);
        var formLayout = new FormLayoutToolViewModel(docDock, eventBus, loc);
        var immediate = new ImmediateToolViewModel(loc, Substitute.For<HexIDE.Runtime.Debugging.IDebugController>());
        var locals = new LocalsToolViewModel(loc, Substitute.For<HexIDE.Runtime.Debugging.IDebugController>());
        var watches = new WatchesToolViewModel(loc, new HexIDE.Debugging.WatchService(), Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(), Substitute.For<HexIDE.IDE.IWindowManager>());
        var callStack = new CallStackToolViewModel(loc, Substitute.For<HexIDE.Runtime.Debugging.IDebugController>());
        var projectExplorer = new ProjectToolViewModel(projectManager, eventBus, projectService, editorService, loc);
        var colorPalette = new ColorPaletteToolViewModel(docDock);
        var objectBrowser = new ObjectBrowserToolViewModel(projectManager, Substitute.For<ILspClient>(),
            editorService, Substitute.For<IComponentRegistry>(), Substitute.For<ITypeLibraryService>(),
            Substitute.For<IFocusedProjectUtil>(), loc);
        var translationEditor = new TranslationEditorViewModel(loc, Substitute.For<IUserTranslationsService>(), windowManager);

        // Substitute returns null from LoadLayoutManifest → factory builds the default layout.
        var wss = Substitute.For<IWindowStateService>();

        var factory = new MainViewViewModel.DockFactory(
            toolBox, projectExplorer, properties, formLayout,
            immediate, locals, watches, callStack, colorPalette, objectBrowser, translationEditor, wss);
        return (factory, immediate);
    }

    [Fact]
    public void CaptureManifest_DefaultLayout_HasExpectedOpenSetAndHomes()
    {
        var (factory, _) = CreateFactory();
        var root = factory.CreateLayout();
        factory.InitLayout(root);

        var m = factory.CaptureManifest(root);

        m.Tools.Where(t => t.Open).Select(t => t.Key)
            .Should().BeEquivalentTo("toolbox", "project", "properties", "formLayout");
        m.Tools.Single(t => t.Key == "properties").Region.Should().Be(DockRegion.Right);
        m.Tools.Single(t => t.Key == "properties").Order.Should().Be(1);
        m.Tools.Single(t => t.Key == "immediate").Open.Should().BeFalse();
    }

    [Fact]
    public void PlaceTool_OpensToolInHomeRegion_AndIsCaptured()
    {
        var (factory, immediate) = CreateFactory();
        var root = factory.CreateLayout();
        factory.InitLayout(root);

        factory.PlaceTool(root, immediate, rightFallback: false);

        var imm = factory.CaptureManifest(root).Tools.Single(t => t.Key == "immediate");
        imm.Open.Should().BeTrue();
        imm.Region.Should().Be(DockRegion.Bottom);
    }

    [Fact]
    public void CaptureManifest_AfterClosingAnOpenedTool_RecordsItClosed()
    {
        // Regression guard: a closed dockable keeps a stale Owner pointing at its old parent.
        // Capture must determine open/closed by searching DOWN from the live root, not via Owner.
        var (factory, immediate) = CreateFactory();
        var root = factory.CreateLayout();
        factory.InitLayout(root);
        factory.PlaceTool(root, immediate, rightFallback: false);
        factory.CaptureManifest(root).Tools.Single(t => t.Key == "immediate").Open.Should().BeTrue();

        factory.CloseDockable(immediate);

        factory.CaptureManifest(root).Tools.Single(t => t.Key == "immediate").Open.Should().BeFalse();
    }
}
