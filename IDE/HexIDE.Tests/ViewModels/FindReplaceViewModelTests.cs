using System.Collections.Generic;
using AvaloniaEdit.Document;
using HexIDE.Bookmarks;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Keymaps;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Tools.LanguageServers;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Themes;
using HexIDE.Tools;
using HexIDE.Tools.ObjectBrowser;
using HexIDE.Tools.TranslationEditor;
using HexIDE.VisualDesigner;
using HexIDE.Conversations;
using HexIDE.Tools.ProtocolInspector;

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
        localization.GetString("Str.FindReplace.Msg.ScopeAllOpenDocuments").Returns("All Open Documents");
        localization.GetString("Str.FindReplace.Msg.NotFound").Returns("The search text '{0}' was not found.");
        localization.GetString("Str.FindReplace.Msg.ReplacementsMade").Returns("{0} replacement(s) made.");
        localization.GetString("Str.FindReplace.Msg.InvalidRegex").Returns("Invalid regular expression pattern.");
        localization.GetString("Str.FindReplace.Msg.NothingToSearch")
            .Returns("There is nothing here for Find to search.");
        return new(_windowManager, _documentDockService, localization);
    }

    /// <summary>
    /// A carried-file editor holding the given text — the plain-text editor for a README or a .json the
    /// project carries but does not compile.
    /// </summary>
    /// <remarks>
    /// <c>Initialize</c> is deliberately skipped: it wants a <c>RelatedDocumentDefinition</c> and a file on
    /// disk, and neither has anything to do with whether Find can search the buffer.
    /// </remarks>
    private static RelatedDocumentEditorViewModel CreateCarriedFileEditor(string text)
    {
        var vm = new RelatedDocumentEditorViewModel(Substitute.For<ILspClient>());
        vm.Document.Text = text;
        return vm;
    }

    private CodeEditorViewModel CreateMockEditor(string text)
    {
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

    private void SetActiveEditor(BaseEditorWindowViewModel editor)
    {
        _documentDockService.ActiveDocument.Returns(editor);
    }

    /// <summary>Puts these documents in the dock, in this order, with the first one active.</summary>
    private void SetOpenDocuments(params BaseEditorWindowViewModel[] documents)
    {
        _documentDockService.OpenDocuments.Returns(documents);
        _documentDockService.ActiveDocument.Returns(documents[0]);
    }

    // --- Title ---

    [AvaloniaFact]
    public void Title_DefaultsToFind()
    {
        var sut = CreateSut();

        sut.Title.Should().Be("Find");
    }

    [AvaloniaFact]
    public void Title_WhenShowReplace_IsReplace()
    {
        var sut = CreateSut();
        sut.ShowReplace = true;

        sut.Title.Should().Be("Replace");
    }

    // --- FindNext ---

    [AvaloniaFact]
    public void FindNextCommand_CannotExecute_WhenSearchTextEmpty()
    {
        var sut = CreateSut();

        sut.FindNextCommand.CanExecute(null).Should().BeFalse();
    }

    [AvaloniaFact]
    public void FindNextCommand_CanExecute_WhenSearchTextSet()
    {
        var sut = CreateSut();
        sut.SearchText = "hello";

        sut.FindNextCommand.CanExecute(null).Should().BeTrue();
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
    public void ScopeItems_OffersOnlyTheScopesThatAreImplemented()
    {
        var sut = CreateSut();

        // "Current Project" was removed rather than left decorative — see the note on FindScope.
        sut.ScopeItems.Should().HaveCount(2);
        sut.ScopeItems[0].Should().Be("Current Module");
        sut.ScopeItems[1].Should().Be("All Open Documents");
    }

    [AvaloniaFact]
    public void SelectedScopeIndex_SelectsAllOpenDocuments()
    {
        var sut = CreateSut();

        sut.SelectedScopeIndex = 1;

        sut.Scope.Should().Be(FindScope.AllOpenDocuments);
    }

    // --- Nothing searchable is active ---
    //
    // These replace a test that asserted only that FindNextCommand did not throw. It passed throughout
    // the whole of hexide-io/HexIDE#363: returning silently does not throw either.

    [AvaloniaFact]
    public void FindNext_NoActiveDocument_SaysSoRatherThanReturningSilently()
    {
        _documentDockService.ActiveDocument.Returns((BaseEditorWindowViewModel?)null);
        var sut = CreateSut();
        sut.SearchText = "hello";

        sut.FindNextCommand.Execute(null);

        _windowManager.Received(1).MessageBox(
            "There is nothing here for Find to search.",
            Arg.Any<string>(),
            Arg.Any<MessageBoxButtons>(),
            Arg.Any<MessageBoxIcon>());
    }

    [AvaloniaFact]
    public void ReplaceOne_NoActiveDocument_SaysSoRatherThanReturningSilently()
    {
        _documentDockService.ActiveDocument.Returns((BaseEditorWindowViewModel?)null);
        var sut = CreateSut();
        sut.SearchText = "hello";

        sut.ReplaceOneCommand.Execute(null);

        _windowManager.Received(1).MessageBox(
            "There is nothing here for Find to search.",
            Arg.Any<string>(),
            Arg.Any<MessageBoxButtons>(),
            Arg.Any<MessageBoxIcon>());
    }

    [AvaloniaFact]
    public void ReplaceAll_NoActiveDocument_SaysSoRatherThanReturningSilently()
    {
        _documentDockService.ActiveDocument.Returns((BaseEditorWindowViewModel?)null);
        var sut = CreateSut();
        sut.SearchText = "hello";

        sut.ReplaceAllCommand.Execute(null);

        _windowManager.Received(1).MessageBox(
            "There is nothing here for Find to search.",
            Arg.Any<string>(),
            Arg.Any<MessageBoxButtons>(),
            Arg.Any<MessageBoxIcon>());
    }

    // --- The carried-file editor ---

    [AvaloniaFact]
    public void FindNext_CarriedFileEditorActive_SelectsTheMatch()
    {
        var editor = CreateCarriedFileEditor("# Notes\nthe needle is here\n");
        SetActiveEditor(editor);
        var sut = CreateSut();
        sut.SearchText = "needle";

        sut.FindNextCommand.Execute(null);

        editor.SelectionStart.Should().Be(editor.Document.Text.IndexOf("needle", StringComparison.Ordinal));
        editor.SelectionLength.Should().Be("needle".Length);
        _windowManager.DidNotReceive().MessageBox(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>());
    }

    [AvaloniaFact]
    public void ReplaceAll_CarriedFileEditorActive_RewritesTheBuffer()
    {
        var editor = CreateCarriedFileEditor("alpha alpha alpha");
        SetActiveEditor(editor);
        var sut = CreateSut();
        sut.SearchText = "alpha";
        sut.ReplaceText = "beta";

        sut.ReplaceAllCommand.Execute(null);

        editor.Document.Text.Should().Be("beta beta beta");
    }

    // --- Scope: All Open Documents ---

    [AvaloniaFact]
    public void FindNext_AllOpenDocuments_FindsAMatchInAnotherDocument()
    {
        var active = CreateMockEditor("nothing of interest here");
        var other = CreateMockEditor("the needle lives over here");
        SetOpenDocuments(active, other);
        var sut = CreateSut();
        sut.Scope = FindScope.AllOpenDocuments;
        sut.SearchText = "needle";

        sut.FindNextCommand.Execute(null);

        other.SelectionStart.Should().Be(other.Document.Text.IndexOf("needle", StringComparison.Ordinal));
        other.SelectionLength.Should().Be("needle".Length);
        active.SelectionLength.Should().Be(0);
    }

    [AvaloniaFact]
    public void FindNext_AllOpenDocuments_BringsTheMatchingDocumentToTheFront()
    {
        var active = CreateMockEditor("nothing of interest here");
        var other = CreateMockEditor("the needle lives over here");
        SetOpenDocuments(active, other);
        var sut = CreateSut();
        sut.Scope = FindScope.AllOpenDocuments;
        sut.SearchText = "needle";

        sut.FindNextCommand.Execute(null);

        // A match selected in a tab nobody can see is not a search result.
        _documentDockService.Received(1).TryActivate<BaseEditorWindowViewModel>(
            Arg.Is<Func<BaseEditorWindowViewModel, bool>>(predicate => predicate(other)));
    }

    [AvaloniaFact]
    public void FindNext_AllOpenDocuments_ReachesACarriedFileFromACodeWindow()
    {
        var active = CreateMockEditor("Sub Nothing()\nEnd Sub");
        var carried = CreateCarriedFileEditor("the needle is in the README");
        SetOpenDocuments(active, carried);
        var sut = CreateSut();
        sut.Scope = FindScope.AllOpenDocuments;
        sut.SearchText = "needle";

        sut.FindNextCommand.Execute(null);

        carried.SelectionLength.Should().Be("needle".Length);
    }

    [AvaloniaFact]
    public void FindNext_CurrentModuleScope_DoesNotReachAnotherDocument()
    {
        var active = CreateMockEditor("nothing of interest here");
        var other = CreateMockEditor("the needle lives over here");
        SetOpenDocuments(active, other);
        var sut = CreateSut();
        sut.Scope = FindScope.CurrentModule;
        sut.SearchText = "needle";

        sut.FindNextCommand.Execute(null);

        other.SelectionLength.Should().Be(0);
        _windowManager.Received(1).MessageBox(
            Arg.Is<string>(s => s.Contains("needle")),
            Arg.Any<string>(),
            Arg.Any<MessageBoxButtons>(),
            Arg.Any<MessageBoxIcon>());
    }

    [AvaloniaFact]
    public void ReplaceAll_AllOpenDocuments_ReplacesInEveryOpenDocument()
    {
        var active = CreateMockEditor("alpha here");
        var other = CreateMockEditor("alpha there, alpha everywhere");
        SetOpenDocuments(active, other);
        var sut = CreateSut();
        sut.Scope = FindScope.AllOpenDocuments;
        sut.SearchText = "alpha";
        sut.ReplaceText = "beta";

        sut.ReplaceAllCommand.Execute(null);

        active.Document.Text.Should().Be("beta here");
        other.Document.Text.Should().Be("beta there, beta everywhere");
        _windowManager.Received(1).MessageBox(
            "3 replacement(s) made.",
            Arg.Any<string>(),
            Arg.Any<MessageBoxButtons>(),
            Arg.Any<MessageBoxIcon>());
    }

    // --- Not found ---

    [AvaloniaFact]
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

    [AvaloniaFact]
    public void FindInCode_DelegatesToFindReplaceService()
    {
        var findReplace = Substitute.For<IFindReplaceService>();
        var sut = CreateMainViewViewModel(findReplace);

        sut.FindInCodeCommand.Execute(null);

        findReplace.Received(1).ShowFind();
    }

    [AvaloniaFact]
    public void ReplaceInCode_DelegatesToFindReplaceService()
    {
        var findReplace = Substitute.For<IFindReplaceService>();
        var sut = CreateMainViewViewModel(findReplace);

        sut.ReplaceInCodeCommand.Execute(null);

        findReplace.Received(1).ShowReplace();
    }

    [AvaloniaFact]
    public void FindNextInCode_DelegatesToFindReplaceService()
    {
        var findReplace = Substitute.For<IFindReplaceService>();
        var sut = CreateMainViewViewModel(findReplace);

        sut.FindNextInCodeCommand.Execute(null);

        findReplace.Received(1).FindNext();
    }

    // --- MainViewViewModel: Edit ▸ Find is greyed out where it would do nothing ---

    [AvaloniaFact]
    public void FindCommands_AreDisabled_WhenNoDocumentIsActive()
    {
        var dock = Substitute.For<IDocumentDockService>();
        dock.ActiveDocument.Returns((BaseEditorWindowViewModel?)null);
        var sut = CreateMainViewViewModel(Substitute.For<IFindReplaceService>(), dock);

        sut.FindInCodeCommand.CanExecute(null).Should().BeFalse();
        sut.ReplaceInCodeCommand.CanExecute(null).Should().BeFalse();
        sut.FindNextInCodeCommand.CanExecute(null).Should().BeFalse();
    }

    [AvaloniaFact]
    public void FindCommands_AreEnabled_ForACodeWindow()
    {
        // Built BEFORE the Returns() call, not inside it: CodeEditorViewModel's constructor talks to the
        // substituted services it is given, and NSubstitute binds Returns() to the last call made on ANY
        // substitute — so inlining this silently configures eventBus.Subscribe instead of ActiveDocument.
        var editor = CreateMockEditor("Sub Foo()\nEnd Sub");
        var dock = Substitute.For<IDocumentDockService>();
        dock.ActiveDocument.Returns(editor);
        var sut = CreateMainViewViewModel(Substitute.For<IFindReplaceService>(), dock);

        sut.FindInCodeCommand.CanExecute(null).Should().BeTrue();
        sut.ReplaceInCodeCommand.CanExecute(null).Should().BeTrue();
        sut.FindNextInCodeCommand.CanExecute(null).Should().BeTrue();
    }

    [AvaloniaFact]
    public void FindCommands_AreEnabled_ForACarriedFileEditor()
    {
        var editor = CreateCarriedFileEditor("# README");
        var dock = Substitute.For<IDocumentDockService>();
        dock.ActiveDocument.Returns(editor);
        var sut = CreateMainViewViewModel(Substitute.For<IFindReplaceService>(), dock);

        // The case the old hard cast to CodeEditorViewModel refused outright.
        sut.FindInCodeCommand.CanExecute(null).Should().BeTrue();
        sut.FindNextInCodeCommand.CanExecute(null).Should().BeTrue();
    }

    private static MainViewViewModel CreateMainViewViewModel(IFindReplaceService findReplace)
        => CreateMainViewViewModel(findReplace, null);

    private static MainViewViewModel CreateMainViewViewModel(
        IFindReplaceService findReplace, IDocumentDockService? documentDock)
    {
        var windowManager = Substitute.For<IWindowManager>();
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(new List<ProjectDefinition>());
        var mockDocDock = documentDock ?? Substitute.For<IDocumentDockService>();
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
        var objectBrowser = new ObjectBrowserToolViewModel(projectManager, Substitute.For<ILspClient>(), editorService, Substitute.For<IComponentRegistry>(), Substitute.For<ITypeLibraryService>(), Substitute.For<IFocusedProjectUtil>(), loc,
            Substitute.For<ILanguageConnectionRegistry>());
        var translationEditor = new TranslationEditorViewModel(loc, Substitute.For<IUserTranslationsService>(), windowManager);
        var windowStateService = Substitute.For<IWindowStateService>();
        // A registry with nothing attached: the view model reads Connections and
        // ConfigurationProblems in its constructor.
        var lsRegistry = Substitute.For<ILanguageConnectionRegistry>();
        lsRegistry.Connections.Returns([]);
        lsRegistry.ConfigurationProblems.Returns([]);
        var languageServers = new LanguageServersToolViewModel(lsRegistry, loc, new ConversationLog());
        var protocolInspector = new ProtocolInspectorToolViewModel(
            new ConversationLog(), loc, new HexIDE.Redaction.Pseudonymiser(), Substitute.For<IWindowManager>());

        var dockFactory = new MainViewViewModel.DockFactory(
            toolBox, projectExplorer, properties, formLayout,
            immediate, locals, watches, callStack, colorPalette, objectBrowser, translationEditor,
            languageServers,
            protocolInspector,
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
            languageServers,
            protocolInspector,
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
