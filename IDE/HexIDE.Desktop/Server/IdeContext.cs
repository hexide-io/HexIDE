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
    public IBookmarkService BookmarkService { get; }
    public MainViewViewModel RootViewModel { get; }
    public DiagnosticsCache Diagnostics { get; }
    public ToolBoxToolViewModel ToolBoxViewModel { get; }
    public IPersonalityService PersonalityService { get; }
    public AddinProjectTemplateService AddinProjectTemplateService { get; }
    public ILanguageSwitchService LanguageSwitch { get; }

    public IdeContext(
        IProjectManager projectManager,
        IDocumentDockService documentDockService,
        ILspClient lspClient,
        IEditorService editorService,
        IProjectRunnerService projectRunnerService,
        IProjectService projectService,
        IBookmarkService bookmarkService,
        MainViewViewModel rootViewModel,
        ToolBoxToolViewModel toolBoxViewModel,
        IPersonalityService personalityService,
        AddinProjectTemplateService addinProjectTemplateService,
        ILanguageSwitchService languageSwitch)
    {
        ProjectManager = projectManager;
        DocumentDockService = documentDockService;
        LspClient = lspClient;
        EditorService = editorService;
        ProjectRunnerService = projectRunnerService;
        ProjectService = projectService;
        BookmarkService = bookmarkService;
        RootViewModel = rootViewModel;
        Diagnostics = new DiagnosticsCache(lspClient);
        ToolBoxViewModel = toolBoxViewModel;
        PersonalityService = personalityService;
        AddinProjectTemplateService = addinProjectTemplateService;
        LanguageSwitch = languageSwitch;
    }

    public void Dispose() => Diagnostics.Dispose();
}
