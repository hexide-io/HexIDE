using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using HexIDE.Automation;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A member's attribute run follows the line it describes, through the code window's own keys, its clipboard and
/// automation (hexide-io/HexIDE#273 tasks 4.5 and 4.5a): a line break at the end of a described line goes after the
/// run, and a deletion of that line takes the run with it, each as one undo step.
/// </summary>
/// <remarks>
/// Against a rendered editor, because the keys are taken by three different handlers, measured headlessly before
/// this was built: Delete, Ctrl+X and Ctrl+V by the code window's commands before the text area sees them, Enter by
/// its tunnel handler, and Backspace, Ctrl+D and Shift+Enter by AvaloniaEdit's own bindings after it. The rules
/// themselves are asked of the view model in the unit tests of the same name.
/// </remarks>
public class MemberRunFollowsItsLineTests : IDisposable
{
    private readonly List<Window> windows = [];
    private readonly List<CodeEditorViewModel> made = [];

    public void Dispose()
    {
        foreach (var w in windows) w.Close();
        foreach (var vm in made) vm.Dispose();
        GC.SuppressFinalize(this);
    }

    // A procedure with VB6's two-line run and a body, and one after it with none.
    private const string Procedure =
        "Option Explicit\r\n" +
        "\r\n" +
        "Public Sub Go()\r\n" +
        "Attribute Go.VB_Description = \"Goes\"\r\n" +
        "Attribute Go.VB_UserMemId = 0\r\n" +
        "    Debug.Print 1\r\n" +
        "End Sub\r\n" +
        "\r\n" +
        "Public Sub Halt()\r\n" +
        "End Sub\r\n";

    private const string RunText =
        "Attribute Go.VB_Description = \"Goes\"\r\n" +
        "Attribute Go.VB_UserMemId = 0\r\n";

    // A described procedure with nothing between its run and its End Sub.
    private const string Empty =
        "Option Explicit\r\n" +
        "\r\n" +
        "Public Sub Go()\r\n" +
        "Attribute Go.VB_Description = \"Goes\"\r\n" +
        "End Sub\r\n";

