using System;
using Pure.DI;
using System.Diagnostics;
using HexIDE.Addins;
using HexIDE.Bookmarks;
using HexIDE.IDE;
using HexIDE.Sidecar;
using HexIDE.Infrastructure;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Keymaps;
using HexIDE.Localization;
using HexIDE.Themes;
using HexIDE.Tools;
using HexIDE.Tools.ObjectBrowser;
using HexIDE.Tools.TranslationEditor;
using HexIDE.VisualDesigner;
using Microsoft.Extensions.Logging;
using static Pure.DI.Lifetime;

namespace HexIDE;

public partial class DISetup
{
    [Conditional("DI")]
    static void Setup() =>
        DI.Setup()
            .Bind().As(Singleton).To<ToolBoxToolViewModel>()
            .Bind().As(Singleton).To<PropertiesToolViewModel>()
            .Bind().As(Singleton).To<ProjectToolViewModel>()
            .Bind().As(Singleton).To<FormLayoutToolViewModel>()
            .Bind().As(Singleton).To<ImmediateToolViewModel>()
            .Bind().As(Singleton).To<LocalsToolViewModel>()
            .Bind().As(Singleton).To<WatchesToolViewModel>()
            .Bind().As(Singleton).To<CallStackToolViewModel>()
            .Bind().As(Singleton).To<ColorPaletteToolViewModel>()
            .Bind().As(Singleton).To<ObjectBrowserToolViewModel>()
            .Bind().As(Singleton).To<TranslationEditorViewModel>()
            .Bind().As(Singleton).To<WindowManager>()
            .Bind().As(Singleton).To<ProjectManager>()
            .Bind().As(Singleton).To<EditorService>()
            .Bind<IDocumentDockService>().As(Singleton).To<DocumentDockService>()
            .Bind().As(Singleton).To<MainViewViewModel.DockFactory>()
            .Bind().As(Singleton).To<EventBus>()
            .Bind().As(Singleton).To<ProjectRunnerService>()
            .Bind().As(Singleton).To<ProjectService>()
            .Bind<IFileBaselineStore>().As(Singleton).To<FileBaselineStore>()
            .Bind<IClock>().As(Singleton).To<SystemClock>()
            .Bind<IFileWatcherService>().As(Singleton).To<FileWatcherService>()
            .Bind<ISettingsService>().As(Singleton).To<SettingsService>()
            .Bind<IWindowStateService>().As(Singleton).To<WindowStateService>()
            .Bind<IStatusBarService>().As(Singleton).To<StatusBarService>()
            .Bind<IRecentProjectsService>().As(Singleton).To<RecentProjectsService>()
            .Bind<IBookmarkService>().As(Singleton).To<BookmarkService>()
            // Debugger: one breakpoint store + one watch store + one interpreter controller per IDE session.
            .Bind<HexIDE.Debugging.IBreakpointService>().As(Singleton).To<HexIDE.Debugging.BreakpointService>()
            .Bind().As(Singleton).To<HexIDE.Debugging.WatchService>()
            .Bind<HexIDE.Runtime.Debugging.IDebugController>().As(Singleton).To<HexIDE.Runtime.Debugging.DebugController>()
            .Bind<IUserSidecarService>().As(Singleton).To<UserSidecarService>()
            .Bind<IFindReplaceService>().As(Singleton).To<FindReplaceService>()
            .Bind().As(Singleton).To<FocusedProjectUtil>()
            .Bind<IVb6ToolchainService>().As(Singleton).To<Vb6ToolchainService>()
            .Bind<IReferenceLibraryService>().As(Singleton).To<ReferenceLibraryService>()
            .Bind<ITypeLibraryService>().As(Singleton).To<TypeLibraryService>()
            .Bind<IComponentRegistry>().As(Singleton).To<ComponentRegistry>()
            // LSP
            .Bind<ILspServerLocator>().As(Singleton).To<LspServerLocator>()
            // Transport selection: WebSocket if HEXIDE_LSP_WS_URL (env, takes precedence) or the
            // LspWebSocketUrl setting is set; otherwise the stdio subprocess transport.
            .Bind<ILspTransport>().As(Singleton).To(ctx =>
            {
                ctx.Inject<ILspServerLocator>(out var locator);
                ctx.Inject<ILoggerFactory>(out var loggerFactory);
                ctx.Inject<ISettingsService>(out var settings);
                var wsUrl = Environment.GetEnvironmentVariable("HEXIDE_LSP_WS_URL");
                if (string.IsNullOrWhiteSpace(wsUrl)) wsUrl = settings.LspWebSocketUrl;
                return string.IsNullOrWhiteSpace(wsUrl)
                    ? new StdioProcessLspTransport(locator, loggerFactory.CreateLogger<StdioProcessLspTransport>())
                    : (ILspTransport)new WebSocketLspTransport(wsUrl, loggerFactory.CreateLogger<WebSocketLspTransport>());
            })
            .Bind<ILspClient>().As(Singleton).To<VBLspClient>()
            .Bind<ILoggerFactory>().As(Singleton).To(_ => LoggingSetup.LoggerFactory)
            .Bind<ILogger<TT>>().As(Singleton).To<Logger<TT>>()
            // Personality
            .Bind<IPersonalityService>().As(Singleton).To<PersonalityService>()
            // Theming
            .Bind<IThemeService>().As(Singleton).To<ThemeService>()
            // Keymaps
            .Bind<IKeymapService>().As(Singleton).To<KeymapService>()
            // Localization
            .Bind<IUserTranslationsService>().As(Singleton).To<UserTranslationsService>()
            .Bind<ILocalizationService>().As(Singleton).To<LocalizationService>()
            .Bind<ILanguageSwitchService>().As(Singleton).To<LanguageSwitchService>()
            // Developer mode (session state from --developer-mode)
            .Bind<IDeveloperModeService>().As(Singleton).To<DeveloperModeService>()
            // Add-ins
            .Bind().As(Singleton).To<AddinMenuService>()
            .Bind().As(Singleton).To<AddinToolWindowService>()
            .Bind().As(Singleton).To<AddinEventService>()
            .Bind().As(Singleton).To<AddinEditorService>()
            .Bind().As(Singleton).To<AddinProjectService>()
            .Bind().As(Singleton).To<AddinDiagnosticsService>()
            .Bind().As(Singleton).To<AddinCommandService>()
            .Bind().As(Singleton).To<AddinProjectTemplateService>()
            .Bind().As(Singleton).To<AddinOptionsService>()
            .Bind<IPackageVerifier>().As(Singleton).To<PackageVerifier>()
            .Bind<IAddinRegistry, IAddinLoader>().As(Singleton).To<AddinRegistry>()
            .Bind<IHexIdeHost>().As(Singleton).To<HexIdeHost>()
            .Root<MainViewViewModel>("Root")
            .Root<ILspClient>("LspClient")
            .Root<IThemeService>("ThemeService")
            .Root<IKeymapService>("KeymapService")
            .Root<ILocalizationService>("LocalizationService")
            .Root<IUserTranslationsService>("UserTranslationsService")
            .Root<ILanguageSwitchService>("LanguageSwitchService")
            .Root<ISettingsService>("SettingsService")
            .Root<IProjectManager>("ProjectManager")
            .Root<IEditorService>("EditorService")
            .Root<IDocumentDockService>("DocumentDockService")
            .Root<IProjectRunnerService>("ProjectRunnerService")
            .Root<IProjectService>("ProjectService")
            .Root<IFileWatcherService>("FileWatcherService")
            .Root<IBookmarkService>("BookmarkService")
            .Root<HexIDE.Debugging.IBreakpointService>("BreakpointService")
            .Root<HexIDE.Runtime.Debugging.IDebugController>("DebugController")
            .Root<IWindowStateService>("WindowStateService")
            .Root<IAddinRegistry>("AddinRegistry")
            .Root<IAddinLoader>("AddinLoader")
            .Root<IHexIdeHost>("HexIdeHost")
            .Root<ToolBoxToolViewModel>("ToolBoxViewModel")
            .Root<TranslationEditorViewModel>("TranslationEditorViewModel")
            .Root<IPersonalityService>("PersonalityService")
            .Root<AddinProjectTemplateService>("AddinProjectTemplateService");

    public static MainViewViewModel DesignTimeRootViewModel => new DISetup().Root;
}