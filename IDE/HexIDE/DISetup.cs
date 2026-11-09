using HexIDE.Forms.ViewModels;
using HexIDE.Debugging;
using HexIDE.Runtime.Debugging;
using HexIDE.Conversations;
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
using HexIDE.Tools.LanguageServers;
using HexIDE.Tools.TranslationEditor;
using HexIDE.VisualDesigner;
using Microsoft.Extensions.Logging;
using static Pure.DI.Lifetime;

namespace HexIDE;

public partial class DISetup
{
    private static IWorkspaceArtifactProviderRegistry CreateArtifactProviders() =>
        new WorkspaceArtifactProviderRegistry([new HexIDE.Lsp.Providers.ClangdCompilationDatabaseProvider(),
            new HexIDE.Lsp.Providers.RdcoreProjectProvider()]);

    private static WorkspaceArtifactService CreateArtifacts(ILspWorkspace workspace,
        IWorkspaceArtifactProviderRegistry providers, IWindowManager windows,
        ILocalizationService localization, ILoggerFactory loggerFactory, ConversationLog capture) =>
        new((IWorkspaceArtifactWorkspace)workspace, providers, new WorkspaceArtifactConsent(windows, localization),
            loggerFactory.CreateLogger<WorkspaceArtifactService>(),
            System.IO.Path.Combine(UserDataPath.Directory, "lsp-artifact-ownership.json"), capture,
            new HexIDE.IDE.TypeLibraryNames());

