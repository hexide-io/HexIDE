using System.Linq;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HexIDE.IDE;
using HexIDE.Keymaps;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Lsp;
using HexIDE.Tools.LanguageServers;
using HexIDE.Themes;
using HexIDE.Tools;
using HexIDE.Tools.ObjectBrowser;
using HexIDE.Tools.TranslationEditor;
using HexIDE.VisualDesigner;
using HexIDE.Conversations;
using HexIDE.Tools.ProtocolInspector;

namespace HexIDE.Tests.ViewModels;

public class MainViewViewModelTests
{
    private readonly IWindowManager _windowManager = Substitute.For<IWindowManager>();
    private readonly IProjectManager _projectManager = Substitute.For<IProjectManager>();
    private readonly IFocusedProjectUtil _focusedProjectUtil = Substitute.For<IFocusedProjectUtil>();
    private readonly IProjectService _projectService = Substitute.For<IProjectService>();
    private readonly IProjectRunnerService _runnerService = Substitute.For<IProjectRunnerService>();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly IVb6ToolchainService _vb6Toolchain = Substitute.For<IVb6ToolchainService>();
    private readonly IRecentProjectsService _recentProjects = Substitute.For<IRecentProjectsService>();
    private readonly IFindReplaceService _findReplace = Substitute.For<IFindReplaceService>();
    private readonly ISettingsService _settingsService = Substitute.For<ISettingsService>();
    private readonly IStatusBarService _statusBarService = Substitute.For<IStatusBarService>();
    private readonly HexIDE.Runtime.Debugging.IDebugController _debugController = Substitute.For<HexIDE.Runtime.Debugging.IDebugController>();
    private readonly ILocalizationService _localization = Substitute.For<ILocalizationService>();
    private readonly MainViewViewModel _sut;

    public MainViewViewModelTests()
    {
        _projectManager.LoadedProjects.Returns(new List<ProjectDefinition>());
        _settingsService.IsStandardToolbarVisible.Returns(true);
        _settingsService.ActiveLanguage.Returns("en");

        _sut = Build(_runnerService);
    }

    private static System.IDisposable MenuTheme()
    {
        var theme = new Avalonia.Themes.Simple.SimpleTheme();
        Avalonia.Application.Current!.Styles.Add(theme);
        return R3.Disposable.Create(() => Avalonia.Application.Current!.Styles.Remove(theme));
    }

    private static System.IDisposable ShowMenu(MainView view)
    {
        var window = new Avalonia.Controls.Window { Content = view };
        window.Show();
        var item = Avalonia.Controls.ControlExtensions.FindControl<Avalonia.Controls.MenuItem>(view, "RemoveProjectMemberMenuItem")!;
        ((Avalonia.Controls.MenuItem)item.Parent!).IsSubMenuOpen = true;
        return R3.Disposable.Create(window.Close);
    }

