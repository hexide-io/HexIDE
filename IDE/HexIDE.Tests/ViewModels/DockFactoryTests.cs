using System.Linq;
using System.Runtime.CompilerServices;
using HexIDE;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Tools.LanguageServers;
using HexIDE.Projects;
using HexIDE.Tools;
using HexIDE.Tools.ObjectBrowser;
using HexIDE.Tools.TranslationEditor;
using HexIDE.VisualDesigner;
using HexIDE.Conversations;
using HexIDE.Tools.ProtocolInspector;

namespace HexIDE.Tests.ViewModels;

public class DockFactoryTests
{
    private static (MainViewViewModel.DockFactory factory, ImmediateToolViewModel immediate) CreateFactory(LayoutManifest? manifest = null)
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
            Substitute.For<IFocusedProjectUtil>(), loc,
            Substitute.For<ILanguageConnectionRegistry>());
        var translationEditor = new TranslationEditorViewModel(loc, Substitute.For<IUserTranslationsService>(), windowManager);

        // No saved manifest means the factory builds the default layout.
        var wss = Substitute.For<IWindowStateService>();
        wss.LoadLayoutManifest().Returns(manifest);

        // A registry with nothing attached: the view model reads Connections and
        // ConfigurationProblems in its constructor.
        var lsRegistry = Substitute.For<ILanguageConnectionRegistry>();
        lsRegistry.Connections.Returns([]);
        lsRegistry.ConfigurationProblems.Returns([]);
        var languageServers = new LanguageServersToolViewModel(
            lsRegistry, loc, new ConversationLog(), Substitute.For<IEventBus>(),
            new HexIDE.Redaction.Pseudonymiser());
        var protocolInspector = new ProtocolInspectorToolViewModel(
            new ConversationLog(), loc, new HexIDE.Redaction.Pseudonymiser(), Substitute.For<IWindowManager>());

        var factory = new MainViewViewModel.DockFactory(
            toolBox, projectExplorer, properties, formLayout,
            immediate, locals, watches, callStack, colorPalette, objectBrowser, translationEditor,
            languageServers, protocolInspector, wss, new DockFactoryReference());
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

    [Theory]
    [InlineData(DockRegion.Left)]
    [InlineData(DockRegion.Right)]
    [InlineData(DockRegion.Bottom)]
    public void AddinTool_LateRegistration_RestoresPlacement(DockRegion region)
    {
        var saved = new ToolLayoutState("addin:sample", true, region, region == DockRegion.Bottom ? 0 : 1, 0.42);
        var (factory, _) = CreateFactory(LayoutManifest.Default with
        {
            LeftProportion = 0.17,
            RightProportion = 0.27,
            Tools = LayoutManifest.Default.Tools.Select(t => region == DockRegion.Right
                && t.Region == region && t.Order >= saved.Order ? t with { Order = t.Order + 1 } : t)
                .Append(saved).ToArray()
        });
        var root = factory.CreateLayout();
        factory.InitLayout(root);
        var pending = factory.CaptureManifest(root);
        pending.Tools.Should().Contain(saved);
        pending.LeftProportion.Should().Be(0.17);
        pending.RightProportion.Should().Be(0.27);
        var tool = new HexIDE.Addins.AddinToolWindowViewModel("sample", "Sample", () => new object());

        factory.RegisterAddinTool(root, tool);

        var captured = factory.CaptureManifest(root).Tools.Single(t => t.Key == saved.Key);
        captured.Open.Should().BeTrue();
        captured.Region.Should().Be(region);
        captured.Order.Should().Be(saved.Order);
        if (region != DockRegion.Left)
            captured.Proportion.Should().Be(0.42);
    }

    [Fact]
    public void AddinTool_ClosedAtSave_StaysClosedUntilShown()
    {
        var saved = new ToolLayoutState("addin:sample", false, DockRegion.Bottom, 0, 0.42);
        var (factory, _) = CreateFactory(LayoutManifest.Default with
        {
            Tools = LayoutManifest.Default.Tools.Append(saved).ToArray()
        });
        var root = factory.CreateLayout();
        factory.InitLayout(root);
        var tool = new HexIDE.Addins.AddinToolWindowViewModel("sample", "Sample", () => new object());
        factory.RegisterAddinTool(root, tool);
        factory.CaptureManifest(root).Tools.Should().Contain(saved);

        factory.PlaceTool(root, tool, rightFallback: true);

        factory.CaptureManifest(root).Tools.Should().Contain(saved with { Open = true });
    }

    [Fact]
    public void AddinTool_NewRegistration_IsCapturedAndResetDiscardsSavedPlacement()
    {
        var (factory, _) = CreateFactory();
        var root = factory.CreateLayout();
        factory.InitLayout(root);
        var tool = new HexIDE.Addins.AddinToolWindowViewModel("sample", "Sample", () => new object());
        factory.PlaceTool(root, tool, rightFallback: false);
        factory.CaptureManifest(root).Tools.Single(t => t.Key == "addin:sample")
            .Region.Should().Be(DockRegion.Bottom);

        root = factory.ResetLayout();
        factory.InitLayout(root);
        factory.PlaceTool(root, tool, rightFallback: true);
        factory.CaptureManifest(root).Tools.Single(t => t.Key == "addin:sample")
            .Region.Should().Be(DockRegion.Right);
    }
}
