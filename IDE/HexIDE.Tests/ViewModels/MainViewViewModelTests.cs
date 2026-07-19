using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HexIDE.IDE;
using HexIDE.Keymaps;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Lsp;
using HexIDE.Themes;
using HexIDE.Tools;
using HexIDE.Tools.ObjectBrowser;
using HexIDE.Tools.TranslationEditor;
using HexIDE.VisualDesigner;

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
    private readonly MainViewViewModel _sut;

    public MainViewViewModelTests()
    {
        AvaloniaTestSetup.EnsureInitialized();
        _projectManager.LoadedProjects.Returns(new List<ProjectDefinition>());
        _settingsService.IsStandardToolbarVisible.Returns(true);

        var mockDocDock = Substitute.For<IDocumentDockService>();
        var loc = Substitute.For<ILocalizationService>();
        loc.ActiveLanguage.Returns("en");

        // ToolBoxToolViewModel loads Avalonia bitmaps in its constructor;
        // bypass with an uninitialized instance since tests never touch it.
        var toolBox = (ToolBoxToolViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ToolBoxToolViewModel));

        var properties = new PropertiesToolViewModel(mockDocDock, _eventBus, _windowManager, loc);
        var immediate = new ImmediateToolViewModel(loc);
        var formLayout = new FormLayoutToolViewModel(mockDocDock, _eventBus, loc);
        var locals = new LocalsToolViewModel(loc);
        var watches = new WatchesToolViewModel(loc);
        var editorService = Substitute.For<IEditorService>();
        var projectExplorer = new ProjectToolViewModel(_projectManager, _eventBus, _projectService, editorService, loc);
        var colorPalette = new ColorPaletteToolViewModel(mockDocDock);
        var objectBrowser = new ObjectBrowserToolViewModel(_projectManager, Substitute.For<ILspClient>(), editorService, Substitute.For<IComponentRegistry>(), Substitute.For<ITypeLibraryService>(), Substitute.For<IFocusedProjectUtil>(), loc);
        var translationEditor = new TranslationEditorViewModel(loc, Substitute.For<IUserTranslationsService>(), _windowManager);
        var windowStateService = Substitute.For<IWindowStateService>();

        var dockFactory = new MainViewViewModel.DockFactory(
            toolBox, projectExplorer, properties, formLayout,
            immediate, locals, watches, colorPalette, objectBrowser, translationEditor,
            windowStateService);

        _sut = new MainViewViewModel(
            _windowManager,
            toolBox,
            properties,
            immediate,
            formLayout,
            locals,
            watches,
            projectExplorer,
            colorPalette,
            objectBrowser,
            translationEditor,
            _projectManager,
            _focusedProjectUtil,
            _projectService,
            editorService,
            mockDocDock,
            dockFactory,
            _runnerService,
            _eventBus,
            _vb6Toolchain,
            _recentProjects,
            _findReplace,
            _settingsService,
            Substitute.For<IThemeService>(),
            Substitute.For<IKeymapService>(),
            Substitute.For<ILanguageSwitchService>(),
            loc,
            Substitute.For<IAddinRegistry>(),
            new AddinOptionsService(),
            Substitute.For<IDeveloperModeService>(),
            _statusBarService,
            Substitute.For<IPersonalityService>(),
            new AddinMenuService(),
            new AddinCommandService(),
            new AddinToolWindowService(),
            windowStateService);
    }

    // --- Title ---

    [Fact]
    public void Title_NoFocusedProject_ReturnsDesignDefault()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns((ProjectDefinition?)null);

        _sut.Title.Should().Be("HexIDE [design]");
    }

    [Fact]
    public void Title_WithFocusedProject_DesignMode()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns(TestHelpers.CreateProject("MyApp"));
        _runnerService.IsRunning.Returns(false);

        _sut.Title.Should().Be("MyApp - HexIDE [design]");
    }

    [Fact]
    public void Title_WithFocusedProject_RunMode()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns(TestHelpers.CreateProject("MyApp"));
        _runnerService.IsRunning.Returns(true);

        _sut.Title.Should().Be("MyApp - HexIDE [run]");
    }

    // --- StartDefaultProjectCommand ---

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StartDefaultProjectCommand_CanExecute_DelegatesToRunner(bool allowed)
    {
        _runnerService.CanStartDefaultProject.Returns(allowed);

        _sut.StartDefaultProjectCommand.CanExecute(null).Should().Be(allowed);
    }

    // --- StartDefaultProjectWithFullCompileCommand ---

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StartDefaultProjectWithFullCompileCommand_CanExecute_DelegatesToRunner(bool allowed)
    {
        _runnerService.CanStartDefaultProjectWithFullCompile.Returns(allowed);

        _sut.StartDefaultProjectWithFullCompileCommand.CanExecute(null).Should().Be(allowed);
    }

    // --- BreakProjectCommand ---

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BreakProjectCommand_CanExecute_DelegatesToRunner(bool allowed)
    {
        _runnerService.CanBreakProject.Returns(allowed);

        _sut.BreakProjectCommand.CanExecute(null).Should().Be(allowed);
    }

    // --- EndProjectCommand ---

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EndProjectCommand_CanExecute_DelegatesToRunner(bool allowed)
    {
        _runnerService.CanEndProject.Returns(allowed);

        _sut.EndProjectCommand.CanExecute(null).Should().Be(allowed);
    }

    // --- RestartProjectCommand ---

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RestartProjectCommand_CanExecute_DelegatesToRunner(bool allowed)
    {
        _runnerService.CanRestartProject.Returns(allowed);

        _sut.RestartProjectCommand.CanExecute(null).Should().Be(allowed);
    }

    // --- ProjectReferencesCommand ---

    [Fact]
    public void ProjectReferencesCommand_CanExecute_WhenFocusedProjectExists()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns(TestHelpers.CreateProject());

        _sut.ProjectReferencesCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void ProjectReferencesCommand_CannotExecute_WhenNoFocusedProject()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns((ProjectDefinition?)null);

        _sut.ProjectReferencesCommand.CanExecute(null).Should().BeFalse();
    }

    // --- ProjectComponentsCommand ---

    [Fact]
    public void ProjectComponentsCommand_CanExecute_WhenFocusedProjectExists()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns(TestHelpers.CreateProject());

        _sut.ProjectComponentsCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void ProjectComponentsCommand_CannotExecute_WhenNoFocusedProject()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns((ProjectDefinition?)null);

        _sut.ProjectComponentsCommand.CanExecute(null).Should().BeFalse();
    }

    // --- ProjectPropertiesCommand ---

    [Fact]
    public void ProjectPropertiesCommand_CanExecute_WhenFocusedProjectExists()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns(TestHelpers.CreateProject());

        _sut.ProjectPropertiesCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void ProjectPropertiesCommand_CannotExecute_WhenNoFocusedProject()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns((ProjectDefinition?)null);

        _sut.ProjectPropertiesCommand.CanExecute(null).Should().BeFalse();
    }

    // --- MakeProjectCommand ---

    [Fact]
    public void MakeProjectCommand_CanExecute_WhenFocusedProjectExists()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns(TestHelpers.CreateProject());

        _sut.MakeProjectCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void MakeProjectCommand_CannotExecute_WhenNoFocusedProject()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns((ProjectDefinition?)null);

        _sut.MakeProjectCommand.CanExecute(null).Should().BeFalse();
    }

    // --- RemoveProjectCommand ---

    [Fact]
    public void RemoveProjectCommand_CanExecute_WhenFocusedProjectExists()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns(TestHelpers.CreateProject());

        _sut.RemoveProjectCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void RemoveProjectCommand_CannotExecute_WhenNoFocusedProject()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns((ProjectDefinition?)null);

        _sut.RemoveProjectCommand.CanExecute(null).Should().BeFalse();
    }

    // --- RunWithVb6Command ---

    [Fact]
    public void RunWithVb6Command_CanExecute_WhenProjectAndToolchainAvailable()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns(TestHelpers.CreateProject());
        _vb6Toolchain.IsAvailable.Returns(true);

        _sut.RunWithVb6Command.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void RunWithVb6Command_CannotExecute_WhenToolchainUnavailable()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns(TestHelpers.CreateProject());
        _vb6Toolchain.IsAvailable.Returns(false);

        _sut.RunWithVb6Command.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void RunWithVb6Command_CannotExecute_WhenNoProject()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns((ProjectDefinition?)null);
        _vb6Toolchain.IsAvailable.Returns(true);

        _sut.RunWithVb6Command.CanExecute(null).Should().BeFalse();
    }

    // --- MakeWithVb6Command ---

    [Fact]
    public void MakeWithVb6Command_CanExecute_WhenProjectAndToolchainAvailable()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns(TestHelpers.CreateProject());
        _vb6Toolchain.IsAvailable.Returns(true);

        _sut.MakeWithVb6Command.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void MakeWithVb6Command_CannotExecute_WhenToolchainUnavailable()
    {
        _focusedProjectUtil.FocusedOrStartupProject.Returns(TestHelpers.CreateProject());
        _vb6Toolchain.IsAvailable.Returns(false);

        _sut.MakeWithVb6Command.CanExecute(null).Should().BeFalse();
    }

    // --- Method delegation ---

    [Fact]
    public void SaveProject_DelegatesToProjectServiceSaveAll()
    {
        _projectService.SaveAllProjects(false).Returns(Task.CompletedTask);

        _sut.SaveProject();

        _projectService.Received(1).SaveAllProjects(false);
    }

    [Fact]
    public void SaveProjectAs_DelegatesToProjectServiceSaveAllWithSaveAs()
    {
        _projectService.SaveAllProjects(true).Returns(Task.CompletedTask);

        _sut.SaveProjectAs();

        _projectService.Received(1).SaveAllProjects(true);
    }

    [Fact]
    public void OpenProject_DelegatesToProjectService()
    {
        _projectService.OpenProject().Returns(Task.CompletedTask);

        _sut.OpenProject();

        _projectService.Received(1).OpenProject();
    }

    [Fact]
    public void MakeProject_DelegatesToProjectService()
    {
        _projectService.MakeProject().Returns(Task.CompletedTask);

        _sut.MakeProject();

        _projectService.Received(1).MakeProject();
    }

    // --- Layout / defaults ---

    [Fact]
    public void Layout_IsNotNull()
    {
        _sut.Layout.Should().NotBeNull();
    }

    [Fact]
    public void IsStandardToolbarVisible_DefaultsToTrue()
    {
        _sut.Settings.IsStandardToolbarVisible.Should().BeTrue();
    }

    [Fact]
    public void FocusedProjectUtil_IsExposed()
    {
        _sut.FocusedProjectUtil.Should().BeSameAs(_focusedProjectUtil);
    }

    // --- OnInitialized ---

    [Fact]
    public void OnInitialized_ShowsNewProjectDialog_WhenNotSuppressed()
    {
        _settingsService.PromptForProjectOnStartup.Returns(true);
        _projectService.CreateNewProject().Returns(Task.CompletedTask);

        _sut.OnInitialized();

        _projectService.Received(1).CreateNewProject();
    }

    [Fact]
    public void OnInitialized_SkipsNewProjectDialog_WhenSuppressed()
    {
        _settingsService.PromptForProjectOnStartup.Returns(false);

        _sut.OnInitialized();

        _projectService.DidNotReceive().CreateNewProject();
    }
}