    private CodeEditorViewModel Open(string code)
    {
        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe<CreateOrNavigateToSubEvent>(Arg.Any<Action<CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(Arg.Any<Action<ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<FormUnloadedEvent>(Arg.Any<Action<FormUnloadedEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<DocumentSavedEvent>(Arg.Any<Action<DocumentSavedEvent>>()).Returns(Substitute.For<IDisposable>());
        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);
        var lsp = Substitute.For<ILspClient>();
        lsp.IsRunning.Returns(false);
        lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<DocumentSymbol>());

        var vm = new CodeEditorViewModel(
            Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(), Substitute.For<IProjectService>(),
            eventBus, lsp, settings, Substitute.For<IStatusBarService>(), new BookmarkService(), new BreakpointService(),
            Substitute.For<IDebugController>(), new RunScope(), Substitute.For<ILocalizationService>());
        made.Add(vm);
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode(code);
        return vm.Initialize(module);
    }

    private TextEditor Show(CodeEditorViewModel vm)
    {
        var window = new Window { Width = 1200, Height = 800 };
        windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var view = new CodeEditorView { DataContext = vm };
        window.Content = view;
        Dispatcher.UIThread.RunJobs();
        var editor = view.FindControl<TextEditor>("TextEditor")!;
        editor.TextArea.Focus();
        Dispatcher.UIThread.RunJobs();
        return editor;
    }

    /// <summary>A key as the input system delivers it: raised on the focused text area, which tunnels from the window.</summary>
    private static void Press(TextEditor editor, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        editor.TextArea.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
        Dispatcher.UIThread.RunJobs();
    }

    private static void TypeText(TextEditor editor, string text)
    {
        editor.TextArea.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text });
        Dispatcher.UIThread.RunJobs();
    }

    private static DocumentLine LineWith(TextEditor editor, string text)
    {
        var at = editor.Document.Text.IndexOf(text, StringComparison.Ordinal);
        at.Should().BeGreaterThanOrEqualTo(0, $"the premise: the text has {text}");
        return editor.Document.GetLineByOffset(at);
    }

    private static FoldingManager Folding(TextEditor editor) =>
        (FoldingManager)editor.TextArea.TextView.GetService(typeof(FoldingManager))!;

    private static FoldingSection? RunFold(TextEditor editor, string declaration) =>
        Folding(editor).GetFoldingsAt(LineWith(editor, declaration).EndOffset).SingleOrDefault();

    private static void PutCaret(TextEditor editor, int offset)
    {
        editor.TextArea.Caret.Offset = offset;
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task<string?> Clipboard(TextEditor editor)
    {
        var text = await TopLevel.GetTopLevel(editor)!.Clipboard!.TryGetTextAsync();
        Dispatcher.UIThread.RunJobs();
        return text;
    }

    private static async Task SetClipboard(TextEditor editor, string text)
    {
        await TopLevel.GetTopLevel(editor)!.Clipboard!.SetTextAsync(text);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Lets an asynchronous paste land: the clipboard is read after the key that asked has been handled.</summary>
    private static async Task UntilChanged(CodeEditorViewModel vm, string before)
    {
        for (var i = 0; i < 50 && vm.Document.Text == before; i++)
        {
            await Task.Delay(10, CancellationToken.None);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static MemberRun RunOf(CodeEditorViewModel vm, string member) =>
        vm.MemberRunsNow.Single(r => r.Member?.Name == member);

    // ── The delta's scenario, and task 4.7's deletion case ──────────────────────────────────────────────────

    [AvaloniaFact]
    public void DeletingADescribedProcedure()
    {
        // The code-editor delta's scenario: WHEN the developer selects a procedure including its declaration line and
        // deletes it, THEN its attribute lines go with it, and none is left behind.
        var vm = Open(Procedure);
        var editor = Show(vm);
        var before = vm.Document.Text;
        var from = LineWith(editor, "Public Sub Go()").Offset;
        var end = before.IndexOf("End Sub\r\n", StringComparison.Ordinal) + "End Sub\r\n".Length;
        editor.Select(from, end - from);

        Press(editor, Key.Delete);

        vm.Document.Text.Should().Be(before.Remove(from, end - from));
        vm.Document.Text.Should().NotContain("Attribute Go.", "none of its attribute lines is left behind");
        vm.MemberRunsNow.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void DeletingADescribedProceduresDeclarationLineAloneTakesItsAttributeLinesWithIt()
    {
        // Task 4.7's deletion case: the declaration line alone, by Ctrl+D. Before 4.5a the library deleted the line and
        // the provider carved the run out of the deletion, so the run was left under the line above, an orphan.
        var vm = Open(Procedure);
        var editor = Show(vm);
        var before = vm.Document.Text;
        var declaration = LineWith(editor, "Public Sub Go()");
        PutCaret(editor, declaration.Offset + 3);

        Press(editor, Key.D, KeyModifiers.Control);

        vm.Document.Text.Should().Be(before.Replace("Public Sub Go()\r\n" + RunText, string.Empty));
        vm.MemberRunsNow.Should().BeEmpty("no attribute line is left behind, orphaned or not");

        editor.Undo();
        Dispatcher.UIThread.RunJobs();
        vm.Document.Text.Should().Be(before, "one undo step brings both back, where VB6 brings back the line alone (C5)");
        RunOf(vm, "Go").Kind.Should().Be(MemberRunKind.Procedure);
        RunFold(editor, "Public Sub Go()")!.IsFolded.Should().BeTrue("the run comes back as it was left");
    }

    // ── Deleting the described line (4.5a) ──────────────────────────────────────────────────────────────

    [AvaloniaTheory]
    [InlineData(Key.Back)]
    [InlineData(Key.Delete)]
    public void DeletingADescribedDeclarationsTextLeavesItsRowAndTakesItsRun(Key key)
    {
        // The line's text without its line break: what is left is the row, empty, as with no run there.
        var vm = Open(Procedure);
        var editor = Show(vm);
        var before = vm.Document.Text;
        var declaration = LineWith(editor, "Public Sub Go()");
        editor.Select(declaration.Offset, declaration.Length);

        Press(editor, key);

        vm.Document.Text.Should().Be(before.Replace("Public Sub Go()\r\n" + RunText, "\r\n"));
        editor.Undo();
        Dispatcher.UIThread.RunJobs();
        vm.Document.Text.Should().Be(before);
    }

    [AvaloniaFact]
    public void DeletingADescribedVariablesLineTakesItsRun()
    {
        var vm = Open(
            "Option Explicit\r\n" +
            "Public Count As Long\r\n" +
            "Attribute Count.VB_VarDescription = \"How many\"\r\n" +
            "Public Total As Long\r\n");
        var editor = Show(vm);
        PutCaret(editor, LineWith(editor, "Public Count").Offset);

        Press(editor, Key.D, KeyModifiers.Control);

        vm.Document.Text.Should().EndWith("Option Explicit\r\nPublic Total As Long\r\n");
    }

    [AvaloniaFact]
    public void CtrlDOnALineNoRunHangsFromIsTheLibrarysOwn()
    {
        // The control: nothing to widen, so the window leaves the key to AvaloniaEdit, which deletes the line.
        var vm = Open(Procedure);
        var editor = Show(vm);
        var before = vm.Document.Text;
        PutCaret(editor, LineWith(editor, "    Debug.Print 1").Offset);

        Press(editor, Key.D, KeyModifiers.Control);

        vm.Document.Text.Should().Be(before.Replace("    Debug.Print 1\r\n", string.Empty));
    }

    [AvaloniaFact]
    public async Task CuttingADescribedDeclarationsLinePutsItsRunOnTheClipboardToo()
    {
        // With nothing selected a cut takes the caret's line, and that line takes its run. VB6 put only the visible
        // text on the clipboard, so a pasted procedure arrived undescribed (C6); here it arrives described.
        var vm = Open(Procedure);
        var editor = Show(vm);
        var before = vm.Document.Text;
        var lineStart = LineWith(editor, "Public Sub Go()").Offset;
        PutCaret(editor, lineStart + 2);

        Press(editor, Key.X, KeyModifiers.Control);

        var cut = "Public Sub Go()\r\n" + RunText;
        vm.Document.Text.Should().Be(before.Replace(cut, string.Empty));
        var clipped = await Clipboard(editor);
        clipped.Should().NotBeNull();
        clipped!.ReplaceLineEndings().Should().Be(cut.ReplaceLineEndings(),
            "the clipboard is written in the platform's newline, as AvaloniaEdit writes every copy");

        PutCaret(editor, lineStart);
        Press(editor, Key.V, KeyModifiers.Control);
        await UntilChanged(vm, before.Replace(cut, string.Empty));

        vm.Document.Text.Should().Be(before, "pasted back, the run is under its declaration again");
        RunOf(vm, "Go").Kind.Should().Be(MemberRunKind.Procedure);
    }

    [AvaloniaFact]
    public async Task CuttingASelectedDescribedDeclarationTakesItsRunToo()
    {
        var vm = Open(Procedure);
        var editor = Show(vm);
        var before = vm.Document.Text;
        var declaration = LineWith(editor, "Public Sub Go()");
        editor.Select(declaration.Offset, declaration.Length);

        Press(editor, Key.X, KeyModifiers.Control);

        vm.Document.Text.Should().Be(before.Replace("Public Sub Go()\r\n" + RunText, "\r\n"));
        (await Clipboard(editor))!.ReplaceLineEndings().Should().Be(
            ("Public Sub Go()\r\n" + RunText.TrimEnd('\r', '\n')).ReplaceLineEndings());
    }

    [AvaloniaFact]
    public void RetypingOverASelectedDeclarationKeepsTheRunForWhatTheNewLineDeclares()
    {
        // A replacement, not a deletion: the run stays and describes whatever the new line declares, which is what VB6
        // does with it (IDE probe C8).
        var vm = Open(Procedure);
        var editor = Show(vm);
        var declaration = LineWith(editor, "Public Sub Go()");
        editor.Select(declaration.Offset, declaration.Length);

        TypeText(editor, "Public Sub Leave()");

        vm.Document.Text.Should().Contain("Public Sub Leave()\r\n" + RunText);
        RunOf(vm, "Leave").Kind.Should().Be(MemberRunKind.Procedure);
    }

    [AvaloniaFact]
    public void PressKeyJudgesADeletionAsThePersonsKeyDoes()
    {
        // press_key used to refuse Ctrl+D on a described declaration, because the line's break is where the run's
        // region starts. A person's key now deletes both, so press_key does; what the key could not do it still refuses.
        var vm = Open(Procedure);
        var editor = Show(vm);
        var before = vm.Document.Text;
        var declaration = LineWith(editor, "Public Sub Go()");

        var run = RunOf(vm, "Go");
        editor.Select(declaration.Offset + 7, run.Region.Start + 5 - declaration.Offset - 7);
        UiAutomationDriver.PressKey(editor, "Back", null).Success.Should().BeFalse(
            "part of a declaration and part of its run: the run is not taken, and would be carved");
        vm.Document.Text.Should().Be(before);

        editor.TextArea.ClearSelection();
        PutCaret(editor, declaration.Offset + 3);
        var pressed = UiAutomationDriver.PressKey(editor, "D", "Ctrl");

        pressed.Success.Should().BeTrue(pressed.Error);
        vm.Document.Text.Should().Be(before.Replace("Public Sub Go()\r\n" + RunText, string.Empty));
    }

    // ── A line break at the end of a described line (4.5) ───────────────────────────────────────────────────

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void EnterAtTheEndOfADescribedDeclarationMakesALineOfItsOwnAfterTheRun(bool folded)
    {
        var vm = Open(Empty);
        var editor = Show(vm);
        var before = vm.Document.Text;
        RunFold(editor, "Public Sub Go()")!.IsFolded = folded;
        PutCaret(editor, LineWith(editor, "Public Sub Go()").EndOffset);

        Press(editor, Key.Enter);

        vm.Document.Text.Should().Be(before.Replace("\"Goes\"\r\nEnd Sub", "\"Goes\"\r\n\r\nEnd Sub"),
            "the closer is looked for past the run, and End Sub is there");
        editor.TextArea.Caret.Offset.Should().Be(LineWith(editor, "End Sub").Offset - 2, "on the new line");
        RunOf(vm, "Go").Kind.Should().Be(MemberRunKind.Procedure);
        var fold = RunFold(editor, "Public Sub Go()")!;
        fold.IsFolded.Should().Be(folded);
        fold.EndOffset.Should().Be(RunOf(vm, "Go").LastContentEnd,
            "text written at a fold's end is left outside it, measured, so the new line is not hidden in the box");
    }

    [AvaloniaFact]
    public void EnterReadsADescribedDeclarationFromWhereTheReaderStartsItForTheCloser()
    {
        // The line above ends in a name ending in an underscore, which VbAutoCloseProvider's looser rule takes for a
        // continuation. Read from DescribedStart, the declaration is Public Sub Go() alone, which wants End Sub.
        var vm = Open("Option Explicit\r\nPrivate y_\r\nPublic Sub Go()\r\nAttribute Go.VB_Description = \"Goes\"\r\n");
        var editor = Show(vm);
        var before = vm.Document.Text;
        PutCaret(editor, LineWith(editor, "Public Sub Go()").EndOffset);

        Press(editor, Key.Enter);

        vm.Document.Text.Should().Be(before + "    \r\nEnd Sub\r\n");
    }

    [AvaloniaFact]
    public void EnterAtTheEndOfADescribedDeclarationWithNoCloserPutsTheCloserAfterTheRun()
    {
        // A run that ends the file, with no line break after its last line: the closer still goes after it.
        var vm = Open("Option Explicit\r\n\r\nPublic Sub Go()\r\nAttribute Go.VB_Description = \"Goes\"");
        var editor = Show(vm);
        var before = vm.Document.Text;
        PutCaret(editor, LineWith(editor, "Public Sub Go()").EndOffset);

        Press(editor, Key.Enter);

        vm.Document.Text.Should().Be(before + "\r\n    \r\nEnd Sub");
        editor.TextArea.Caret.Offset.Should().Be(before.Length + "\r\n    ".Length, "on the body line, after its indent");
    }

    [AvaloniaFact]
    public void EnterRecasesTheDescribedDeclarationAndOneUndoTakesBothBack()
    {
        var vm = Open(
            "Option Explicit\r\n\r\npublic sub go()\r\nAttribute go.VB_Description = \"Goes\"\r\nend sub\r\n");
        var editor = Show(vm);
        var before = vm.Document.Text;
        PutCaret(editor, LineWith(editor, "public sub go()").EndOffset);

        Press(editor, Key.Enter);

        vm.Document.Text.Should().Contain("Public Sub go()\r\nAttribute go.VB_Description = \"Goes\"\r\n\r\nend sub");
        editor.Undo();
        Dispatcher.UIThread.RunJobs();
        vm.Document.Text.Should().Be(before, "the re-casing and the line break are one step");
    }

    [AvaloniaFact]
    public void EnterAtTheEndOfADeclarationEndingInANameEndingInAnUnderscoreIsNotAContinuation()
    {
        // 4.1's continuation rule, an underscore after whitespace: Total_ is a name, so the line is the declaration the
        // run describes, and the line break goes after the run. VbAutoCloseProvider.IsContinuationLine's looser rule
        // took it for a continuation and broke the line at the caret, under the declaration and above its run.
        var vm = Open("Option Explicit\r\nPublic Total_\r\nAttribute Total_.VB_VarDescription = \"x\"\r\nPublic Other As Long\r\n");
        var editor = Show(vm);
        PutCaret(editor, LineWith(editor, "Public Total_").EndOffset);

        Press(editor, Key.Enter);

        vm.Document.Text.Should().EndWith(
            "Public Total_\r\nAttribute Total_.VB_VarDescription = \"x\"\r\n\r\nPublic Other As Long\r\n");
    }

    [AvaloniaFact]
    public void ShiftEnterAtTheEndOfADescribedDeclarationMakesALineAfterTheRunIndentedLikeTheDeclaration()
    {
        var vm = Open(
            "Option Explicit\r\n\r\n    Public Property Get Total() As Long\r\n" +
            "Attribute Total.VB_Description = \"The total\"\r\n    End Property\r\n");
        var editor = Show(vm);
        var before = vm.Document.Text;
        PutCaret(editor, LineWith(editor, "Public Property Get Total").EndOffset);

        Press(editor, Key.Enter, KeyModifiers.Shift);

        vm.Document.Text.Should().Be(before.Replace("\"The total\"\r\n", "\"The total\"\r\n    \r\n"));
        editor.TextArea.Caret.Offset.Should().Be(before.IndexOf("    End Property", StringComparison.Ordinal) + 4,
            "at the end of the new line's indent, where the old next line started");
    }

    [AvaloniaFact]
    public async Task APasteOfSeveralLinesAtTheEndOfADescribedDeclarationPutsAllButTheFirstAfterTheRun()
    {
        // As if the run were not there: the first line joins the declaration, the rest follow the run, and every line
        // break is written in the module's newline.
        var vm = Open(Procedure);
        var editor = Show(vm);
        var before = vm.Document.Text;
        await SetClipboard(editor, " ' first\n    Debug.Print 0");
        PutCaret(editor, LineWith(editor, "Public Sub Go()").EndOffset);

        Press(editor, Key.V, KeyModifiers.Control);
        await UntilChanged(vm, before);

        vm.Document.Text.Should().Be(before
            .Replace("Public Sub Go()\r\n", "Public Sub Go() ' first\r\n")
            .Replace("VB_UserMemId = 0\r\n", "VB_UserMemId = 0\r\n    Debug.Print 0\r\n"));
        editor.TextArea.Caret.Offset.Should().Be(LineWith(editor, "    Debug.Print 0").EndOffset);
        editor.Undo();
        Dispatcher.UIThread.RunJobs();
        vm.Document.Text.Should().Be(before);
    }

    [AvaloniaFact]
    public async Task APasteWithNoLineBreakAtTheEndOfADescribedDeclarationJoinsIt()
    {
        var vm = Open(Procedure);
        var editor = Show(vm);
        var before = vm.Document.Text;
        await SetClipboard(editor, " ' goes");
        PutCaret(editor, LineWith(editor, "Public Sub Go()").EndOffset);

        Press(editor, Key.V, KeyModifiers.Control);
        await UntilChanged(vm, before);

        vm.Document.Text.Should().Be(before.Replace("Public Sub Go()\r\n", "Public Sub Go() ' goes\r\n"));
    }

    [AvaloniaFact]
    public void TypeTextOfSeveralLinesAtTheEndOfADescribedDeclarationPutsThemAfterTheRunAndSaysSo()
    {
        var vm = Open(Procedure);
        var editor = Show(vm);
        var before = vm.Document.Text;
        PutCaret(editor, LineWith(editor, "Public Sub Go()").EndOffset);

        var typed = UiAutomationDriver.TypeText(editor, "\n    Debug.Print 0");

        typed.Success.Should().BeTrue(typed.Error);
        typed.Detail.Should().Contain("Go").And.Contain("after").And.Contain($"offset {editor.TextArea.Caret.Offset}");
        vm.Document.Text.Should().Be(before.Replace("VB_UserMemId = 0\r\n", "VB_UserMemId = 0\r\n    Debug.Print 0\r\n"));
    }

    [AvaloniaFact]
    public void PressKeyEnterAtTheEndOfADescribedDeclarationDoesWhatAPersonsEnterDoes()
    {
        var vm = Open(Empty);
        var editor = Show(vm);
        var before = vm.Document.Text;
        PutCaret(editor, LineWith(editor, "Public Sub Go()").EndOffset);

        UiAutomationDriver.PressKey(editor, "Enter", null).Success.Should().BeTrue();

        vm.Document.Text.Should().Be(before.Replace("\"Goes\"\r\nEnd Sub", "\"Goes\"\r\n\r\nEnd Sub"));
    }
}
