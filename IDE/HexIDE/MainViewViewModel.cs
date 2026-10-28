using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Labs.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HexIDE.Controls;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Keymaps;
using HexIDE.Localization;
using HexIDE.Themes;
using HexIDE.Projects;
using HexIDE.Runtime;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Addins;
using HexIDE.Tools;
using HexIDE.Tools.ObjectBrowser;
using System.Windows.Input;
using HexIDE.Tools.LanguageServers;
using HexIDE.Tools.TranslationEditor;
using HexIDE.Utils;
using HexIDE.VisualDesigner;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using PropertyChanged.SourceGenerator;
using R3;
using Serilog;

namespace HexIDE;

public partial class MainViewViewModel : ObservableObject
{
    /// <summary>The last runtime error a program raised, kept after its dialog has gone.</summary>
    public RuntimeErrorLog RuntimeErrors { get; } = new();

    private readonly IWindowManager windowManager;
    private readonly IProjectService projectService;
    private readonly IDocumentDockService documentDockService;
    public DelegateCommand ReopenClosedDocumentCommand { get; }
    public ObservableCollection<RecentProjectItemViewModel> ClosedDocumentMenuItems { get; } = new();
    private readonly DockFactory dockFactory;
    private readonly IProjectRunnerService projectRunnerService;
    private readonly IEventBus eventBus;
    private readonly IVb6ToolchainService vb6Toolchain;
    private readonly IRecentProjectsService recentProjectsService;
    private readonly IFindReplaceService findReplaceService;
    private readonly ISettingsService settingsService;
    private readonly IThemeService themeService;
    private readonly IKeymapService keymapService;
    private readonly ILanguageSwitchService languageSwitch;
    private readonly ILocalizationService localization;
    private readonly IAddinRegistry addinRegistry;
    private readonly AddinOptionsService addinOptionsService;
    private readonly IDeveloperModeService developerModeService;
    private readonly IProjectManager projectManager;
    private readonly IEditorService editorService;
    private readonly IWindowStateService windowStateService;

    // Eager dock-layout persistence: a layout change schedules a debounced flush; the flush captures the
    // live manifest, dedupes against the last write, and writes off the UI thread (Phase 5).
    private readonly DispatcherTimer _layoutSaveTimer;
    private string? _lastSavedLayoutJson;

    public IWindowManager WindowManager => windowManager;

    public ToolBoxToolViewModel ToolBox { get; }

    public PropertiesToolViewModel Properties { get; }
    public ImmediateToolViewModel Immediate { get; }
    public FormLayoutToolViewModel FormLayout { get; }
    public LocalsToolViewModel Locals { get; }
    public WatchesToolViewModel Watches { get; }
    public CallStackToolViewModel CallStack { get; }
    public RecentEditsToolViewModel RecentEdits { get; }
    public ProjectToolViewModel ProjectExplorer { get; }
    public ColorPaletteToolViewModel ColorPalette { get; }
    public ObjectBrowserToolViewModel ObjectBrowser { get; }
    public TranslationEditorViewModel TranslationEditor { get; }
    public LanguageServersToolViewModel LanguageServers { get; }

    /// <summary>What actually went over the wire to a language server.</summary>
    public HexIDE.Tools.ProtocolInspector.ProtocolInspectorToolViewModel ProtocolInspector { get; }
    public IFocusedProjectUtil FocusedProjectUtil { get; }

    public IStatusBarService StatusBar { get; }

    private IRootDock _layout = null!;
    public IRootDock Layout
    {
        get => _layout;
        private set => SetProperty(ref _layout, value);
    }

    public DelegateCommand SaveFormCommand { get; }
    public DelegateCommand RemoveDocumentCommand { get; }
    public DelegateCommand SaveFormAsCommand { get; }

    public DelegateCommand StartDefaultProjectCommand { get; }
    public DelegateCommand StartExternalDebugCommand { get; }
    public HexIDE.Debugging.ExternalDebugService? ExternalDebugger { get; }

    public DelegateCommand StartDefaultProjectWithFullCompileCommand { get; }

    public DelegateCommand BreakProjectCommand { get; }

    public DelegateCommand EndProjectCommand { get; }

    public DelegateCommand RestartProjectCommand { get; }

    public DelegateCommand ProjectReferencesCommand { get; }

    public DelegateCommand ProjectComponentsCommand { get; }

    public DelegateCommand ProjectPropertiesCommand { get; }

    public DelegateCommand AddFormCommand { get; }

    public DelegateCommand AddModuleCommand { get; }

    public DelegateCommand AddClassModuleCommand { get; }

    public DelegateCommand AddUserControlCommand { get; }

    public DelegateCommand AddPropertyPageCommand { get; }

    public DelegateCommand AddFileCommand { get; }

    public ObservableCollection<RecentProjectItemViewModel> RecentProjects { get; } = new();
    public bool HasRecentProjects => RecentProjects.Count > 0;

