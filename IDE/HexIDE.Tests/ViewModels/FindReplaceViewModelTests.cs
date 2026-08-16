using System.Collections.Generic;
using AvaloniaEdit.Document;
using HexIDE.Bookmarks;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Keymaps;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Themes;
using HexIDE.Tools;
using HexIDE.Tools.ObjectBrowser;
using HexIDE.Tools.TranslationEditor;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.ViewModels;

public class FindReplaceViewModelTests
{
    private readonly IWindowManager _windowManager = Substitute.For<IWindowManager>();
    private readonly IDocumentDockService _documentDockService = Substitute.For<IDocumentDockService>();

    private FindReplaceViewModel CreateSut()
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.FindReplace.Msg.TitleFind").Returns("Find");
        localization.GetString("Str.FindReplace.Msg.TitleReplace").Returns("Replace");
        localization.GetString("Str.FindReplace.Msg.ScopeCurrentModule").Returns("Current Module");
        localization.GetString("Str.FindReplace.Msg.ScopeCurrentProject").Returns("Current Project");
        localization.GetString("Str.FindReplace.Msg.ScopeAllOpenDocuments").Returns("All Open Documents");
        localization.GetString("Str.FindReplace.Msg.NotFound").Returns("The search text '{0}' was not found.");
        localization.GetString("Str.FindReplace.Msg.ReplacementsMade").Returns("{0} replacement(s) made.");
        localization.GetString("Str.FindReplace.Msg.InvalidRegex").Returns("Invalid regular expression pattern.");
        return new(_windowManager, _documentDockService, localization);
    }

    private CodeEditorViewModel CreateMockEditor(string text)
    {
        AvaloniaTestSetup.EnsureInitialized();
        var wm = Substitute.For<IWindowManager>();
        var es = Substitute.For<IEditorService>();
        var ps = Substitute.For<IProjectService>();
        var eb = Substitute.For<IEventBus>();
        var lsp = Substitute.For<Lsp.ILspClient>();
        var ss = Substitute.For<ISettingsService>();
        var sb = Substitute.For<IStatusBarService>();
        var bs = Substitute.For<IBookmarkService>();
        var vm = new CodeEditorViewModel(wm, es, ps, eb, lsp, ss, sb, bs,
            Substitute.For<HexIDE.Debugging.IBreakpointService>(),
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            Substitute.For<ILocalizationService>());
        vm.Document.Text = text;
        vm.CaretOffset = 0;
        return vm;
    }

    private void SetActiveEditor(CodeEditorViewModel editor)
    {
        _documentDockService.ActiveDocument.Returns(editor);
    }

    // --- Title ---

    [Fact]
    public void Title_DefaultsToFind()
    {
        var sut = CreateSut();

        sut.Title.Should().Be("Find");
    }

    [Fact]
    public void Title_WhenShowReplace_IsReplace()
    {
        var sut = CreateSut();
        sut.ShowReplace = true;

        sut.Title.Should().Be("Replace");
    }

    // --- FindNext ---

    [Fact]
    public void FindNextCommand_CannotExecute_WhenSearchTextEmpty()
    {
        var sut = CreateSut();

        sut.FindNextCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void FindNextCommand_CanExecute_WhenSearchTextSet()
    {
        var sut = CreateSut();
        sut.SearchText = "hello";

        sut.FindNextCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void FindNext_SelectsMatchInEditor()
    {
        var editor = CreateMockEditor("Hello World Hello");
        SetActiveEditor(editor);
        var sut = CreateSut();
        sut.SearchText = "Hello";

        sut.FindNextCommand.Execute(null);

        editor.SelectionStart.Should().Be(0);
        editor.SelectionLength.Should().Be(5);
    }

    [Fact]
    public void FindNext_AdvancesToSecondMatch()
    {
        var editor = CreateMockEditor("Hello World Hello");
        SetActiveEditor(editor);
        var sut = CreateSut();
        sut.SearchText = "Hello";

        sut.FindNextCommand.Execute(null);
        // Caret is now at 5 (after first match)
        sut.FindNextCommand.Execute(null);

        editor.SelectionStart.Should().Be(12);
        editor.SelectionLength.Should().Be(5);
    }

    [Fact]
    public void FindNext_WrapsAround()
    {
        var editor = CreateMockEditor("Hello World");
        SetActiveEditor(editor);
        editor.CaretOffset = 6; // After "Hello "
        var sut = CreateSut();
        sut.SearchText = "Hello";

        sut.FindNextCommand.Execute(null);

        // Should wrap and find "Hello" at 0
        editor.SelectionStart.Should().Be(0);
        editor.SelectionLength.Should().Be(5);
    }

    // --- Case sensitivity ---

    [Fact]
    public void FindNext_IsCaseInsensitive_ByDefault()
    {
        var editor = CreateMockEditor("HELLO world");
        SetActiveEditor(editor);
        var sut = CreateSut();
        sut.SearchText = "hello";

        sut.FindNextCommand.Execute(null);

        editor.SelectionStart.Should().Be(0);
        editor.SelectionLength.Should().Be(5);
    }

    [Fact]
    public void FindNext_IsCaseSensitive_WhenMatchCaseEnabled()
    {
        var editor = CreateMockEditor("HELLO hello");
        SetActiveEditor(editor);
        var sut = CreateSut();
        sut.SearchText = "hello";
        sut.MatchCase = true;

        sut.FindNextCommand.Execute(null);

        editor.SelectionStart.Should().Be(6);
        editor.SelectionLength.Should().Be(5);
    }

    // --- Whole word ---

    [Fact]
    public void FindNext_WholeWord_SkipsPartialMatches()
    {
        var editor = CreateMockEditor("helloworld hello");
        SetActiveEditor(editor);
        var sut = CreateSut();
        sut.SearchText = "hello";
        sut.WholeWordOnly = true;

        sut.FindNextCommand.Execute(null);

        editor.SelectionStart.Should().Be(11);
        editor.SelectionLength.Should().Be(5);
    }

    // --- Direction ---

    [Fact]
    public void FindNext_DirectionUp_SearchesBackward()
    {
        var editor = CreateMockEditor("Hello World Hello");
        SetActiveEditor(editor);
        editor.CaretOffset = 17; // End of text
        var sut = CreateSut();
        sut.SearchText = "Hello";
        sut.Direction = FindDirection.Up;

        sut.FindNextCommand.Execute(null);

        editor.SelectionStart.Should().Be(12);
        editor.SelectionLength.Should().Be(5);
    }

    // --- Replace ---

    [Fact]
    public void ReplaceAll_ReplacesAllOccurrences()
    {
        var editor = CreateMockEditor("Hello World Hello");
        SetActiveEditor(editor);
        var sut = CreateSut();
        sut.SearchText = "Hello";
        sut.ReplaceText = "Hi";

        sut.ReplaceAllCommand.Execute(null);

        editor.Document.Text.Should().Be("Hi World Hi");
    }

    [Fact]
    public void ReplaceAll_RespectsCaseSensitivity()
    {
        var editor = CreateMockEditor("Hello HELLO hello");
        SetActiveEditor(editor);
        var sut = CreateSut();
        sut.SearchText = "hello";
        sut.ReplaceText = "Hi";
        sut.MatchCase = true;

        sut.ReplaceAllCommand.Execute(null);

        editor.Document.Text.Should().Be("Hello HELLO Hi");
    }

    // --- Pattern matching (regex) ---

    [Fact]
    public void FindNext_WithPatternMatching_UsesRegex()
    {
        var editor = CreateMockEditor("Dim x As Integer");
        SetActiveEditor(editor);
        var sut = CreateSut();
        sut.SearchText = @"Dim \w+";
        sut.UsePatternMatching = true;

        sut.FindNextCommand.Execute(null);

        editor.SelectionStart.Should().Be(0);
        editor.SelectionLength.Should().Be(5); // "Dim x"
    }

    // --- Scope items ---

    [Fact]
    public void ScopeItems_HasExpectedEntries()
    {
        var sut = CreateSut();

        sut.ScopeItems.Should().HaveCount(3);
        sut.ScopeItems[0].Should().Be("Current Module");
        sut.ScopeItems[1].Should().Be("Current Project");
        sut.ScopeItems[2].Should().Be("All Open Documents");
    }

    // --- No active editor ---

    [Fact]
    public void FindNext_NoActiveEditor_DoesNotThrow()
    {
        _documentDockService.ActiveDocument.Returns((BaseEditorWindowViewModel?)null);
        var sut = CreateSut();
        sut.SearchText = "hello";

        var act = () => sut.FindNextCommand.Execute(null);

        act.Should().NotThrow();
    }

    // --- Not found ---

    [Fact]
    public void FindNext_NotFound_ShowsMessageBox()
    {
        var editor = CreateMockEditor("Hello World");
        SetActiveEditor(editor);
        var sut = CreateSut();
        sut.SearchText = "xyz";

        sut.FindNextCommand.Execute(null);

        _windowManager.Received(1).MessageBox(
            Arg.Is<string>(s => s.Contains("xyz")),
            Arg.Any<string>(),
            Arg.Any<MessageBoxButtons>(),
            Arg.Any<MessageBoxIcon>());
    }

    // --- MainViewViewModel delegation ---

    [Fact]
    public void FindInCode_DelegatesToFindReplaceService()
    {
        AvaloniaTestSetup.EnsureInitialized();
        var findReplace = Substitute.For<IFindReplaceService>();
        var sut = CreateMainViewViewModel(findReplace);

        sut.FindInCode();

        findReplace.Received(1).ShowFind();
    }

    [Fact]
    public void ReplaceInCode_DelegatesToFindReplaceService()
    {
        AvaloniaTestSetup.EnsureInitialized();
        var findReplace = Substitute.For<IFindReplaceService>();
        var sut = CreateMainViewViewModel(findReplace);

        sut.ReplaceInCode();

        findReplace.Received(1).ShowReplace();
    }

    [Fact]
    public void FindNextInCode_DelegatesToFindReplaceService()
    {
        AvaloniaTestSetup.EnsureInitialized();
        var findReplace = Substitute.For<IFindReplaceService>();
        var sut = CreateMainViewViewModel(findReplace);

        sut.FindNextInCode();

        findReplace.Received(1).FindNext();
    }

    private static MainViewViewModel CreateMainViewViewModel(IFindReplaceService findReplace)
    {
        var windowManager = Substitute.For<IWindowManager>();
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(new List<ProjectDefinition>());
        var mockDocDock = Substitute.For<IDocumentDockService>();
        var toolBox = (ToolBoxToolViewModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ToolBoxToolViewModel));
        var eventBus = Substitute.For<IEventBus>();
        var loc = Substitute.For<ILocalizationService>();
        loc.ActiveLanguage.Returns("en");
        var properties = new PropertiesToolViewModel(mockDocDock, eventBus, windowManager, loc);
        var immediate = new ImmediateToolViewModel(loc, Substitute.For<HexIDE.Runtime.Debugging.IDebugController>());
        var formLayout = new FormLayoutToolViewModel(mockDocDock, eventBus, loc);
        var locals = new LocalsToolViewModel(loc, Substitute.For<HexIDE.Runtime.Debugging.IDebugController>());
        var watches = new WatchesToolViewModel(loc, new HexIDE.Debugging.WatchService(), Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(), Substitute.For<HexIDE.IDE.IWindowManager>());
        var callStack = new CallStackToolViewModel(loc, Substitute.For<HexIDE.Runtime.Debugging.IDebugController>());
        var editorService = Substitute.For<IEditorService>();
        var projectService = Substitute.For<IProjectService>();
        var projectExplorer = new ProjectToolViewModel(projectManager, eventBus, projectService, editorService, loc);
        var colorPalette = new ColorPaletteToolViewModel(mockDocDock);
        var objectBrowser = new ObjectBrowserToolViewModel(projectManager, Substitute.For<ILspClient>(), editorService, Substitute.For<IComponentRegistry>(), Substitute.For<ITypeLibraryService>(), Substitute.For<IFocusedProjectUtil>(), loc);
        var translationEditor = new TranslationEditorViewModel(loc, Substitute.For<IUserTranslationsService>(), windowManager);
        var windowStateService = Substitute.For<IWindowStateService>();
        var dockFactory = new MainViewViewModel.DockFactory(
            toolBox, projectExplorer, properties, formLayout,
            immediate, locals, watches, callStack, colorPalette, objectBrowser, translationEditor,
            windowStateService);

        return new MainViewViewModel(
            windowManager,
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
            projectManager,
            Substitute.For<IFocusedProjectUtil>(),
            projectService,
            editorService,
            mockDocDock,
            dockFactory,
            Substitute.For<IProjectRunnerService>(),
            eventBus,
            Substitute.For<IVb6ToolchainService>(),
            Substitute.For<IRecentProjectsService>(),
            findReplace,
            Substitute.For<ISettingsService>(),
            Substitute.For<IThemeService>(),
            Substitute.For<IKeymapService>(),
            Substitute.For<ILanguageSwitchService>(),
            loc,
            Substitute.For<IAddinRegistry>(),
            new AddinOptionsService(),
            Substitute.For<IDeveloperModeService>(),
            Substitute.For<IStatusBarService>(),
            Substitute.For<IPersonalityService>(),
            new AddinMenuService(),
            new AddinCommandService(),
            new AddinToolWindowService(),
            windowStateService,
            Substitute.For<HexIDE.Debugging.IBreakpointService>(),
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>());
    }
}
