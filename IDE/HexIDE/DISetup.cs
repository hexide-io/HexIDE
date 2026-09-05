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
            .Bind<ILspWorkspace>().As(Singleton).To<ProjectLspWorkspace>()
            // ILspClient is the ROUTER, not one connection. It implements the same interface a single
            // server does — that interface already takes a URI and hides which server answered — so the
            // editor and view-models gained plurality without changing a line. ILanguageConnectionRegistry
            // is the same object seen from the other side: what is attached, and is it working.
            .Bind<ILspClient>().Bind<ILanguageConnectionRegistry>().As(Singleton).To(ctx =>
            {
                ctx.Inject<ILspServerLocator>(out var locator);
                ctx.Inject<ILoggerFactory>(out var loggerFactory);
                ctx.Inject<ILspWorkspace>(out var workspace);

                // The whole of #255 meets here. Defaults are contributed in code, the user's file is
                // layered over them by id, and the result becomes registrations — so the bundled server is
                // an ordinary row a user can replace or switch off, not a special case beside the file.
                //
                // Read once, here. Changes take effect on restart, so resolving later would only make
                // "when did this take effect" ambiguous.
                var log = loggerFactory.CreateLogger<DISetup>();
                var defaults = LanguageServerDefaults.For(locator, log);
                var configuration = new LanguageServerConfigLoader(
                        loggerFactory.CreateLogger<LanguageServerConfigLoader>())
                    .Load(defaults, new LanguageServerCommandStore());

                foreach (var problem in configuration.Problems)
                    log.LogWarning("Language server configuration: {Message}", problem.Message);

                var registrations = new LanguageServerRegistrationFactory(loggerFactory, workspace)
                    .Create(configuration.Entries);

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
                            false),
                    ];
                }

                return new LspClientRegistry(
                    registrations, loggerFactory.CreateLogger<LspClientRegistry>(), workspace, problems);
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
            // A root only so a test can ask it the one question it exists to answer, without first
            // building the language client. It closes a dependency cycle, and a cycle's back edge is
            // exactly what Pure.DI leaves unguarded — see ProjectLspWorkspace.
            .Root<ILspWorkspace>("LspWorkspace")
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