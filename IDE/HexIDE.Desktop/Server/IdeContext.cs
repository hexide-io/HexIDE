using HexIDE.Addins;
using HexIDE.Bookmarks;
using HexIDE.IDE;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.VisualDesigner;

namespace HexIDE.Desktop.Server;

internal sealed class IdeContext : IDisposable
{
    public IProjectManager ProjectManager { get; }
    public IDocumentDockService DocumentDockService { get; }
    public ILspClient LspClient { get; }
    public IEditorService EditorService { get; }
    public IProjectRunnerService ProjectRunnerService { get; }
    public IProjectService ProjectService { get; }
    public IEventBus EventBus { get; }
    public IBookmarkService BookmarkService { get; }
    public HexIDE.Debugging.IBreakpointService BreakpointService { get; }
    public HexIDE.Runtime.Debugging.IDebugController DebugController { get; }
    public MainViewViewModel RootViewModel { get; }
    public DiagnosticsCache Diagnostics { get; }
    public ToolBoxToolViewModel ToolBoxViewModel { get; }
    public IProfileService ProfileService { get; }
    public AddinProjectTemplateService AddinProjectTemplateService { get; }
    public ILanguageSwitchService LanguageSwitch { get; }

    /// <summary>The saved settings, for a tool that confirms a choice the Options dialog would otherwise save (#2268).</summary>
    public ISettingsService Settings { get; }

    /// <summary>The IDE's strings, for a refusal a tool shares with the UI.</summary>
    public HexIDE.Localization.ILocalizationService Localization { get; }

    /// <summary>The recorded language-server conversations, for the capture tools.</summary>
    public HexIDE.Conversations.ConversationLog Capture { get; }

    /// <summary>The session's pseudonym mapping, so every export names a path the same way.</summary>
    public HexIDE.Redaction.Pseudonymiser Pseudonyms { get; }

    /// <summary>The project file format, for a tool that has to know a document's companion file.</summary>
    public IProjectFileFormat ProjectFileFormat { get; }

    public IdeContext(
        IProjectManager projectManager,
        IDocumentDockService documentDockService,
        ILspClient lspClient,
        IEditorService editorService,
        IProjectRunnerService projectRunnerService,
        IProjectService projectService,
        IEventBus eventBus,
        IBookmarkService bookmarkService,
        HexIDE.Debugging.IBreakpointService breakpointService,
        HexIDE.Runtime.Debugging.IDebugController debugController,
        MainViewViewModel rootViewModel,
        ToolBoxToolViewModel toolBoxViewModel,
        IProfileService profileService,
        AddinProjectTemplateService addinProjectTemplateService,
        ILanguageSwitchService languageSwitch,
        HexIDE.Conversations.ConversationLog capture,
        HexIDE.Redaction.Pseudonymiser pseudonyms,
        HexIDE.Localization.ILocalizationService localization,
        ISettingsService settings,
        IProjectFileFormat? projectFileFormat = null)
    {
        ProjectManager = projectManager;
        DocumentDockService = documentDockService;
        LspClient = lspClient;
        EditorService = editorService;
        ProjectRunnerService = projectRunnerService;
        ProjectService = projectService;
        EventBus = eventBus;
        BookmarkService = bookmarkService;
        BreakpointService = breakpointService;
        DebugController = debugController;
        RootViewModel = rootViewModel;
        Diagnostics = new DiagnosticsCache(lspClient);
        ToolBoxViewModel = toolBoxViewModel;
        ProfileService = profileService;
        AddinProjectTemplateService = addinProjectTemplateService;
        LanguageSwitch = languageSwitch;
        Capture = capture;
        Pseudonyms = pseudonyms;
        Localization = localization;
        Settings = settings;
        ProjectFileFormat = projectFileFormat ?? new Vb6ProjectFileFormat();
    }

    public void Dispose() => Diagnostics.Dispose();
}