    private ProjectService RemovalService(bool cancel = false)
    {
        _windowManager.ShowDialog(Arg.Any<IDialog>()).Returns(ci =>
        {
            if (ci.Arg<IDialog>() is HexIDE.Forms.ViewModels.SaveChangesViewModel prompt)
            {
                prompt.No();
                return Task.FromResult(!cancel);
            }
            return Task.FromResult(true);
        });
        return new ProjectService(() => throw new System.InvalidOperationException(), _windowManager,
            _eventBus, _projectManager, _recentProjects, Substitute.For<IReferenceLibraryService>(),
            Substitute.For<HexIDE.Sidecar.IUserSidecarService>(), new FileBaselineStore(), _localization,
            Substitute.For<IHeaderRefresher>());
    }

    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData(ModuleKind.StandardModule)]
    [InlineData(ModuleKind.ClassModule)]
    [InlineData(ModuleKind.UserControl)]
    [InlineData(ModuleKind.PropertyPage)]
    public void ProjectRemoveMenu_RemovesTheNamedSelection(ModuleKind? kind)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1");
        var document = kind is { } moduleKind
            ? DocumentIdentity.For(new ModuleDefinition(project, "Member1", moduleKind))
            : DocumentIdentity.For(new FormDefinition(project, HexIDE.Runtime.Components.FormComponentClass.Instance, "Member1"));
        if (document.Module is { } module) project.AddModule(module);
        else project.AddForm(document.Form!);
        _projectManager.LoadedProjects.Returns(new List<ProjectDefinition> { project });
        var root = Build(_runnerService, projects: RemovalService());
        var tree = root.ProjectExplorer.LoadedProjects.Single();
        root.ProjectExplorer.SelectedItem = tree.Elements.OfType<IProjectFileNode>().Single();
        _localization.GetString("Str.Menu.Project.RemoveFormFormat").Returns("Remove {0}");
        using var theme = MenuTheme();
        var view = new MainView { DataContext = root };
        using var window = ShowMenu(view);
        var item = Avalonia.Controls.ControlExtensions.FindControl<Avalonia.Controls.MenuItem>(view, "RemoveProjectMemberMenuItem")!;

        item.Should().NotBeNull();
        item.Header.Should().Be("Remove Member1");
        item.IsEffectivelyEnabled.Should().BeTrue();
        item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent));

        DocumentLookup.DocumentsOf(project).Should().BeEmpty();
        item.IsEffectivelyEnabled.Should().BeFalse();
        _windowManager.Received(1).ShowDialog(Arg.Is<IDialog>(d => d is HexIDE.Forms.ViewModels.SaveChangesViewModel));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProjectRemoveMenu_UsesFocusedMemberAndRetainsItOnCancellation(bool cancel)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1");
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        project.AddModule(module);
        var dock = Substitute.For<IDocumentDockService>();
        var editor = Substitute.For<HexIDE.Forms.ViewModels.BaseEditorWindowViewModel>();
        editor.OpenDocument.Returns(DocumentIdentity.For(module));
        dock.ActiveDocument.Returns(editor);
        var root = Build(_runnerService, dock, projects: RemovalService(cancel));
        _localization.GetString("Str.Menu.Project.RemoveFormFormat").Returns("Remove {0}");
        using var theme = MenuTheme();
        var view = new MainView { DataContext = root };
        using var window = ShowMenu(view);
        var item = Avalonia.Controls.ControlExtensions.FindControl<Avalonia.Controls.MenuItem>(view, "RemoveProjectMemberMenuItem")!;
        item.Header.Should().Be("Remove Module1");
        item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent));

        _windowManager.Received(1).ShowDialog(Arg.Is<IDialog>(d => d is HexIDE.Forms.ViewModels.SaveChangesViewModel));
        project.Modules.Contains(module).Should().Be(cancel);
    }

    [AvaloniaFact]
    public void ProjectRemoveMenu_DisabledWithoutAMember()
    {
        using var theme = MenuTheme();
        var view = new MainView { DataContext = _sut };
        using var window = ShowMenu(view);
        var item = Avalonia.Controls.ControlExtensions.FindControl<Avalonia.Controls.MenuItem>(view, "RemoveProjectMemberMenuItem")!;
        item.Should().NotBeNull();
        item.IsEffectivelyEnabled.Should().BeFalse();
    }

    [AvaloniaFact]
    public void OpenAddInManager_OpensOptionsAtAddIns()
    {
        _sut.OpenAddInManager();

        _windowManager.Received(1).ShowDialog(Arg.Is<HexIDE.Forms.ViewModels.OptionsViewModel>(
            vm => vm.SelectedNode != null && vm.SelectedNode.Key == "Str.Options.Node.AddIns"));
    }

    /// <summary>
    /// The view model over the given runner. A substitute serves most tests; a real runner lets a test start
    /// where F5 does and end at what the user is shown.
    /// </summary>
    private MainViewViewModel Build(IProjectRunnerService runner, IDocumentDockService? dock = null,
        HexIDE.Debugging.IBreakpointService? breakpoints = null, IProjectService? projects = null,
        HexIDE.Recovery.IRecoveryOffers? recoveryOffers = null,
        HexIDE.Debugging.ExternalDebugService? external = null, IEditorService? editor = null)
    {
        projects ??= _projectService;
        var mockDocDock = dock ?? Substitute.For<IDocumentDockService>();
        var loc = _localization;
        loc.GetString("Str.MainWindow.NoProjectTitle").Returns("HexIDE [design]");
        loc.GetString("Str.MainWindow.DesignTag").Returns("[design]");
        loc.GetString("Str.MainWindow.RunTag").Returns("[run]");
        loc.GetString("Str.MainWindow.BreakTag").Returns("[break]");
        loc.GetString("Str.StatusBar.Running").Returns("Running…");
        loc.ActiveLanguage.Returns("en");

        // ToolBoxToolViewModel loads Avalonia bitmaps in its constructor;
        // bypass with an uninitialized instance since tests never touch it.
        var toolBox = (ToolBoxToolViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ToolBoxToolViewModel));

        var properties = new PropertiesToolViewModel(mockDocDock, _eventBus, _windowManager, loc);
        var immediate = new ImmediateToolViewModel(loc, Substitute.For<HexIDE.Runtime.Debugging.IDebugController>());
        var formLayout = new FormLayoutToolViewModel(mockDocDock, _eventBus, loc);
        var locals = new LocalsToolViewModel(loc, Substitute.For<HexIDE.Runtime.Debugging.IDebugController>());
        var watches = new WatchesToolViewModel(loc, new HexIDE.Debugging.WatchService(), Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(), Substitute.For<HexIDE.IDE.IWindowManager>());
        var callStack = new CallStackToolViewModel(loc, Substitute.For<HexIDE.Runtime.Debugging.IDebugController>());
        var editorService = editor ?? Substitute.For<IEditorService>();
        var projectExplorer = new ProjectToolViewModel(_projectManager, _eventBus, projects, editorService, loc);
        var colorPalette = new ColorPaletteToolViewModel(mockDocDock);
        var objectBrowser = new ObjectBrowserToolViewModel(_projectManager, Substitute.For<ILspClient>(), editorService, Substitute.For<IComponentRegistry>(), Substitute.For<ITypeLibraryService>(), Substitute.For<IFocusedProjectUtil>(), loc,
            Substitute.For<ILanguageConnectionRegistry>());
        var translationEditor = new TranslationEditorViewModel(loc, Substitute.For<IUserTranslationsService>(), _windowManager);
        var windowStateService = Substitute.For<IWindowStateService>();

        // A registry with nothing attached: the view model reads Connections and
        // ConfigurationProblems in its constructor.
        var lsRegistry = Substitute.For<ILanguageConnectionRegistry>();
        lsRegistry.Connections.Returns([]);
        lsRegistry.ConfigurationProblems.Returns([]);
        lsRegistry.Diagnostics.Returns([]);
        var languageServers = new LanguageServersToolViewModel(
            lsRegistry, loc, new ConversationLog(), Substitute.For<IEventBus>(),
            new HexIDE.Redaction.Pseudonymiser());
        var protocolInspector = new ProtocolInspectorToolViewModel(
            new ConversationLog(), loc, new HexIDE.Redaction.Pseudonymiser(), Substitute.For<IWindowManager>());

        var dockFactory = new MainViewViewModel.DockFactory(
            toolBox, projectExplorer, properties, formLayout,
            immediate, locals, watches, callStack, colorPalette, objectBrowser, translationEditor,
            languageServers,
            protocolInspector,
            windowStateService, new DockFactoryReference(), new HexIDE.Tools.OutlineToolViewModel(
                Substitute.For<HexIDE.Localization.ILocalizationService>(), Substitute.For<IDocumentDockService>()));

        var languageSwitch = Substitute.For<ILanguageSwitchService>();
        languageSwitch.ActiveLanguage.Returns("en");
        languageSwitch.AvailableLanguages.Returns([new LanguageInfo("en", "English", "ltr")]);
        languageSwitch.RegionsFor(Arg.Any<string>()).Returns([]);

        return new MainViewViewModel(
            _windowManager,
            toolBox,
            properties,
            immediate,
            formLayout,
            locals,
            watches,
            callStack,
            projectExplorer,
            colorPalette,
            objectBrowser,
            translationEditor,
            languageServers,
            protocolInspector,
            new HexIDE.Tools.Diagnostics.DiagnosticsToolViewModel(lsRegistry, Substitute.For<HexIDE.Lsp.ILspClient>(),
                mockDocDock, editorService, loc),
            _projectManager,
            _focusedProjectUtil,
            projects,
            editorService,
            mockDocDock,
            dockFactory,
            runner,
            _eventBus,
            _vb6Toolchain,
            _recentProjects,
            _findReplace,
            _settingsService,
            Substitute.For<IThemeService>(),
            Substitute.For<IKeymapService>(),
            languageSwitch,
            loc,
            Substitute.For<IAddinRegistry>(),
            new AddinOptionsService(),
            Substitute.For<IDeveloperModeService>(),
            _statusBarService,
            Substitute.For<IProfileService>(),
            new AddinMenuService(),
            new AddinCommandService(),
            new AddinToolWindowService(),
            windowStateService,
            breakpoints ?? Substitute.For<HexIDE.Debugging.IBreakpointService>(),
            _debugController,
            new RecentEditsToolViewModel(new RecentEditHistory(_eventBus), () => editorService, loc),
            recoveryOffers: recoveryOffers, externalDebugger: external);
    }

    [AvaloniaTheory]
    [InlineData("goto-refuse", "goto")]
    [InlineData("resume-refusal", "next")]
    public async Task Refused_external_move_keeps_caret_scroll_and_inspection(string mode, string command)
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hexide-reveal-" + Guid.NewGuid());
        System.IO.Directory.CreateDirectory(directory);
        await using var log = new ConversationLog();
        var external = new HexIDE.Debugging.ExternalDebugService(log);
        using var theme = MenuTheme();
        var editorTheme = new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://AvaloniaEdit/"))
        {
            Source = new Uri("avares://AvaloniaEdit/Themes/Simple/AvaloniaEdit.xaml"),
        };
        Avalonia.Application.Current!.Styles.Add(editorTheme);
        var editor = new AvaloniaEdit.TextEditor { Text = string.Join("\n", Enumerable.Repeat("Debug.Print 1", 200)) };
        var window = new Avalonia.Controls.Window { Width = 400, Height = 200, Content = editor };
        var navigation = Substitute.For<IEditorService>();
        navigation.NavigateTo(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
        {
            editor.TextArea.Caret.Line = call.ArgAt<int>(1);
            editor.ScrollToLine(call.ArgAt<int>(1));
            return true;
        });
        var sut = Build(_runnerService, external: external, editor: navigation);
        window.Show();
        var executable = System.IO.Path.GetFullPath("../../../dotnet" + (OperatingSystem.IsWindows() ? ".exe" : ""),
            System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory());
        var file = System.IO.Path.Combine(directory, "launch.json");
        await System.IO.File.WriteAllTextAsync(file, System.Text.Json.JsonSerializer.Serialize(new
        {
            executable, arguments = new[] { typeof(HexIDE.Dap.TestAdapter.Program).Assembly.Location, mode },
            adapterId = "fixture", launch = new { },
        }));
        var run = external.StartAsync(file);
        async Task Until(Func<bool> done)
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!done())
            {
                DateTime.UtcNow.Should().BeBefore(deadline);
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
        }
        try
        {
            await Until(() => external.CanEvaluate && external.Variables.Count == 1);
            navigation.Received().NavigateTo(Arg.Any<string>(), 6, 1);
            editor.TextArea.Caret.Line = 100;
            editor.ScrollToLine(100);
            window.UpdateLayout();
            var caret = editor.CaretOffset;
            var scroll = editor.VerticalOffset;
            scroll.Should().BeGreaterThan(0);
            var frames = external.Frames;
            var variables = external.Variables;
            var location = external.CurrentLocation;
            var stop = external.Stop;
            navigation.ClearReceivedCalls();

            if (command == "goto") await external.SetNextStatementAsync(new Uri(System.IO.Path.Combine(directory, "Module1.bas")).AbsoluteUri, 100);
            else await external.CommandAsync(command);
            await Until(() => external.CanEvaluate && external.Stop != stop);

            editor.CaretOffset.Should().Be(caret);
            editor.VerticalOffset.Should().Be(scroll);
            navigation.DidNotReceive().NavigateTo(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>());
            external.Frames.Should().BeSameAs(frames);
            external.Variables.Should().BeSameAs(variables);
            external.CurrentLocation.Should().Be(location);
            GC.KeepAlive(sut);
        }
        finally
        {
            window.Close();
            Avalonia.Application.Current!.Styles.Remove(editorTheme);
            await external.StopAsync();
            await run.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            System.IO.Directory.Delete(directory, true);
        }
    }

    // --- Add File: a file that joined read byte for byte ---

    private ProjectDefinition PickToAdd(string path, Adopted<ModuleDefinition> adopted)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1");
        _projectManager.StartupProject.Returns(project);
        _focusedProjectUtil.FocusedOrStartupProject.Returns(project);
        _windowManager.OpenFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerOpenOptions>())
            .Returns(Task.FromResult<IReadOnlyList<string>?>([path]));
        _projectService.AddExistingModule(project, path, ModuleKind.StandardModule).Returns(Task.FromResult(adopted));
        return project;
    }

    [AvaloniaFact]
    public void Add_File_warns_when_the_file_joined_read_byte_for_byte()
    {
        var path = System.IO.Path.Join(System.IO.Path.GetTempPath(), "Western.bas");
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1");
        PickToAdd(path, Adopted<ModuleDefinition>.Added(
            new ModuleDefinition(project, "Western", ModuleKind.StandardModule), "Western", unreadableInCodePage: 932));

        _sut.AddFileCommand.Execute(null);

        _windowManager.Received(1).MessageBox(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), MessageBoxIcon.Warning);
    }

    [AvaloniaFact]
    public void Add_File_says_nothing_when_the_file_was_read_normally()
    {
        var path = System.IO.Path.Join(System.IO.Path.GetTempPath(), "Plain.bas");
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1");
        PickToAdd(path, Adopted<ModuleDefinition>.Added(
            new ModuleDefinition(project, "Plain", ModuleKind.StandardModule), "Plain"));

        _sut.AddFileCommand.Execute(null);

        _windowManager.DidNotReceive().MessageBox(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>());
    }

    // Regression for hexide-io/HexIDE#2965; the final comment specifies Ready at a stop.
    [AvaloniaTheory]
    [InlineData("stopped", "[break]", "Ready")]
    [InlineData("running", "[run]", "Running…")]
    [InlineData("ended", "[design]", "Ready")]
    public async Task External_session_refreshes_titles_and_status(string state, string tag, string message)
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hexide-dap-title-" + System.Guid.NewGuid());
        System.IO.Directory.CreateDirectory(directory);
        await using var log = new ConversationLog();
        var external = new HexIDE.Debugging.ExternalDebugService(log);
        _focusedProjectUtil.FocusedOrStartupProject.Returns(new ProjectDefinition(VBProjectType.EXE, "MyApp"));
        var sut = Build(_runnerService, external: external);
        var status = "stale";
        _statusBarService.When(x => x.ClearMessage()).Do(_ => status = "Ready");
        _statusBarService.When(x => x.SetMessage(Arg.Any<string>())).Do(c => status = c.Arg<string>());
        var changed = new List<string?>();
        sut.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var run = external.StartAsync(await DebugAdapter.ExternalDebugServiceTests.Configuration(directory, "evaluate-running"));
        try
        {
            await DebugAdapter.ExternalDebugServiceTests.WaitFor(external, () => external.CanEvaluate);
            changed.Clear();
            status = "stale";
            if (state == "ended")
            {
                await external.StopAsync();
                await run;
            }
            else if (state == "running")
            {
                await external.CommandAsync("continue");
                await DebugAdapter.ExternalDebugServiceTests.WaitFor(external, () => external.State == HexIDE.Dap.DapSessionState.Running);
            }
            else
            {
                var stop = external.Stop;
                await external.CommandAsync("next");
                await DebugAdapter.ExternalDebugServiceTests.WaitFor(external, () => external.Stop != stop && external.CanEvaluate);
            }
            sut.Title.Should().Be("MyApp - HexIDE " + tag);
            sut.WindowTitle.Should().Be(sut.Title);
            status.Should().Be(message);
            changed.Should().Contain(nameof(sut.Title)).And.Contain(nameof(sut.WindowTitle));
        }
        finally
        {
            await external.StopAsync();
            await run;
            System.IO.Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public async Task Ending_external_session_restores_design_before_disconnect_completes()
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hexide-dap-end-" + System.Guid.NewGuid());
        System.IO.Directory.CreateDirectory(directory);
        await using var log = new ConversationLog();
        var external = new HexIDE.Debugging.ExternalDebugService(log);
        // As the real ProjectRunnerService does: an external session counts as running until its cleanup ends.
        _runnerService.IsRunning.Returns(_ => external.IsActive);
        _focusedProjectUtil.FocusedOrStartupProject.Returns(new ProjectDefinition(VBProjectType.EXE, "MyApp"));
        var sut = Build(_runnerService, external: external);
        var status = "stale";
        _statusBarService.When(x => x.ClearMessage()).Do(_ => status = "Ready");
        _statusBarService.When(x => x.SetMessage(Arg.Any<string>())).Do(c => status = c.Arg<string>());
        var changed = new List<string?>();
        sut.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var disconnectPending = false;
        external.Output += text => disconnectPending |= text.Contains("disconnect-pending", StringComparison.Ordinal);
        var run = external.StartAsync(await DebugAdapter.ExternalDebugServiceTests.Configuration(directory, "disconnect-held"));
        try
        {
            await DebugAdapter.ExternalDebugServiceTests.WaitFor(external, () => external.CanEvaluate);
            changed.Clear();
            status = "stale";
            var ending = external.StopAsync();
            await DebugAdapter.ExternalDebugServiceTests.WaitFor(external, () => disconnectPending);

            external.State.Should().Be(HexIDE.Dap.DapSessionState.Ended);
            external.IsActive.Should().BeTrue("adapter cleanup is still pending");
            ending.IsCompleted.Should().BeFalse();
            run.IsCompleted.Should().BeFalse();
            sut.Title.Should().Be("MyApp - HexIDE [design]");
            sut.WindowTitle.Should().Be(sut.Title);
            status.Should().Be("Ready");
            changed.Should().Contain(nameof(sut.Title)).And.Contain(nameof(sut.WindowTitle));
        }
        finally
        {
            await external.StopAsync();
            await run;
            System.IO.Directory.Delete(directory, true);
        }
    }

    // --- Title ---

    [AvaloniaFact]
    public void Title_NoFocusedProject_ReturnsDesignDefault()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns((ProjectDefinition?)null);

        _sut.Title.Should().Be("HexIDE [design]");
    }

    [AvaloniaFact]
    public void Title_WithFocusedProject_DesignMode()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns(TestHelpers.CreateProject("MyApp"));
        _runnerService.IsRunning.Returns(false);

        _sut.Title.Should().Be("MyApp - HexIDE [design]");
    }

    [AvaloniaFact]
    public void Title_WithFocusedProject_RunMode()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns(TestHelpers.CreateProject("MyApp"));
        _runnerService.IsRunning.Returns(true);

        _sut.Title.Should().Be("MyApp - HexIDE [run]");
    }

    [AvaloniaFact]
    public void Title_Paused_ShowsBreak()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns(TestHelpers.CreateProject("MyApp"));
        _runnerService.IsRunning.Returns(true);
        _debugController.State.Returns(HexIDE.Runtime.Debugging.DebugState.Paused);

        _sut.Title.Should().Be("MyApp - HexIDE [break]");
        _sut.WindowTitle.Should().Be(_sut.Title);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Debugger_events_refresh_titles_and_status(bool paused)
    {
        _runnerService.IsRunning.Returns(true);
        _debugController.State.Returns(paused ? HexIDE.Runtime.Debugging.DebugState.Paused : HexIDE.Runtime.Debugging.DebugState.Running);
        var changed = new List<string?>();
        _sut.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        _statusBarService.ClearReceivedCalls();

        if (paused)
            _debugController.Stopped += Raise.Event<System.Action<HexIDE.Runtime.Debugging.StoppedInfo>>(
                new HexIDE.Runtime.Debugging.StoppedInfo(HexIDE.Runtime.Debugging.StopReason.Breakpoint, "Form1", 6));
        else
            _debugController.Continued += Raise.Event<System.Action>();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        changed.Should().Contain(nameof(_sut.Title)).And.Contain(nameof(_sut.WindowTitle));
        if (paused)
            _statusBarService.Received().ClearMessage();
        else
            _statusBarService.Received().SetMessage("Running…");
    }

    [AvaloniaTheory]
    [InlineData(null, false, false, "NoProjectTitle", "translated")]
    [InlineData("P", false, false, "DesignTag", "P - HexIDE translated")]
    [InlineData("P", true, false, "RunTag", "P - HexIDE translated")]
    [InlineData("P", true, true, "BreakTag", "P - HexIDE translated")]
    public void Language_change_refreshes_localized_titles(string? project, bool running, bool paused, string key, string expected)
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns(project == null ? null : TestHelpers.CreateProject(project));
        _runnerService.IsRunning.Returns(running);
        _debugController.State.Returns(paused ? HexIDE.Runtime.Debugging.DebugState.Paused : HexIDE.Runtime.Debugging.DebugState.Running);
        _localization.GetString("Str.MainWindow." + key).Returns("translated");
        var changed = new List<string?>();
        _sut.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _localization.LanguageChanged += Raise.Event<System.Action>();

        _sut.Title.Should().Be(expected);
        _sut.WindowTitle.Should().Be(expected);
        changed.Should().Contain(nameof(_sut.Title)).And.Contain(nameof(_sut.WindowTitle));
    }

    [AvaloniaTheory]
    [InlineData(null, false, false)]
    [InlineData("P", false, false)]
    [InlineData("P", true, false)]
    [InlineData("P", true, true)]
    public void Recovery_window_title_keeps_notice_across_project_run_and_language_changes(string? project, bool running, bool paused)
    {
        var previous = Static.IsRecoveryMode;
        try
        {
            Static.IsRecoveryMode = true;
            _focusedProjectUtil.FocusedOrStartupProject.Returns(project == null ? null : TestHelpers.CreateProject(project));
            _runnerService.IsRunning.Returns(running);
            _debugController.State.Returns(paused ? HexIDE.Runtime.Debugging.DebugState.Paused : HexIDE.Runtime.Debugging.DebugState.Running);
            _localization.GetString("Str.MainWindow.RecoveryNotice").Returns("Recovery session — settings changes won't be saved");
            var changed = new List<string?>();
            _sut.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            _sut.WindowTitle.Should().Be(_sut.Title + " — Recovery session — settings changes won't be saved");
            _localization.GetString("Str.MainWindow.RecoveryNotice").Returns("Translated recovery notice");
            _localization.LanguageChanged += Raise.Event<System.Action>();

            _sut.WindowTitle.Should().Be(_sut.Title + " — Translated recovery notice");
            changed.Should().Contain(nameof(_sut.WindowTitle));
            Static.IsRecoveryMode = false;
            _sut.WindowTitle.Should().Be(_sut.Title);
        }
        finally
        {
            Static.IsRecoveryMode = previous;
        }
    }

    // --- StartDefaultProjectCommand ---

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void StartDefaultProjectCommand_CanExecute_DelegatesToRunner(bool allowed)
    {
        _runnerService.CanStartDefaultProject.Returns(allowed);

        _sut.StartDefaultProjectCommand.CanExecute(null).Should().Be(allowed);
    }

    // --- StartDefaultProjectWithFullCompileCommand ---

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void StartDefaultProjectWithFullCompileCommand_CanExecute_DelegatesToRunner(bool allowed)
    {
        _runnerService.CanStartDefaultProjectWithFullCompile.Returns(allowed);

        _sut.StartDefaultProjectWithFullCompileCommand.CanExecute(null).Should().Be(allowed);
    }

    // --- BreakProjectCommand ---

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void BreakProjectCommand_CanExecute_DelegatesToRunner(bool allowed)
    {
        _runnerService.CanBreakProject.Returns(allowed);

        _sut.BreakProjectCommand.CanExecute(null).Should().Be(allowed);
    }

    // --- EndProjectCommand ---

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void EndProjectCommand_CanExecute_DelegatesToRunner(bool allowed)
    {
        _runnerService.CanEndProject.Returns(allowed);

        _sut.EndProjectCommand.CanExecute(null).Should().Be(allowed);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void EndProjectCommand_NotifiesWhenRunnerAvailabilityChanges(bool allowed)
    {
        _runnerService.CanEndProject.Returns(!allowed);
        var changes = 0;
        _sut.EndProjectCommand.CanExecuteChanged += (_, _) => changes++;
        _runnerService.CanEndProject.Returns(allowed);

        _runnerService.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(
            _runnerService, new PropertyChangedEventArgs(nameof(IProjectRunnerService.CanEndProject)));

        changes.Should().Be(1);
        _sut.EndProjectCommand.CanExecute(null).Should().Be(allowed);
    }

    // --- RestartProjectCommand ---

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void RestartProjectCommand_CanExecute_DelegatesToRunner(bool allowed)
    {
        _runnerService.CanRestartProject.Returns(allowed);

        _sut.RestartProjectCommand.CanExecute(null).Should().Be(allowed);
    }

    // --- MakeProjectGroupCommand ---

    // VB6 greys Make Project Group out until a group is open, and offers it for a group of one project (measured, #746).
    // HexIDE has a group whenever two projects are loaded, and may not have made its Group1 yet, so two count as one.
    [AvaloniaTheory]
    [InlineData(0, false, false)]
    [InlineData(1, false, false)]
    [InlineData(2, false, true)]
    [InlineData(1, true, true)]
    public void MakeProjectGroupCommand_is_offered_only_with_a_group(int projects, bool groupOpen, bool offered)
    {
        _projectManager.LoadedProjects.Returns(
            Enumerable.Range(1, projects).Select(i => new ProjectDefinition(VBProjectType.EXE, "Project" + i)).ToList());
        _projectManager.CurrentGroup.Returns(groupOpen ? new ProjectGroupDefinition("Group1") : null);

        _sut.MakeProjectGroupCommand.CanExecute(null).Should().Be(offered);
    }

    [AvaloniaFact]
    public void MakeProjectGroupCommand_is_asked_again_when_a_project_or_the_group_comes_or_goes()
    {
        var asked = 0;
        _sut.MakeProjectGroupCommand.CanExecuteChanged += (_, _) => asked++;
        var project = new ProjectDefinition(VBProjectType.EXE, "Project2");

        _projectManager.ProjectLoaded += Raise.Event<System.Action<ProjectDefinition>>(project);
        _projectManager.ProjectUnloaded += Raise.Event<System.Action<ProjectDefinition>>(project);
        _projectManager.PropertyChanged += Raise.Event<System.ComponentModel.PropertyChangedEventHandler>(
            _projectManager, new System.ComponentModel.PropertyChangedEventArgs(nameof(IProjectManager.CurrentGroup)));

        asked.Should().Be(3);
    }

    // VB6's builds the group's projects. HexIDE has no build of a group yet (#1515), so the command says so and saves
    // nothing, where it used to save every project and the group under a Make label.
    [AvaloniaFact]
    public void MakeProjectGroupCommand_says_it_is_not_implemented_and_saves_nothing()
    {
        _projectManager.LoadedProjects.Returns([new ProjectDefinition(VBProjectType.EXE, "Project1")]);
        _projectManager.CurrentGroup.Returns(new ProjectGroupDefinition("Group1"));
        _projectService.ClearReceivedCalls();

        _sut.MakeProjectGroupCommand.Execute(null);

        _windowManager.Received(1).MessageBox("This feature is not yet implemented", "NYI", MessageBoxButtons.Ok, MessageBoxIcon.Information);
        _projectService.ReceivedCalls().Should().BeEmpty("nothing is saved, built or asked of the project service");
    }

    // --- ProjectReferencesCommand ---

    [AvaloniaFact]
    public void ProjectReferencesCommand_CanExecute_WhenFocusedProjectExists()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns(TestHelpers.CreateProject());

        _sut.ProjectReferencesCommand.CanExecute(null).Should().BeTrue();
    }

    [AvaloniaFact]
    public void ProjectReferencesCommand_CannotExecute_WhenNoFocusedProject()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns((ProjectDefinition?)null);

        _sut.ProjectReferencesCommand.CanExecute(null).Should().BeFalse();
    }

    // --- ProjectComponentsCommand ---

    [AvaloniaFact]
    public void ProjectComponentsCommand_CanExecute_WhenFocusedProjectExists()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns(TestHelpers.CreateProject());

        _sut.ProjectComponentsCommand.CanExecute(null).Should().BeTrue();
    }

    [AvaloniaFact]
    public void ProjectComponentsCommand_CannotExecute_WhenNoFocusedProject()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns((ProjectDefinition?)null);

        _sut.ProjectComponentsCommand.CanExecute(null).Should().BeFalse();
    }

    // --- ProjectPropertiesCommand ---

    [AvaloniaFact]
    public void ProjectPropertiesCommand_CanExecute_WhenFocusedProjectExists()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns(TestHelpers.CreateProject());

        _sut.ProjectPropertiesCommand.CanExecute(null).Should().BeTrue();
    }

    [AvaloniaFact]
    public void ProjectPropertiesCommand_CannotExecute_WhenNoFocusedProject()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns((ProjectDefinition?)null);

        _sut.ProjectPropertiesCommand.CanExecute(null).Should().BeFalse();
    }

    // --- MakeProjectCommand ---

    [AvaloniaFact]
    public void MakeProjectCommand_CanExecute_WhenFocusedProjectExists()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns(TestHelpers.CreateProject());

        _sut.MakeProjectCommand.CanExecute(null).Should().BeTrue();
    }

    [AvaloniaFact]
    public void MakeProjectCommand_CannotExecute_WhenNoFocusedProject()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns((ProjectDefinition?)null);

        _sut.MakeProjectCommand.CanExecute(null).Should().BeFalse();
    }

    // --- RemoveProjectCommand ---

    [AvaloniaFact]
    public void RemoveProjectCommand_CanExecute_WhenFocusedProjectExists()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns(TestHelpers.CreateProject());

        _sut.RemoveProjectCommand.CanExecute(null).Should().BeTrue();
    }

    [AvaloniaFact]
    public void RemoveProjectCommand_CannotExecute_WhenNoFocusedProject()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns((ProjectDefinition?)null);

        _sut.RemoveProjectCommand.CanExecute(null).Should().BeFalse();
    }

    // --- RunWithVb6Command ---

    [AvaloniaFact]
    public void RunWithVb6Command_CanExecute_WhenProjectAndToolchainAvailable()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns(TestHelpers.CreateProject());
        _vb6Toolchain.IsAvailable.Returns(true);

        _sut.RunWithVb6Command.CanExecute(null).Should().BeTrue();
    }

    [AvaloniaFact]
    public void RunWithVb6Command_CannotExecute_WhenToolchainUnavailable()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns(TestHelpers.CreateProject());
        _vb6Toolchain.IsAvailable.Returns(false);

        _sut.RunWithVb6Command.CanExecute(null).Should().BeFalse();
    }

    [AvaloniaFact]
    public void RunWithVb6Command_CannotExecute_WhenNoProject()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns((ProjectDefinition?)null);
        _vb6Toolchain.IsAvailable.Returns(true);

        _sut.RunWithVb6Command.CanExecute(null).Should().BeFalse();
    }

    // --- MakeWithVb6Command ---

    [AvaloniaFact]
    public void MakeWithVb6Command_CanExecute_WhenProjectAndToolchainAvailable()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns(TestHelpers.CreateProject());
        _vb6Toolchain.IsAvailable.Returns(true);

        _sut.MakeWithVb6Command.CanExecute(null).Should().BeTrue();
    }

    [AvaloniaFact]
    public void MakeWithVb6Command_CannotExecute_WhenToolchainUnavailable()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns(TestHelpers.CreateProject());
        _vb6Toolchain.IsAvailable.Returns(false);

        _sut.MakeWithVb6Command.CanExecute(null).Should().BeFalse();
    }

    // --- Method delegation ---

    [AvaloniaFact]
    public void SaveProject_DelegatesToProjectServiceSaveAll()
    {
        _projectService.SaveAllProjects(false).Returns(Task.CompletedTask);

        _sut.SaveProject();

        _projectService.Received(1).SaveAllProjects(false);
    }

    [AvaloniaFact]
    public void SaveProjectAs_DelegatesToProjectServiceSaveAllWithSaveAs()
    {
        _projectService.SaveAllProjects(true).Returns(Task.CompletedTask);

        _sut.SaveProjectAs();

        _projectService.Received(1).SaveAllProjects(true);
    }

    [AvaloniaFact]
    public void OpenProject_DelegatesToProjectService()
    {
        _projectService.OpenProject().Returns(Task.CompletedTask);

        _sut.OpenProject();

        _projectService.Received(1).OpenProject();
    }

    [AvaloniaFact]
    public void MakeProject_DelegatesToProjectService()
    {
        _projectService.MakeProject().Returns(Task.CompletedTask);

        _sut.MakeProject();

        _projectService.Received(1).MakeProject();
    }

    // --- Layout / defaults ---

    [AvaloniaFact]
    public void Layout_IsNotNull()
    {
        _sut.Layout.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void IsStandardToolbarVisible_DefaultsToTrue()
    {
        _sut.Settings.IsStandardToolbarVisible.Should().BeTrue();
    }

    [AvaloniaFact]
    public void FocusedProjectUtil_IsExposed()
    {
        _sut.FocusedProjectUtil.Should().BeSameAs(_focusedProjectUtil);
    }

    // --- OnInitialized ---

    [AvaloniaFact]
    public void OnInitialized_ShowsNewProjectDialog_WhenNotSuppressed()
    {
        _settingsService.PromptForProjectOnStartup.Returns(true);
        _projectService.CreateNewProject().Returns(Task.CompletedTask);

        _sut.OnInitialized();

        _projectService.Received(1).CreateNewProject();
    }

    [AvaloniaFact]
    public void OnInitialized_SkipsNewProjectDialog_WhenSuppressed()
    {
        _settingsService.PromptForProjectOnStartup.Returns(false);

        _sut.OnInitialized();

        _projectService.DidNotReceive().CreateNewProject();
    }

    [AvaloniaFact]
    public async Task OnInitialized_OffersUnsavedWorkFirst_AndARestoredProjectStandsInForTheNewProjectPrompt()
    {
        _settingsService.PromptForProjectOnStartup.Returns(true);
        var offers = Substitute.For<HexIDE.Recovery.IRecoveryOffers>();
        offers.OfferAtStartAsync().Returns(true);
        var sut = Build(_runnerService, recoveryOffers: offers);

        sut.OnInitialized();
        await Task.Yield();

        await offers.Received(1).OfferAtStartAsync();
        await _projectService.DidNotReceive().CreateNewProject();
    }

    [AvaloniaFact]
    public async Task OnInitialized_StillPrompts_WhenNothingWasRestored()
    {
        _settingsService.PromptForProjectOnStartup.Returns(true);
        _projectService.CreateNewProject().Returns(Task.CompletedTask);
        var offers = Substitute.For<HexIDE.Recovery.IRecoveryOffers>();
        offers.OfferAtStartAsync().Returns(false);
        var sut = Build(_runnerService, recoveryOffers: offers);

        sut.OnInitialized();
        await Task.Yield();

        await _projectService.Received(1).CreateNewProject();
    }

    [AvaloniaFact]
    public async Task OnInitialized_OpensTheProjectNamedAtLaunch_EvenAfterARestore()
    {
        var offers = Substitute.For<HexIDE.Recovery.IRecoveryOffers>();
        offers.OfferAtStartAsync().Returns(true);
        var opened = false;
        var previous = Static.StartupProjectHook;
        Static.StartupProjectHook = (_, _) => { opened = true; return Task.CompletedTask; };
        try
        {
            Build(_runnerService, recoveryOffers: offers).OnInitialized();
            await Task.Yield();
        }
        finally
        {
            Static.StartupProjectHook = previous;
        }

        opened.Should().BeTrue("the restored work and the project named on the command line are both wanted");
    }

    // --- A compile error met mid-run (#759) ---

    [AvaloniaFact]
    public void A_compile_error_met_while_a_form_runs_is_recorded_and_shown_as_a_run_time_error_is()
    {
        _windowManager.ShowDialog(Arg.Any<IDialog>()).Returns(false);

        // A call to a Sub that does not exist, reached from a click: the compile error a tree-walker meets mid-run.
        var form = new HexIDE.Runtime.VBWindowContext(Substitute.For<HexIDE.Runtime.Interpreter.IBasicStandardLibrary>());
        form.SetCode("Private Sub Command1_Click()\n    NoSuchSub\nEnd Sub\n", "Form1");
        form.ExecuteSub("Command1_Click");

        _sut.RuntimeErrors.Last.Should().NotBeNull("it used to be a message box that nothing recorded");
        _sut.RuntimeErrors.Last!.Value.Message.Should().Contain("Sub or Function not defined");
        _windowManager.Received(1).ShowDialog(Arg.Any<HexIDE.Forms.ViewModels.RuntimeErrorViewModel>());
        _windowManager.DidNotReceiveWithAnyArgs().MessageBox(default!, default, default, default);
    }

    [AvaloniaTheory]
    [InlineData(true, true)]     // End pressed while the run goes on: it ends
    [InlineData(false, false)]   // closed any other way: the run carries on
    public void End_on_a_mid_run_compile_errors_dialog_ends_the_run(bool answer, bool ends)
    {
        _windowManager.ShowDialog(Arg.Any<IDialog>()).Returns(answer);
        _runnerService.CanEndProject.Returns(true);

        var form = new HexIDE.Runtime.VBWindowContext(Substitute.For<HexIDE.Runtime.Interpreter.IBasicStandardLibrary>());
        form.SetCode("Private Sub Command1_Click()\n    NoSuchSub\nEnd Sub\n", "Form1");
        form.ExecuteSub("Command1_Click");

        if (ends)
            _runnerService.Received(1).EndProject();
        else
            _runnerService.DidNotReceive().EndProject();
    }

    // --- The runtime-error dialog (#600) ---

    [AvaloniaTheory]
    [InlineData(true, true, true)]     // End pressed while a run is going: the run ends
    [InlineData(false, true, false)]   // closed any other way: nothing ends
    [InlineData(true, false, false)]   // End after the run has ended some other way: nothing to end
    public void The_runtime_error_dialog_ends_the_run_only_through_End(bool answer, bool canEnd, bool ends)
    {
        _windowManager.ShowDialog(Arg.Any<IDialog>()).Returns(answer);
        _runnerService.CanEndProject.Returns(canEnd);

        // A VB6 run-time error in an event procedure, which leaves the run going: the one dialog whose End has a
        // run to end. A run that failed or never started has its own test below.
        var form = new HexIDE.Runtime.VBWindowContext(Substitute.For<HexIDE.Runtime.Interpreter.IBasicStandardLibrary>());
        form.SetCode("Private Sub Command1_Click()\n    Error 5\nEnd Sub\n", "Form1");
        form.ExecuteSub("Command1_Click");

        if (ends)
            _runnerService.Received(1).EndProject();
        else
            _runnerService.DidNotReceive().EndProject();
        _sut.RuntimeErrors.Last.Should().NotBeNull("the error is recorded whatever the dialog answers");
    }

    [Fact]
    public void Only_End_closes_the_runtime_error_dialog_answering_true()
    {
        var vm = new HexIDE.Forms.ViewModels.RuntimeErrorViewModel("Run-time error '5'");
        var answers = new List<bool>();
        vm.CloseRequested += answers.Add;

        vm.Continue();
        vm.End();
        vm.Debug();
        vm.Help();

        answers.Should().Equal(false, true, false, false);
    }

    // --- A run that something ended (#754) ---

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_run_that_ended_or_never_started_is_shown_and_End_on_its_dialog_ends_no_later_run(bool runFailed)
    {
        const string text = "HexIDE does not implement this yet:\n\nGoSub not implemented";
        var dialog = new TaskCompletionSource<bool>();
        _windowManager.ShowDialog(Arg.Any<IDialog>()).Returns(dialog.Task);
        _runnerService.CanEndProject.Returns(false);

        if (runFailed)
            _runnerService.RunFailed += Raise.Event<System.Action<string>>(text);
        else
            _runnerService.StartFailed += Raise.Event<System.Action<string>>(text);

        _sut.RuntimeErrors.Last.Should().NotBeNull();
        _sut.RuntimeErrors.Last!.Value.Message.Should().Be(text);
        _windowManager.Received(1).ShowDialog(
            Arg.Is<HexIDE.Forms.ViewModels.RuntimeErrorViewModel>(vm => vm.ErrorText == text));

        // While the dialog is up, a later run starts, as run_project can do. End on the stale dialog must not end it:
        // nothing of the run the dialog is about is still running.
        _runnerService.CanEndProject.Returns(true);
        dialog.SetResult(true);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        _runnerService.DidNotReceive().EndProject();
    }

    [AvaloniaFact]
    public void F5_on_a_Sub_Main_that_reaches_something_unimplemented_shows_the_person_why()
    {
        // From the start call to what the person is shown, with a real runner between them. Before #754 this
        // run ended in the log: no dialog, and nothing for get_last_runtime_error to return.
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode("Sub Main()\r\n    GoSub Later\r\n    Exit Sub\r\nLater:\r\n    Return\r\nEnd Sub\r\n");
        project.AddModule(module);
        project.StartsAtSubMain = true;
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.ProjectRunner.NotImplementedYet").Returns("Not implemented yet: {0}");
        var runner = new ProjectRunnerService(_eventBus, _windowManager, _projectManager, localization,
            new HexIDE.Runtime.Debugging.DebugController(), Substitute.For<HexIDE.Debugging.IBreakpointService>(),
            new HexIDE.Debugging.WatchService(), new HexIDE.Debugging.RunScope());
        var sut = Build(runner);

        runner.RunProject(project);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var recorded = sut.RuntimeErrors.Last;
        recorded.Should().NotBeNull("get_last_runtime_error reads this");
        recorded!.Value.Message.Should().StartWith("Not implemented yet: ").And.Contain("GoSub");
        _windowManager.Received(1).ShowDialog(
            Arg.Is<HexIDE.Forms.ViewModels.RuntimeErrorViewModel>(vm => vm.ErrorText == recorded.Value.Message));
        runner.IsRunning.Should().BeFalse();
    }

    [AvaloniaTheory]
    [InlineData("#If {0} Then\n#End If\n")]
    [InlineData("Const X = {0}\n")]
    public void F5_on_a_long_chain_reports_failure_and_survives(string declaration)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode(string.Format(declaration, string.Join(" + ", System.Linq.Enumerable.Repeat("1", 10000)))
            + "Sub Main()\nEnd Sub\n");
        project.AddModule(module);
        project.StartsAtSubMain = true;
        var runner = new ProjectRunnerService(_eventBus, _windowManager, _projectManager,
            Substitute.For<ILocalizationService>(), new HexIDE.Runtime.Debugging.DebugController(),
            Substitute.For<HexIDE.Debugging.IBreakpointService>(), new HexIDE.Debugging.WatchService(),
            new HexIDE.Debugging.RunScope());
        var failures = new List<string>();
        runner.StartFailed += failures.Add;
        runner.RunFailed += failures.Add;

        runner.RunProject(project);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        failures.Should().ContainSingle();
        runner.IsRunning.Should().BeFalse();
    }

    // --- Breakpoints from the main window (the interpreter-debugger delta's restated scenarios) ---

    /// <summary>A code window on a module of the given project, keeping its marks in <paramref name="breakpoints"/>.</summary>
    private static HexIDE.Forms.ViewModels.CodeEditorViewModel CodeWindow(
        ProjectDefinition project, string name, HexIDE.Debugging.IBreakpointService breakpoints)
    {
        var module = new ModuleDefinition(project, name, ModuleKind.StandardModule);
        module.UpdateCode("Option Explicit\r\n\r\nSub Main()\r\n    Debug.Print 1\r\nEnd Sub\r\n");
        project.AddModule(module);
        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe<HexIDE.Events.CreateOrNavigateToSubEvent>(Arg.Any<Action<HexIDE.Events.CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<HexIDE.Events.ApplyAllUnsavedChangesEvent>(Arg.Any<Action<HexIDE.Events.ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<HexIDE.Events.FormUnloadedEvent>(Arg.Any<Action<HexIDE.Events.FormUnloadedEvent>>()).Returns(Substitute.For<IDisposable>());
        return new HexIDE.Forms.ViewModels.CodeEditorViewModel(
            Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(), Substitute.For<IProjectService>(), eventBus,
            Substitute.For<ILspClient>(), Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(),
            Substitute.For<HexIDE.Bookmarks.IBookmarkService>(), breakpoints,
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(), Substitute.For<HexIDE.Debugging.IRunScope>(),
            Substitute.For<ILocalizationService>()).Initialize(module);
    }

    [AvaloniaFact]
    public void TogglingABreakpointOnTheCaretLine()
    {
        // F9 and Debug > Toggle Breakpoint both run the main window's ToggleBreakpoint (MainView.axaml binds the
        // command they share to it). It toggles the active code window's caret line, counted from the top of the
        // file, and toggling it again removes the breakpoint. The gutter's click and the dot it draws are
        // MarkGutterOnReadOnlyLineTests'.
        var breakpoints = new HexIDE.Debugging.BreakpointService();
        var dock = Substitute.For<IDocumentDockService>();
        using var code = CodeWindow(new ProjectDefinition(VBProjectType.EXE, "P"), "Module1", breakpoints);
        dock.ActiveDocument.Returns(code);
        var sut = Build(_runnerService, dock, breakpoints);
        code.CaretOffset = code.Document.Text.IndexOf("Debug.Print", StringComparison.Ordinal);
        var line = code.Document.GetLineByOffset(code.CaretOffset).LineNumber;
        line.Should().Be(5, "the premise: the Attribute VB_Name line is line 1, so the statement is line 5");

        sut.ToggleBreakpoint();

        breakpoints.GetBreakpoints(code.Identity).Should().Equal(line);

        sut.ToggleBreakpoint();

        breakpoints.GetBreakpoints(code.Identity).Should().BeEmpty("toggling again removes it");
    }

    [AvaloniaFact]
    public void ClearingEveryBreakpointInTheProject()
    {
        // Ctrl+Shift+F9 and Debug > Clear All Breakpoints both run the main window's ClearAllBreakpoints. Every
        // document loses its breakpoints, in a second loaded project as well.
        var breakpoints = new HexIDE.Debugging.BreakpointService();
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        using var first = CodeWindow(project, "Module1", breakpoints);
        using var second = CodeWindow(project, "Module2", breakpoints);
        using var elsewhere = CodeWindow(new ProjectDefinition(VBProjectType.EXE, "Q"), "Module1", breakpoints);
        first.ToggleBreakpoint(4).Should().BeTrue();
        second.ToggleBreakpoint(5).Should().BeTrue();
        elsewhere.ToggleBreakpoint(4).Should().BeTrue();
        var sut = Build(_runnerService, Substitute.For<IDocumentDockService>(), breakpoints);

        sut.ClearAllBreakpoints();

        breakpoints.GetBreakpoints(first.Identity).Should().BeEmpty();
        breakpoints.GetBreakpoints(second.Identity).Should().BeEmpty();
        breakpoints.GetBreakpoints(elsewhere.Identity).Should().BeEmpty();
    }

    // --- Tool windows tabbed in one dock: hiding one leaves the rest (#2362) ---

    // The Properties window dragged into the Toolbox's dock, as a person can since #2239, so the two are tabs of one dock.
    private (Dock.Model.Core.IDock Shared, Dock.Model.Core.IDockable Toolbox, Dock.Model.Core.IDockable Properties) TabPropertiesWithToolbox()
    {
        var factory = _sut.Layout.Factory!;
        var toolbox = (Dock.Model.Core.IDockable)_sut.ToolBox;
        var properties = (Dock.Model.Core.IDockable)_sut.Properties;
        var shared = (Dock.Model.Core.IDock)toolbox.Owner!;
        factory.MoveDockable((Dock.Model.Core.IDock)properties.Owner!, shared, properties, toolbox);
        shared.VisibleDockables.Should().Contain([toolbox, properties]);
        return (shared, toolbox, properties);
    }

    [AvaloniaFact]
    public void Hiding_a_tool_tabbed_with_another_closes_it_alone()
    {
        var (shared, toolbox, _) = TabPropertiesWithToolbox();

        var (error, note) = _sut.SetToolWindowVisible("Properties", false);

        error.Should().BeNull();
        note.Should().Be("Properties is now hidden.");
        var windows = _sut.GetToolWindows();
        windows.Single(t => t.Name == "Properties").Visible.Should().BeFalse();
        windows.Single(t => t.Name == "Toolbox").Visible.Should().BeTrue();
        shared.VisibleDockables.Should().ContainSingle().Which.Should().BeSameAs(toolbox);
    }

    [AvaloniaFact]
    public void A_hidden_tool_shows_again_and_the_reply_says_when_nothing_changed()
    {
        TabPropertiesWithToolbox();
        _sut.SetToolWindowVisible("Properties", false);

        _sut.SetToolWindowVisible("properties", true).Should().Be(((string?)null, "Properties is now shown."));
        _sut.GetToolWindows().Where(t => t.Name is "Properties" or "Toolbox").Should().OnlyContain(t => t.Visible);
        _sut.SetToolWindowVisible("Properties", true).Note.Should().Be("Properties is now shown, as it was already.");
    }
}
