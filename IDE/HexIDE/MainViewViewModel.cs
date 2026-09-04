using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Addins;
using HexIDE.Tools;
using HexIDE.Tools.ObjectBrowser;
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
    private readonly IWindowManager windowManager;
    private readonly IProjectService projectService;
    private readonly IDocumentDockService documentDockService;
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
    public ProjectToolViewModel ProjectExplorer { get; }
    public ColorPaletteToolViewModel ColorPalette { get; }
    public ObjectBrowserToolViewModel ObjectBrowser { get; }
    public TranslationEditorViewModel TranslationEditor { get; }
    public IFocusedProjectUtil FocusedProjectUtil { get; }

    public IStatusBarService StatusBar { get; }

    private IRootDock _layout = null!;
    public IRootDock Layout
    {
        get => _layout;
        private set => SetProperty(ref _layout, value);
    }

    public DelegateCommand StartDefaultProjectCommand { get; }

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

    public string Title
    {
        get
        {
            var prefix = projectManager.CurrentGroup?.Name
                      ?? FocusedProjectUtil.FocusedOrStartupProject?.Name;
            return prefix == null
                ? "HexIDE [design]"
                : $"{prefix} - HexIDE " + (projectRunnerService.IsRunning ? "[run]" : "[design]");
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
    public string RemoveFormMenuHeader => FormatHeader("Str.Menu.Project.RemoveFormFormat", "Str.Menu.Project.RemoveFormFallback", FocusedProjectUtil.FocusedForm?.Name);
    public string ProjectPropertiesMenuHeader => FormatHeader("Str.Menu.Project.PropertiesFormat", "Str.Menu.Project.PropertiesFallback", FocusedProjectUtil.FocusedOrStartupProject?.Name);

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
        private readonly IReadOnlyDictionary<string, IDockable> toolsByKey;

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
            IWindowStateService windowStateService)
        {
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
            };
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
            base.CloseDockable(dockable);
            LayoutChanged?.Invoke();
        }

        /// <summary>Discard the saved manifest and rebuild the default arrangement, reusing the live
        /// document dock so open editor/designer tabs (and their service subscription) survive the reset.</summary>
        public IRootDock ResetLayout()
        {
            windowStateService.ResetLayoutManifest();
            return BuildLayout(LayoutManifest.Default, DocumentDock);
        }

        // ---- build a fresh layout tree from a manifest ----

        private IRootDock BuildLayout(LayoutManifest m, IDocumentDock? reuseDocument)
        {
            List<ToolLayoutState> OpenIn(DockRegion r) =>
                m.Tools.Where(t => t.Open && t.Region == r).OrderBy(t => t.Order).ToList();

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

            var rootDock = CreateRootDock();
            rootDock.IsFocusableRoot = true;
            rootDock.IsCollapsable = false;
            rootDock.ActiveDockable = mainLayout;
            rootDock.DefaultDockable = mainLayout;
            rootDock.VisibleDockables = CreateList<IDockable>(mainLayout);
            return rootDock;
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

            var left = LayoutManifest.DefaultLeftProportion;
            if (main?.VisibleDockables?.OfType<IToolDock>().FirstOrDefault(d => d.Alignment == Alignment.Left)
                is { Proportion: > 0 } ld)
                left = ld.Proportion;

            var right = LayoutManifest.DefaultRightProportion;
            if (Find<IProportionalDock>(layout, d => (d.Context as string) == nameof(RightDock))
                is { Proportion: > 0 } rd)
                right = rd.Proportion;

            var states = new List<ToolLayoutState>(LayoutManifest.Default.Tools.Count);
            foreach (var def in LayoutManifest.Default.Tools)
            {
                var located = Locate(layout, toolsByKey[def.Key]);
                states.Add(located is { } loc
                    ? new ToolLayoutState(def.Key, true, loc.Region, loc.Order, loc.Proportion)
                    : def with { Open = false });
            }

            return new LayoutManifest(LayoutManifest.CurrentVersion, left, right, states);
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
                    return (DockRegion.Left, 0, prop);

                var ctx = parent.Context as string;
                if (ctx == nameof(RightDock))
                    return (DockRegion.Right, ToolDockIndex(parent, td), prop);
                if (ctx == nameof(MiddleDock))
                    return (DockRegion.Bottom, ToolDockIndex(parent, td), prop);
            }

            Log.Debug("DockFactory: tool {Key} in an unrecognized dock; snapping to its default region", KeyOf(tool));
            return null;
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

        public void PlaceTool(IRootDock layout, Tool tool, bool rightFallback)
        {
            var key = KeyOf(tool);
            var region = key != null ? Home[key].Region : (rightFallback ? DockRegion.Right : DockRegion.Bottom);
            var order = key != null ? Home[key].Order : int.MaxValue;

            switch (region)
            {
                case DockRegion.Left:
                {
                    var left = GetOrCreateLeftDock(layout);
                    left.VisibleDockables ??= CreateList<IDockable>();
                    left.VisibleDockables.Add(tool);
                    left.ActiveDockable = tool;
                    InitDockable(tool, left);
                    SetFocusedDockable(left, tool);
                    break;
                }
                case DockRegion.Right:
                {
                    var rd = GetOrCreateRightDock(layout);
                    var td = MakeToolDock(tool, Alignment.Top, FairProportion(rd));
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
                    var td = MakeToolDock(tool, Alignment.Bottom, FairProportion(mid) ?? 0.3);
                    InsertToolDock(mid, td, order, documentFirst: true);
                    InitDockable(td, mid);
                    SetFocusedDockable(td, tool);
                    break;
                }
            }

            LayoutChanged?.Invoke();
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
                Proportion = LayoutManifest.DefaultRightProportion,
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
            left.Proportion = LayoutManifest.DefaultLeftProportion;
            left.VisibleDockables = CreateList<IDockable>();
            main.VisibleDockables ??= CreateList<IDockable>();
            main.VisibleDockables.Insert(0, new ProportionalDockSplitter());
            main.VisibleDockables.Insert(0, left);
            InitDockable(left, main);
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
                ? Home[k].Order : int.MaxValue;

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
        HexIDE.Runtime.Debugging.IDebugController debugController)
    {
        Personality = personalityService;
        AddinMenuService = addinMenuService;
        AddinCommandService = addinCommandService;
        _addinToolWindowService = addinToolWindowService;
        addinToolWindowService.ToolRegistered   += tool => OpenOrActivateTool(tool, true);
        addinToolWindowService.ShowRequested    += tool => OpenOrActivateTool(tool, true);
        addinToolWindowService.ToolUnregistered += tool =>
        {
            var parent = FindDock<IDockable>(x => ReferenceEquals(x, tool))?.Owner as IDock;
            if (parent is not null) dockFactory.CloseDockable(parent);
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
        // When the interpreter breaks, bring the offending form/module's code editor forward. The editor view then
        // paints the amber current-statement bar and scrolls to it (it reads the controller's CurrentStop on open).
        debugController.Stopped += info => RevealBreak(info.Module);
        StatusBar = statusBarService;
        ToolBox = toolBox;
        Properties = properties;
        Immediate = immediate;
        FormLayout = formLayout;
        Locals = locals;
        Watches = watches;
        CallStack = callStack;
        ProjectExplorer = projectExplorer;
        ColorPalette = colorPalette;
        ObjectBrowser = objectBrowser;
        TranslationEditor = translationEditor;
        FocusedProjectUtil = focusedProjectUtil;

        this.windowStateService = windowStateService;

        Layout = dockFactory.CreateLayout();
        dockFactory.InitLayout(Layout);

        // Eager-save wiring: baseline the just-restored layout (so a no-op gesture won't rewrite it),
        // debounce changes ~400ms, and listen for programmatic open/close. Mouse-driven changes
        // (splitter resize, drag-dock) are funnelled in by the view via ScheduleLayoutSave().
        _layoutSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _layoutSaveTimer.Tick += (_, _) => FlushLayoutSaveDebounced();
        _lastSavedLayoutJson = LayoutManifestJson.Serialize(dockFactory.CaptureManifest(Layout));
        dockFactory.LayoutChanged += ScheduleLayoutSave;

        VBWindowContext.RunTimeError += (form, e) =>
        {
            // Sliced from the context's OWN input stream, not from form.Code.
            //
            // Two bugs in one line, both reachable the moment a form can call into a .bas (#220). The
            // offsets belong to whichever module raised, so indexing the FORM's source threw
            // ArgumentOutOfRangeException inside this handler — which meant no dialog at all: a runtime
            // error inside a module vanished silently and the program appeared to carry on. And the length
            // was `Stop - Start`, but ANTLR's StopIndex is INCLUSIVE, so every message lost its last
            // character ("NoSuchSub" rendered as "NoSuchSu").
            //
            // GetText takes an inclusive interval, so it fixes the truncation as well as the source.
            var at = e.Context is { Start: { InputStream: { } input } start, Stop: { } stop }
                  && stop.StopIndex >= start.StartIndex
                ? "\n\nat " + input.GetText(new Antlr4.Runtime.Misc.Interval(start.StartIndex, stop.StopIndex))
                : "";   // built-in errors carry no parse context
            var vm = new RuntimeErrorViewModel(e.Message + at);
            windowManager.ShowDialog(vm);
        };

        VBWindowContext.CompileError += (form, e) =>
        {
            //var line = form.Code.Substring(e.Context.Start.StartIndex, e.Context.Stop.StopIndex - e.Context.Start.StartIndex);
            //var vm = new RuntimeErrorViewModel(e.Message + "\n\nat " + line);
            windowManager.MessageBox(e.Message, "HexIDE", MessageBoxButtons.Ok, MessageBoxIcon.Warning);
        };

        // Route `Debug.Print` output from the running program into the Immediate pane (VB6's Immediate window).
        VBDebugConsole.Output += text =>
        {
            Dispatcher.UIThread.Post(() =>
                Immediate.Document.Insert(Immediate.Document.TextLength, text + "\n"));
        };

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
        RunWithVb6Command = new DelegateCommand(
            () => vb6Toolchain.RunWithVb6Async(FocusedProjectUtil.FocusedOrStartupProject!).ListenErrors(),
            () => FocusedProjectUtil.FocusedOrStartupProject != null && vb6Toolchain.IsAvailable);
        MakeWithVb6Command = new DelegateCommand(
            () => vb6Toolchain.MakeWithVb6Async(FocusedProjectUtil.FocusedOrStartupProject!).ListenErrors(),
            () => FocusedProjectUtil.FocusedOrStartupProject != null && vb6Toolchain.IsAvailable);

        AddFormCommand = new DelegateCommand(
            () => AddFormAsync().ListenErrors(),
            () => projectManager.StartupProject != null);
        AddModuleCommand = new DelegateCommand(
            () => AddModuleAsync(ModuleKind.StandardModule, "Module").ListenErrors(),
            () => projectManager.StartupProject != null);
        AddClassModuleCommand = new DelegateCommand(
            () => AddModuleAsync(ModuleKind.ClassModule, "Class").ListenErrors(),
            () => projectManager.StartupProject != null);
        AddUserControlCommand = new DelegateCommand(
            () => AddUserControlAsync().ListenErrors(),
            () => projectManager.StartupProject != null);
        AddPropertyPageCommand = new DelegateCommand(
            () => AddPropertyPageAsync().ListenErrors(),
            () => projectManager.StartupProject != null);
        AddFileCommand = new DelegateCommand(
            () => AddFileAsync().ListenErrors(),
            () => projectManager.StartupProject != null);

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
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(WindowTitle));
                RaiseDynamicMenuHeaders();
            });

        projectRunnerService.ObservePropertyChanged(x => x.IsRunning)
            .Subscribe(_ =>
            {
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(WindowTitle));
                if (projectRunnerService.IsRunning)
                    StatusBar.SetMessage(localization.GetString("Str.StatusBar.Running"));
                else
                    StatusBar.ClearMessage();
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
            });

        // The Save/Remove "<form>" headers depend on the focused form, not just the project.
        FocusedProjectUtil.ObservePropertyChanged(x => x.FocusedForm)
            .Subscribe(_ => RaiseDynamicMenuHeaders());

        // Re-localize every code-built header live when the language changes.
        localization.LanguageChanged += () =>
        {
            RaiseDynamicMenuHeaders();
            UpdateUndoHeaders();
        };

        // Options → Language → Customize commits-and-closes the modal dialog, then publishes this event so
        // the Translation Editor tab opens on the now-unblocked dock. MainViewViewModel is an app-lifetime
        // singleton, so the subscription never needs disposing.
        eventBus.Subscribe<OpenTranslationEditorEvent>(_ => OpenTranslationEditor());
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

    public void FindInCode() => findReplaceService.ShowFind();

    public void ReplaceInCode() => findReplaceService.ShowReplace();

    public void FindNextInCode() => findReplaceService.FindNext();

    public DelegateCommand MakeProjectCommand { get; }

    public DelegateCommand RemoveProjectCommand { get; }

    public DelegateCommand RunWithVb6Command { get; }

    public DelegateCommand MakeWithVb6Command { get; }

    public void MakeProject() => projectService.MakeProject().ListenErrors();

    public void OpenOptions() => windowManager.ShowDialog(new OptionsViewModel(settingsService, themeService, keymapService, languageSwitch, addinRegistry, addinOptionsService, developerModeService, localization, eventBus));

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

    public void MakeProjectGroup() => projectService.MakeProjectGroup().ListenErrors();

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
            var uri = code.GetDocumentUriPublic();
            var module = uri[(uri.LastIndexOf('/') + 1)..];   // vb6://form/Form1 → Form1
            projectRunnerService.RunToCursorProject(module, line);
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

    // Toggle a breakpoint on the active code editor's caret line (1-based). The gutter margin repaints itself via
    // IBreakpointService.BreakpointsChanged, and a live run picks it up immediately (ProjectRunnerService pushes it).
    public void ToggleBreakpoint()
    {
        if (documentDockService.ActiveDocument is HexIDE.Forms.ViewModels.CodeEditorViewModel code)
        {
            var line = code.Document.GetLineByOffset(code.CaretOffset).LineNumber;
            breakpointService.Toggle(code.GetDocumentUriPublic(), line);
        }
    }

    public void ClearAllBreakpoints() => breakpointService.ClearAll();

    // Bring the code editor for the module the interpreter just broke in to the front (creating/activating its tab).
    private void RevealBreak(string module)
    {
        var project = projectManager.StartupProject;
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
        if (documentDockService.ActiveDocument is HexIDE.Forms.ViewModels.CodeEditorViewModel code)
        {
            var line = code.Document.GetLineByOffset(code.CaretOffset).LineNumber;
            var uri = code.GetDocumentUriPublic();
            var module = uri[(uri.LastIndexOf('/') + 1)..];
            if (!debugController.SetNextStatement(module, line))
                windowManager.MessageBox(localization.GetString("Str.Debug.SetNextStatement.Refused"),
                    icon: MessageBoxIcon.Information).ListenErrors();
        }
    }

    // Show Next Statement (Debug menu): bring the paused module's current-statement line into view — RevealBreak
    // opens/activates that editor, which scrolls to and paints the amber current-statement bar (it reads the
    // controller's CurrentStop on open). A no-op when not paused.
    public void ShowNextStatement()
    {
        if (debugController.CurrentStop is { } stop)
            RevealBreak(stop.Module);
    }

    public Task OpenGithubRepo()
    {
        TopLevel.GetTopLevel(Static.MainView)!.Launcher.LaunchUriAsync(new Uri("https://github.com/hexide-io/hexide")).ListenErrors();
        return Task.CompletedTask;
    }

    private async Task AddFormAsync()
    {
        var p = projectManager.StartupProject!;
        var name = NextName(p.Forms.Select(f => f.Name), "Form");
        var form = await projectService.AddNewForm(p, name);
        editorService.EditForm(form);
    }

    private async Task AddModuleAsync(ModuleKind kind, string prefix)
    {
        var p = projectManager.StartupProject!;
        var name = NextName(p.Modules.Where(m => m.Kind == kind).Select(m => m.Name), prefix);
        var module = await projectService.AddNewModule(p, name, kind);
        editorService.EditCode(module);
    }

    private async Task AddUserControlAsync()
    {
        var p = projectManager.StartupProject!;
        var name = NextName(
            p.Modules.Where(m => m.Kind == ModuleKind.UserControl).Select(m => m.Name),
            "UserControl");
        var module = await projectService.AddNewUserControl(p, name);
        editorService.EditForm(module.FormPart);
    }

    private async Task AddPropertyPageAsync()
    {
        var p = projectManager.StartupProject!;
        var name = NextName(
            p.Modules.Where(m => m.Kind == ModuleKind.PropertyPage).Select(m => m.Name),
            "PropertyPage");
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
        var p = projectManager.StartupProject!;
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
            if (await projectService.AddExistingForm(project, path) is { } form)
                editorService.EditForm(form);
            else
                Log.Warning("Add File: {Path} could not be parsed as a form; not added", path);
            return;
        }

        if (kind.AsModuleKind() is { } moduleKind)
        {
            var module = await projectService.AddExistingModule(project, path, moduleKind);
            // A UserControl or PropertyPage opens in its designer, matching Add User Control; a .bas or
            // .cls has no designer half, so it opens as code.
            if (module.FormPart is { } formPart)
                editorService.EditForm(formPart);
            else
                editorService.EditCode(module);
            return;
        }

        editorService.EditRelatedDocument(await projectService.AddExistingRelatedDocument(project, path));
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

    private static string NextName(IEnumerable<string> existing, string prefix)
    {
        var used = existing
            .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(n[prefix.Length..], out _))
            .Select(n => int.Parse(n[prefix.Length..]))
            .ToHashSet();
        for (var i = 1; ; i++)
            if (!used.Contains(i)) return prefix + i;
    }

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
        // unknown tools (add-ins) fall back to the right column.
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
        var fresh = dockFactory.ResetLayout();
        dockFactory.InitLayout(fresh);
        Layout = fresh;
        _lastSavedLayoutJson = LayoutManifestJson.Serialize(dockFactory.CaptureManifest(Layout));
    }

    public void OpenImmediateTool() => OpenOrActivateTool(Immediate, false);
    public void OpenLocalsTool() => OpenOrActivateTool(Locals, false);
    public void OpenWatchesTool() => OpenOrActivateTool(Watches, false);
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

    public string? SetToolWindowVisible(string name, bool visible)
    {
        var (tool, right) = name.ToLowerInvariant() switch
        {
            "toolbox"                     => ((Tool?)ToolBox,        true),
            "properties"                  => (Properties,             true),
            "projectgroup" or "project"   => (ProjectExplorer,        true),
            "formlayout" or "form layout" => (FormLayout,             true),
            "immediate"                   => (Immediate,              false),
            "locals"                      => (Locals,                 false),
            "watches"                     => (Watches,                false),
            "callstack" or "call stack"   => (CallStack,              false),
            _                             => (null,                   false)
        };

        if (tool is null)
        {
            var addinTool = _addinToolWindowService.Tools.FirstOrDefault(t =>
                string.Equals(t.Title, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Id, name, StringComparison.OrdinalIgnoreCase));
            if (addinTool is not null) { tool = addinTool; right = true; }
        }

        if (tool is null)
            return $"Unknown tool window '{name}'";

        var inLayout = IsToolInLayout(tool);

        if (visible && !inLayout)
            OpenOrActivateTool(tool, right);
        else if (!visible && inLayout)
        {
            // Close the parent ToolDock (which holds exactly this one tool)
            var parent = FindDock<IDockable>(x => ReferenceEquals(x, tool))?.Owner as IDock;
            if (parent is not null)
                dockFactory.CloseDockable(parent);
        }

        return null;
    }

    private bool IsToolInLayout(Tool tool) =>
        FindDock<IDockable>(x => ReferenceEquals(x, tool)) is not null;
}

