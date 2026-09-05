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
            // ILspClient is the ROUTER, not one connection. It implements the same interface a single
            // server does — that interface already takes a URI and hides which server answered — so the
            // editor and view-models gained plurality without changing a line. ILanguageConnectionRegistry
            // is the same object seen from the other side: what is attached, and is it working.
            .Bind<ILspClient>().Bind<ILanguageConnectionRegistry>().As(Singleton).To(ctx =>
            {
                ctx.Inject<ILspServerLocator>(out var locator);
                ctx.Inject<ILoggerFactory>(out var loggerFactory);
                ctx.Inject<ISettingsService>(out var settings);

                // Resolved here rather than inside CreateClient, because whether this server can be reached
                // at all decides whether it is REGISTERED — and that has to be known before the list is
                // built. Constructing a transport is cheap; nothing is launched until ConnectAsync, so the
                // server still starts lazily on the first document that claims it.
                var transport = CreateBundledTransport(locator, loggerFactory, settings);

                // Still the only registration HexIDE ships with. Making this list configuration — so a user
                // can attach a server, and so this row stops being a special case — is
                // hexide-io/HexIDE#255, whose first step is the explicit-command transport above.
                var registrations = new List<LanguageServerRegistration>();
                if (transport is not null)
                    registrations.Add(new LanguageServerRegistration(
                        Id: "hexide.vb6",
                        DisplayName: "HexIDE VB6 Language Server",
                        Extensions: DocumentLanguage.Vb6Extensions,
                        LanguageId: DocumentLanguage.Vb6,
                        CreateClient: () => new VBLspClient(
                            transport, loggerFactory.CreateLogger<VBLspClient>(), DocumentLanguage.Vb6)));

                return new LspClientRegistry(registrations, loggerFactory.CreateLogger<LspClientRegistry>());
            })
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

    /// <summary>
    /// Builds the transport for the bundled VB6 server, in precedence order:
    /// <list type="number">
    ///   <item>WebSocket, if <c>HEXIDE_LSP_WS_URL</c> (env, wins) or the <c>LspWebSocketUrl</c> setting is set</item>
    ///   <item>Named pipe, if <c>HEXIDE_LSP_PIPE</c> is set (<c>HEXIDE_LSP_PIPE_ROLE</c> = listen|connect,
    ///         default connect — a server already running that owns the pipe)</item>
    ///   <item>The stdio subprocess transport</item>
    /// </list>
    ///
    /// <para>
    /// This is a factory rather than a registered service because transport is a property of a <em>server</em>,
    /// not of the IDE: one server may speak stdio while another accepts only a named pipe, and a single
    /// global choice cannot describe both. These environment variables therefore configure the bundled
    /// server specifically, and are not a setting for "the" transport.
    /// </para>
    ///
    /// <para>
    /// The pipe option stays env-only and deliberately has no Options field: pointing HexIDE at a foreign
    /// server is a development activity today, and a UI field would mean a new localized label in every
    /// shipped language pack for a backend nobody can yet select.
    /// </para>
    /// </summary>
    ///
    /// <para>
    /// Returns null when the bundled server cannot be located at all. The caller then contributes no
    /// registration for it, rather than one whose transport is known in advance to fail — a server that is
    /// not there should not appear as attached-but-broken, and a registration that can never connect is a
    /// row in a list that lies.
    /// </para>
    private static ILspTransport? CreateBundledTransport(
        ILspServerLocator locator, ILoggerFactory loggerFactory, ISettingsService settings)
    {
        var wsUrl = Environment.GetEnvironmentVariable("HEXIDE_LSP_WS_URL");
        if (string.IsNullOrWhiteSpace(wsUrl)) wsUrl = settings.LspWebSocketUrl;
        if (!string.IsNullOrWhiteSpace(wsUrl))
            return new WebSocketLspTransport(wsUrl, loggerFactory.CreateLogger<WebSocketLspTransport>());

        var pipeName = Environment.GetEnvironmentVariable("HEXIDE_LSP_PIPE");
        if (!string.IsNullOrWhiteSpace(pipeName))
        {
            var role = string.Equals(
                Environment.GetEnvironmentVariable("HEXIDE_LSP_PIPE_ROLE"), "listen",
                StringComparison.OrdinalIgnoreCase)
                ? NamedPipeRole.Listen
                : NamedPipeRole.Connect;
            return new NamedPipeLspTransport(
                pipeName, role, loggerFactory.CreateLogger<NamedPipeLspTransport>());
        }

        // The locator's job, now that the transport takes an explicit command: work out where the bundled
        // server actually is. It walks up from the base directory because that path differs between a dev
        // build and a publish, which is a real problem that does not go away just because the transport
        // stopped asking the question itself.
        var serverInfo = locator.FindLspServer();
        if (serverInfo is null)
        {
            loggerFactory.CreateLogger<DISetup>()
                .LogWarning("Bundled VB6 language server not found — it contributes no registration.");
            return null;
        }

        return new StdioProcessLspTransport(serverInfo, loggerFactory.CreateLogger<StdioProcessLspTransport>());
    }

    public static MainViewViewModel DesignTimeRootViewModel => new DISetup().Root;
}