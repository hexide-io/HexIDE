using System.Linq;
using Dock.Model.Core;
using Dock.Model.Controls;
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

        // A missing manifest makes the factory build the default layout.
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

    [Theory]
    [InlineData(DockRegion.Left)]
    [InlineData(DockRegion.Right)]
    [InlineData(DockRegion.Bottom)]
    [InlineData(DockRegion.Document)]
    public void CreateLayout_UnknownKey_PreservesKnownWindows(DockRegion region)
    {
        var json = LayoutManifestJson.Serialize(new LayoutManifest(LayoutManifest.CurrentVersion, 0.17, 0.29,
        [
            new("futureTool", true, region, 0, 0.2),
            new("properties", true, DockRegion.Left, 1, null),
            new("toolbox", true, DockRegion.Right, 2, 0.4),
            new("immediate", true, DockRegion.Bottom, 3, 0.6),
            new("objectBrowser", true, DockRegion.Document, 4, null),
        ]));
        var (factory, _) = CreateFactory(LayoutManifestJson.Deserialize(json));

        var root = factory.CreateLayout();
        factory.InitLayout(root);

        var restored = factory.CaptureManifest(root);
        restored.LeftProportion.Should().Be(0.17);
        restored.RightProportion.Should().Be(0.29);
        foreach (var expected in LayoutManifestJson.Deserialize(json)!.Tools.Where(t => t.Key != "futureTool"))
        {
            var actual = restored.Tools.Single(t => t.Key == expected.Key);
            actual.Open.Should().BeTrue();
            actual.Region.Should().Be(expected.Region);
            if (expected.Proportion is { } proportion)
                actual.Proportion.Should().Be(proportion);
        }
        restored.Tools.Should().NotContain(t => t.Key == "futureTool");
    }

    [Fact]
    public void CreateLayout_RepeatedUnknownKey_LogsOnce()
    {
        using var log = new HexIDE.Tests.Sidecar.CapturedLog();
        var (factory, _) = CreateFactory(new LayoutManifest(LayoutManifest.CurrentVersion, 0.1, 0.3,
        [
            new("futureTool", true, DockRegion.Left, 0, null),
            new("futureTool", true, DockRegion.Right, 0, null),
            new("futureClosedTool", false, DockRegion.Document, 0, null),
        ]));

        factory.CreateLayout();

        log.Messages.Where(m => m.Contains("futureTool")).Should().ContainSingle();
        log.Messages.Where(m => m.Contains("futureClosedTool")).Should().ContainSingle();
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
    public void PlaceTool_ReopensToolbox_InNonemptyLeftDock()
    {
        var (factory, _) = CreateFactory();
        var root = factory.CreateLayout();
        factory.InitLayout(root);
        var main = (IDock)root.DefaultDockable!;
        var original = main.VisibleDockables!.OfType<IToolDock>().Single(d => d.Alignment == Alignment.Left);
        var toolbox = (Dock.Model.Mvvm.Controls.Tool)original.ActiveDockable!;
        toolbox.CanClose = true;
        factory.CloseDockable(toolbox);
        main.VisibleDockables!.OfType<IToolDock>().Should().NotContain(d => d.Alignment == Alignment.Left);

        factory.PlaceTool(root, toolbox, rightFallback: false);

        var left = main.VisibleDockables!.OfType<IToolDock>().Single(d => d.Alignment == Alignment.Left);
        left.VisibleDockables.Should().ContainSingle().Which.Should().BeSameAs(toolbox);
        left.ActiveDockable.Should().BeSameAs(toolbox);
        left.Proportion.Should().BeGreaterThan(0);
        left.IsEmpty.Should().BeFalse();
        toolbox.Owner.Should().BeSameAs(left);
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

    [Theory]
    [InlineData(DockRegion.Left)]
    [InlineData(DockRegion.Right)]
    [InlineData(DockRegion.Bottom)]
    public void AddinTool_Reset_ReturnsOpenToolsToDefaultRegion_AndKeepsClosedToolsClosed(DockRegion region)
    {
        var saved = new ToolLayoutState("addin:sample", true, region, 0, 0.42);
        var (factory, _) = CreateFactory(LayoutManifest.Default with
        {
            LeftProportion = 0.17,
            RightProportion = 0.27,
            Tools = LayoutManifest.Default.Tools.Append(saved).ToArray()
        });
        var root = factory.CreateLayout();
        factory.InitLayout(root);
        var tool = new HexIDE.Addins.AddinToolWindowViewModel("sample", "Sample", () => new object());
        var closed = new HexIDE.Addins.AddinToolWindowViewModel("closed", "Closed", () => new object());
        factory.RegisterAddinTool(root, tool);
        factory.RegisterAddinTool(root, closed);
        factory.CloseDockable(closed);

        root = factory.ResetLayout(root);
        factory.InitLayout(root);

        var manifest = factory.CaptureManifest(root);
        var reset = manifest.Tools.Single(t => t.Key == saved.Key);
        reset.Open.Should().BeTrue();
        reset.Region.Should().Be(DockRegion.Right);
        reset.Proportion.Should().NotBe(0.42);
        manifest.Tools.Single(t => t.Key == "addin:closed").Open.Should().BeFalse();
        manifest.LeftProportion.Should().Be(LayoutManifest.DefaultLeftProportion);
        manifest.RightProportion.Should().Be(LayoutManifest.DefaultRightProportion);
    }
}