    [Conditional("DI")]
    static void Setup() =>
        DI.Setup()
            .Bind().As(Singleton).To<ToolBoxToolViewModel>()
            .Bind().As(Singleton).To<PropertiesToolViewModel>()
            .Bind().As(Singleton).To(ctx =>
            {
                ctx.Inject<IProjectManager>(out var projectManager);
                ctx.Inject<IEventBus>(out var eventBus);
                ctx.Inject<IProjectService>(out var projectService);
                ctx.Inject<IEditorService>(out var editorService);
                ctx.Inject<ILocalizationService>(out var localization);
                ctx.Inject<ArchiveProjectService>(out var archives);
                return new ProjectToolViewModel(projectManager, eventBus, projectService, editorService, localization, archives);
            })
            .Bind().As(Singleton).To<FormLayoutToolViewModel>()
            .Bind<FindReferencesToolViewModel>().As(Singleton).To<FindReferencesToolViewModel>()
            .Bind<CallHierarchyToolViewModel>().As(Singleton).To<CallHierarchyToolViewModel>()
            .Bind().As(Singleton).To<ImmediateToolViewModel>()
            .Bind().As(Singleton).To<LocalsToolViewModel>()
            .Bind().As(Singleton).To<OutlineToolViewModel>()
            .Bind<WatchesToolViewModel>().As(Singleton).To(ctx =>
            {
                // Make the optional debugger dependency explicit instead of expanding constructor alternatives.
                ctx.Inject<ILocalizationService>(out var localization);
                ctx.Inject<HexIDE.Debugging.WatchService>(out var watches);
                ctx.Inject<HexIDE.Runtime.Debugging.IDebugController>(out var debugger);
                ctx.Inject<IWindowManager>(out var windows);
                ctx.Inject<HexIDE.Debugging.ExternalDebugService>(out var external);
                return new WatchesToolViewModel(localization, watches, debugger, windows, external);
            })
            .Bind().As(Singleton).To<CallStackToolViewModel>()
            .Bind().As(Singleton).To<RecentEditHistory>()
            .Bind().As(Singleton).To<RecentEditsToolViewModel>()
            .Bind<CodeEditorViewModel>().To(ctx =>
            {
                // Keep optional debugger dependencies explicit, as for the Watches pane above.
                ctx.Inject<IWindowManager>(out var windows);
                ctx.Inject<IEditorService>(out var editors);
                ctx.Inject<IProjectService>(out var projects);
                ctx.Inject<IEventBus>(out var events);
                ctx.Inject<ILspClient>(out var lsp);
                ctx.Inject<ISettingsService>(out var settings);
                ctx.Inject<IStatusBarService>(out var status);
                ctx.Inject<HexIDE.Bookmarks.IBookmarkService>(out var bookmarks);
                ctx.Inject<HexIDE.Debugging.IBreakpointService>(out var breakpoints);
                ctx.Inject<HexIDE.Runtime.Debugging.IDebugController>(out var debugger);
                ctx.Inject<HexIDE.Debugging.IRunScope>(out var run);
                ctx.Inject<ILocalizationService>(out var localization);
                ctx.Inject<HexIDE.Debugging.ExternalDebugService>(out var external);
                ctx.Inject<CallStackToolViewModel>(out var stack);
                ctx.Inject<RecentEditHistory>(out var recentEdits);
                return new CodeEditorViewModel(windows, editors, projects, events, lsp, settings, status,
                    bookmarks, breakpoints, debugger, run, localization, external, stack, recentEdits);
            })
            .Bind().As(Singleton).To<ColorPaletteToolViewModel>()
            .Bind().As(Singleton).To<ObjectBrowserToolViewModel>()
            .Bind().As(Singleton).To<TranslationEditorViewModel>()
            .Bind().As(Singleton).To<LanguageServersToolViewModel>()
            .Bind().As(Singleton).To<HexIDE.Tools.Diagnostics.DiagnosticsToolViewModel>()
            .Bind().As(Singleton).To<HexIDE.Tools.ProtocolInspector.ProtocolInspectorToolViewModel>()
            .Bind().As(Singleton).To<WindowManager>()
            .Bind().As(Singleton).To<ProjectManager>()
            .Bind().As(Singleton).To<EditorService>()
            .Bind().As(Singleton).To<DockFactoryReference>()
            .Bind<IRunEngine>().As(Singleton).To<InterpreterRunEngine>()
            .Bind<IDocumentDockService>().As(Singleton).To<DocumentDockService>()
            .Bind().As(Singleton).To<MainViewViewModel.DockFactory>()
            .Bind().As(Singleton).To<EventBus>()
            .Bind().As(Singleton).To(ctx =>
            {
                ctx.Inject<IEventBus>(out var eventBus);
                ctx.Inject<IProjectManager>(out var projectManager);
                ctx.Inject<ILocalizationService>(out var localization);
                ctx.Inject<IDebugController>(out var debugController);
                ctx.Inject<IBreakpointService>(out var breakpointService);
                ctx.Inject<WatchService>(out var watchService);
                ctx.Inject<RunScope>(out var runScope);
                ctx.Inject<IRunEngine>(out var runEngine);
                ctx.Inject<ExternalDebugService>(out var externalDebugger);
                ctx.Inject<HexIDE.Projects.ArchiveProjectService>(out var archives);
                ctx.Inject<HexIDE.Debugging.DebugLaunchService>(out var debugLaunches);
                ctx.Inject<HexIDE.Debugging.IRunChoiceStore>(out var runChoices);
                ctx.Inject<IStatusBarService>(out var statusBar);
                return new ProjectRunnerService(eventBus, projectManager, localization, debugController, breakpointService, watchService, runScope, runEngine, externalDebugger, archives,
                    debugLaunches, runChoices, statusBar);
            })
            .Bind().As(Singleton).To(ctx =>
            {
                ctx.Inject<Func<NewProjectViewModel>>(out var newProjectVm);
                ctx.Inject<IWindowManager>(out var windowManager);
                ctx.Inject<IEventBus>(out var eventBus);
                ctx.Inject<IProjectManager>(out var projectManager);
                ctx.Inject<IRecentProjectsService>(out var recentProjects);
                ctx.Inject<IReferenceLibraryService>(out var referenceLibraryService);
                ctx.Inject<IUserSidecarService>(out var sidecar);
                ctx.Inject<IFileBaselineStore>(out var baselineStore);
                ctx.Inject<ILocalizationService>(out var localization);
                ctx.Inject<IHeaderRefresher>(out var headerRefresher);
                ctx.Inject<ArchiveProjectService>(out var archives);
                ctx.Inject<Func<ILspClient>>(out var lspClient);
                ctx.Inject<HexIDE.Projects.ContributedProjects>(out var contributed);
                return new ProjectService(newProjectVm, windowManager, eventBus, projectManager, recentProjects, referenceLibraryService, sidecar, baselineStore, localization, headerRefresher, archives, lspClient,
                    contributed);
            })
            .Bind().As(Singleton).To<ArchiveProjectService>()
            .Bind<IFileBaselineStore>().As(Singleton).To<FileBaselineStore>()
            // One per session, and it must be the same one everywhere: it holds no state itself, but the
            // three refresh triggers reach it through three different dependents (the save path, the file
            // watcher's reloader, and its own subscription to FormLayoutChangedEvent) and a second instance
            // would mean a second, silent subscription.
            .Bind<IHeaderRefresher>().As(Singleton).To<HeaderRefresher>()
            .Bind<IClock>().As(Singleton).To<SystemClock>()
            .Bind<IFileWatcherService>().As(Singleton).To<FileWatcherService>()
            // One per session: the run's recovery folder is made once, at start, and a second timer would back up twice.
            .Bind<HexIDE.Recovery.IRecoveryOffers>().As(Singleton).To<HexIDE.Recovery.RecoveryOffers>()
            .Bind<HexIDE.Recovery.IRecoveryService>().As(Singleton).To(ctx =>
            {
                ctx.Inject<IProjectService>(out var projects);
                ctx.Inject<IClock>(out var clock);
                return new HexIDE.Recovery.RecoveryService(projects, clock);
            })
            .Bind<ISettingsService>().As(Singleton).To(_ => new SettingsService())
            .Bind<IWindowStateService>().As(Singleton).To(ctx =>
            {
                ctx.Inject<ISettingsService>(out var settings);
                return new WindowStateService(settings);
            })
            .Bind<IStatusBarService>().As(Singleton).To<StatusBarService>()
            .Bind<IRecentProjectsService>().As(Singleton).To<RecentProjectsService>()
            .Bind<IBookmarkService>().As(Singleton).To<BookmarkService>()
            // Debugger: one breakpoint store + one watch store + one interpreter controller per IDE session.
            .Bind<HexIDE.Debugging.IBreakpointService>().As(Singleton).To<HexIDE.Debugging.BreakpointService>()
            .Bind().As(Singleton).To<HexIDE.Debugging.WatchService>()
            .Bind<HexIDE.Debugging.IRunScope>().Bind().As(Singleton).To<HexIDE.Debugging.RunScope>()
            .Bind<HexIDE.Runtime.Debugging.IDebugController>().As(Singleton).To<HexIDE.Runtime.Debugging.DebugController>()
            .Bind().As(Singleton).To<HexIDE.Debugging.ExternalDebugService>()
            .Bind<HexIDE.Debugging.IRunChoiceStore>().As(Singleton).To<HexIDE.Debugging.RunChoiceStore>()
            // Bound, so the main view's optional parameter is given it rather than its default.
            .Bind().As(Singleton).To<HexIDE.Forms.ViewModels.RunWithMenuViewModel>()
            // The launches add-ins contribute for that session; F5 runs one only for a project whose user chose it.
            .Bind().As(Singleton).To(ctx =>
            {
                ctx.Inject<HexIDE.Debugging.ExternalDebugService>(out var debugger);
                ctx.Inject<ILoggerFactory>(out var loggerFactory);
                return new HexIDE.Debugging.DebugLaunchService(debugger, loggerFactory.CreateLogger<HexIDE.Debugging.DebugLaunchService>());
            })
            .Bind<ILanguageConnectionRegistry>().As(Singleton).To<HexIDE.Debugging.ExternalDebugConnectionRegistry>()
            .Bind<IUserSidecarService>().As(Singleton).To(ctx =>
            {
                ctx.Inject<IBookmarkService>(out var bookmarks);
                ctx.Inject<HexIDE.Debugging.IBreakpointService>(out var breakpoints);
                ctx.Inject<IProjectManager>(out var projectManager);
                ctx.Inject<HexIDE.Debugging.IRunChoiceStore>(out var runChoices);
                return new UserSidecarService(bookmarks, breakpoints, projectManager, runChoices);
            })
            .Bind<IFindReplaceService>().As(Singleton).To<FindReplaceService>()
            .Bind().As(Singleton).To(ctx =>
            {
                ctx.Inject<IDocumentDockService>(out var documentDockService);
                ctx.Inject<IProjectManager>(out var projectManager);
                ctx.Inject<ArchiveProjectService>(out var archives);
                return new FocusedProjectUtil(documentDockService, projectManager, archives);
            })
            .Bind<IVb6ToolchainService>().As(Singleton).To<Vb6ToolchainService>()
            .Bind<IReferenceLibraryService>().As(Singleton).To<ReferenceLibraryService>()
            .Bind<ITypeLibraryService>().As(Singleton).To<TypeLibraryService>()
            .Bind<IComponentRegistry>().As(Singleton).To<ComponentRegistry>()
            // LSP
            // The configuration, read exactly once for the session.
            //
            // It used to be read inside the router's own factory, which was fine until the capture needed
            // its limits: the capture is constructed first, so it would have had to read the file a second
            // time and the two reads could disagree about what the user wrote. One binding, two consumers.
            // Changes still take effect on restart, so nothing here resolves it later.
            .Bind<LanguageServerConfigResult>().As(Singleton).To(ctx =>
            {
                ctx.Inject<ILspServerLocator>(out var locator);
                ctx.Inject<ILoggerFactory>(out var loggerFactory);
                ctx.Inject<IWorkspaceArtifactProviderRegistry>(out var providers);

                var log = loggerFactory.CreateLogger<DISetup>();
                var defaults = LanguageServerDefaults.For(locator, log);

                return new LanguageServerConfigLoader(
                        loggerFactory.CreateLogger<LanguageServerConfigLoader>())
                    .Load(defaults, new LanguageServerCommandStore(), providers);
            })
            // One record for the session, shared by every connection and outliving each of them. A server
            // that dies and is respawned gets a new client and keeps its history, because the history
            // belongs to the connection rather than to whichever process was serving it.
            // ONE mapping for the whole session, and a singleton for a reason rather than by habit: two
            // exports of the same conversation have to agree about what each path is called, or comparing
            // them is impossible. It is also what bounds the disclosure — the permutation is drawn from
            // the cryptographic generator at construction and is never written anywhere, so it dies with
            // the process.
            .Bind<HexIDE.Redaction.Pseudonymiser>().As(Singleton).To(_ => new HexIDE.Redaction.Pseudonymiser())
            .Bind<ConversationLog>().As(Singleton).To(ctx =>
            {
                ctx.Inject<LanguageServerConfigResult>(out var configuration);

                // `--capture-lsp` is honoured HERE rather than in the desktop startup hook, which is the
                // only place early enough. Servers start on the first document of a language they claim,
                // and that happens while the shell is still building — long before the hook runs.
                return new ConversationLog(
                    configuration.CaptureLimits,
                    perConnection: configuration.PerServerCaptureLimits,
                    armEveryConnection: Static.CaptureLsp);
            })
            .Bind<ILspServerLocator>().As(Singleton).To<LspServerLocator>()
            .Bind<ILspWorkspace>().As(Singleton).To<ProjectLspWorkspace>()
            .Bind<IWorkspaceArtifactProviderRegistry>().As(Singleton).To(_ =>
                CreateArtifactProviders())
            // ILspClient is the ROUTER, not one connection. It implements the same interface a single
            // server does — that interface already takes a URI and hides which server answered — so the
            // editor and view-models gained plurality without changing a line. The connection registry above
            // combines this router's connections with the external debug session.
            .Bind<ILspClient>().As(Singleton).To(ctx =>
            {
                ctx.Inject<ILoggerFactory>(out var loggerFactory);
                ctx.Inject<ILspWorkspace>(out var workspace);
                ctx.Inject<ConversationLog>(out var capture);
                ctx.Inject<HexIDE.Redaction.Pseudonymiser>(out var pseudonyms);
                ctx.Inject<IWorkspaceArtifactProviderRegistry>(out var providers);
                ctx.Inject<IWindowManager>(out var windows);
                ctx.Inject<ILocalizationService>(out var localization);
                var artifacts = CreateArtifacts(workspace, providers, windows, localization, loggerFactory, capture);
                ctx.Inject<LanguageServerConfigResult>(out var configuration);

                // The whole of #255 meets here. Defaults are contributed in code, the user's file is
                // layered over them by id, and the result becomes registrations — so the bundled server is
                // an ordinary row a user can replace or switch off, not a special case beside the file.
                var log = loggerFactory.CreateLogger<DISetup>();

                foreach (var problem in configuration.Problems)
                    log.LogWarning("Language server configuration: {Message}", problem.Message);

                ctx.Inject<LanguageServerRegistrationFactory>(out var factory);
                var registrations = factory.Create(configuration.Entries);

                // Distinguishable from "servers fine, nothing to say" — the confusion #231 documents, and
                // which a user has no way to tell apart from inside the editor.
                var problems = configuration.Problems;
                if (registrations.Count == 0)
                {
                    log.LogWarning("No language servers are configured; language features are unavailable.");
                    problems =
                    [
                        .. problems,
                        new LanguageServerConfigProblem(
                            null,
                            "No language servers are configured, so language features are unavailable.",
                            false).WithDetails("NoServers"),
                    ];
                }

                return new LspClientRegistry(
                    registrations, loggerFactory.CreateLogger<LspClientRegistry>(), workspace, problems, artifacts);
            })
            // One factory for the configured servers and the ones add-ins contribute, so both are made the same way.
            .Bind<LanguageServerRegistrationFactory>().As(Singleton).To(ctx =>
            {
                ctx.Inject<ILoggerFactory>(out var loggerFactory);
                ctx.Inject<ILspWorkspace>(out var workspace);
                ctx.Inject<ConversationLog>(out var capture);
                ctx.Inject<HexIDE.Redaction.Pseudonymiser>(out var pseudonyms);
                ctx.Inject<IWorkspaceArtifactProviderRegistry>(out var providers);
                ctx.Inject<ILocalizationService>(out var localization);
                ctx.Inject<ISettingsService>(out var settings);
                ctx.Inject<Func<IEditorService>>(out var editors);
                ctx.Inject<Func<IProjectRunnerService>>(out var runners);
                ctx.Inject<IWindowManager>(out var windows);
                var serverPrompts = new ServerMessagePrompt(() => runners().IsRunning || windows.IsNativeDialogOpen);
                // Taken when the composition builds this factory, not when a request arrives. A server's
                // showDocument and applyEdit arrive on the JSON-RPC thread, and asking Dispatcher.UIThread there
                // while a headless test's setup has it cleared makes that thread the UI thread (#2530).
                var uiThread = Avalonia.Threading.Dispatcher.UIThread;
                var serverDocuments = new ServerDocumentNavigation(serverPrompts, localization,
                    (uri, token) => uiThread.InvokeAsync(
                        () => Avalonia.Controls.TopLevel.GetTopLevel(Static.MainView)!.Launcher.LaunchUriAsync(uri),
                        Avalonia.Threading.DispatcherPriority.Normal, token).GetTask().Unwrap());
                return new LanguageServerRegistrationFactory(loggerFactory, workspace, capture, providers,
                    new HexIDE.Redaction.ConversationRedactor(pseudonyms),
                    () => localization.ResolvedLanguage, settings,
                    async (title, request, token) => request.External
                        ? await serverDocuments.ShowExternalAsync(title, request.Uri, token)
                        : await uiThread.InvokeAsync(
                            () => editors().ShowDocument(request), Avalonia.Threading.DispatcherPriority.Normal, token),
                    serverPrompts.ShowAsync,
                    (edit, documentFor, token) => uiThread.InvokeAsync(
                        () => editors().ApplyWorkspaceEdit(edit, documentFor), Avalonia.Threading.DispatcherPriority.Normal, token).GetTask());
            })
            // The servers add-ins contribute (#2760), added to the router above while the IDE runs. The ids the
            // configuration names, switched off or not, keep their entries.
            .Bind<LanguageServerContributions>().As(Singleton).To(ctx =>
            {
                ctx.Inject<ILspClient>(out var router);
                ctx.Inject<LanguageServerRegistrationFactory>(out var factory);
                ctx.Inject<LanguageServerConfigResult>(out var configuration);
                ctx.Inject<IWorkspaceArtifactProviderRegistry>(out var providers);
                ctx.Inject<ILoggerFactory>(out var loggerFactory);
                return new LanguageServerContributions(
                    (LspClientRegistry)router, factory,
                    new LanguageServerConfigLoader(loggerFactory.CreateLogger<LanguageServerConfigLoader>()),
                    configuration.Entries.Select(entry => entry.Id).OfType<string>().ToHashSet(StringComparer.Ordinal),
                    loggerFactory.CreateLogger<LanguageServerContributions>(),
                    new LanguageServerCommandStore(), providers);
            })
            .Bind<ILoggerFactory>().As(Singleton).To(_ => LoggingSetup.LoggerFactory)
            .Bind<ILogger<TT>>().As(Singleton).To<Logger<TT>>()
            // Pin production constructors: path/snapshot overloads are test seams, not dependencies.
            // Profiles
            .Bind<IProfileService>().As(Singleton).To(_ => new ProfileService())
            // Theming
            .Bind<IThemeService>().As(Singleton).To(_ => new ThemeService())
            // Keymaps
            .Bind<IKeymapService>().As(Singleton).To(_ => new KeymapService())
            // Localization
            .Bind<IUserTranslationsService>().As(Singleton).To(_ => new UserTranslationsService())
            .Bind<ILocalizationService>().As(Singleton).To(ctx =>
            {
                ctx.Inject<IUserTranslationsService>(out var translations);
                ctx.Inject<IDeveloperModeService>(out var developerMode);
                return new LocalizationService(Static.Profile, translations, developerMode);
            })
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
            .Bind().As(Singleton).To<LanguageServerMessageReporter>()
            .Bind().As(Singleton).To<AddinCommandService>()
            .Bind().As(Singleton).To<AddinProjectTemplateService>()
            // The project types add-ins contribute, which Open Project and the recent list open as projects.
            .Bind().As(Singleton).To<HexIDE.Projects.ContributedProjects>()
            .Bind().As(Singleton).To<AddinOptionsService>()
            .Bind<IPackageVerifier>().As(Singleton).To<PackageVerifier>()
            .Bind<IAddinRegistry, IAddinLoader>().As(Singleton).To(ctx =>
            {
                // Use the production constructor rather than searching the test-only path overloads.
                ctx.Inject<IPackageVerifier>(out var verifier);
                ctx.Inject<ISettingsService>(out var settings);
                ctx.Inject<IDeveloperModeService>(out var developerMode);
                ctx.Inject<IWindowManager>(out var windows);
                ctx.Inject<ILocalizationService>(out var localization);
                return new AddinRegistry(verifier, settings, developerMode, windows, localization);
            })
            .Bind<IHexIdeHost>().As(Singleton).To<HexIdeHost>()
            .Root<MainViewViewModel>("Root")
            .Root<ILspClient>("LspClient")
            // The conversation record. A root because the automation tools read it directly, and Pure.DI
            // resolves only what is declared as one — a singleton reachable transitively is not enough.
            .Root<HexIDE.Conversations.ConversationLog>("Capture")
            .Root<HexIDE.Redaction.Pseudonymiser>("Pseudonyms")
            .Root<LanguageServerMessageReporter>("LanguageServerMessages")
            .Root<IThemeService>("ThemeService")
            .Root<IKeymapService>("KeymapService")
            .Root<ILocalizationService>("LocalizationService")
            .Root<IUserTranslationsService>("UserTranslationsService")
            .Root<ILanguageSwitchService>("LanguageSwitchService")
            .Root<ISettingsService>("SettingsService")
            .Root<IProjectManager>("ProjectManager")
            // A root only so a test can ask it the one question it exists to answer, without first
            // building the language client. It closes a dependency cycle, and a cycle's back edge is
            // exactly what Pure.DI leaves unguarded — see ProjectLspWorkspace.
            .Root<ILspWorkspace>("LspWorkspace")
            .Root<IEditorService>("EditorService")
            .Root<IDocumentDockService>("DocumentDockService")
            .Root<IProjectRunnerService>("ProjectRunnerService")
            .Root<IProjectService>("ProjectService")
            .Root<IEventBus>("EventBus")
            .Root<IUserSidecarService>("UserSidecarService")
            .Root<IFileWatcherService>("FileWatcherService")
            .Root<HexIDE.Recovery.IRecoveryService>("Recovery")
            .Root<HexIDE.Recovery.IRecoveryOffers>("RecoveryOffers")
            .Root<IBookmarkService>("BookmarkService")
            .Root<HexIDE.Debugging.IBreakpointService>("BreakpointService")
            .Root<HexIDE.Runtime.Debugging.IDebugController>("DebugController")
            .Root<HexIDE.Debugging.ExternalDebugService>("ExternalDebugger")
            .Root<HexIDE.Debugging.IRunChoiceStore>("RunChoices")
            .Root<IWindowStateService>("WindowStateService")
            .Root<IAddinRegistry>("AddinRegistry")
            .Root<IAddinLoader>("AddinLoader")
            .Root<IHexIdeHost>("HexIdeHost")
            .Root<ToolBoxToolViewModel>("ToolBoxViewModel")
            .Root<TranslationEditorViewModel>("TranslationEditorViewModel")
            .Root<LanguageServersToolViewModel>("LanguageServersToolViewModel")
            .Root<HexIDE.Tools.ProtocolInspector.ProtocolInspectorToolViewModel>("ProtocolInspectorToolViewModel")
            .Root<IProfileService>("ProfileService")
            .Root<AddinProjectTemplateService>("AddinProjectTemplateService");

    public static MainViewViewModel DesignTimeRootViewModel => new DISetup().Root;
}
