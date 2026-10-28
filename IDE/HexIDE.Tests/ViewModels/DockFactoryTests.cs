using Avalonia.VisualTree;
using System.Linq;
using Dock.Model.Core;
using Dock.Model.Controls;
using System.Runtime.CompilerServices;
using Dock.Model.Mvvm.Controls;
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
    private static (MainViewViewModel.DockFactory factory, ImmediateToolViewModel immediate) CreateFactory(LayoutManifest? manifest = null, ToolBoxToolViewModel? toolbox = null)
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
        var toolBox = toolbox ?? (ToolBoxToolViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ToolBoxToolViewModel));
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

    private static void LayoutWindow(Avalonia.Controls.Window window)
    {
        var context = System.Threading.ExecutionContext.Capture();
        try
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
        finally { System.Threading.ExecutionContext.Restore(context!); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosedLeftDock_RepeatedLayoutCyclesKeepItsWidth(bool restart)
    {
        Avalonia.Application.Current!.Styles.Add(new Avalonia.Themes.Simple.SimpleTheme());
        Avalonia.Application.Current.Styles.Add(new Dock.Avalonia.Themes.Simple.DockSimpleTheme());
        var (factory, _) = CreateFactory();
        var root = factory.CreateLayout();
        factory.InitLayout(root);
        var main = (IDock)root.DefaultDockable!;
        var left = (IToolDock)main.VisibleDockables![0];
        var tool = (Tool)left.VisibleDockables![0];
        tool.CanClose = true;
        left.Proportion = 0.14;
        var host = new Dock.Avalonia.Controls.DockControl { Layout = root };
        var window = new Avalonia.Controls.Window { Content = host, Width = 1000, Height = 700 };
        window.Show();
        try
        {
            LayoutWindow(window);
            double Width() => host.GetVisualDescendants().OfType<Dock.Avalonia.Controls.ToolChromeControl>()
                .Single(c => ReferenceEquals(c.DataContext, tool.Owner)).Bounds.Width;
            var expected = Width();
            expected.Should().BeGreaterThan(0);
            for (var cycle = 0; cycle < 4; cycle++)
            {
                factory.CloseDockable(tool);
                factory.CaptureManifest(root).Tools.Single(t => t.Key == "toolbox").Open.Should().BeFalse();
                LayoutWindow(window);
                if (restart)
                {
                    var saved = factory.CaptureManifest(root);
                    (factory, _) = CreateFactory(LayoutManifestJson.Deserialize(LayoutManifestJson.Serialize(saved)),
                        (ToolBoxToolViewModel)tool);
                    root = factory.CreateLayout();
                    factory.InitLayout(root);
                    host.Layout = root;
                    LayoutWindow(window);
                }
                factory.PlaceTool(root, tool, rightFallback: false);
                LayoutWindow(window);
                Width().Should().BeApproximately(expected, 0.01);
            }
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ClosedSplitMember_ReturnsToItsOwnDock(bool addin, bool closeContainer)
    {
        var (factory, immediate) = CreateFactory();
        var root = factory.CreateLayout();
        factory.InitLayout(root);
        Tool tool = immediate;
        if (addin)
        {
            var extra = new HexIDE.Addins.AddinToolWindowViewModel("sample", "Sample", () => new object());
            factory.RegisterAddinTool(root, extra);
            tool = extra;
        }
        (tool.Owner as IDock)?.VisibleDockables?.Remove(tool);
        var main = (IDock)root.DefaultDockable!;
        var left = main.VisibleDockables![0];
        left.Proportion = 0.4;
        var dock = factory.CreateToolDock();
        dock.Proportion = 0.6;
        dock.VisibleDockables = factory.CreateList<IDockable>(tool);
        var split = new ProportionalDock
        {
            Proportion = 0.25,
            VisibleDockables = factory.CreateList(left, new ProportionalDockSplitter(), dock),
        };
        main.VisibleDockables[0] = split;
        factory.InitLayout(root);
        for (var cycle = 0; cycle < 3; cycle++)
        {
            factory.CloseDockable(closeContainer ? dock : tool);
            factory.PlaceTool(root, tool, rightFallback: false);
            tool.Owner.Should().BeSameAs(dock);
            dock.ActiveDockable.Should().BeSameAs(tool);
            main.VisibleDockables[0].Should().BeSameAs(split);
            split.VisibleDockables.Should().Equal(left, split.VisibleDockables![1], dock);
            dock.Proportion.Should().Be(0.6);
            left.Proportion.Should().Be(0.4);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ClosedSplitMember_FileRoundTripRestoresItsOwnDock(bool addin, bool closeContainer)
    {
        var (factory, immediate) = CreateFactory();
        var root = factory.CreateLayout();
        factory.InitLayout(root);
        Tool tool = immediate;
        if (addin)
        {
            tool = new HexIDE.Addins.AddinToolWindowViewModel("sample", "Sample", () => new object());
            factory.RegisterAddinTool(root, (HexIDE.Addins.AddinToolWindowViewModel)tool);
        }
        (tool.Owner as IDock)?.VisibleDockables?.Remove(tool);
        var main = (IDock)root.DefaultDockable!;
        var left = main.VisibleDockables![0];
        left.Proportion = 0.4;
        var dock = factory.CreateToolDock();
        dock.Proportion = 0.6;
        dock.VisibleDockables = factory.CreateList<IDockable>(tool);
        main.VisibleDockables[0] = new ProportionalDock
        {
            Proportion = 0.25,
            VisibleDockables = factory.CreateList(left, new ProportionalDockSplitter(), dock),
        };
        factory.InitLayout(root);
        factory.CloseDockable(closeContainer ? dock : tool);
        var file = System.IO.Path.Combine(AppContext.BaseDirectory, System.IO.Path.GetRandomFileName());
        try
        {
            // Two restarts include an eager save before the closed add-in registers.
            for (var restart = 0; restart < 2; restart++)
            {
                System.IO.File.WriteAllText(file, LayoutManifestJson.Serialize(factory.CaptureManifest(root)));
                (factory, immediate) = CreateFactory(LayoutManifestJson.Deserialize(System.IO.File.ReadAllText(file)));
                root = factory.CreateLayout();
                factory.InitLayout(root);
                factory.CaptureManifest(root).Tools.Single(t => t.Key == (addin ? "addin:sample" : "immediate"))
                    .Open.Should().BeFalse();
            }
            tool = addin
                ? new HexIDE.Addins.AddinToolWindowViewModel("sample", "Sample", () => new object()) : immediate;
            if (addin)
                factory.RegisterAddinTool(root, (HexIDE.Addins.AddinToolWindowViewModel)tool);
            factory.PlaceTool(root, tool, rightFallback: false);
            var split = (IProportionalDock)((IDock)root.DefaultDockable!).VisibleDockables![0];
            split.Proportion.Should().Be(0.25);
            split.VisibleDockables.Should().HaveCount(3);
            split.VisibleDockables![0].Proportion.Should().Be(0.4);
            var restored = (IToolDock)split.VisibleDockables[2];
            restored.VisibleDockables.Should().Equal(tool);
            restored.ActiveDockable.Should().BeSameAs(tool);
            restored.Proportion.Should().Be(0.6);
        }
        finally { System.IO.File.Delete(file); }
    }

    [AvaloniaTheory]
    [InlineData(Dock.Model.Core.Orientation.Horizontal, false, false)]
    [InlineData(Dock.Model.Core.Orientation.Vertical, false, false)]
    [InlineData(Dock.Model.Core.Orientation.Horizontal, true, false)]
    [InlineData(Dock.Model.Core.Orientation.Vertical, true, false)]
    [InlineData(Dock.Model.Core.Orientation.Horizontal, false, true)]
    [InlineData(Dock.Model.Core.Orientation.Vertical, true, true)]
    public void SplitDocks_RoundTripPreservesTopologyTabsAndProportions(Dock.Model.Core.Orientation orientation, bool nested, bool addin)
    {
        var (factory, immediate) = CreateFactory();
        var root = factory.CreateLayout();
        factory.InitLayout(root);
        Dock.Model.Mvvm.Controls.Tool splitTool = immediate;
        if (addin)
        {
            var tool = new HexIDE.Addins.AddinToolWindowViewModel("sample", "Sample", () => new object());
            factory.RegisterAddinTool(root, tool);
            ((Dock.Model.Controls.IToolDock)tool.Owner!).VisibleDockables!.Remove(tool);
            splitTool = tool;
        }
        var main = (Dock.Model.Controls.IProportionalDock)root.DefaultDockable!;
        var toolboxDock = (Dock.Model.Controls.IToolDock)main.VisibleDockables![0];
        toolboxDock.Proportion = 0.35;
        var right = (Dock.Model.Core.IDock)main.VisibleDockables![4];
        var projectDock = (Dock.Model.Controls.IToolDock)right.VisibleDockables![0];
        var project = projectDock.VisibleDockables![0];
        projectDock.VisibleDockables.Remove(project);
        toolboxDock.VisibleDockables!.Add(project);
        toolboxDock.ActiveDockable = project;
        var extra = factory.CreateToolDock();
        extra.Alignment = Dock.Model.Core.Alignment.Left;
        extra.Proportion = 0.65;
        extra.VisibleDockables = factory.CreateList<Dock.Model.Core.IDockable>(splitTool);
        extra.ActiveDockable = splitTool;
        var split = new Dock.Model.Mvvm.Controls.ProportionalDock
        {
            Orientation = orientation,
            Proportion = 0.28,
            VisibleDockables = factory.CreateList<Dock.Model.Core.IDockable>(toolboxDock,
                new Dock.Model.Mvvm.Controls.ProportionalDockSplitter(), extra),
        };
        if (nested)
        {
            extra.Proportion = 0.8;
            split.VisibleDockables![2] = new Dock.Model.Mvvm.Controls.ProportionalDock
            {
                Orientation = orientation == Dock.Model.Core.Orientation.Horizontal
                    ? Dock.Model.Core.Orientation.Vertical : Dock.Model.Core.Orientation.Horizontal,
                Proportion = 0.65,
                VisibleDockables = factory.CreateList<Dock.Model.Core.IDockable>(extra),
            };
        }
        main.VisibleDockables[0] = split;
        factory.InitLayout(root);
        var saved = LayoutManifestJson.Deserialize(LayoutManifestJson.Serialize(factory.CaptureManifest(root)))!;
        var key = addin ? "addin:sample" : "immediate";
        saved.Tools.Single(t => t.Key == key).Open.Should().BeTrue();
        saved.Tools.Single(t => t.Key == key).Region.Should().Be(DockRegion.Left);

        var (restoredFactory, restoredImmediate) = CreateFactory(saved);
        var restoredRoot = restoredFactory.CreateLayout();
        restoredFactory.InitLayout(restoredRoot);

        Dock.Model.Mvvm.Controls.Tool restoredTool = restoredImmediate;
        if (addin)
        {
            // A save before add-in registration must retain its slot too.
            var pending = LayoutManifestJson.Deserialize(LayoutManifestJson.Serialize(restoredFactory.CaptureManifest(restoredRoot)))!;
            (restoredFactory, _) = CreateFactory(pending);
            restoredRoot = restoredFactory.CreateLayout();
            restoredFactory.InitLayout(restoredRoot);
            // An empty late-registration slot can be collapsed by the first layout pass.
            var pendingMain = (IProportionalDock)restoredRoot.DefaultDockable!;
            var pendingSplit = (IProportionalDock)pendingMain.VisibleDockables![0];
            IDockable pendingSlot = pendingSplit.VisibleDockables![2];
            if (nested)
                pendingSlot = ((IProportionalDock)pendingSlot).VisibleDockables!.Single();
            pendingSlot.Proportion = 0;
            pendingSplit.Proportion = 0;
            var tool = new HexIDE.Addins.AddinToolWindowViewModel("sample", "Sample", () => new object());
            if (!nested)
            {
                Avalonia.Application.Current!.Styles.Add(new Avalonia.Themes.Simple.SimpleTheme());
                Avalonia.Application.Current.Styles.Add(new Dock.Avalonia.Themes.Simple.DockSimpleTheme());
                var host = new Dock.Avalonia.Controls.DockControl { Layout = restoredRoot };
                var window = new Avalonia.Controls.Window { Content = host, Width = 1000, Height = 700 };
                window.Show();
                try
                {
                    LayoutWindow(window);
                    restoredFactory.RegisterAddinTool(restoredRoot, tool);
                    LayoutWindow(window);
                    var chrome = host.GetVisualDescendants().OfType<Dock.Avalonia.Controls.ToolChromeControl>()
                        .Single(c => ReferenceEquals(c.DataContext, tool.Owner));
                    chrome.Bounds.Width.Should().BeGreaterThan(0);
                    chrome.Bounds.Height.Should().BeGreaterThan(0);
                }
                finally { window.Close(); }
            }
            else
                restoredFactory.RegisterAddinTool(restoredRoot, tool);
            restoredTool = tool;
        }
        var restoredMain = (Dock.Model.Controls.IProportionalDock)restoredRoot.DefaultDockable!;
        restoredMain.VisibleDockables![0].Should().BeAssignableTo<Dock.Model.Controls.IProportionalDock>();
        var restoredSplit = (Dock.Model.Controls.IProportionalDock)restoredMain.VisibleDockables[0];
        restoredSplit.Orientation.Should().Be(orientation);
        restoredSplit.Proportion.Should().Be(0.28);
        restoredSplit.VisibleDockables.Should().HaveCount(3);
        restoredSplit.VisibleDockables![0].Proportion.Should().Be(0.35);
        var restoredTabs = (Dock.Model.Controls.IToolDock)restoredSplit.VisibleDockables[0];
        restoredTabs.VisibleDockables!.Select(restoredFactory.KeyOf).Should().Equal("toolbox", "project");
        restoredFactory.KeyOf(restoredTabs.ActiveDockable!).Should().Be("project");
        restoredSplit.VisibleDockables[1].Should().BeOfType<Dock.Model.Mvvm.Controls.ProportionalDockSplitter>();
        restoredSplit.VisibleDockables[2].Proportion.Should().Be(0.65);
        var restoredExtra = restoredSplit.VisibleDockables[2];
        if (nested)
        {
            var inner = (Dock.Model.Controls.IProportionalDock)restoredExtra;
            inner.Orientation.Should().NotBe(orientation);
            restoredExtra = inner.VisibleDockables!.Single();
            restoredExtra.Proportion.Should().Be(0.8);
        }
        restoredTool.Owner.Should().BeSameAs(restoredExtra);
    }

    [Theory]
    [InlineData(DockRegion.Left, false, false)]
    [InlineData(DockRegion.Left, true, false)]
    [InlineData(DockRegion.Right, false, false)]
    [InlineData(DockRegion.Right, true, false)]
    [InlineData(DockRegion.Bottom, false, false)]
    [InlineData(DockRegion.Bottom, true, false)]
    [InlineData(DockRegion.Left, false, true)]
    [InlineData(DockRegion.Right, false, true)]
    [InlineData(DockRegion.Bottom, false, true)]
    public void ClosedTool_RetainsResizedProportion(DockRegion region, bool restart, bool closeContainer)
    {
        var (factory, immediate) = CreateFactory(LayoutManifest.Default with
        {
            Tools = LayoutManifest.Default.Tools.Select(t => t.Key == "immediate"
                ? t with { Open = true, Region = region }
                : t.Region == region ? t with { Open = false } : t).ToArray()
        });
        var root = factory.CreateLayout();
        factory.InitLayout(root);
        var dock = (Dock.Model.Controls.IToolDock)immediate.Owner!;
        dock.Proportion = 0.42;

        factory.CloseDockable(closeContainer ? dock : immediate);
        var saved = LayoutManifestJson.Deserialize(LayoutManifestJson.Serialize(factory.CaptureManifest(root)))!;
        saved.Tools.Single(t => t.Key == "immediate").Open.Should().BeFalse();
        if (restart)
        {
            (factory, immediate) = CreateFactory(saved);
            root = factory.CreateLayout();
            factory.InitLayout(root);
        }

        factory.PlaceTool(root, immediate, rightFallback: false);

        var restored = factory.CaptureManifest(root).Tools.Single(t => t.Key == "immediate");
        restored.Open.Should().BeTrue();
        restored.Region.Should().Be(region);
        restored.Proportion.Should().Be(0.42);
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
    [InlineData(false)]
    [InlineData(true)]
    public void CaptureManifest_LiveToolInUnrecognizedDock_RestoresOpenInHomeRegion(bool nested)
    {
        var (factory, immediate) = CreateFactory();
        var root = factory.CreateLayout();
        factory.InitLayout(root);
        if (nested)
        {
            // A far-left drop wraps the previous main dock in another proportional dock.
            var main = root.DefaultDockable!;
            var wrapper = new ProportionalDock { VisibleDockables = factory.CreateList(main) };
            root.VisibleDockables = factory.CreateList<IDockable>(wrapper);
            root.DefaultDockable = wrapper;
            main.Owner = wrapper;
            wrapper.Owner = root;
        }
        else
        {
            var main = (IDock)root.DefaultDockable!;
            var dock = new ToolDock { VisibleDockables = factory.CreateList<IDockable>(immediate) };
            main.VisibleDockables!.Add(dock);
            dock.Owner = main;
            immediate.Owner = dock;
        }

        var key = nested ? "toolbox" : "immediate";
        var home = LayoutManifest.Default.Tools.Single(t => t.Key == key);
        var manifest = factory.CaptureManifest(root);
        manifest.Tools.Single(t => t.Key == key).Should().Be(home with { Open = true });

        var (restoredFactory, _) = CreateFactory(LayoutManifestJson.Deserialize(LayoutManifestJson.Serialize(manifest)));
        var restoredRoot = restoredFactory.CreateLayout();
        restoredFactory.InitLayout(restoredRoot);
        restoredFactory.CaptureManifest(restoredRoot).Tools.Single(t => t.Key == key).Open.Should().BeTrue();
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

        tool.KeepSavedWidth.Should().BeTrue("a saved dock width must not be replaced by first-display sizing");
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