    private void UpdateRunState()
    {
        // Break is bound directly so a running form's focus cannot divert it from the IDE (#2179).
        BreakProjectCommand.RaiseCanExecutedChanged();
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(WindowTitle));
        if (projectRunnerService.IsRunning && debugController.State != DebugState.Paused)
            StatusBar.SetMessage(localization.GetString("Str.StatusBar.Running"));
        else
            StatusBar.ClearMessage();
    }

    public string Title
    {
        get
        {
            var prefix = projectManager.CurrentGroup?.Name
                      ?? FocusedProjectUtil.FocusedOrStartupProject?.Name;
            return prefix == null
                ? localization.GetString("Str.MainWindow.NoProjectTitle")
                : $"{prefix} - HexIDE " + localization.GetString(debugController.State == DebugState.Paused
                    ? "Str.MainWindow.BreakTag"
                    : projectRunnerService.IsRunning ? "Str.MainWindow.RunTag" : "Str.MainWindow.DesignTag");
        }
    }

    /// <summary>The OS window title: <see cref="Title"/> plus a loud developer-mode suffix when the
    /// session is in developer mode. Bound by the window chrome; recomputed when <see cref="Title"/>
    /// changes (dev mode is session-fixed). Kept separate from <see cref="Title"/> so the VB6 Caption
    /// proxy (Get/SetPropertyValue) is unaffected.</summary>
    public string WindowTitle =>
        developerModeService.IsEnabled ? $"{Title}  *** DEVELOPER MODE ***" : Title;

    public string SaveProjectMenuHeader =>
        projectManager.CurrentGroup != null
            ? string.Format(localization.GetString("Str.Menu.File.SaveProjectGroup"), projectManager.CurrentGroup.Name)
            : FocusedProjectUtil.FocusedOrStartupProject?.Name is { } n
                ? string.Format(localization.GetString("Str.Menu.File.SaveProjectFormat"), n)
                : localization.GetString("Str.Menu.File.SaveProjectFallback");

    // Name-aware, localized menu headers (formerly AXAML StringFormat/FallbackValue). Each re-raises on
    // the relevant focus change and on LanguageChanged (see the subscriptions in the constructor).
    public string SaveFormMenuHeader => FormatHeader("Str.Menu.File.SaveFormFormat", "Str.Menu.File.SaveFormFallback", FocusedProjectUtil.FocusedForm?.Name);
    public string SaveFormAsMenuHeader => FormatHeader("Str.Menu.File.SaveFormAsFormat", "Str.Menu.File.SaveFormAsFallback", FocusedProjectUtil.FocusedForm?.Name);
    public string RemoveProjectMenuHeader => FormatHeader("Str.Menu.File.RemoveProjectFormat", "Str.Menu.File.RemoveProjectFallback", FocusedProjectUtil.FocusedOrStartupProject?.Name);
    public string MakeProjectMenuHeader => FormatHeader("Str.Menu.File.MakeProjectFormat", "Str.Menu.File.MakeProjectFallback", FocusedProjectUtil.FocusedOrStartupProject?.Name);
    public string MakeWithVb6MenuHeader => FormatHeader("Str.Menu.File.MakeWithVb6Format", "Str.Menu.File.MakeWithVb6Fallback", FocusedProjectUtil.FocusedOrStartupProject?.Name);
    public string RemoveFormMenuHeader => FormatHeader("Str.Menu.Project.RemoveFormFormat", "Str.Menu.Project.RemoveFormFallback", RemovableDocument?.Name);
    public string ProjectPropertiesMenuHeader => FormatHeader("Str.Menu.Project.PropertiesFormat", "Str.Menu.Project.PropertiesFallback", FocusedProjectUtil.FocusedOrStartupProject?.Name);

    private DocumentIdentity? RemovableDocument
    {
        get
        {
            var document = ProjectExplorer.SelectedForm is { } form ? DocumentIdentity.For(form.FormDefinition)
                : ProjectExplorer.SelectedModule is { } module ? DocumentIdentity.For(module.ModuleDefinition)
                : documentDockService.ActiveDocument?.OpenDocument;
            return document != null && DocumentLookup.DocumentsOf(document.Project).Contains(document) ? document : null;
        }
    }

    private void UpdateRemoveDocumentMenu()
    {
        OnPropertyChanged(nameof(RemoveFormMenuHeader));
        RemoveDocumentCommand.RaiseCanExecutedChanged();
    }

    private async Task RemoveActiveDocument()
    {
        if (RemovableDocument is not { } document) return;
        if (ProjectExplorer.HasRemovableSelection)
            await ProjectExplorer.RemoveSelectedDocument();
        else
            await projectService.RemoveDocument(document);
        UpdateRemoveDocumentMenu();
    }

    private string FormatHeader(string formatKey, string fallbackKey, string? name) =>
        name is { } n ? string.Format(localization.GetString(formatKey), n) : localization.GetString(fallbackKey);

    private static readonly string[] DynamicMenuHeaders =
    [
        nameof(SaveProjectMenuHeader), nameof(SaveFormMenuHeader), nameof(SaveFormAsMenuHeader),
        nameof(RemoveProjectMenuHeader), nameof(MakeProjectMenuHeader), nameof(MakeWithVb6MenuHeader),
        nameof(RemoveFormMenuHeader), nameof(ProjectPropertiesMenuHeader),
    ];

    private void RaiseDynamicMenuHeaders()
    {
        foreach (var name in DynamicMenuHeaders)
            OnPropertyChanged(name);
    }

    public ISettingsService Settings => settingsService;

    public IPersonalityService Personality { get; }

    internal AddinMenuService AddinMenuService { get; }

    internal AddinCommandService AddinCommandService { get; }

    private readonly AddinToolWindowService _addinToolWindowService;

    public class DockFactory : Factory
    {
        private readonly IWindowStateService windowStateService;
        private readonly Dictionary<string, IDockable> toolsByKey;
        private readonly Dictionary<string, ToolLayoutState> deferredTools = new();
        private readonly Dictionary<string, int> savedOrders = new();
        private readonly Dictionary<string, ToolLayoutState> lastPlacements = new();
        private sealed record DockSlot(IDock Parent, IDockable Child, int Index,
            (IDockable Dock, double Proportion)[] Siblings);
        private readonly Dictionary<string, List<DockSlot>> closedSlots = new();
        private readonly Dictionary<IDockable, double> restoredProportions = new();
        private IRootDock? liveLayout;
        private readonly Dictionary<string, IToolDock> pendingToolDocks = new();
        private readonly Dictionary<IToolDock, DockLayoutNode> pendingToolGroups = new();
        private double leftProportion = LayoutManifest.DefaultLeftProportion;
        private double rightProportion = LayoutManifest.DefaultRightProportion;

        // Every tool's "home" region/order — the single source of truth for defaults and for
        // restoring a closed-then-reopened tool to its slot.
        private static readonly IReadOnlyDictionary<string, ToolLayoutState> Home =
            LayoutManifest.Default.Tools.ToDictionary(t => t.Key);

        public ProportionalDock? RightDock;
        public ProportionalDock? MiddleDock;
        public DocumentDock? DocumentDock { get; private set; }

        public DockFactory(ToolBoxToolViewModel toolBox,
            ProjectToolViewModel project,
            PropertiesToolViewModel properties,
            FormLayoutToolViewModel formLayout,
            ImmediateToolViewModel immediate,
            LocalsToolViewModel locals,
            WatchesToolViewModel watches,
            CallStackToolViewModel callStack,
            ColorPaletteToolViewModel colorPalette,
            ObjectBrowserToolViewModel objectBrowser,
            TranslationEditorViewModel translationEditor,
            LanguageServersToolViewModel languageServers,
            HexIDE.Tools.ProtocolInspector.ProtocolInspectorToolViewModel protocolInspector,
            IWindowStateService windowStateService,
            DockFactoryReference documentDockFactory)
        {
            ArgumentNullException.ThrowIfNull(toolBox);
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(properties);
            ArgumentNullException.ThrowIfNull(formLayout);
            ArgumentNullException.ThrowIfNull(immediate);
            ArgumentNullException.ThrowIfNull(locals);
            ArgumentNullException.ThrowIfNull(watches);
            ArgumentNullException.ThrowIfNull(callStack);
            ArgumentNullException.ThrowIfNull(colorPalette);
            ArgumentNullException.ThrowIfNull(objectBrowser);
            ArgumentNullException.ThrowIfNull(translationEditor);
            ArgumentNullException.ThrowIfNull(languageServers);
            ArgumentNullException.ThrowIfNull(protocolInspector);
            ArgumentNullException.ThrowIfNull(windowStateService);
            ArgumentNullException.ThrowIfNull(documentDockFactory);

            this.windowStateService = windowStateService;
            toolsByKey = new Dictionary<string, IDockable>
            {
                ["toolbox"]           = toolBox,
                ["project"]           = project,
                ["properties"]        = properties,
                ["formLayout"]        = formLayout,
                ["immediate"]         = immediate,
                ["locals"]            = locals,
                ["watches"]           = watches,
                ["callStack"]         = callStack,
                ["colorPalette"]      = colorPalette,
                ["objectBrowser"]     = objectBrowser,
                ["translationEditor"] = translationEditor,
                ["languageServers"]   = languageServers,
            };
            documentDockFactory.Value = this;
        }

        public override IToolDock CreateToolDock()
        {
            var toolDock = base.CreateToolDock();
            toolDock.CanFloat = false;
            return toolDock;
        }

        public override IRootDock CreateLayout() =>
            BuildLayout(windowStateService.LoadLayoutManifest() ?? LayoutManifest.Default, reuseDocument: null);

        /// <summary>Raised after a programmatic layout mutation (a tool opened or closed) so the owner can
        /// schedule an eager save. Mouse-driven changes (splitter resize, drag-dock) are caught by the
        /// view's pointer handler instead.</summary>
        public event Action? LayoutChanged;

        public override void CloseDockable(IDockable dockable)
        {
            if (liveLayout != null)
            {
                CaptureManifest(liveLayout);
                foreach (var (key, tool) in toolsByKey)
                    if (ReferenceEquals(tool, dockable) || FindContainer(dockable, tool) != null)
                    {
                        var path = new List<DockSlot>();
                        IDockable child = tool;
                        while (FindContainer(liveLayout, child) is { } parent && parent is not IRootDock)
                        {
                            path.Add(new DockSlot(parent, child, parent.VisibleDockables!.IndexOf(child),
                                parent.VisibleDockables.Where(d => d is not IProportionalDockSplitter)
                                    .Select(d => (d, d.Proportion)).ToArray()));
                            child = parent;
                        }
                        closedSlots[key] = path;
                    }
            }
            base.CloseDockable(dockable);
            LayoutChanged?.Invoke();
        }

        /// <summary>Discard the saved manifest and rebuild the default arrangement, reusing the live
        /// document dock so open editor/designer tabs (and their service subscription) survive the reset.</summary>
        public IRootDock ResetLayout(IRootDock layout)
        {
            windowStateService.ResetLayoutManifest();
            var defaults = LayoutManifest.Default with
            {
                Tools = LayoutManifest.Default.Tools.Concat(toolsByKey
                    .Where(t => t.Key.StartsWith("addin:", StringComparison.Ordinal))
                    .Select(t => new ToolLayoutState(t.Key, Locate(layout, t.Value) != null,
                        DockRegion.Right, int.MaxValue, null))).ToArray()
            };
            return BuildLayout(defaults, DocumentDock);
        }

        // ---- build a fresh layout tree from a manifest ----

        private IRootDock BuildLayout(LayoutManifest m, IDocumentDock? reuseDocument)
        {
            closedSlots.Clear();
            restoredProportions.Clear();
            pendingToolDocks.Clear();
            pendingToolGroups.Clear();
            leftProportion = m.LeftProportion;
            rightProportion = m.RightProportion;
            lastPlacements.Clear();
            foreach (var state in m.Tools)
                lastPlacements[state.Key] = state;
            savedOrders.Clear();
            foreach (var state in m.Tools)
                savedOrders[state.Key] = state.Order;
            deferredTools.Clear();
            foreach (var state in m.Tools.Where(t => t.Key.StartsWith("addin:", StringComparison.Ordinal)))
                deferredTools[state.Key] = state;

            var knownStates = new List<ToolLayoutState>();
            var unknownKeys = new HashSet<string>();
            foreach (var state in m.Tools)
            {
                if (toolsByKey.ContainsKey(state.Key))
                    knownStates.Add(state);
                else if (!deferredTools.ContainsKey(state.Key) && unknownKeys.Add(state.Key))
                    Log.Warning("DockFactory: Skipping unknown layout tool key {Key}", state.Key);
            }

            List<ToolLayoutState> OpenIn(DockRegion r) =>
                knownStates.Where(t => t.Open && t.Region == r).OrderBy(t => t.Order).ToList();

            // LEFT — single ToolDock holding the left tool(s) (the toolbox).
            IToolDock? leftDock = null;
            var leftTools = OpenIn(DockRegion.Left)
                .Select(s => toolsByKey[s.Key]).OfType<Tool>().Cast<IDockable>().ToArray();
            if (leftTools.Length > 0)
            {
                leftDock = CreateToolDock();
                leftDock.VisibleDockables = CreateList(leftTools);
                leftDock.ActiveDockable = leftTools[0];
                leftDock.Alignment = Alignment.Left;
                leftDock.Proportion = m.LeftProportion;
            }

            // RIGHT — vertical stack of single-tool ToolDocks.
            RightDock = null;
            var rightStates = OpenIn(DockRegion.Right);
            if (rightStates.Any(s => toolsByKey[s.Key] is Tool))
            {
                var children = new List<IDockable>();
                foreach (var s in rightStates)
                {
                    if (toolsByKey[s.Key] is not Tool t) continue;
                    if (children.Count > 0) children.Add(new ProportionalDockSplitter());
                    children.Add(MakeToolDock(t, Alignment.Top, s.Proportion));
                }
                RightDock = new ProportionalDock
                {
                    Orientation = Orientation.Vertical,
                    Proportion = m.RightProportion,
                    CanClose = false,
                    CanFloat = false,
                    IsCollapsable = false,
                    Context = nameof(RightDock),
                    VisibleDockables = CreateList(children.ToArray()),
                };
            }

            // DOCUMENT dock (editor/designer file tabs + Object Browser / Translation Editor).
            // On reset we reuse the live dock so open editor/designer tabs — and the
            // DocumentDockService subscription bound to it — survive; only the manifest-managed
            // document tools (Object Browser / Translation Editor) are reconciled. Keep the same
            // VisibleDockables list instance so that subscription is not lost.
            DocumentDock dd;
            if (reuseDocument is DocumentDock live)
            {
                dd = live;
                dd.VisibleDockables ??= CreateList<IDockable>();
                foreach (var managed in dd.VisibleDockables.OfType<Document>().Where(d => KeyOf(d) != null).ToList())
                    dd.VisibleDockables.Remove(managed);
            }
            else
            {
                dd = new DocumentDock
                {
                    CanClose = false,
                    CanFloat = false,
                    CanCreateDocument = false,
                    IsCollapsable = false,
                    VisibleDockables = CreateList<IDockable>(),
                };
            }
            DocumentDock = dd;

            var documentTools = OpenIn(DockRegion.Document)
                .Select(s => toolsByKey[s.Key]).OfType<Document>().Cast<IDockable>().ToArray();
            foreach (var d in documentTools)
                if (!dd.VisibleDockables!.Contains(d))
                    dd.VisibleDockables!.Add(d);
            if (documentTools.Length > 0)
                dd.ActiveDockable = documentTools[0];

            // MIDDLE — document on top, bottom tools stacked below.
            var middleChildren = new List<IDockable> { DocumentDock };
            foreach (var s in OpenIn(DockRegion.Bottom))
            {
                if (toolsByKey[s.Key] is not Tool t) continue;
                middleChildren.Add(new ProportionalDockSplitter());
                middleChildren.Add(MakeToolDock(t, Alignment.Bottom, s.Proportion ?? 0.3));
            }
            MiddleDock = new ProportionalDock
            {
                Orientation = Orientation.Vertical,
                CanClose = false,
                IsCollapsable = false,
                CanFloat = false,
                Context = nameof(MiddleDock),
                VisibleDockables = CreateList(middleChildren.ToArray()),
            };

            // MAIN horizontal split: [left?] middle [right?]
            var horiz = new List<IDockable>();
            if (leftDock != null) horiz.Add(leftDock);
            horiz.Add(MiddleDock);
            if (RightDock != null) horiz.Add(RightDock);

            var mainLayout = new ProportionalDock
            {
                Orientation = Orientation.Horizontal,
                CanFloat = false,
                VisibleDockables = CreateList(Interleave(horiz)),
            };

            if (m.Topology is { Kind: "split" } topology && ValidTopology(topology) && RestoreTopology(topology) is ProportionalDock restoredMain)
            {
                mainLayout = restoredMain;
                RightDock = Find<ProportionalDock>(mainLayout, d => (d.Context as string) == nameof(RightDock));
                MiddleDock = Find<ProportionalDock>(mainLayout, d => (d.Context as string) == nameof(MiddleDock)) ?? MiddleDock;
            }

            var rootDock = CreateRootDock();
            rootDock.IsFocusableRoot = true;
            rootDock.IsCollapsable = false;
            rootDock.ActiveDockable = mainLayout;
            rootDock.DefaultDockable = mainLayout;
            rootDock.VisibleDockables = CreateList<IDockable>(mainLayout);
            liveLayout = rootDock;
            // Recreate the ordinary close/show paths before exposing the restored layout.
            // Closed add-ins need a temporary leaf until their real tool registers.
            var closed = m.Tools.Where(t => !t.Open).Select(t => t.Key).ToHashSet();
            var placeholders = new List<string>();
            if (m.Topology != null)
            {
                foreach (var key in closed.Where(k => deferredTools.ContainsKey(k) && !toolsByKey.ContainsKey(k)))
                {
                    if (!pendingToolDocks.Remove(key, out var pending)) continue;
                    var placeholder = new Tool { CanClose = true };
                    toolsByKey[key] = placeholder;
                    placeholders.Add(key);
                    pending.VisibleDockables!.Add(placeholder);
                    if (!pendingToolDocks.Values.Contains(pending)) pendingToolGroups.Remove(pending);
                }
                InitLayout(rootDock);
                foreach (var key in closed)
                    if (toolsByKey.TryGetValue(key, out var tool) && FindContainer(rootDock, tool) != null)
                        CloseDockable(tool);
                foreach (var key in placeholders) toolsByKey.Remove(key);
                foreach (var key in closed)
                    if (lastPlacements.TryGetValue(key, out var placement))
                        lastPlacements[key] = placement with { Open = false };
            }
            return rootDock;
        }

        private static bool ValidTopology(DockLayoutNode root)
        {
            var keys = new HashSet<string>();
            var documents = 0;
            bool Visit(DockLayoutNode node)
            {
                if (node.Tools == null || node.Children == null || node.Tools.Any(k => !keys.Add(k)))
                    return false;
                if (node.Kind == "document") documents++;
                return node.Kind is "split" or "tool" or "document" && node.Children.All(Visit);
            }
            return Visit(root) && documents == 1;
        }

        private DockLayoutNode? CaptureTopology(IDockable node)
        {
            if (node is not IDock dock) return null;
            var kind = node is IDocumentDock ? "document" : node is IToolDock ? "tool" : "split";
            // Save removed branches alongside live ones; Tools.Open keeps them closed on restore.
            var slots = closedSlots.Values.SelectMany(p => p).ToArray();
            IEnumerable<IDockable> SavedChildren(IDock parent) =>
                liveLayout != null && !ReferenceEquals(parent, liveLayout.DefaultDockable)
                    && FindContainer(liveLayout, parent) == null && slots.FirstOrDefault(s => ReferenceEquals(s.Parent, parent)) is { } saved
                    ? saved.Siblings.Select(s => s.Dock)
                    : (parent.VisibleDockables ?? CreateList<IDockable>()).Where(d => d is not IProportionalDockSplitter);
            bool WasInside(IDockable branch, IDockable child) => branch is IDock parent
                && SavedChildren(parent).Any(d => ReferenceEquals(d, child) || WasInside(d, child));
            var children = SavedChildren(dock).ToList();
            var proportions = new Dictionary<IDockable, double>();
            foreach (var slot in closedSlots.Values.SelectMany(p => p).Where(s => ReferenceEquals(s.Parent, dock)))
            {
                if (liveLayout != null && FindContainer(liveLayout, dock) == null)
                    foreach (var (sibling, proportion) in slot.Siblings) proportions[sibling] = proportion;
                if (children.Contains(slot.Child)) continue;
                // Dock may have promoted a surviving descendant when it collapsed this branch.
                children.RemoveAll(child => WasInside(slot.Child, child));
                var index = slot.Siblings.TakeWhile(s => !ReferenceEquals(s.Dock, slot.Child))
                    .Count(s => children.Contains(s.Dock));
                children.Insert(Math.Min(index, children.Count), slot.Child);
                foreach (var (sibling, proportion) in slot.Siblings) proportions[sibling] = proportion;
            }
            string? SavedKey(IDockable child) => KeyOf(child) ?? closedSlots
                .FirstOrDefault(p => p.Value.Count > 0 && ReferenceEquals(p.Value[0].Child, child)).Key;
            var keys = children.Select(SavedKey).OfType<string>()
                .Concat(pendingToolDocks.Where(p => ReferenceEquals(p.Value, dock)).Select(p => p.Key)).ToArray();
            var pendingGroup = node is IToolDock group ? pendingToolGroups.GetValueOrDefault(group) : null;
            if (pendingGroup != null)
                keys = keys.OrderBy(k => pendingGroup.Tools.ToList().IndexOf(k)).ToArray();
            DockLayoutNode? CaptureChild(IDockable child)
            {
                var captured = CaptureTopology(child);
                return captured != null && proportions.TryGetValue(child, out var proportion)
                    ? captured with { Proportion = double.IsNaN(proportion) ? null : proportion } : captured;
            }
            return new DockLayoutNode(kind, double.IsNaN(node.Proportion) ? null : node.Proportion,
                node is IProportionalDock pd ? pd.Orientation : Orientation.Horizontal,
                node is IToolDock td ? td.Alignment : Alignment.Unset, node.Context as string,
                keys, pendingGroup?.ActiveTool ?? (dock.ActiveDockable is { } active ? KeyOf(active) : null),
                kind == "split" ? children.Select(CaptureChild).OfType<DockLayoutNode>().ToArray() : []);
        }

        private IDock? RestoreTopology(DockLayoutNode node)
        {
            IDock dock;
            if (node.Kind == "document")
                dock = DocumentDock!;
            else if (node.Kind == "tool")
            {
                var td = CreateToolDock();
                td.Alignment = node.Alignment;
                td.VisibleDockables = CreateList(node.Tools.Where(toolsByKey.ContainsKey)
                    .Select(k => toolsByKey[k]).OfType<Tool>().Cast<IDockable>().ToArray());
                foreach (var key in node.Tools.Where(k => !toolsByKey.ContainsKey(k) && deferredTools.ContainsKey(k)))
                    pendingToolDocks[key] = td;
                if (pendingToolDocks.Values.Contains(td))
                    pendingToolGroups[td] = node;
                if (td.VisibleDockables.Count == 0 && !pendingToolDocks.Values.Contains(td))
                    return null;
                dock = td;
            }
            else
                dock = new ProportionalDock
                {
                    Orientation = node.Orientation,
                    CanClose = false,
                    CanFloat = false,
                    IsCollapsable = false,
                    VisibleDockables = CreateList(Interleave(node.Children.Select(RestoreTopology).OfType<IDockable>().ToList())),
                };
            dock.Proportion = node.Proportion ?? double.NaN;
            restoredProportions[dock] = dock.Proportion;
            dock.Context = node.Context;
            dock.ActiveDockable = node.ActiveTool is { } activeKey && toolsByKey.TryGetValue(activeKey, out var active)
                && dock.VisibleDockables?.Contains(active) == true ? active : dock.VisibleDockables?.FirstOrDefault();
            return dock;
        }

        private IToolDock MakeToolDock(Tool tool, Alignment alignment, double? proportion)
        {
            var d = CreateToolDock();
            d.ActiveDockable = tool;
            d.VisibleDockables = CreateList<IDockable>(tool);
            d.Alignment = alignment;
            if (proportion is { } p && !double.IsNaN(p))
                d.Proportion = p;
            return d;
        }

        // Dock writes back concrete proportions after the first render, so a NaN-proportioned dock
        // inserted into an already-laid-out stack gets squeezed to nothing. Give a runtime-inserted
        // dock the average of its (concrete) siblings so it appears at a fair size; null when the stack
        // has no sized tool docks yet (a single auto dock fills the region).
        private static double? FairProportion(IDock container)
        {
            var props = (container.VisibleDockables ?? new List<IDockable>())
                .OfType<IToolDock>()
                .Select(d => d.Proportion)
                .Where(p => !double.IsNaN(p) && p > 0)
                .ToList();
            return props.Count > 0 ? props.Average() : (double?)null;
        }

        private static IDockable[] Interleave(List<IDockable> items)
        {
            var res = new List<IDockable>();
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) res.Add(new ProportionalDockSplitter());
                res.Add(items[i]);
            }
            return res.ToArray();
        }

        // ---- capture the live tree back into a manifest (Closing save) ----

        public LayoutManifest CaptureManifest(IRootDock layout)
        {
            var main = layout.DefaultDockable as IDock;

            var left = leftProportion;
            if (main?.VisibleDockables?.OfType<IToolDock>().FirstOrDefault(d => d.Alignment == Alignment.Left)
                is { Proportion: > 0 } ld)
                left = ld.Proportion;

            var right = rightProportion;
            if (Find<IProportionalDock>(layout, d => (d.Context as string) == nameof(RightDock))
                is { Proportion: > 0 } rd)
                right = rd.Proportion;

            var states = new List<ToolLayoutState>(LayoutManifest.Default.Tools.Count);
            foreach (var def in LayoutManifest.Default.Tools)
            {
                var located = Locate(layout, toolsByKey[def.Key]);
                states.Add(located is { } loc
                    ? new ToolLayoutState(def.Key, true, loc.Region, loc.Order, loc.Proportion)
                    : lastPlacements.GetValueOrDefault(def.Key, def) with { Open = false });
            }

            foreach (var key in deferredTools.Keys.Concat(toolsByKey.Keys.Where(k => k.StartsWith("addin:", StringComparison.Ordinal))).Distinct())
            {
                var saved = lastPlacements.GetValueOrDefault(key) ?? deferredTools.GetValueOrDefault(key)
                    ?? new ToolLayoutState(key, false, DockRegion.Right, int.MaxValue, null);
                var located = toolsByKey.TryGetValue(key, out var tool) ? Locate(layout, tool) : null;
                states.Add(located is { } loc
                    ? new ToolLayoutState(key, true, loc.Region, loc.Order, loc.Proportion)
                    : toolsByKey.ContainsKey(key) ? saved with { Open = false } : saved);
            }

            leftProportion = left;
            rightProportion = right;
            foreach (var state in states)
                lastPlacements[state.Key] = state;
            return new LayoutManifest(LayoutManifest.CurrentVersion, left, right, states)
            {
                Topology = layout.DefaultDockable is { } tree ? CaptureTopology(tree) : null,
            };
        }

        private (DockRegion Region, int Order, double? Proportion)? Locate(IRootDock layout, IDockable tool)
        {
            // Resolve the container by searching DOWN from the live root — never via tool.Owner, which
            // goes stale on closed/orphaned dockables (a discarded ToolDock keeps pointing at its old
            // parent) and would otherwise report a closed tool as still open.
            var container = FindContainer(layout, tool);
            if (container is null)
                return null; // not in the live tree → closed

            // Documents (Object Browser / Translation Editor) only round-trip in the document region.
            if (tool is Document)
                return container is IDocumentDock && KeyOf(tool) is { } dk
                    ? (DockRegion.Document, Home[dk].Order, (double?)null)
                    : null;

            if (container is IToolDock td && FindContainer(layout, td) is { } parent)
            {
                double? prop = double.IsNaN(td.Proportion) ? null : td.Proportion;

                if (ReferenceEquals(parent, layout.DefaultDockable) && td.Alignment == Alignment.Left)
                    return (DockRegion.Left, td.VisibleDockables!.IndexOf(tool), prop);

                var ctx = parent.Context as string;
                if (ctx == nameof(RightDock))
                    return (DockRegion.Right, ToolDockIndex(parent, td), prop);
                if (ctx == nameof(MiddleDock))
                    return (DockRegion.Bottom, ToolDockIndex(parent, td), prop);
            }

            if (container is IToolDock splitTool)
            {
                IDockable branch = splitTool;
                while (FindContainer(layout, branch) is { } ancestor && !ReferenceEquals(ancestor, layout.DefaultDockable))
                    branch = ancestor;
                var siblings = (layout.DefaultDockable as IDock)?.VisibleDockables;
                var documentBranch = siblings?.FirstOrDefault(d => Find<IDocumentDock>(d, _ => true) != null);
                if (siblings != null && documentBranch != null && !ReferenceEquals(branch, documentBranch)
                    && (!ReferenceEquals(branch, splitTool) || splitTool.Alignment != Alignment.Unset))
                    return (siblings.IndexOf(branch) < siblings.IndexOf(documentBranch) ? DockRegion.Left : DockRegion.Right,
                        container.VisibleDockables!.IndexOf(tool),
                        double.IsNaN(splitTool.Proportion) ? null : splitTool.Proportion);
            }

            Log.Debug("DockFactory: tool {Key} in an unrecognized dock; snapping to its default region", KeyOf(tool));
            return KeyOf(tool) is { } key && Home.TryGetValue(key, out var home)
                ? (home.Region, home.Order, home.Proportion)
                : null;
        }

        // The IDock that directly contains 'target' in the live tree, or null if not present.
        private static IDock? FindContainer(IDockable root, IDockable target)
        {
            if (root is IDock dock && dock.VisibleDockables != null)
            {
                foreach (var child in dock.VisibleDockables)
                {
                    if (ReferenceEquals(child, target))
                        return dock;
                    if (FindContainer(child, target) is { } found)
                        return found;
                }
            }
            return null;
        }

        private static int ToolDockIndex(IDock parent, IToolDock td)
        {
            int i = 0;
            foreach (var c in parent.VisibleDockables ?? new List<IDockable>())
            {
                if (ReferenceEquals(c, td)) return i;
                if (c is IToolDock) i++;
            }
            return i;
        }

        // ---- runtime open: place a tool into its home region, restoring its slot ----

        public void RegisterAddinTool(IRootDock layout, AddinToolWindowViewModel tool)
        {
            var key = "addin:" + tool.Id;
            toolsByKey[key] = tool;
            tool.KeepSavedWidth = deferredTools.GetValueOrDefault(key)?.Proportion is not null;
            if (!deferredTools.TryGetValue(key, out var saved) || saved.Open)
                PlaceTool(layout, tool, rightFallback: true);
        }

        public void PlaceTool(IRootDock layout, Tool tool, bool rightFallback)
        {
            if (tool is AddinToolWindowViewModel)
                toolsByKey["addin:" + tool.Id] = tool;
            var key = KeyOf(tool);
            if (key != null && closedSlots.Remove(key, out var path) && path.Count > 0)
            {
                var leaf = path[0];
                if (!ReferenceEquals(leaf.Child, tool))
                    path[0] = leaf with
                    {
                        Child = tool,
                        Siblings = leaf.Siblings.Select(s => ReferenceEquals(s.Dock, leaf.Child)
                            ? ((IDockable)tool, s.Proportion) : s).ToArray(),
                    };
                foreach (var slot in path.AsEnumerable().Reverse())
                {
                    var list = slot.Parent.VisibleDockables ??= CreateList<IDockable>();
                    if (!list.Contains(slot.Child))
                    {
                        var index = Math.Min(slot.Index, list.Count);
                        if (slot.Parent is IProportionalDock && list.Count > 0)
                        {
                            list.Insert(index, new ProportionalDockSplitter());
                            if (index == list.Count - 1) index++;
                        }
                        list.Insert(index, slot.Child);
                    }
                    foreach (var (sibling, siblingProportion) in slot.Siblings)
                        ApplyProportion(sibling, siblingProportion);
                    slot.Parent.ActiveDockable = slot.Child;
                }
                InitDockable(path[^1].Parent, path[^1].Parent.Owner);
                SetFocusedDockable((IDock)tool.Owner!, tool);
                RefreshLayout(layout);
                LayoutChanged?.Invoke();
                return;
            }
            if (key != null && pendingToolDocks.Remove(key, out var pending) && FindContainer(layout, pending) != null)
            {
                pending.VisibleDockables ??= CreateList<IDockable>();
                var group = pendingToolGroups[pending];
                var tabOrder = group.Tools.ToList();
                var index = pending.VisibleDockables.TakeWhile(t => tabOrder.IndexOf(KeyOf(t)!) < tabOrder.IndexOf(key)).Count();
                pending.VisibleDockables.Insert(index, tool);
                if (group.ActiveTool == key || pending.ActiveDockable == null)
                    pending.ActiveDockable = tool;
                if (!pendingToolDocks.Values.Contains(pending))
                    pendingToolGroups.Remove(pending);
                // The first layout pass can collapse an empty add-in slot before registration.
                IDockable branch = pending;
                while (FindContainer(layout, branch) is { } parent)
                {
                    foreach (var sibling in parent.VisibleDockables ?? CreateList<IDockable>())
                        if (restoredProportions.TryGetValue(sibling, out var savedProportion))
                            ApplyProportion(sibling, savedProportion);
                    branch = parent;
                }
                InitDockable(pending, pending.Owner);
                RefreshLayout(layout);
                LayoutChanged?.Invoke();
                return;
            }
            var placement = key != null ? lastPlacements.GetValueOrDefault(key) ?? deferredTools.GetValueOrDefault(key) ?? Home.GetValueOrDefault(key) : null;
            var region = placement?.Region ?? (rightFallback ? DockRegion.Right : DockRegion.Bottom);
            var order = placement?.Order ?? int.MaxValue;
            var proportion = placement?.Proportion;

            switch (region)
            {
                case DockRegion.Left:
                {
                    var left = GetOrCreateLeftDock(layout);
                    left.VisibleDockables ??= CreateList<IDockable>();
                    left.VisibleDockables.Insert(Math.Min(order, left.VisibleDockables.Count), tool);
                    left.ActiveDockable = tool;
                    // Initialize after adding the tool so Dock does not retain IsEmpty = true.
                    InitDockable(left, layout.DefaultDockable);
                    SetFocusedDockable(left, tool);
                    break;
                }
                case DockRegion.Right:
                {
                    var rd = GetOrCreateRightDock(layout);
                    var td = MakeToolDock(tool, Alignment.Top, proportion ?? FairProportion(rd));
                    InsertToolDock(rd, td, order, documentFirst: false);
                    InitDockable(td, rd);
                    SetFocusedDockable(td, tool);
                    break;
                }
                case DockRegion.Bottom:
                default:
                {
                    if (Find<IProportionalDock>(layout, d => (d.Context as string) == nameof(MiddleDock)) is not { } mid)
                        return;
                    var td = MakeToolDock(tool, Alignment.Bottom, proportion ?? FairProportion(mid) ?? 0.3);
                    InsertToolDock(mid, td, order, documentFirst: true);
                    InitDockable(td, mid);
                    SetFocusedDockable(td, tool);
                    break;
                }
            }

            RefreshLayout(layout);
            LayoutChanged?.Invoke();
        }

        // Recreate the proportional presenters: Dock keeps local sizing values on them which
        // override later model updates. The document dock and its tab collection stay intact.
        private void RefreshLayout(IRootDock layout)
        {
            if (layout.DefaultDockable is IDock main)
                main.VisibleDockables = CreateList(main.VisibleDockables?.ToArray() ?? []);
        }

        // Dock prefers its cached collapsed share during layout, even after Proportion changes.
        private static void ApplyProportion(IDockable dock, double proportion)
        {
            dock.Proportion = proportion;
            dock.CollapsedProportion = proportion;
        }

        // Reserve the closed tool's share of a stack that has since expanded to fill its space.
        private static void ReserveProportion(IDock container, double proportion)
        {
            var siblings = container.VisibleDockables?.Where(d => d is not IProportionalDockSplitter).ToArray() ?? [];
            if (proportion <= 0 || proportion >= 1 || double.IsNaN(proportion))
                return;
            if (siblings.Any(d => double.IsNaN(d.Proportion)))
            {
                foreach (var sibling in siblings)
                    ApplyProportion(sibling, sibling.Proportion);
                return;
            }
            var total = siblings.Sum(d => d.Proportion);
            if (total > 0)
                foreach (var sibling in siblings)
                    ApplyProportion(sibling, sibling.Proportion * (1 - proportion) / total);
        }

        // Insert a single-tool dock into a stack at its home-order position, with a separating splitter.
        private void InsertToolDock(IDock container, IToolDock td, int order, bool documentFirst)
        {
            container.VisibleDockables ??= CreateList<IDockable>();
            var list = container.VisibleDockables;

            int idx = list.Count;
            for (int i = 0; i < list.Count; i++)
                if (list[i] is IToolDock ex && HomeOrderOf(ex) > order) { idx = i; break; }

            // Never insert ahead of the editor DocumentDock in the middle region.
            if (documentFirst && idx == 0 && list.Count > 0 && list[0] is IDocumentDock)
                idx = 1;

            // VisibleDockables is the alternating sequence [dock, splitter, dock, splitter, ...].
            // Front and mid insertions both prepend [splitter, dock] at the target dock's index
            // (yielding [td, splitter, next]); only the empty/append cases differ.
            if (list.Count == 0)
                list.Add(td);
            else if (idx >= list.Count)
            {
                list.Add(new ProportionalDockSplitter());
                list.Add(td);
            }
            else
            {
                list.Insert(idx, new ProportionalDockSplitter());
                list.Insert(idx, td);
            }
        }

        private ProportionalDock GetOrCreateRightDock(IRootDock layout)
        {
            if (Find<IProportionalDock>(layout, d => (d.Context as string) == nameof(RightDock)) is ProportionalDock existing)
            {
                RightDock = existing;
                return existing;
            }
            var main = (IDock)layout.DefaultDockable!;
            var rd = new ProportionalDock
            {
                Orientation = Orientation.Vertical,
                Proportion = rightProportion,
                CanClose = false,
                CanFloat = false,
                IsCollapsable = false,
                Context = nameof(RightDock),
                VisibleDockables = CreateList<IDockable>(),
            };
            main.VisibleDockables ??= CreateList<IDockable>();
            main.VisibleDockables.Add(new ProportionalDockSplitter());
            main.VisibleDockables.Add(rd);
            InitDockable(rd, main);
            RightDock = rd;
            return rd;
        }

        private IToolDock GetOrCreateLeftDock(IRootDock layout)
        {
            var main = (IDock)layout.DefaultDockable!;
            if (main.VisibleDockables?.OfType<IToolDock>().FirstOrDefault(d => d.Alignment == Alignment.Left) is { } existing)
                return existing;
            var left = CreateToolDock();
            left.Alignment = Alignment.Left;
            left.Proportion = leftProportion;
            left.VisibleDockables = CreateList<IDockable>();
            main.VisibleDockables ??= CreateList<IDockable>();
            ReserveProportion(main, left.Proportion);
            main.VisibleDockables.Insert(0, new ProportionalDockSplitter());
            main.VisibleDockables.Insert(0, left);
            return left;
        }

        // ---- helpers ----

        public string? KeyOf(IDockable dockable)
        {
            foreach (var kv in toolsByKey)
                if (ReferenceEquals(kv.Value, dockable)) return kv.Key;
            return null;
        }

        private int HomeOrderOf(IDockable dockable) =>
            dockable is IToolDock { ActiveDockable: { } active } && KeyOf(active) is { } k
                ? savedOrders.GetValueOrDefault(k, Home.GetValueOrDefault(k)?.Order ?? int.MaxValue) : int.MaxValue;

        private static T? Find<T>(IDockable node, Func<T, bool> predicate) where T : class, IDockable
        {
            if (node is T t && predicate(t))
                return t;
            if (node is IDock dock && dock.VisibleDockables != null)
                foreach (var child in dock.VisibleDockables)
                    if (Find(child, predicate) is { } found)
                        return found;
            return null;
        }
    }

    private readonly HexIDE.Debugging.IBreakpointService breakpointService;
    private readonly HexIDE.Runtime.Debugging.IDebugController debugController;

    public MainViewViewModel(IWindowManager windowManager,
        ToolBoxToolViewModel toolBox,
        PropertiesToolViewModel properties,
        ImmediateToolViewModel immediate,
        FormLayoutToolViewModel formLayout,
        LocalsToolViewModel locals,
        WatchesToolViewModel watches,
        CallStackToolViewModel callStack,
        ProjectToolViewModel projectExplorer,
        ColorPaletteToolViewModel colorPalette,
        ObjectBrowserToolViewModel objectBrowser,
        TranslationEditorViewModel translationEditor,
        LanguageServersToolViewModel languageServers,
        HexIDE.Tools.ProtocolInspector.ProtocolInspectorToolViewModel protocolInspector,
        IProjectManager projectManager,
        IFocusedProjectUtil focusedProjectUtil,
        IProjectService projectService,
        IEditorService editorService,
        IDocumentDockService documentDockService,
        DockFactory dockFactory,
        IProjectRunnerService projectRunnerService,
        IEventBus eventBus,
        IVb6ToolchainService vb6Toolchain,
        IRecentProjectsService recentProjectsService,
        IFindReplaceService findReplaceService,
        ISettingsService settingsService,
        IThemeService themeService,
        IKeymapService keymapService,
        ILanguageSwitchService languageSwitch,
        ILocalizationService localization,
        IAddinRegistry addinRegistry,
        AddinOptionsService addinOptionsService,
        IDeveloperModeService developerModeService,
        IStatusBarService statusBarService,
        IPersonalityService personalityService,
        AddinMenuService addinMenuService,
        AddinCommandService addinCommandService,
        AddinToolWindowService addinToolWindowService,
        IWindowStateService windowStateService,
        HexIDE.Debugging.IBreakpointService breakpointService,
        HexIDE.Runtime.Debugging.IDebugController debugController,
        RecentEditsToolViewModel recentEdits,
        HexIDE.Debugging.ExternalDebugService? externalDebugger = null)
    {
        RecentEdits = recentEdits;
        Personality = personalityService;
        AddinMenuService = addinMenuService;
        AddinCommandService = addinCommandService;
        _addinToolWindowService = addinToolWindowService;
        addinToolWindowService.ToolRegistered   += tool => dockFactory.RegisterAddinTool(Layout, tool);
        addinToolWindowService.ShowRequested    += tool => OpenOrActivateTool(tool, true);
        addinToolWindowService.ToolUnregistered += tool =>
        {
            if (IsToolInLayout(tool)) CloseTool(tool);
        };
        this.windowManager = windowManager;
        this.projectService = projectService;
        this.documentDockService = documentDockService;
        this.dockFactory = dockFactory;
        this.projectRunnerService = projectRunnerService;
        this.eventBus = eventBus;
        this.vb6Toolchain = vb6Toolchain;
        this.recentProjectsService = recentProjectsService;
        this.findReplaceService = findReplaceService;
        this.settingsService = settingsService;
        this.themeService = themeService;
        this.keymapService = keymapService;
        this.languageSwitch = languageSwitch;
        this.localization = localization;
        this.addinRegistry = addinRegistry;
        this.addinOptionsService = addinOptionsService;
        this.developerModeService = developerModeService;
        this.projectManager = projectManager;
        this.editorService = editorService;
        this.breakpointService = breakpointService;
        this.debugController = debugController;
        ExternalDebugger = externalDebugger;
        StartExternalDebugCommand = new DelegateCommand(() => StartExternalDebugAsync().ListenErrors(),
            () => externalDebugger is not null && !projectRunnerService.IsRunning);
        // When the interpreter breaks, bring the offending form/module's code editor forward. The editor view then
        // paints the amber current-statement bar and scrolls to it (it reads the controller's CurrentStop on open).
        debugController.Stopped += info => RevealBreak(info.Module);
        StatusBar = statusBarService;
        debugController.Stopped += _ => Dispatcher.UIThread.Post(UpdateRunState);
        debugController.Continued += () => Dispatcher.UIThread.Post(UpdateRunState);
        ToolBox = toolBox;
        Properties = properties;
        Immediate = immediate;
        FormLayout = formLayout;
        Locals = locals;
        Watches = watches;
        CallStack = callStack;
        callStack.FrameShown += () =>
        {
            if (callStack.ShownFrame?.Module is { } module) RevealBreak(module);
        };
        ProjectExplorer = projectExplorer;
        ColorPalette = colorPalette;
        ObjectBrowser = objectBrowser;
        TranslationEditor = translationEditor;
        LanguageServers = languageServers;
        OpenLanguageServersCommand = new DelegateCommand(OpenLanguageServers);
        ProtocolInspector = protocolInspector;
        OpenProtocolInspectorCommand = new DelegateCommand(OpenProtocolInspector);
        FocusedProjectUtil = focusedProjectUtil;

        this.windowStateService = windowStateService;

        Layout = dockFactory.CreateLayout();
        dockFactory.InitLayout(Layout);
        documentDockService.TrackTabs();
        ReopenClosedDocumentCommand = new DelegateCommand(
            () => documentDockService.ReopenClosedDocument(editorService),
            () => documentDockService.RecentlyClosed?.Entries.Count > 0);
        if (documentDockService.RecentlyClosed is { } history)
            ((System.Collections.Specialized.INotifyCollectionChanged)history.Entries).CollectionChanged += (_, _) =>
            {
                ReopenClosedDocumentCommand.RaiseCanExecutedChanged();
                ClosedDocumentMenuItems.Clear();
                foreach (var entry in history.Entries)
                    ClosedDocumentMenuItems.Add(new RecentProjectItemViewModel(entry.Title,
                        new DelegateCommand(() => documentDockService.ReopenClosedDocument(editorService, entry))));
            };

        // Eager-save wiring: baseline the just-restored layout (so a no-op gesture won't rewrite it),
        // debounce changes ~400ms, and listen for programmatic open/close. Mouse-driven changes
        // (splitter resize, drag-dock) are funnelled in by the view via ScheduleLayoutSave().
        _layoutSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _layoutSaveTimer.Tick += (_, _) => LoggedTimerCallback.Run("dock-layout save", FlushLayoutSaveDebounced);
        _lastSavedLayoutJson = LayoutManifestJson.Serialize(dockFactory.CaptureManifest(Layout));
        dockFactory.LayoutChanged += ScheduleLayoutSave;

        VBWindowContext.RunTimeError += (form, e) =>
        {
            // Recorded BEFORE the dialog, and deliberately not tied to its lifetime: stop_project and
            // shutdown_ide both close open dialogs, so anything that only exists while the modal is up is
            // gone by the time a caller looks. (docs/mcp-server-gaps.md)
            ReportRuntimeError(RunErrorText.For(e), HexIDE.Runtime.Interpreter.ErrorLocation.Of(e));
        };

        // A startup form that cannot be built is reported the way a runtime error is, so the person sees it and
        // get_last_runtime_error returns it. It used to reach only the log, while F5 appeared to do nothing (#590).
        projectRunnerService.StartFailed += ReportEndedRun;

        // So is a run that something ended: a construct HexIDE does not implement, a fault in the engine, or an
        // error Sub Main did not handle. Those reached only the log or an unrecorded message box (#754).
        projectRunnerService.RunFailed += ReportEndedRun;

        void ReportRuntimeError(string message, HexIDE.Runtime.Interpreter.ErrorLocation? where)
        {
            RuntimeErrors.Record(message, where);
            EndIfAsked(new RuntimeErrorViewModel(message)).ListenErrors();
        }

        // Nothing of a run that failed to start, or that something ended, is still running when it is reported.
        // So End on its dialog has nothing to end, and its answer is not acted on. Acting on it would end whatever
        // run is live when the dialog closes, which can be a later one started meanwhile by run_project.
        void ReportEndedRun(string message)
        {
            RuntimeErrors.Record(message, projectRunnerService.FailureLocation);
            windowManager.ShowDialog(new RuntimeErrorViewModel(message)).ListenErrors();
        }

        // End on this dialog ends the run, as VB6's does. It used to close the dialog and leave the program
        // running (#600). The dialog answers true only for End. The run may have ended while the dialog was up,
        // so the runner is asked first.
        async Task EndIfAsked(RuntimeErrorViewModel vm)
        {
            if (await windowManager.ShowDialog(vm) && projectRunnerService.CanEndProject)
                projectRunnerService.EndProject();
        }

        // A run-time error nothing handles halts the program at the statement that raised it, as VB6's IDE does (#1011).
        // The dialog offers End and Debug, and nothing runs behind it. Debug breaks there with the procedure still live,
        // and Continue runs the statement again. Recorded before the dialog, as every run-time error is. A dialog closed
        // without an answer, as stop_project and shutdown_ide close it, takes Debug, which loses nothing: what VB6 does
        // then is not measured.
        debugController.HaltHost = async error =>
        {
            var message = RunErrorText.For(error);
            RuntimeErrors.Record(message, HexIDE.Runtime.Interpreter.ErrorLocation.Of(error));
            if (!await windowManager.ShowDialog(new RuntimeErrorViewModel(message, canDebug: true)))
                return true;
            if (projectRunnerService.CanEndProject)
                projectRunnerService.EndProject();
            return false;
        };

        // A compile error met while a form runs, such as a call to a Sub that does not exist, reached from a click.
        // Reported as a run-time error is: recorded, so get_last_runtime_error sees it, and shown in the dialog
        // whose End ends the run. It was a message box nothing recorded (#759). One route for every error a run
        // meets, rather than VB6's separate compile-error box with OK and Help, because #754 already sends Sub
        // Main's compile errors through this dialog and two routes would answer the same error differently.
        // The run carries on, with the handler abandoned, as it does after a run-time error.
        VBWindowContext.CompileError += (_, e) => ReportRuntimeError(e.Message, HexIDE.Runtime.Interpreter.ErrorLocation.Of(e));

        // Route `Debug.Print` output from the running program into the Immediate pane (VB6's Immediate window), as it is
        // written, so a line a statement left open shows at once and the next Debug.Print continues it (#1380).
        VBDebugConsole.Written += text =>
        {
            Dispatcher.UIThread.Post(() =>
                Immediate.Document.Insert(Immediate.Document.TextLength, text.Replace("\r\n", "\n")));
        };

        if (externalDebugger is not null)
        {
            externalDebugger.Output += text => Immediate.Document.Insert(Immediate.Document.TextLength, text);
            externalDebugger.Failed += message =>
            {
                Immediate.Document.Insert(Immediate.Document.TextLength, message + "\n");
                OpenImmediateTool();
            };
            HexIDE.Dap.DapStop? revealed = null;
            int? revealedFrame = null;
            externalDebugger.Changed += () =>
            {
                if (externalDebugger.Stop is not { } stop || externalDebugger.SelectedFrame is not { } frame)
                { revealed = null; revealedFrame = null; return; }
                var frameId = frame.GetProperty("id").GetInt32();
                if (stop == revealed && frameId == revealedFrame) return;
                if (stop != revealed) { OpenLocalsTool(); OpenCallStackTool(); }
                revealed = stop;
                revealedFrame = frameId;
                if (externalDebugger.SelectedLocation is { } location)
                    editorService.NavigateTo(location.Uri, location.Line, 1);
            };
        }

        RemoveDocumentCommand = new DelegateCommand(() => RemoveActiveDocument().ListenErrors(),
            () => RemovableDocument != null);
        ProjectExplorer.ObservePropertyChanged(x => x.SelectedItem).Subscribe(_ => UpdateRemoveDocumentMenu());
        SaveFormCommand = new DelegateCommand(() => SaveActiveForm(false),
            () => FocusedProjectUtil.FocusedForm != null);
        SaveFormAsCommand = new DelegateCommand(() => SaveActiveForm(true),
            () => FocusedProjectUtil.FocusedForm != null);

        // F5 doubles as Continue in break mode (VB6 semantics): resume if paused, otherwise start.
        StartDefaultProjectCommand = new DelegateCommand(
            () =>
            {
                if (projectRunnerService.CanContinueProject)
                    projectRunnerService.ContinueProject();
                else
                    projectRunnerService.RunStartupProject();
            },
            () => projectRunnerService.CanStartDefaultProject || projectRunnerService.CanContinueProject);
        StartDefaultProjectWithFullCompileCommand = new DelegateCommand(() => projectRunnerService.RunStartupProject(),
            () => projectRunnerService.CanStartDefaultProjectWithFullCompile);
        BreakProjectCommand = new DelegateCommand(projectRunnerService.BreakCurrentProject,
            () => projectRunnerService.CanBreakProject);
        EndProjectCommand = new DelegateCommand(projectRunnerService.EndProject,
            () => projectRunnerService.CanEndProject);
        RestartProjectCommand = new DelegateCommand(projectRunnerService.RestartProject,
            () => projectRunnerService.CanRestartProject);
        ProjectReferencesCommand = new DelegateCommand(() => projectService.EditProjectReferences(FocusedProjectUtil.FocusedOrStartupProject!),
            () => FocusedProjectUtil.FocusedOrStartupProject != null);
        ProjectComponentsCommand = new DelegateCommand(() => projectService.EditProjectComponents(FocusedProjectUtil.FocusedOrStartupProject!),
            () => FocusedProjectUtil.FocusedOrStartupProject != null);
        ProjectPropertiesCommand = new DelegateCommand(() => projectService.EditProjectProperties(FocusedProjectUtil.FocusedOrStartupProject!),
            () => FocusedProjectUtil.FocusedOrStartupProject != null);
        MakeProjectCommand = new DelegateCommand(() => projectService.MakeProject(FocusedProjectUtil.FocusedOrStartupProject!).ListenErrors(),
            () => FocusedProjectUtil.FocusedOrStartupProject != null);
        RemoveProjectCommand = new DelegateCommand(() => projectService.UnloadProject(FocusedProjectUtil.FocusedOrStartupProject!).ListenErrors(),
            () => FocusedProjectUtil.FocusedOrStartupProject != null);
        // Offered only with a group open, as VB6 offers it (measured, #746). Two projects loaded are a group, even before
        // HexIDE has made its Group1.
        MakeProjectGroupCommand = new DelegateCommand(MakeProjectGroup,
            () => projectManager.CurrentGroup != null || projectManager.LoadedProjects.Count > 1);
        projectManager.ProjectLoaded += _ => MakeProjectGroupCommand.RaiseCanExecutedChanged();
        projectManager.ProjectUnloaded += _ => MakeProjectGroupCommand.RaiseCanExecutedChanged();
        RunWithVb6Command = new DelegateCommand(
            () => vb6Toolchain.RunWithVb6Async(FocusedProjectUtil.FocusedOrStartupProject!).ListenErrors(),
            () => FocusedProjectUtil.FocusedOrStartupProject != null && vb6Toolchain.IsAvailable);
        MakeWithVb6Command = new DelegateCommand(
            () => vb6Toolchain.MakeWithVb6Async(FocusedProjectUtil.FocusedOrStartupProject!).ListenErrors(),
            () => FocusedProjectUtil.FocusedOrStartupProject != null && vb6Toolchain.IsAvailable);

        AddFormCommand = new DelegateCommand(
            () => AddFormAsync().ListenErrors(),
            () => FocusedProjectUtil.FocusedOrStartupProject != null);
        AddModuleCommand = new DelegateCommand(
            () => AddModuleAsync(ModuleKind.StandardModule).ListenErrors(),
            () => FocusedProjectUtil.FocusedOrStartupProject != null);
        AddClassModuleCommand = new DelegateCommand(
            () => AddModuleAsync(ModuleKind.ClassModule).ListenErrors(),
            () => FocusedProjectUtil.FocusedOrStartupProject != null);
        AddUserControlCommand = new DelegateCommand(
            () => AddUserControlAsync().ListenErrors(),
            () => FocusedProjectUtil.FocusedOrStartupProject != null);
        AddPropertyPageCommand = new DelegateCommand(
            () => AddPropertyPageAsync().ListenErrors(),
            () => FocusedProjectUtil.FocusedOrStartupProject != null);
        AddFileCommand = new DelegateCommand(
            () => AddFileAsync().ListenErrors(),
            () => FocusedProjectUtil.FocusedOrStartupProject != null);

        bool CanSearchActiveDocument() =>
            documentDockService.ActiveDocument is HexIDE.Forms.ViewModels.ISearchableDocument;

        FindInCodeCommand = new DelegateCommand(findReplaceService.ShowFind, CanSearchActiveDocument);
        ReplaceInCodeCommand = new DelegateCommand(findReplaceService.ShowReplace, CanSearchActiveDocument);
        FindNextInCodeCommand = new DelegateCommand(findReplaceService.FindNext, CanSearchActiveDocument);

        FocusedProjectUtil.ObservePropertyChanged(x => x.FocusedOrStartupProject)
            .Subscribe(_ =>
            {
                ProjectReferencesCommand.RaiseCanExecutedChanged();
                ProjectComponentsCommand.RaiseCanExecutedChanged();
                ProjectPropertiesCommand.RaiseCanExecutedChanged();
                MakeProjectCommand.RaiseCanExecutedChanged();
                RemoveProjectCommand.RaiseCanExecutedChanged();
                RunWithVb6Command.RaiseCanExecutedChanged();
                MakeWithVb6Command.RaiseCanExecutedChanged();
                AddFormCommand.RaiseCanExecutedChanged();
                AddModuleCommand.RaiseCanExecutedChanged();
                AddClassModuleCommand.RaiseCanExecutedChanged();
                AddUserControlCommand.RaiseCanExecutedChanged();
                AddPropertyPageCommand.RaiseCanExecutedChanged();
                AddFileCommand.RaiseCanExecutedChanged();
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(WindowTitle));
                RaiseDynamicMenuHeaders();
            });

        projectManager.ObservePropertyChanged(x => x.CurrentGroup)
            .Subscribe(_ =>
            {
                MakeProjectGroupCommand.RaiseCanExecutedChanged();
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(WindowTitle));
                RaiseDynamicMenuHeaders();
            });

        projectRunnerService.ObservePropertyChanged(x => x.CanEndProject)
            .Subscribe(_ => EndProjectCommand.RaiseCanExecutedChanged());

        projectRunnerService.ObservePropertyChanged(x => x.IsRunning)
            .Subscribe(_ =>
            {
                StartExternalDebugCommand.RaiseCanExecutedChanged();
                UpdateRunState();
            });

        FocusedProjectUtil.ObservePropertyChanged(x => x.FocusedOrStartupProject)
            .Subscribe(_ => RefreshRecentProjects());

        recentProjectsService.Changed += RefreshRecentProjects;

        RefreshRecentProjects();

        documentDockService.ObservePropertyChanged(x => x.ActiveDocument)
            .Subscribe(document =>
            {
                if (_activeDesigner != null && _activeDesignerUndoHandler != null)
                    _activeDesigner.UndoStack.Changed -= _activeDesignerUndoHandler;

                _activeDesigner = document as FormEditViewModel;
                _activeDesignerUndoHandler = null;

                if (_activeDesigner != null)
                {
                    _activeDesignerUndoHandler = UpdateUndoHeaders;
                    _activeDesigner.UndoStack.Changed += _activeDesignerUndoHandler;
                }
                UpdateUndoHeaders();

                UpdateRemoveDocumentMenu();

                // Whether Find has anything to search is a property of the active document, so this is
                // the only moment at which the answer can change.
                FindInCodeCommand.RaiseCanExecutedChanged();
                ReplaceInCodeCommand.RaiseCanExecutedChanged();
                FindNextInCodeCommand.RaiseCanExecutedChanged();
            });

        // The Save/Remove "<form>" headers depend on the focused form, not just the project.
        FocusedProjectUtil.ObservePropertyChanged(x => x.FocusedForm)
            .Subscribe(_ =>
            {
                RaiseDynamicMenuHeaders();
                SaveFormCommand.RaiseCanExecutedChanged();
                SaveFormAsCommand.RaiseCanExecutedChanged();
                CommandManager.InvalidateRequerySuggested();
            });

        // Re-localize every code-built header live when the language changes.
        localization.LanguageChanged += () =>
        {
            RaiseDynamicMenuHeaders();
            UpdateUndoHeaders();
            UpdateRunState();
        };

        // Options → Language → Customize commits-and-closes the modal dialog, then publishes this event so
        // the Translation Editor tab opens on the now-unblocked dock. MainViewViewModel is an app-lifetime
        // singleton, so the subscription never needs disposing.
        eventBus.Subscribe<OpenTranslationEditorEvent>(_ => OpenTranslationEditor());
        eventBus.Subscribe<OpenProtocolInspectorEvent>(e => OpenProtocolInspector(e.ConnectionId));
    }

    [Notify] private string undoHeader = "_Undo";
    [Notify] private string redoHeader = "_Redo";

    private FormEditViewModel? _activeDesigner;
    private System.Action? _activeDesignerUndoHandler;

    private void UpdateUndoHeaders()
    {
        UndoHeader = _activeDesigner is { CanUndo: true } d
            ? string.Format(localization.GetString("Str.Menu.Edit.UndoFormat"), d.UndoStack.UndoDescription)
            : localization.GetString("Str.Menu.Edit.Undo");
        RedoHeader = _activeDesigner is { CanRedo: true } d2
            ? string.Format(localization.GetString("Str.Menu.Edit.RedoFormat"), d2.UndoStack.RedoDescription)
            : localization.GetString("Str.Menu.Edit.Redo");
    }

    public void OnInitialized()
    {
        if (Static.StartupProjectHook is { } hook)
            hook(projectService, projectManager).ListenErrors();
        else if (settingsService.PromptForProjectOnStartup)
            projectService.CreateNewProject().ListenErrors();
    }

    public void Exit()
    {
        // Close the window rather than calling Shutdown(), so File > Exit goes through the same
        // Closing handler as the title-bar X — one interception point for the save-changes prompt.
        (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Close();
    }

    public void NYI()
    {
        windowManager.MessageBox("This feature is not yet implemented", "NYI", MessageBoxButtons.Ok, MessageBoxIcon.Information);
    }

    /// <summary>
    /// VB6's Make Project Group builds the group's projects, through a "Build the following projects" dialog (measured,
    /// #746). HexIDE has no build of a group yet (#1515), so it says so and writes nothing. It used to save every project
    /// and the group, which is Save Project Group's job, under a Make label.
    /// </summary>
    private void MakeProjectGroup()
    {
        Log.Information("Make Project Group: building a project group is not implemented yet (#1515); nothing is written");
        NYI();
    }

    private void SaveActiveForm(bool saveAs)
    {
        // Use the document's save path so form code keeps format-on-save.
        if (documentDockService.ActiveDocument is FormEditViewModel designer)
        {
            if (saveAs) designer.SaveFormAs();
            else designer.SaveForm();
        }
        else if (documentDockService.ActiveDocument is CodeEditorViewModel code)
        {
            if (saveAs) code.SaveFormAs();
            else code.SaveForm();
        }
    }

    public void SaveProject() => projectService.SaveAllProjects(false).ListenErrors();

    public void SaveProjectAs() => projectService.SaveAllProjects(true).ListenErrors();

    public void OpenProject() => projectService.OpenProject().ListenErrors();

    private void RefreshRecentProjects()
    {
        RecentProjects.Clear();
        var recent = recentProjectsService.GetRecent();
        for (var i = 0; i < recent.Count; i++)
        {
            var path = recent[i];
            var index = i + 1;
            var header = $"_{index} {path}";
            var cmd = new DelegateCommand(() => OpenRecentProject(path));
            RecentProjects.Add(new RecentProjectItemViewModel(header, cmd));
        }
        OnPropertyChanged(nameof(HasRecentProjects));
    }

    private void OpenRecentProject(string path)
        => OpenRecentProjectAsync(path).ListenErrors();

    private async Task OpenRecentProjectAsync(string path)
    {
        await projectService.UnloadAllProjects();
        await projectService.OpenProject(path);
    }

    /// <summary>
    /// Edit ▸ Find, Edit ▸ Replace and F3 — enabled only while a document Find can actually search is
    /// active.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Commands rather than the plain methods these were, purely so they can be <em>disabled</em>. VB6
    /// greyed Find out when no code window was active, and that is the fidelity-correct answer for the
    /// form designer, the Object Browser, the Translation Editor and the Language &amp; Debug Servers
    /// document alike: better than offering a dialog that opens, accepts a search term and does nothing
    /// with it (hexide-io/HexIDE#363).
    /// </para>
    /// <para>
    /// Not the whole answer, because the dialog is modeless: one opened over a code window survives a
    /// switch to a tab that has no text, and its own buttons stay live. That path is covered inside
    /// <c>FindReplaceViewModel</c>, which says so rather than returning.
    /// </para>
    /// </remarks>
    public DelegateCommand FindInCodeCommand { get; }

    public DelegateCommand ReplaceInCodeCommand { get; }

    public DelegateCommand FindNextInCodeCommand { get; }

    public DelegateCommand MakeProjectCommand { get; }

    public DelegateCommand RemoveProjectCommand { get; }

    public DelegateCommand MakeProjectGroupCommand { get; }

    public DelegateCommand RunWithVb6Command { get; }

    public DelegateCommand MakeWithVb6Command { get; }

    public void MakeProject() => projectService.MakeProject().ListenErrors();

    public void OpenOptions() => OpenOptionsAt(null);

    public void OpenAddInManager() => OpenOptionsAt("Str.Options.Node.AddIns");

    private void OpenOptionsAt(string? initialNodeKey) => windowManager.ShowDialog(new OptionsViewModel(settingsService, themeService, keymapService, languageSwitch, addinRegistry, addinOptionsService, developerModeService, localization, eventBus, initialNodeKey));

    public void About()
    {
        windowManager.ShowAbout(new AboutDialogOptions()
        {
            Copyright = "© 2026 The HexIDE Authors. Not affiliated with, endorsed by, or sponsored by Microsoft.",
            Title = $"HexIDE {BuildInfo.Display}",
            SubTitle = "A cross-platform, VB6-compatible IDE",
            Icon = new Bitmap(AssetLoader.Open(new Uri("avares://HexIDE/Icons/AppIcon/hexide-logo-64.png")))
        }).ListenErrors();
    }

    public async Task NewProject()
    {
        await projectService.UnloadAllProjects();
        await projectService.CreateNewProject();
    }

    public async Task AddProject(IProjectTemplate? projectTemplate)
    {
        if (projectTemplate == null)
            await projectService.CreateNewProject();
        else
            await projectService.CreateNewProject(projectTemplate);
    }

    private void DebuggingNotImplementedYet() =>
        windowManager.MessageBox("Debugging is not yet implemented. Maybe one day?", icon: MessageBoxIcon.Information).ListenErrors();

    // F8 (Step Into): from idle, start + break at the first statement; while paused, step one statement; while
    // running, break at the next. StepIntoProject handles all three states, so this is safe to invoke any time.
    public void StepInto() => projectRunnerService.StepIntoProject();
    public void StepOver() => projectRunnerService.StepOverProject();
    public void StepOut() => projectRunnerService.StepOutProject();
    // Run To Cursor (Ctrl+F8): run (starting the project if idle, else continuing) until the active editor's caret
    // line, then break — a one-shot temp breakpoint.
    public void RunToCursor()
    {
        if (documentDockService.ActiveDocument is HexIDE.Forms.ViewModels.CodeEditorViewModel code)
        {
            var line = code.Document.GetLineByOffset(code.CaretOffset).LineNumber;
            projectRunnerService.RunToCursorProject(code.Identity, line);
        }
    }

    // Debug → Add Watch: open the Add Watch dialog (new, empty). Edit Watch (Ctrl+W): re-open it for the Watches
    // window's selected row. Quick Watch (Shift+F9): open it pre-filled with the identifier under the caret.
    public void AddWatch() => Watches.OpenAddWatchDialog().ListenErrors();
    public void EditWatch() => Watches.EditSelected().ListenErrors();

    public void QuickWatch()
    {
        string? expr = documentDockService.ActiveDocument is HexIDE.Forms.ViewModels.CodeEditorViewModel code
            ? IdentifierAt(code.Document.Text, code.CaretOffset)
            : null;
        Watches.OpenAddWatchDialog(string.IsNullOrWhiteSpace(expr) ? null : expr).ListenErrors();
    }

    // The run of identifier characters (letters / digits / _ / .) surrounding an offset — the word under the caret,
    // including a simple member access like "obj.Field". Null when the caret isn't on an identifier.
    private static string? IdentifierAt(string text, int offset)
    {
        if (string.IsNullOrEmpty(text) || offset < 0 || offset > text.Length)
            return null;
        static bool IsIdent(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '.';
        int start = offset, end = offset;
        while (start > 0 && IsIdent(text[start - 1])) start--;
        while (end < text.Length && IsIdent(text[end])) end++;
        return end > start ? text[start..end] : null;
    }

    // Toggle a breakpoint on the active code editor's caret line (1-based), through the editor so a read-only line
    // is refused (#273 task 3.12). The gutter margin repaints itself via IBreakpointService.BreakpointsChanged, and
    // a live run picks it up immediately (ProjectRunnerService pushes it).
    public void ToggleBreakpoint()
    {
        if (documentDockService.ActiveDocument is HexIDE.Forms.ViewModels.CodeEditorViewModel code)
        {
            var line = code.Document.GetLineByOffset(code.CaretOffset).LineNumber;
            code.ToggleBreakpoint(line);
        }
    }

    public void ClearAllBreakpoints()
    {
        if (ExternalDebugger?.IsActive == true) ExternalDebugger.ClearAllBreakpoints();
        else breakpointService.ClearAll();
    }

    private bool RefuseExternalCommand()
    {
        if (ExternalDebugger?.IsActive != true) return false;
        windowManager.MessageBox(localization.GetString("Str.Debug.External.Unsupported"), icon: MessageBoxIcon.Information).ListenErrors();
        return true;
    }

    // Bring the code editor for the module the interpreter just broke in to the front (creating/activating its tab).
    private void RevealBreak(string module)
    {
        // The project that is running, which is not always the startup one — RunProject takes a project, and
        // a group's other project may hold a module of the same name.
        var project = projectRunnerService.RunningProject ?? projectManager.StartupProject;
        if (project is null)
            return;
        var form = project.Forms.FirstOrDefault(f => string.Equals(f.Name, module, StringComparison.OrdinalIgnoreCase));
        if (form is not null)
        {
            editorService.EditCode(form);
            return;
        }
        var mod = project.Modules.FirstOrDefault(m => string.Equals(m.Name, module, StringComparison.OrdinalIgnoreCase));
        if (mod is not null)
            editorService.EditCode(mod);
    }

    // Set Next Statement (Ctrl+F9): move the execution point to the active editor's caret line without running the
    // statements in between. TOP-LEVEL-body granularity only — a nested target (inside an If/For/Do/Select block), or
    // a move while paused inside such a block, is refused with a message. This is an INTERPRETER limit, not VB6's:
    // the top-level body is a pc-addressable loop, but nested blocks run via recursive C# descent that can't be
    // jumped into without a linearized CFG (a parked rewrite). See docs/debugger-vb6-divergences.md.
    public void SetNextStatement()
    {
        if (ExternalDebugger?.IsActive == true)
        {
            windowManager.MessageBox(localization.GetString("Str.Debug.External.Unsupported"), icon: MessageBoxIcon.Information).ListenErrors();
            return;
        }
        if (documentDockService.ActiveDocument is HexIDE.Forms.ViewModels.CodeEditorViewModel code)
        {
            var line = code.Document.GetLineByOffset(code.CaretOffset).LineNumber;
            // The definition's own name, not a tail read off a URI: a module named Utilities saved as
            // util.bas answers to "Utilities", and the file stem would name nothing the controller knows.
            if (!debugController.SetNextStatement(code.Identity.Name, line))
                windowManager.MessageBox(localization.GetString("Str.Debug.SetNextStatement.Refused"),
                    icon: MessageBoxIcon.Information).ListenErrors();
        }
    }

    // Show Next Statement (Debug menu): bring the paused module's current-statement line into view — RevealBreak
    // opens/activates that editor, which scrolls to and paints the amber current-statement bar (it reads the
    // controller's CurrentStop on open). A no-op when not paused.
    public void ShowNextStatement()
    {
        if (ExternalDebugger?.IsActive == true)
        {
            if (ExternalDebugger.CurrentLocation is { } location)
                editorService.NavigateTo(location.Uri, location.Line, 1);
            return;
        }
        if (debugController.CurrentStop is { } stop)
            RevealBreak(stop.Module);
    }

    private async Task StartExternalDebugAsync()
    {
        if (ExternalDebugger is null || projectRunnerService.IsRunning) return;
        var paths = await windowManager.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = localization.GetString("Str.Menu.Run.StartExternal"),
            AllowMultiple = false,
            FileTypeFilter = [new Avalonia.Platform.Storage.FilePickerFileType("JSON") { Patterns = ["*.json"] }],
        });
        if (paths?.FirstOrDefault() is not { } file || projectRunnerService.IsRunning) return;
        OpenImmediateTool();
        await ExternalDebugger.StartAsync(file);
    }

    public Task OpenGithubRepo()
    {
        TopLevel.GetTopLevel(Static.MainView)!.Launcher.LaunchUriAsync(new Uri("https://github.com/hexide-io/hexide")).ListenErrors();
        return Task.CompletedTask;
    }

    private async Task AddFormAsync()
    {
        if (FocusedProjectUtil.FocusedOrStartupProject is not { } p) return;
        var name = projectService.NextFreeFormName(p);
        var form = await projectService.AddNewForm(p, name);
        editorService.EditForm(form);
    }

    private async Task AddModuleAsync(ModuleKind kind)
    {
        if (FocusedProjectUtil.FocusedOrStartupProject is not { } p) return;
        var name = projectService.NextFreeModuleName(p, kind);
        var module = await projectService.AddNewModule(p, name, kind);
        editorService.EditCode(module);
    }

    private async Task AddUserControlAsync()
    {
        if (FocusedProjectUtil.FocusedOrStartupProject is not { } p) return;
        var name = projectService.NextFreeModuleName(p, ModuleKind.UserControl);
        var module = await projectService.AddNewUserControl(p, name);
        editorService.EditForm(module.FormPart);
    }

    private async Task AddPropertyPageAsync()
    {
        if (FocusedProjectUtil.FocusedOrStartupProject is not { } p) return;
        var name = projectService.NextFreeModuleName(p, ModuleKind.PropertyPage);
        var module = await projectService.AddNewPropertyPage(p, name);
        editorService.EditForm(module.FormPart);
    }

    /// <summary>
    /// Add File — adopts files that already exist on disk, rather than authoring new ones like the rest of
    /// the Add family.
    ///
    /// <para>
    /// Multi-select is on because VB6's own Add File dialog allowed it, and because the common shape of
    /// this gesture is "pull in the three files I already wrote", not one at a time.
    /// </para>
    /// </summary>
    private async Task AddFileAsync()
    {
        if (FocusedProjectUtil.FocusedOrStartupProject is not { } p) return;
        var files = await windowManager.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = localization.GetString("Str.AddItem.AddFile"),
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("VB6 Files")
                {
                    Patterns = ["*.bas", "*.cls", "*.frm", "*.ctl", "*.pag"],
                },
                new FilePickerFileType("All Files") { Patterns = ["*.*"] },
            ],
        });

        if (files == null) return;

        foreach (var path in files)
            await AddOneExistingFileAsync(p, path);
    }

    /// <summary>
    /// Adds one picked file and opens it in whichever editor suits what it turned out to be.
    ///
    /// <para>
    /// Classification is by extension, and everything unrecognised joins as a related document. That is the
    /// conservative direction on purpose: a related document is read and written verbatim, so mis-filing a
    /// VB6 file as one costs the developer a designer they have to add again — while mis-filing prose as
    /// source hands it to the header writer, which is the corruption in hexide-io/HexIDE#245.
    /// </para>
    /// </summary>
    private async Task AddOneExistingFileAsync(ProjectDefinition project, string path)
    {
        // Already carried? Open it instead of adding a second node for the same file. VB6 refused the add
        // outright; opening reads the gesture as what the developer evidently wants, and cannot produce two
        // tabs writing to one path.
        if (TryOpenAlreadyCarried(project, path)) return;

        var kind = ProjectFileClassifier.Classify(path);

        if (kind == ProjectFileKind.Form)
        {
            // A .frm that will not parse adds nothing at all, rather than a tree node with no form behind it.
            var adopted = await projectService.AddExistingForm(project, path);
            if (adopted.Document is { } form)
            {
                editorService.EditForm(form);
                await ReportReadByteForByte(adopted.UnreadableInCodePage, path);
            }
            else
                await ReportRefusedAdoption(adopted.Refusal, adopted.Name, path);
            return;
        }

        if (kind.AsModuleKind() is { } moduleKind)
        {
            var adopted = await projectService.AddExistingModule(project, path, moduleKind);
            if (adopted.Document is not { } module)
            {
                await ReportRefusedAdoption(adopted.Refusal, adopted.Name, path);
                return;
            }
            // A UserControl or PropertyPage opens in its designer, matching Add User Control; a .bas or
            // .cls has no designer half, so it opens as code.
            if (module.FormPart is { } formPart)
                editorService.EditForm(formPart);
            else
                editorService.EditCode(module);
            await ReportReadByteForByte(adopted.UnreadableInCodePage, path);
            return;
        }

        editorService.EditRelatedDocument(await projectService.AddExistingRelatedDocument(project, path));
    }

    /// <summary>
    /// Tells the developer a file joined the project read byte for byte, because this computer's code page
    /// could not read it. It saves back unchanged; the warning is about what they see in it.
    /// </summary>
    private async Task ReportReadByteForByte(int? unreadableInCodePage, string path)
    {
        if (unreadableInCodePage is not { } codePage)
            return;
        await windowManager.MessageBox(
            string.Format(localization.GetString("Str.Dialog.SourceEncoding.ReadByteForByte"),
                Path.GetFileName(path), codePage),
            icon: MessageBoxIcon.Warning);
    }

    /// <summary>
    /// Tells the developer why one of the files they picked did not join the project.
    /// </summary>
    /// <remarks>
    /// Named, both the file and the name it asked for, because Add File is multi-select: a refusal that
    /// says only "a file was refused" leaves the developer to work out which of five it meant. A file that
    /// could not be read stays a log line, as it was — it is a malformed file rather than a decision the
    /// developer can revisit by choosing a different name.
    /// </remarks>
    private async Task ReportRefusedAdoption(AdoptionRefusal refusal, string? name, string path)
    {
        if (refusal == AdoptionRefusal.CouldNotRead || name is null)
        {
            Log.Warning("Add File: {Path} could not be read as the kind it was classified as; not added", path);
            return;
        }

        var key = refusal == AdoptionRefusal.NameTaken
            ? "Str.Naming.Msg.AdoptedNameTaken"
            : "Str.Naming.Msg.AdoptedNameNotValid";
        await windowManager.MessageBox(
            string.Format(localization.GetString(key), name, Path.GetFileName(path)),
            icon: MessageBoxIcon.Warning);
    }

    private bool TryOpenAlreadyCarried(ProjectDefinition project, string path)
    {
        foreach (var form in project.Forms)
            if (SamePath(form.AbsolutePath, path))
            {
                editorService.EditForm(form);
                return true;
            }

        foreach (var module in project.Modules)
            if (SamePath(module.AbsolutePath, path))
            {
                if (module.FormPart is { } formPart) editorService.EditForm(formPart);
                else editorService.EditCode(module);
                return true;
            }

        foreach (var document in project.RelatedDocuments)
            if (SamePath(document.AbsolutePath, path))
            {
                editorService.EditRelatedDocument(document);
                return true;
            }

        return false;
    }

    /// <summary>
    /// Whether two paths name the same file. Case-folded on Windows only, matching the rule
    /// <c>LspDocumentUri</c> applies to <c>file:</c> URIs — the filesystem decides this, not the IDE, and
    /// assuming either answer everywhere is how a file gets carried twice or refused wrongly.
    /// </summary>
    private static bool SamePath(string? carried, string picked) =>
        carried != null
     && string.Equals(
            Path.GetFullPath(carried),
            Path.GetFullPath(picked),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private T? FindDock<T>(Func<T, bool> action) where T : class, IDockable => FindDock<T>(Layout, action);

    private T? FindDock<T>(IDockable dockable, Func<T, bool> predicate) where T : class, IDockable
    {
        if (dockable is T t && predicate(t))
            return t;
        if (dockable is IDock dock && dock.VisibleDockables != null)
        {
            foreach (var visible in dock.VisibleDockables)
                if (FindDock<T>(visible, predicate) is { } ret)
                    return ret;
        }

        return null;
    }

    private void OpenOrActivateTool(Tool tool, bool right)
    {
        var opened = FindDock<IDockable>(x => ReferenceEquals(x, tool));
        if (opened != null)
        {
            dockFactory.SetFocusedDockable((IDock)opened.Owner!, opened);
            return;
        }

        // Place the tool back in its home region at its slot (fixes the reopen-to-end bug);
        // add-ins without saved placement fall back to the right column.
        dockFactory.PlaceTool(Layout, tool, right);
    }

    /// <summary>Schedule an eager, debounced save — coalesces a burst of layout changes into one write.
    /// Called from the view on any dock pointer gesture (splitter resize, drag-dock) and from the factory
    /// on programmatic open/close.</summary>
    public void ScheduleLayoutSave()
    {
        _layoutSaveTimer.Stop();
        _layoutSaveTimer.Start();
    }

    private void FlushLayoutSaveDebounced()
    {
        _layoutSaveTimer.Stop();
        var json = LayoutManifestJson.Serialize(dockFactory.CaptureManifest(Layout));
        if (json == _lastSavedLayoutJson) return; // nothing actually changed since the last write
        _lastSavedLayoutJson = json;
        Task.Run(() => windowStateService.SaveLayoutManifestJson(json)); // atomic write off the UI thread
    }

    /// <summary>Capture and persist the current dock layout synchronously (called on window close).
    /// Unconditional, so the final state lands on disk even if a debounced write was still pending.</summary>
    public void SaveLayout()
    {
        _layoutSaveTimer.Stop();
        var json = LayoutManifestJson.Serialize(dockFactory.CaptureManifest(Layout));
        _lastSavedLayoutJson = json;
        windowStateService.SaveLayoutManifestJson(json);
    }

    /// <summary>Discard the saved layout and rebuild the default arrangement live, keeping open documents.</summary>
    public void ResetWindowLayout()
    {
        var fresh = dockFactory.ResetLayout(Layout);
        dockFactory.InitLayout(fresh);
        Layout = fresh;
        _lastSavedLayoutJson = LayoutManifestJson.Serialize(dockFactory.CaptureManifest(Layout));
    }

    public void OpenImmediateTool() => OpenOrActivateTool(Immediate, false);
    public void OpenLocalsTool() => OpenOrActivateTool(Locals, false);
    public void OpenWatchesTool() => OpenOrActivateTool(Watches, false);
    public void OpenRecentEditsTool() => OpenOrActivateTool(RecentEdits, false);

    public void OpenCallStackTool() => OpenOrActivateTool(CallStack, false);
    public void OpenColorPaletteTool() => OpenOrActivateTool(ColorPalette, false);
    public void OpenObjectBrowserTool()
    {
        var docDock = FindDock<DocumentDock>(_ => true);
        if (docDock == null) return;

        if (docDock.VisibleDockables?.Contains(ObjectBrowser) == true)
        {
            dockFactory.SetFocusedDockable(docDock, ObjectBrowser);
            docDock.ActiveDockable = ObjectBrowser;
            return;
        }

        docDock.VisibleDockables ??= dockFactory.CreateList<IDockable>();
        docDock.VisibleDockables.Add(ObjectBrowser);
        dockFactory.InitDockable(ObjectBrowser, docDock);
        dockFactory.SetFocusedDockable(docDock, ObjectBrowser);
        docDock.ActiveDockable = ObjectBrowser;
        ScheduleLayoutSave();
    }
    /// <summary>Opens the language-server view, or brings it forward if it is already open.</summary>
    public ICommand OpenLanguageServersCommand { get; private set; } = null!;

    /// <summary>Opens the protocol inspector, or brings it forward if it is already open.</summary>
    /// <remarks>
    /// <b>It never opens itself.</b> A window that appears uninvited is one people learn to close
    /// reflexively, and this one has to be trusted when it does appear. It is reached from the menu and
    /// from the connection list, which is where trouble is reported.
    /// </remarks>
    public ICommand OpenProtocolInspectorCommand { get; private set; } = null!;

    public void OpenProtocolInspector() => OpenProtocolInspector(null);

    /// <param name="connectionId">
    /// One server to filter to, or null for the whole interleaved timeline.
    /// </param>
    /// <remarks>
    /// The filter is applied BEFORE the tab is shown, so a reader arriving from one server's row never sees
    /// the merged view flick past on the way to the one they asked for — and never has to wonder whether
    /// what they are looking at is filtered yet.
    /// </remarks>
    public void OpenProtocolInspector(string? connectionId)
    {
        var docDock = FindDock<DocumentDock>(_ => true);
        if (docDock == null) return;

        if (connectionId is { Length: > 0 }) ProtocolInspector.ShowOnly(connectionId);

        // Re-read on every open rather than only on construction. The capture has been recording since
        // the session began, so a tab opened an hour in must not show what was there an hour ago.
        ProtocolInspector.Refresh();

        if (docDock.VisibleDockables?.Contains(ProtocolInspector) == true)
        {
            dockFactory.SetFocusedDockable(docDock, ProtocolInspector);
            docDock.ActiveDockable = ProtocolInspector;
            return;
        }

        docDock.VisibleDockables ??= dockFactory.CreateList<IDockable>();
        docDock.VisibleDockables.Add(ProtocolInspector);
        dockFactory.InitDockable(ProtocolInspector, docDock);
        dockFactory.SetFocusedDockable(docDock, ProtocolInspector);
        docDock.ActiveDockable = ProtocolInspector;
        ScheduleLayoutSave();
    }

    public void OpenLanguageServers()
    {
        var docDock = FindDock<DocumentDock>(_ => true);
        if (docDock == null) return;

        if (docDock.VisibleDockables?.Contains(LanguageServers) == true)
        {
            dockFactory.SetFocusedDockable(docDock, LanguageServers);
            docDock.ActiveDockable = LanguageServers;
            return;
        }

        docDock.VisibleDockables ??= dockFactory.CreateList<IDockable>();
        docDock.VisibleDockables.Add(LanguageServers);
        dockFactory.InitDockable(LanguageServers, docDock);
        dockFactory.SetFocusedDockable(docDock, LanguageServers);
        docDock.ActiveDockable = LanguageServers;
        ScheduleLayoutSave();
    }

    public void OpenTranslationEditor()
    {
        var docDock = FindDock<DocumentDock>(_ => true);
        if (docDock == null) return;

        if (docDock.VisibleDockables?.Contains(TranslationEditor) == true)
        {
            dockFactory.SetFocusedDockable(docDock, TranslationEditor);
            docDock.ActiveDockable = TranslationEditor;
            return;
        }

        docDock.VisibleDockables ??= dockFactory.CreateList<IDockable>();
        docDock.VisibleDockables.Add(TranslationEditor);
        dockFactory.InitDockable(TranslationEditor, docDock);
        dockFactory.SetFocusedDockable(docDock, TranslationEditor);
        docDock.ActiveDockable = TranslationEditor;
        ScheduleLayoutSave();
    }
    public void OpenProjectExplorerTool() => OpenOrActivateTool(ProjectExplorer, true);
    public void OpenPropertiesTool() => OpenOrActivateTool(Properties, true);
    public void OpenFormLayoutTool() => OpenOrActivateTool(FormLayout, true);
    public void OpenToolBox() => OpenOrActivateTool(ToolBox, true);

    public record ToolWindowInfo(string Name, bool Visible);

    public IReadOnlyList<ToolWindowInfo> GetToolWindows()
    {
        var result = new List<ToolWindowInfo>
        {
            new("Toolbox",      IsToolInLayout(ToolBox)),
            new("Properties",   IsToolInLayout(Properties)),
            new("ProjectGroup", IsToolInLayout(ProjectExplorer)),
            new("FormLayout",   IsToolInLayout(FormLayout)),
            new("Immediate",    IsToolInLayout(Immediate)),
            new("Locals",       IsToolInLayout(Locals)),
            new("Watches",      IsToolInLayout(Watches)),
            new("CallStack",    IsToolInLayout(CallStack)),
        };
        result.AddRange(_addinToolWindowService.Tools
            .Select(t => new ToolWindowInfo(t.Title, IsToolInLayout(t))));
        return result;
    }

    /// <summary>The built-in tool windows, by the names get_tool_windows reports and set_tool_window_visible takes.</summary>
    internal static readonly string[] ToolWindowNames =
        ["Toolbox", "Properties", "ProjectGroup", "FormLayout", "Immediate", "Locals", "Watches", "CallStack"];

    /// <summary>
    /// A built-in tool window's name from however a caller spells it: the name in <see cref="ToolWindowNames"/>,
    /// or the View menu's ("Immediate Window", "Watch Window", "Project Explorer", "Call Stack..."), without
    /// regard to case or spaces. Null when it names none of them.
    /// </summary>
    /// <remarks>
    /// The menu's names were refused for six of the eight panels, so a caller who read the name off the menu,
    /// or off a snapshot of it, was told the panel did not exist (#586).
    /// </remarks>
    internal static string? CanonicalToolWindowName(string name)
    {
        var key = name.Trim().TrimEnd('.').Trim().ToLowerInvariant();
        if (key.EndsWith(" window", StringComparison.Ordinal))
            key = key[..^" window".Length];
        return key.Replace(" ", "") switch
        {
            "toolbox"                                         => "Toolbox",
            "properties"                                      => "Properties",
            "projectgroup" or "project" or "projectexplorer"  => "ProjectGroup",
            "formlayout"                                      => "FormLayout",
            "immediate"                                       => "Immediate",
            "locals"                                          => "Locals",
            "watches" or "watch"                              => "Watches",
            "callstack"                                       => "CallStack",
            _                                                 => null,
        };
    }

    /// <summary>
    /// Shows or hides the tool window <paramref name="name"/> names, as its View menu item and its tab's close button do.
    /// The error, or a note saying what it is now and naming any other tool window that opened or closed with it.
    /// </summary>
    public (string? Error, string? Note) SetToolWindowVisible(string name, bool visible)
    {
        var canonical = CanonicalToolWindowName(name);
        var (tool, right) = canonical switch
        {
            "Toolbox"      => ((Tool?)ToolBox,        true),
            "Properties"   => (Properties,             true),
            "ProjectGroup" => (ProjectExplorer,        true),
            "FormLayout"   => (FormLayout,             true),
            "Immediate"    => (Immediate,              false),
            "Locals"       => (Locals,                 false),
            "Watches"      => (Watches,                false),
            "CallStack"    => (CallStack,              false),
            _              => (null,                   false)
        };

        var title = canonical;
        if (tool is null)
        {
            var addinTool = _addinToolWindowService.Tools.FirstOrDefault(t =>
                string.Equals(t.Title, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Id, name, StringComparison.OrdinalIgnoreCase));
            if (addinTool is not null) { tool = addinTool; right = true; title = addinTool.Title; }
        }

        if (tool is null)
        {
            var addins = _addinToolWindowService.Tools.Select(t => t.Title).ToList();
            return ($"Unknown tool window '{name}'. Built in: {string.Join(", ", ToolWindowNames)}"
                   + (addins.Count > 0 ? $". From add-ins: {string.Join(", ", addins)}" : "")
                   + ". The View menu's names, such as 'Immediate Window' or 'Project Explorer', work too.", null);
        }

        var before = GetToolWindows();
        var inLayout = IsToolInLayout(tool);

        if (visible && !inLayout)
            OpenOrActivateTool(tool, right);
        else if (!visible && inLayout)
            CloseTool(tool);

        if (IsToolInLayout(tool) != visible)
            return ($"{title} could not be {(visible ? "shown" : "hidden")}: it is still {(visible ? "hidden" : "shown")}.", null);
        var others = GetToolWindows()
            .Where(t => t.Name != title && before.Any(b => b.Name == t.Name && b.Visible != t.Visible))
            .Select(t => $"{t.Name} {(t.Visible ? "opened" : "closed")} with it")
            .ToList();
        return (null, $"{title} is now {(visible ? "shown" : "hidden")}{(inLayout == visible ? ", as it was already" : "")}"
                      + (others.Count > 0 ? $"; {string.Join(", ", others)}" : "") + ".");
    }

    private bool IsToolInLayout(Tool tool) =>
        FindDock<IDockable>(x => ReferenceEquals(x, tool)) is not null;

    // The tool alone, as its tab's close button closes it: its dock goes only when it is left empty. Closing the dock
    // instead hid every tool tabbed beside it, once drags could put several in one (#2362).
    private void CloseTool(Tool tool) => dockFactory.CloseDockable(tool);
}
