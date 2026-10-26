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
        localization.GetString("Str.FindReplace.Msg.ProtectedOnly").Returns("Protected only: {0}");
        localization.GetString("Str.FindReplace.Msg.ReplacementsWithSkipped").Returns("{0} replaced; {1} skipped.");
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
        var vm = new RelatedDocumentEditorViewModel(Substitute.For<ILspClient>(), Substitute.For<IWindowManager>(), Substitute.For<ILocalizationService>());
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
            Substitute.For<HexIDE.Debugging.IRunScope>(),
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

    /// <summary>A form whose control's name appears in its designer block and in its code.</summary>
    private const string FormWithCommand1 =
        "VERSION 5.00\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Begin VB.CommandButton Command1 \r\n" +
        "      Caption         =   \"Command1\"\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n" +
        "Private Sub Command1_Click()\r\n" +
        "    Command1.Enabled = False\r\n" +
        "End Sub\r\n";

    [AvaloniaFact]
    public void ReplaceAll_ReplacingTextThatAlsoAppearsInTheHeader()
    {
        // The code-editor delta's scenario (#273 task 3.9): the code is changed and the designer block is
        // not. Replacing the control's name in the header would rename it behind the designer's back.
        var editor = CreateMockEditor(FormWithCommand1);
        SetActiveEditor(editor);
        var sut = CreateSut();
        sut.SearchText = "Command1";
        sut.ReplaceText = "cmdOK";

        sut.ReplaceAllCommand.Execute(null);

        var codeStart = FormWithCommand1.IndexOf("Private Sub", StringComparison.Ordinal);
        editor.Document.Text.Should().Be(
            FormWithCommand1[..codeStart] + FormWithCommand1[codeStart..].Replace("Command1", "cmdOK"));
        _windowManager.Received(1).MessageBox("2 replaced; 2 skipped.", "Replace",
            MessageBoxButtons.Ok, MessageBoxIcon.Information);
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ProtectedOnlyMatches_AreExplained(bool pattern, bool replaceOne)
    {
        var editor = CreateMockEditor(FormWithCommand1);
        SetActiveEditor(editor);
        var sut = CreateSut();
        sut.SearchText = "Caption";
        sut.UsePatternMatching = pattern;

        (replaceOne ? sut.ReplaceOneCommand : sut.FindNextCommand).Execute(null);

        _windowManager.Received(1).MessageBox("Protected only: Caption", "Find",
            MessageBoxButtons.Ok, MessageBoxIcon.Information);
        editor.SelectionLength.Should().Be(0);
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ReplaceAll_ReportsProtectedMatchesAcrossScope(bool pattern, bool allDocuments)
    {
        var editor = CreateMockEditor(FormWithCommand1);
        var other = CreateMockEditor("Attribute VB_Name = \"Caption\"\r\nPrivate Sub Total()\r\n" +
            "Attribute Total.VB_Description = \"Caption\"\r\nEnd Sub\r\n");
        SetOpenDocuments(editor, other);
        var sut = CreateSut();
        sut.SearchText = "Caption";
        sut.ReplaceText = "Changed";
        sut.UsePatternMatching = pattern;
        sut.Scope = allDocuments ? FindScope.AllOpenDocuments : FindScope.CurrentModule;

        sut.ReplaceAllCommand.Execute(null);

        _windowManager.Received(1).MessageBox($"0 replaced; {(allDocuments ? 3 : 1)} skipped.", "Replace",
            MessageBoxButtons.Ok, MessageBoxIcon.Information);
        editor.Document.Text.Should().Be(FormWithCommand1);
        other.Document.Text.Should().NotContain("Changed");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FindNext_ExplainsMemberAttributeMatchesInAnotherDocument(bool pattern)
    {
        var editor = CreateMockEditor("Option Explicit\r\n");
        var other = CreateMockEditor("Private Sub Total()\r\n" +
            "Attribute Total.VB_Description = \"description\"\r\nEnd Sub\r\n");
        SetOpenDocuments(editor, other);
        var sut = CreateSut();
        sut.SearchText = "description";
        sut.UsePatternMatching = pattern;
        sut.Scope = FindScope.AllOpenDocuments;

        sut.FindNextCommand.Execute(null);

        _windowManager.Received(1).MessageBox("Protected only: description", "Find",
            MessageBoxButtons.Ok, MessageBoxIcon.Information);
        other.SelectionLength.Should().Be(0);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplaceAll_SkippedCountHonoursCaseAndWholeWord(bool pattern)
    {
        var editor = CreateMockEditor("Attribute VB_Name = \"needle Needle needles\"\r\n" +
            "Private Sub Total()\r\nEnd Sub\r\n");
        SetActiveEditor(editor);
        var sut = CreateSut();
        sut.SearchText = "needle";
        sut.MatchCase = true;
        sut.WholeWordOnly = true;
        sut.UsePatternMatching = pattern;

        sut.ReplaceAllCommand.Execute(null);

        _windowManager.Received(1).MessageBox("0 replaced; 1 skipped.", "Replace",
            MessageBoxButtons.Ok, MessageBoxIcon.Information);
    }

    private sealed class NullSink : HexIDE.Runtime.Serialization.IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    /// <summary>
    /// <see cref="FormWithCommand1"/> with a blank line after <c>VERSION</c>. The deserializer reads it, and the
    /// window's header is the designer block it read. A header found from the text alone stops at the blank
    /// line, taking <c>VERSION 5.00</c> for the whole of it, so this is a form on which the two ways of finding
    /// the header disagree.
    /// </summary>
    private const string FormWithCommand1AndABlankLine =
        "VERSION 5.00\r\n" +
        "\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Begin VB.CommandButton Command1 \r\n" +
        "      Caption         =   \"Command1\"\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n" +
        "Private Sub Command1_Click()\r\n" +
        "    Command1.Enabled = False\r\n" +
        "End Sub\r\n";

    [AvaloniaFact]
    public void ReplaceAllDoesNotTouchAControlsBeginLine()
    {
        // Task 3.21's case, in the window as the IDE opens one: a form read by the deserializer and composed with
        // its designer block in front of its code. ReplaceAll_ReplacingTextThatAlsoAppearsInTheHeader assigns its
        // text to a window that was never given a form, so its header is found from the text alone; here the
        // header is the prefix the window was opened with, which is the path every real form takes. The form is
        // one the text alone misreads (FormWithCommand1AndABlankLine), so a window that stopped using its prefix
        // would let the replacement into the Begin line, and this test would say so.
        const string file = FormWithCommand1AndABlankLine;
        var form = new HexIDE.Runtime.Serialization.FormDeserializer()
            .Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), file, NullSink.Instance)!;
        var editor = CreateMockEditor("");
        editor.Initialize(form);
        editor.CaretOffset = 0;
        editor.Document.Text.Should().Be(file, "the premise: the window holds the form's file");
        SetActiveEditor(editor);
        var sut = CreateSut();
        sut.SearchText = "Command1";
        sut.ReplaceText = "cmdOK";

        sut.ReplaceAllCommand.Execute(null);

        editor.Document.Text.Should().Contain("   Begin VB.CommandButton Command1 \r\n", "the control's Begin line is untouched")
            .And.Contain("      Caption         =   \"Command1\"\r\n", "and so is everything else in the designer block");
        var codeStart = file.IndexOf("Private Sub", StringComparison.Ordinal);
        editor.Document.Text.Should().Be(
            file[..codeStart] + file[codeStart..].Replace("Command1", "cmdOK"));
        editor.BufferBody.Should().Be(form.Code.Replace("Command1", "cmdOK"), "the code is what changed");
        editor.Dispose();
    }

    [AvaloniaFact]
    public void ReplaceOne_WithAMatchSelectedInTheHeader_LeavesItAlone()
    {
        var editor = CreateMockEditor(FormWithCommand1);
        SetActiveEditor(editor);
        var sut = CreateSut();
        sut.SearchText = "Command1";
        sut.ReplaceText = "cmdOK";
        editor.SelectionStart = FormWithCommand1.IndexOf("Command1", StringComparison.Ordinal);
        editor.SelectionLength = "Command1".Length;

        sut.ReplaceOneCommand.Execute(null);

        editor.Document.Text.Should().Be(FormWithCommand1);
        editor.SelectionStart.Should().Be(FirstCodeMatch,
            "the Find that follows a Replace skips the header too, so it lands on the code");
    }

    // --- Read-only regions: Find does not search them (#273 task 3.11) ---
    //
    // The code-editor delta's "Searching for a control's name": only matches in the code are found. Each
    // direction and each wrap is its own scan, plain and pattern alike, so each has its own test here. A
    // scan that forgot the regions would land in the designer block, usually inside a fold.

    private static readonly int CodeStart = FormWithCommand1.IndexOf("Private Sub", StringComparison.Ordinal);
    private static readonly int FirstCodeMatch = FormWithCommand1.IndexOf("Command1", CodeStart, StringComparison.Ordinal);
    private static readonly int LastCodeMatch = FormWithCommand1.LastIndexOf("Command1", StringComparison.Ordinal);

    private FindReplaceViewModel SearchingTheFormFor(string term, int caret, FindDirection direction,
        bool pattern = false)
    {
        var editor = CreateMockEditor(FormWithCommand1);
        editor.CaretOffset = caret;
        SetActiveEditor(editor);
        var sut = CreateSut();
        sut.SearchText = term;
        sut.Direction = direction;
        sut.UsePatternMatching = pattern;
        return sut;
    }

    private CodeEditorViewModel ActiveEditor => (CodeEditorViewModel)_documentDockService.ActiveDocument!;

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SearchingForAControlsName_Down_FindsTheCodeNotTheDesignerBlock(bool pattern)
    {
        var sut = SearchingTheFormFor("Command1", caret: 0, FindDirection.Down, pattern);

        sut.FindNextCommand.Execute(null);

        ActiveEditor.SelectionStart.Should().Be(FirstCodeMatch,
            "the designer block's Begin line and Caption come first in the file, and neither is searched");
        ActiveEditor.SelectionLength.Should().Be("Command1".Length);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SearchingForAControlsName_Down_WrapsToTheFirstMatchInTheCode(bool pattern)
    {
        var sut = SearchingTheFormFor("Command1", caret: LastCodeMatch + "Command1".Length, FindDirection.Down, pattern);

        sut.FindNextCommand.Execute(null);

        ActiveEditor.SelectionStart.Should().Be(FirstCodeMatch, "the wrap starts at the top of the file");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SearchingForAControlsName_Up_FindsTheCodeNotTheDesignerBlock(bool pattern)
    {
        var sut = SearchingTheFormFor("Command1", caret: LastCodeMatch, FindDirection.Up, pattern);

        sut.FindNextCommand.Execute(null);

        ActiveEditor.SelectionStart.Should().Be(FirstCodeMatch);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SearchingForAControlsName_Up_WrapsToTheLastMatchInTheCode(bool pattern)
    {
        // Upward from the first match in the code, the next two candidates are the designer block's. Both
        // are passed over and the search wraps to the bottom of the file.
        var sut = SearchingTheFormFor("Command1", caret: FirstCodeMatch, FindDirection.Up, pattern);

        sut.FindNextCommand.Execute(null);

        ActiveEditor.SelectionStart.Should().Be(LastCodeMatch);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SearchingUp_WrapsPastAnAttributeLineAtTheBottomOfTheFile(bool pattern)
    {
        // The Up wrap scans from the bottom of the file, so what it meets first is whatever read-only region
        // lies below the caret: here, the last procedure's attribute line. A header is always above the
        // caret, so the test above cannot reach this scan's own filter.
        const string module =
            "Attribute VB_Name = \"Module1\"\r\n" +
            "Private Sub Other()\r\n" +
            "    x = Total\r\n" +
            "End Sub\r\n" +
            "Public Function Total() As Currency\r\n" +
            "Attribute Total.VB_Description = \"x\"\r\n" +
            "End Function\r\n";
        var editor = CreateMockEditor(module);
        editor.CaretOffset = module.IndexOf("Total", StringComparison.Ordinal);
        SetActiveEditor(editor);
        var sut = CreateSut();
        sut.SearchText = "Total";
        sut.Direction = FindDirection.Up;
        sut.UsePatternMatching = pattern;

        sut.FindNextCommand.Execute(null);

        editor.SelectionStart.Should().Be(module.IndexOf("Function Total", StringComparison.Ordinal) + "Function ".Length,
            "the attribute line's Total is the last in the file and is passed over");
    }

    [AvaloniaFact]
    public void AllOpenDocuments_TheFinalWrapIntoTheActiveDocumentPassesOverItsHeader()
    {
        // Nothing below the caret and nothing in the other document, so the search comes back round to the
        // top of the active one, where the designer block's matches come first.
        var form = CreateMockEditor(FormWithCommand1);
        form.CaretOffset = LastCodeMatch + "Command1".Length;
        SetOpenDocuments(form, CreateCarriedFileEditor("nothing to see here"));
        var sut = CreateSut();
        sut.SearchText = "Command1";
        sut.Scope = FindScope.AllOpenDocuments;

        sut.FindNextCommand.Execute(null);

        form.SelectionStart.Should().Be(FirstCodeMatch);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SearchingForTextOnlyTheHeaderHolds_ExplainsWhyItIsSkipped(bool pattern)
    {
        var sut = SearchingTheFormFor("VB_Name", caret: 0, FindDirection.Down, pattern);

        sut.FindNextCommand.Execute(null);

        ActiveEditor.SelectionLength.Should().Be(0);
        _windowManager.Received(1).MessageBox("Protected only: VB_Name", "Find",
            MessageBoxButtons.Ok, MessageBoxIcon.Information);
    }

    [AvaloniaFact]
    public void SearchingForAMembersName_PassesOverItsAttributeLines()
    {
        const string cls =
            "VERSION 1.0 CLASS\r\n" +
            "BEGIN\r\n" +
            "  MultiUse = -1  'True\r\n" +
            "END\r\n" +
            "Attribute VB_Name = \"Order\"\r\n" +
            "Option Explicit\r\n" +
            "Public Function Total() As Currency\r\n" +
            "Attribute Total.VB_Description = \"The order total\"\r\n" +
            "    Total = 0\r\n" +
            "End Function\r\n";
        var editor = CreateMockEditor(cls);
        editor.CaretOffset = cls.IndexOf("Total()", StringComparison.Ordinal) + "Total".Length;
        SetActiveEditor(editor);
        var sut = CreateSut();
        sut.SearchText = "Total";

        sut.FindNextCommand.Execute(null);

        editor.SelectionStart.Should().Be(cls.IndexOf("    Total = 0", StringComparison.Ordinal) + 4,
            "a member's attribute run is a read-only region as much as the header is");
    }

    [AvaloniaFact]
    public void AllOpenDocuments_SearchesAnotherDocumentOutsideItsOwnRegions()
    {
        var active = CreateMockEditor("Option Explicit\r\n");
        var form = CreateMockEditor(FormWithCommand1);
        SetOpenDocuments(active, form);
        var sut = CreateSut();
        sut.SearchText = "Command1";
        sut.Scope = FindScope.AllOpenDocuments;

        sut.FindNextCommand.Execute(null);

        form.SelectionStart.Should().Be(FirstCodeMatch,
            "each document is searched against its own regions, not the active one's");
    }

    [AvaloniaFact]
    public void ReplaceAll_WithPatternMatching_LeavesTheHeaderAlone()
    {
        // Replace All walks backwards through its own scan, FindPrevious, which has a pattern half of its own.
        var editor = CreateMockEditor(FormWithCommand1);
        SetActiveEditor(editor);
        var sut = CreateSut();
        sut.SearchText = "Command[0-9]";
        sut.ReplaceText = "cmdOK";
        sut.UsePatternMatching = true;

        sut.ReplaceAllCommand.Execute(null);

        editor.Document.Text.Should().Be(
            FormWithCommand1[..CodeStart] + FormWithCommand1[CodeStart..].Replace("Command1", "cmdOK"));
    }

    [AvaloniaTheory]
    [InlineData(FindDirection.Down, 0)]
    [InlineData(FindDirection.Up, -1)]
    public void APatternMatchStraddlingTheHeadersEnd_DoesNotHideTheMatchInsideTheCode(FindDirection direction, int caret)
    {
        // \s* first matches from the header's last line break, which overlaps the header and is refused.
        // Resuming after that match, as NextMatch and Matches do, would skip "Private Sub" inside it.
        var sut = SearchingTheFormFor(@"\s*Private Sub", caret < 0 ? FormWithCommand1.Length : caret, direction,
            pattern: true);

        sut.FindNextCommand.Execute(null);

        ActiveEditor.SelectionStart.Should().Be(CodeStart);
        ActiveEditor.SelectionLength.Should().Be("Private Sub".Length);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SearchingUp_FromEitherEndOfTheFile_FindsTheLastMatchInTheCode(int caret)
    {
        // Up from offset 0 scans from the bottom of the file, like Up from the end of it.
        var sut = SearchingTheFormFor("Command1", caret < 0 ? FormWithCommand1.Length : caret, FindDirection.Up);

        sut.FindNextCommand.Execute(null);

        ActiveEditor.SelectionStart.Should().Be(LastCodeMatch);
    }

    [AvaloniaFact]
    public void ReplaceAll_WithAPatternStraddlingAnAttributeRunsEnd_ReplacesTheMatchAfterIt()
    {
        const string cls =
            "Attribute VB_Name = \"Order\"\r\n" +
            "Public Function Total() As Currency\r\n" +
            "Attribute Total.VB_Description = \"The order total\"\r\n" +
            "    Total = 0\r\n" +
            "End Function\r\n";
        var editor = CreateMockEditor(cls);
        SetActiveEditor(editor);
        var sut = CreateSut();
        sut.SearchText = @"\s+Total = 0";
        sut.ReplaceText = "    Total = 1";
        sut.UsePatternMatching = true;

        sut.ReplaceAllCommand.Execute(null);

        editor.Document.Text.Should().Be(cls.Replace("    Total = 0", "    Total = 1"),
            "the first match begins on the attribute line's terminator and is refused; the one a character "
            + "later begins on the code line");
    }

    /// <summary>A procedure with a description: the attribute run hangs from the declaration's line break.</summary>
    private const string DescribedFunction =
        "Attribute VB_Name = \"Order\"\r\n" +
        "Public Function Total() As Currency\r\n" +
        "Attribute Total.VB_Description = \"The order total\"\r\n" +
        "    Total = 0\r\n" +
        "End Function\r\n";

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void EmptyReplacementTakesTheDeclarationsRun(bool all, bool variable)
    {
        var declaration = variable ? "Public Count As Long" : "Public Sub Go()";
        var run = variable ? "Attribute Count.VB_VarDescription = \"Count\"" : "Attribute Go.VB_Description = \"Go\"";
        var code = "Option Explicit\r\n" + declaration + "\r\n" + run + "\r\n' after\r\n";
        using var editor = CreateMockEditor(code);
        using var other = CreateMockEditor(code);
        SetOpenDocuments(editor, other);
        var sut = CreateSut();
        sut.SearchText = declaration;
        sut.ReplaceText = "";
        sut.Scope = FindScope.AllOpenDocuments;

        if (all) sut.ReplaceAllCommand.Execute(null);
        else
        {
            sut.FindNextCommand.Execute(null);
            sut.ReplaceOneCommand.Execute(null);
        }

        var expected = "Option Explicit\r\n\r\n' after\r\n";
        editor.Document.Text.Should().Be(expected);
        other.Document.Text.Should().Be(all ? expected : code);
        editor.Document.UndoStack.Undo();
        editor.Document.Text.Should().Be(code);
        sut.Scope = FindScope.CurrentModule;
        sut.ReplaceText = declaration + " ' retyped";
        editor.CaretOffset = 0;
        editor.SelectionLength = 0;
        sut.FindNextCommand.Execute(null);
        sut.ReplaceOneCommand.Execute(null);
        editor.Document.Text.Should().Be(code.Replace(declaration, sut.ReplaceText));
    }

    [AvaloniaFact]
    public void ReplaceOne_NeverJoinsAMembersAttributeLineOntoItsDeclaration()
    {
        // Found by review. The match ends on the declaration's line break, which is outside the attribute
        // run, and replacing it would join the attribute line onto the declaration. Typing was already
        // refused that deletion (task 3.7); Find and Replace now ask the same question.
        var editor = CreateMockEditor(DescribedFunction);
        SetActiveEditor(editor);
        var sut = CreateSut();
        sut.SearchText = "Currency\r\n";
        sut.ReplaceText = "Currency";

        sut.FindNextCommand.Execute(null);
        sut.ReplaceOneCommand.Execute(null);

        editor.Document.Text.Should().Be(DescribedFunction);
    }

    [AvaloniaFact]
    public void ReplaceAll_NeverJoinsAMembersAttributeLineOntoItsDeclaration()
    {
        var editor = CreateMockEditor(DescribedFunction);
        SetActiveEditor(editor);
        var sut = CreateSut();
        sut.SearchText = @"Currency\s+";
        sut.ReplaceText = "Long ";
        sut.UsePatternMatching = true;

        sut.ReplaceAllCommand.Execute(null);

        editor.Document.Text.Should().Be(DescribedFunction);
    }

    [AvaloniaFact]
    public void FindNext_WithPatternMatchingAndWholeWord_FindsAWholeWordInsideARefusedMatch()
    {
        // "Total.*" first matches "total = Total + 1" inside "Subtotal", which is not a whole word. The
        // whole-word match overlaps it and starts later.
        var editor = CreateMockEditor("Subtotal = Total + 1");
        SetActiveEditor(editor);
        var sut = CreateSut();
        sut.SearchText = "Total.*";
        sut.UsePatternMatching = true;
        sut.WholeWordOnly = true;

        sut.FindNextCommand.Execute(null);

        editor.SelectionStart.Should().Be("Subtotal = ".Length);
        editor.SelectionLength.Should().Be("Total + 1".Length);
    }

    [AvaloniaFact]
    public void FindNext_WithPatternMatchingAndWholeWord_WalksPastAMatchThatIsNotAWholeWord()
    {
        // The pattern scan used to take the first match and stop, so a first match that failed Whole Word
        // meant "not found" however many whole-word matches followed. Walking past a match is what the
        // regions need anyway, and it fixes this with them.
        var editor = CreateMockEditor("Dim Totals\r\nTotal = 1\r\n");
        SetActiveEditor(editor);
        var sut = CreateSut();
        sut.SearchText = "Total";
        sut.UsePatternMatching = true;
        sut.WholeWordOnly = true;

        sut.FindNextCommand.Execute(null);

        editor.SelectionStart.Should().Be("Dim Totals\r\n".Length);
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

    [AvaloniaTheory]
    [InlineData("needle")]
    [InlineData("x needle")]
    public void FindNext_AllOpenDocuments_UpFromZero_SearchesOtherDocumentsBeforeWrapping(string activeText)
    {
        var active = CreateMockEditor(activeText);
        var other = CreateMockEditor("needle then needle");
        SetOpenDocuments(active, other);
        var sut = CreateSut();
        sut.Scope = FindScope.AllOpenDocuments;
        sut.Direction = FindDirection.Up;
        sut.SearchText = "needle";

        sut.FindNextCommand.Execute(null);

        other.SelectionStart.Should().Be("needle then ".Length);
        other.SelectionLength.Should().Be("needle".Length);
        active.SelectionLength.Should().Be(0);
        _documentDockService.Received(1).TryActivate<BaseEditorWindowViewModel>(
            Arg.Is<Func<BaseEditorWindowViewModel, bool>>(predicate => predicate(other)));
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
            windowStateService, new DockFactoryReference());

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
            new HexIDE.Tools.Diagnostics.DiagnosticsToolViewModel(lsRegistry, Substitute.For<HexIDE.Lsp.ILspClient>(),
                mockDocDock, editorService, loc),
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
