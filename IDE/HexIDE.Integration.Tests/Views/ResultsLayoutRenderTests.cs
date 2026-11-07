using Avalonia;
using Avalonia.VisualTree;
using Avalonia.Headless;
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
using Avalonia.Headless.XUnit;

namespace HexIDE.Integration.Tests.Views;

public class ResultsLayoutRenderTests
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
            languageServers, protocolInspector, wss, new DockFactoryReference(), new HexIDE.Tools.OutlineToolViewModel(
                Substitute.For<HexIDE.Localization.ILocalizationService>(), Substitute.For<IDocumentDockService>()));
        return (factory, immediate);
    }

    [AvaloniaTheory]
    [InlineData(false, false, 649)]
    [InlineData(false, true, 649)]
    [InlineData(true, false, 380)]
    [InlineData(true, true, 380)]
    public void ResultsShowSettlesAfterOnePostedAdjustment(bool reset, bool hierarchy, double height)
    {
        var (factory, immediate) = CreateFactory();
        var root = factory.CreateLayout();
        factory.InitLayout(root);
        factory.PlaceTool(root, immediate, rightFallback: false);
        var original = factory.CaptureManifest(root);
        DockLayoutNode ToolNode(string key, double proportion) => new("tool", proportion,
            Orientation.Horizontal, Alignment.Bottom, null, [key], key, []);
        DockLayoutNode Stack(DockLayoutNode node) => node.Context == nameof(factory.MiddleDock)
            ? node with { Children = [new("document", 0.370, Orientation.Horizontal,
                Alignment.Unset, null, [], null, []), ToolNode("immediate", 0.111),
                ToolNode("locals", 0.144), ToolNode("callStack", 0.144), ToolNode("watches", 0.231)] }
            : node with { Children = node.Children.Select(Stack).ToArray() };
        (factory, immediate) = CreateFactory(original with
        {
            Tools = original.Tools.Select(t => t.Key is "locals" or "callStack" or "watches"
                ? t with { Open = true } : t).ToArray(),
            Topology = Stack(original.Topology!)
        });
        root = factory.CreateLayout();
        factory.InitLayout(root);
        if (reset)
        {
            root = factory.ResetLayout(root);
            factory.InitLayout(root);
            factory.PlaceTool(root, immediate, rightFallback: false);
        }
        var loc = new LocalizationService();
        loc.Apply("en");
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([]);
        var documents = Substitute.For<IDocumentDockService>();
        documents.OpenDocuments.Returns([]);
        var references = new FindReferencesToolViewModel(Substitute.For<ILspClient>(),
            Substitute.For<IEditorService>(), projects, documents, loc);
        var calls = new CallHierarchyToolViewModel(Substitute.For<ILspClient>(), Substitute.For<IEditorService>(), loc);
        ResultsToolViewModel results = hierarchy ? calls : references;
        var host = new Dock.Avalonia.Controls.DockControl { Layout = root, Factory = factory };
        host.DataTemplates.Add(new Avalonia.Controls.Templates.FuncDataTemplate<FindReferencesToolViewModel>(
            (_, _) => new FindReferencesToolView()));
        host.DataTemplates.Add(new Avalonia.Controls.Templates.FuncDataTemplate<CallHierarchyToolViewModel>(
            (_, _) => new CallHierarchyToolView()));
        var window = new Avalonia.Controls.Window { Content = host, Width = 1200, Height = height };
        window.SetRenderScaling(1.5);
        var context = System.Threading.ExecutionContext.Capture();
        try
        {
            window.Show();
            window.UpdateLayout();
            var slot = (IToolDock)immediate.Owner!;
            factory.AddDockable(slot, results);
            slot.ActiveDockable = results;
            var changes = 0;
            ((System.ComponentModel.INotifyPropertyChanged)slot).PropertyChanged += (_, e) =>
            {
                // Dock also writes rounded proportions during arrange. Count only our resize writes.
                if (e.PropertyName == nameof(slot.Proportion)
                    && new System.Diagnostics.StackTrace().GetFrames().Any(frame =>
                        frame.GetMethod()?.DeclaringType?.FullName?.StartsWith("HexIDE.Tools.ResultsToolHeight",
                            System.StringComparison.Ordinal) == true)) changes++;
            };
            // The server answer can arrive after the empty pane has already rendered.
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            for (var i = 0; i < 12; i++) calls.Roots.Add(new CallHierarchyNode("Caller " + i));
            if (!hierarchy) references.ShowLocations(Enumerable.Range(0, 12).Select(i =>
                new HexIDE.Lsp.Messages.Location("untitled:P/M.bas", new(new(i, 0), new(i, 1)))));
            System.Action render = () =>
            {
                for (var i = 0; i < 12; i++)
                {
                    window.UpdateLayout();
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    using var frame = window.CaptureRenderedFrame();
                    frame.Should().NotBeNull();
                }
            };
            render.Should().NotThrow("opening results must not cause an Infinite layout loop");
            changes.Should().BeLessThanOrEqualTo(1, "each show may adjust proportions only once");
        }
        finally
        {
            window.Close();
            System.Threading.ExecutionContext.Restore(context!);
        }
    }
}