public class VBMDIWindow : MDIWindow, IModuleExecutionRoot
{
    protected override Type StyleKeyOverride => typeof(MDIWindow);

    public IReadOnlyList<PropertyClass> AccessibleProperties { get; } = [VBProperties.CaptionProperty];

    private BasicInterpreter? interpreter;

    public ModuleExecutionContext ExecutionContext { get; } = new();

    public ExecutionEnvironment Environment { get; } = new();

    public void SetCode(string code)
    {
        interpreter = new BasicInterpreter(new StandaloneStandardLib(this), ExecutionContext, Environment, code);
    }

    public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null)
    {
        interpreter!.ExecuteSub(name, args is null ? null : new List<Vb6Value>(args), true);
    }

    public Vb6Value? GetPropertyValue(PropertyClass property)
    {
        if (property == VBProperties.CaptionProperty)
            return Title;
        return null;
    }

    public void SetPropertyValue(PropertyClass property, Vb6Value value)
    {
        if (property == VBProperties.CaptionProperty)
            Title = value.Value?.ToString() ?? "";
    }

    public class StandaloneStandardLib : IBasicStandardLibrary
    {
        private readonly VBMDIWindow form;

        public StandaloneStandardLib(VBMDIWindow form)
        {
            this.form = form;
        }

        public async Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return default;
            //return await MessageBox.ShowDialog(form, text, caption, buttons, icon);
        }

        public async Task<string?> InputBox(string prompt, string? title, string defaultText)
        {
            return default;
        }

        public void DebugPrint(Vb6Value value) => VBDebugConsole.Emit(value);
    }
}
