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
    private readonly IWindowManager windowManager = Substitute.For<IWindowManager>();
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

    [AvaloniaTheory]
    [InlineData("enter")]
    [InlineData("shift")]
    [InlineData("paste")]
    [InlineData("type")]
    public async Task ALineBreakBeforeATrailingCommentKeepsTheRunWithItsDeclaration(string writer)
    {
        var vm = Open(Procedure.Replace("Public Sub Go()", "Public Sub Go() ' does X"));
        var editor = Show(vm);
        var before = vm.Document.Text;
        PutCaret(editor, before.IndexOf("' does X", StringComparison.Ordinal));
        var inserted = writer is "paste" or "type" ? "A\r\nB" : "\r\n";
        if (writer == "paste")
        {
            await SetClipboard(editor, "A\nB");
            PressCommand(editor, HexIDE.ApplicationCommands.PasteCommand);
            await UntilChanged(vm, before);
        }
        else if (writer == "type")
            UiAutomationDriver.TypeText(editor, "A\nB").Success.Should().BeTrue();
        else
            Press(editor, Key.Enter, writer == "shift" ? KeyModifiers.Shift : KeyModifiers.None);

        var head = inserted.StartsWith("A", StringComparison.Ordinal) ? "A" : "";
        var tail = head.Length > 0 ? "B" : "";
        vm.Document.Text.Should().Be(before.Replace("' does X", head)
            .Replace("VB_UserMemId = 0\r\n", "VB_UserMemId = 0\r\n" + tail + "' does X\r\n"));
        editor.CaretOffset.Should().Be(vm.Document.Text.IndexOf("' does X", StringComparison.Ordinal));
        RunOf(vm, "Go").DescribesMember.Should().BeTrue();
        RunFold(editor, "Public Sub Go()")!.IsFolded.Should().BeTrue();
        editor.Undo();
        Dispatcher.UIThread.RunJobs();
        vm.Document.Text.Should().Be(before);
    }

    [AvaloniaTheory]
    [InlineData("    public sub Go() ' does X", "X", "Go", "    ")]
    [InlineData("Public Sub Go(Optional s As String = \"it's\") ' does X", "' does X", "Go", "")]
    [InlineData("Public Count As Long ' how many", "' how many", "Count", "")]
    [InlineData("Public Sub Go( _\r\n    ByVal x As Long) ' does X", "' does X", "Go", "    ")]
    public void EnterSplitsACommentTailWithoutRecasingOrClosing(string declaration, string tail, string member, string indent)
    {
        var attribute = member == "Count" ? "VB_VarDescription" : "VB_Description";
        var vm = Open($"Option Explicit\r\n{declaration}\r\nAttribute {member}.{attribute} = \"Goes\"\r\n");
        var editor = Show(vm);
        var before = vm.Document.Text;
        PutCaret(editor, before.IndexOf(tail, StringComparison.Ordinal));

        Press(editor, Key.Enter);

        vm.Document.Text.Should().Be(before.Replace(tail, "")
            .Replace("\"Goes\"\r\n", "\"Goes\"\r\n" + indent + tail + "\r\n"));
        editor.CaretOffset.Should().Be(vm.Document.Text.IndexOf(tail, StringComparison.Ordinal));
        RunOf(vm, member).DescribesMember.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task InsertFileBeforeATrailingCommentLeavesTheFoldAndCaretBeforeTheMovedTail()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"hexide-insert-{Guid.NewGuid():N}.txt");
        await System.IO.File.WriteAllTextAsync(path, "A\nB", TestContext.Current.CancellationToken);
        try
        {
            windowManager.OpenFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerOpenOptions>())
                .Returns(Task.FromResult<IReadOnlyList<string>?>([path]));
            var vm = Open(Procedure.Replace("Public Sub Go()", "Public Sub Go() ' does X"));
            var editor = Show(vm);

            await vm.InsertFileAsync(vm.Document.Text.IndexOf("' does X", StringComparison.Ordinal), 0);
            Dispatcher.UIThread.RunJobs();

            vm.Document.Text.Should().Contain("Public Sub Go() A\r\nAttribute")
                .And.Contain("VB_UserMemId = 0\r\nB' does X\r\n");
            editor.CaretOffset.Should().Be(vm.Document.Text.IndexOf("' does X", StringComparison.Ordinal));
            RunFold(editor, "Public Sub Go()")!.IsFolded.Should().BeTrue();
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void AnAddInsLineBreakBeforeATrailingCommentLeavesTheEditorUntouched()
    {
        var vm = Open(Procedure.Replace("Public Sub Go()", "Public Sub Go() ' does X"));
        var editor = Show(vm);
        var before = vm.Document.Text;

        vm.ApplyEdits([new TextChange(before.IndexOf("' does X", StringComparison.Ordinal), 0, "\n")])
            .Should().NotBeNull();

        vm.Document.Text.Should().Be(before);
        RunFold(editor, "Public Sub Go()")!.IsFolded.Should().BeTrue();
    }

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
            windowManager, Substitute.For<IEditorService>(), Substitute.For<IProjectService>(),
            eventBus, lsp, settings, Substitute.For<IStatusBarService>(), new BookmarkService(), new BreakpointService(),
            Substitute.For<IDebugController>(), new RunScope(), new LocalizationService());
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

    /// <summary>
    /// The key the IDE binds to <paramref name="command"/> on this platform: Ctrl+X on Windows and Linux, Cmd+X on
    /// macOS, where Ctrl+X cuts nothing (CI's macOS run, #273).
    /// </summary>
    private static void PressCommand(TextEditor editor, Avalonia.Labs.Input.RoutedCommand command)
    {
        var gesture = command.Gestures[0];
        Press(editor, gesture.Key, gesture.KeyModifiers);
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

        PressCommand(editor, HexIDE.ApplicationCommands.CutCommand);

        var cut = "Public Sub Go()\r\n" + RunText;
        vm.Document.Text.Should().Be(before.Replace(cut, string.Empty));
        var clipped = await Clipboard(editor);
        clipped.Should().NotBeNull();
        clipped!.ReplaceLineEndings().Should().Be(cut.ReplaceLineEndings(),
            "the clipboard is written in the platform's newline, as AvaloniaEdit writes every copy");

        PutCaret(editor, lineStart);
        PressCommand(editor, HexIDE.ApplicationCommands.PasteCommand);
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

        PressCommand(editor, HexIDE.ApplicationCommands.CutCommand);

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
        var start = declaration.Offset + 7;
        var length = declaration.EndOffset - start;
        var partial = UiAutomationDriver.PressKey(editor, "Back", null);
        partial.Success.Should().BeTrue(partial.Error);
        vm.Document.Text.Should().Be(before.Remove(start, length),
            "the provider carves out the run and deletes the editable part (#1055)");
        editor.Document.UndoStack.Undo();
        vm.Document.Text.Should().Be(before, "one undo restores the partial deletion");

        editor.TextArea.ClearSelection();
        PutCaret(editor, vm.Document.Text.IndexOf("Public Sub Go()", StringComparison.Ordinal) + 3);
        var gesture = AvaloniaEditCommands.DeleteLine.Gesture!;
        var pressed = UiAutomationDriver.PressKey(editor, gesture.Key.ToString(), gesture.KeyModifiers.ToString());

        pressed.Success.Should().BeTrue(pressed.Error);
        vm.Document.Text.Should().Be(before.Replace("Public Sub Go()\r\n" + RunText, string.Empty));
    }

    [AvaloniaFact]
    public void PressKeyCtrlDJudgesTheLinesTheSelectionTouches()
    {
        // From part-way along the declaration into the run: what Ctrl+D deletes is both lines, which takes the run
        // whole, and not the selection, which would carve it.
        var vm = Open(Procedure);
        var editor = Show(vm);
        var before = vm.Document.Text;
        var declaration = LineWith(editor, "Public Sub Go()");
        var run = RunOf(vm, "Go");
        editor.Select(declaration.Offset + 7, run.Region.Start + 5 - declaration.Offset - 7);

        var pressed = UiAutomationDriver.PressKey(editor, "D", "Ctrl");

        pressed.Success.Should().BeTrue(pressed.Error);
        vm.Document.Text.Should().Be(before.Replace("Public Sub Go()\r\n" + RunText, string.Empty));
    }

    [AvaloniaFact]
    public void ALineBreakTypedOverASelectedDeclarationIsAReplacementAndLeavesTheRunAnOrphan()
    {
        // Only the keys that delete widen the selection. Shift+Enter over the declaration's text replaces it with a line
        // break, which goes after the run; the run is not taken, and is left under the emptied line, which declares
        // nothing, as an orphan kept as it was.
        var vm = Open(Procedure);
        var editor = Show(vm);
        var declaration = LineWith(editor, "Public Sub Go()");
        editor.Select(declaration.Offset, declaration.Length);

        Press(editor, Key.Enter, KeyModifiers.Shift);

        vm.Document.Text.Should().Contain("Option Explicit\r\n\r\n\r\n" + RunText + "\r\n    Debug.Print 1");
        vm.MemberRunsNow.Should().ContainSingle().Which.Kind.Should().Be(MemberRunKind.Orphan);
    }

    // ── Enter and type_text over a selection write over it, as typing does (#1040) ─────────────────────────────

    private const string Foo = "Option Explicit\r\n\r\nPrivate Sub Foo()\r\n    Debug.Print 1, 2\r\nEnd Sub\r\n";

    // Selects ", 2", with the caret at the end of the line or before the comma, as Shift+End or Shift+Left leaves it.
    private static void SelectTwo(TextEditor editor, bool caretAtEnd)
    {
        var line = LineWith(editor, "    Debug.Print 1, 2");
        var (from, to) = (line.EndOffset - 3, line.EndOffset);
        editor.TextArea.Caret.Offset = caretAtEnd ? to : from;
        editor.TextArea.Selection = AvaloniaEdit.Editing.Selection.Create(editor.TextArea, caretAtEnd ? from : to, caretAtEnd ? to : from);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void EnterOverASelectionReplacesItAsOneChange(bool caretAtEnd)
    {
        // It used to put the line break at the caret, one end of the selection, and leave the selection's text: under
        // the line with the caret at its end, or split off onto a line of its own with the caret before it.
        var vm = Open(Foo);
        var editor = Show(vm);
        var before = vm.Document.Text;
        SelectTwo(editor, caretAtEnd);

        Press(editor, Key.Enter);

        // Compared as lines: a plain Enter writes "\n" whatever the file's line breaks, with or without a selection (#530).
        vm.Document.Text.ReplaceLineEndings("\n").Should().Be(before.Replace("1, 2\r\n", "1\r\n    \r\n").ReplaceLineEndings("\n"));
        editor.TextArea.Caret.Offset.Should().Be(LineWith(editor, "End Sub").Offset - 2, "on the new line, indented");
        editor.Document.UndoStack.Undo();
        vm.Document.Text.Should().Be(before, "one change, which one undo takes back");
    }

    [AvaloniaFact]
    public void EnterOverASelectedDescribedDeclarationIsTheReplacementShiftEnterMakes()
    {
        var shifted = Open(Procedure);
        var shiftedEditor = Show(shifted);
        var declaration = LineWith(shiftedEditor, "Public Sub Go()");
        shiftedEditor.Select(declaration.Offset, declaration.Length);
        Press(shiftedEditor, Key.Enter, KeyModifiers.Shift);

        var vm = Open(Procedure);
        var editor = Show(vm);
        declaration = LineWith(editor, "Public Sub Go()");
        editor.Select(declaration.Offset, declaration.Length);
        Press(editor, Key.Enter);

        vm.Document.Text.ReplaceLineEndings("\n").Should().Be(shifted.Document.Text.ReplaceLineEndings("\n"),
            "typing over a selection does not take the run, as a delete does");
    }

    [AvaloniaFact]
    public void EnterOverASelectionReachingARunWritesNothing()
    {
        var vm = Open(Procedure);
        var editor = Show(vm);
        var before = vm.Document.Text;
        var declaration = LineWith(editor, "Public Sub Go()");
        editor.Select(declaration.Offset, LineWith(editor, "Attribute Go.VB_UserMemId").Offset + 5 - declaration.Offset);

        Press(editor, Key.Enter);

        // Refused whole, where a typed key writes over what the read-only sections let it: #1055 is that alignment.
        vm.Document.Text.Should().Be(before, "the run is the IDE's to change, and Enter writes over the whole selection or nothing");
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void TypeTextOverASelectionReplacesItAndSaysHowMuch(bool caretAtEnd)
    {
        var vm = Open(Foo);
        var editor = Show(vm);
        var before = vm.Document.Text;
        SelectTwo(editor, caretAtEnd);

        var outcome = UiAutomationDriver.TypeText(editor, ", 3");

        outcome.Success.Should().BeTrue(outcome.Error);
        outcome.Detail.Should().Be("typed 3 char(s) into TextEditor, replacing the 3 selected char(s)");
        vm.Document.Text.Should().Be(before.Replace("1, 2", "1, 3"));
        editor.TextArea.Selection.IsEmpty.Should().BeTrue();
        editor.Document.UndoStack.Undo();
        vm.Document.Text.Should().Be(before, "one change, which one undo takes back");
    }

    [AvaloniaFact]
    public void TypeTextOverASelectionReachingARunReplacesOnlyTheEditablePart()
    {
        var vm = Open(Procedure);
        var editor = Show(vm);
        var before = vm.Document.Text;
        var declaration = LineWith(editor, "Public Sub Go()");
        editor.Select(declaration.Offset, LineWith(editor, "Attribute Go.VB_UserMemId").Offset + 5 - declaration.Offset);

        var start = declaration.Offset;
        var length = declaration.Length;
        var outcome = UiAutomationDriver.TypeText(editor, "x");

        outcome.Success.Should().BeTrue(outcome.Error);
        outcome.Detail.Should().Contain("the Attribute lines describing Go");
        vm.Document.Text.Should().Be(before.Remove(start, length).Insert(start, "x"));
        editor.SelectionLength.Should().Be(0);
        editor.Document.UndoStack.Undo();
        vm.Document.Text.Should().Be(before, "one undo restores the replacement");
    }

    [AvaloniaFact]
    public void TypeTextPastARunLeavesNoSelectionForTheNextWriteToGoOver()
    {
        var vm = Open(Procedure);
        var editor = Show(vm);
        var declaration = LineWith(editor, "Public Sub Go()");
        editor.Select(declaration.EndOffset - 2, 2);

        var typed = UiAutomationDriver.TypeText(editor, "(a)\n    Dim z");
        typed.Success.Should().BeTrue(typed.Error);
        editor.TextArea.Selection.IsEmpty.Should().BeTrue("the replaced text's selection does not carry over onto its replacement");
        var next = UiAutomationDriver.TypeText(editor, "Q");

        next.Detail.Should().NotContain("replacing");
        vm.Document.Text.Should().Contain("Public Sub Go(a)\r\n" + RunText + "    Dim zQ\r\n");
    }

    // A box from a column of "Option Explicit" to the end of "Public Sub Go()": from column 8, "Explicit", nothing, "Sub Go()".
    private static void SelectBox(TextEditor editor, int column = 8)
    {
        var (top, bottom) = (LineWith(editor, "Option Explicit"), LineWith(editor, "Public Sub Go()"));
        editor.TextArea.Caret.Offset = bottom.EndOffset;
        editor.TextArea.Selection = new AvaloniaEdit.Editing.RectangleSelection(editor.TextArea,
            new TextViewPosition(top.LineNumber, column), new TextViewPosition(bottom.LineNumber, bottom.Length + 1));
    }

    [AvaloniaFact]
    public void TypeTextOverABoxSelectionWritesOverTheBoxAndNothingOutsideIt()
    {
        var vm = Open(Procedure);
        var editor = Show(vm);

        SelectBox(editor);
        var typed = UiAutomationDriver.TypeText(editor, "x");

        typed.Success.Should().BeTrue(typed.Error);
        typed.Detail.Should().Contain("box selection");
        // Matched, not spelled out: a box is a span of the view's x, so which characters it covers on each line depends on
        // the font, which is not monospaced on every headless host.
        vm.Document.Text.Should().MatchRegex(
            @"\r\nOption [^\r\n]*x[^\r\n]*\r\n[^\r\n]*\r\nPublic [^\r\n]*x\r\n" + System.Text.RegularExpressions.Regex.Escape(RunText),
            "only the box is written over, so the declaration keeps what is left of it, and its run");
        vm.Document.Text.Should().NotContain("Go()", "the box reaches the end of the declaration's line");
    }

    /// <summary>
    /// Ends the test process if what it guards is still running after <paramref name="seconds"/>. A hang on the UI thread
    /// cannot be timed out by the test framework, and every test after it would queue behind it for as long as CI lets
    /// the run go on (#1251).
    /// </summary>
    private static Timer Watchdog(string what, int seconds = 30) =>
        new(_ => Environment.FailFast($"{what} was still running after {seconds} s: the UI thread hung (#1251)."),
            null, TimeSpan.FromSeconds(seconds), Timeout.InfiniteTimeSpan);

    // AvaloniaEdit's box replacement loops for ever in the text view's height tree when the run is folded under the box's
    // last line, so a person's key hung the IDE. Measured on this box (#1251): one character hangs it, while "()" and
    // "zz" do not, so each case writes "x", and with the window's guard removed each hangs.
    private static void ShouldHaveWrittenXOverTheBoxAndKeptTheFold(CodeEditorViewModel vm, TextEditor editor)
    {
        vm.Document.Text.Should().MatchRegex(
            @"\r\nOption Explicx\r\n[^\r\n]*\r\nPublic [^\r\n]*x\r\n" + System.Text.RegularExpressions.Regex.Escape(RunText),
            "the box is written over, and the run stays under its declaration");
        RunFold(editor, "Public ")!.IsFolded.Should().BeTrue("the fold is closed again once written");
    }

    [AvaloniaFact]
    public void TypingOverABoxSelectionEndingOnAFoldedDeclarationWritesAndKeepsTheFold()
    {
        var vm = Open(Procedure);
        var editor = Show(vm);
        RunFold(editor, "Public Sub Go()")!.IsFolded.Should().BeTrue("the premise: a run is folded as it opens");

        SelectBox(editor, column: 14);
        using (Watchdog("Typing over the box"))
            TypeText(editor, "x");

        ShouldHaveWrittenXOverTheBoxAndKeptTheFold(vm, editor);
    }

    [AvaloniaFact]
    public async Task PastingOverABoxSelectionEndingOnAFoldedDeclarationWritesAndKeepsTheFold()
    {
        // Ctrl+V took the library's paste, which replaced the box with the fold closed (#1251).
        var vm = Open(Procedure);
        var editor = Show(vm);
        var before = vm.Document.Text;
        RunFold(editor, "Public Sub Go()")!.IsFolded.Should().BeTrue("the premise: a run is folded as it opens");
        await SetClipboard(editor, "x");

        SelectBox(editor, column: 14);
        using (Watchdog("A paste over the box"))
        {
            PressCommand(editor, HexIDE.ApplicationCommands.PasteCommand);
            await UntilChanged(vm, before);
        }

        ShouldHaveWrittenXOverTheBoxAndKeptTheFold(vm, editor);
        editor.Undo();
        Dispatcher.UIThread.RunJobs();
        vm.Document.Text.Should().Be(before, "one change, which one undo takes back");
    }

    [AvaloniaFact]
    public void EnterOverABoxSelectionTakesTheBoxsText()
    {
        var vm = Open(Procedure);
        var editor = Show(vm);

        var before = vm.Document.Text;
        SelectBox(editor);
        Press(editor, Key.Enter);

        // It used to put the line break at the caret and leave the box's text.
        vm.Document.Text.Should().NotContain("Explicit").And.NotContain("Sub Go()").And.Contain("\r\nOption \r\n");
        editor.TextArea.Selection.IsEmpty.Should().BeTrue();
        editor.Document.UndoStack.Undo();
        vm.Document.Text.Should().Be(before, "one change, which one undo takes back");
    }

    [AvaloniaTheory]
    [InlineData("public sub Go() 'x", " 'x")]
    [InlineData("public function Go() as long", " as long")]
    public void EnterOverTheTailOfADescribedDeclarationClosesItsBlockAsEnterAtItsEndDoes(string declaration, string tail)
    {
        static string Module(string line) => $"Option Explicit\r\n\r\n{line}\r\nAttribute Go.VB_Description = \"Goes\"\r\n";
        var plain = Open(Module(declaration[..^tail.Length]));
        var plainEditor = Show(plain);
        PutCaret(plainEditor, LineWith(plainEditor, "Go()").EndOffset);
        Press(plainEditor, Key.Enter);

        var vm = Open(Module(declaration));
        var editor = Show(vm);
        var line = LineWith(editor, "Go()");
        editor.Select(line.EndOffset - tail.Length, tail.Length);
        Press(editor, Key.Enter);

        vm.Document.Text.Should().Be(plain.Document.Text, "the line it leaves is re-cased and its block closed");
        vm.Document.Text.Should().Contain("End ");
    }

    [AvaloniaFact]
    public void TypeTextOverATextBoxSelectionReplacesIt()
    {
        var box = new TextBox { Text = "Debug.Print 1, 2", Width = 300 };
        var window = new Window { Width = 400, Height = 100, Content = box };
        windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        box.SelectionStart = 16;
        box.SelectionEnd = 13;
        var texts = new List<string?>();
        box.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) texts.Add(box.Text); };

        var outcome = UiAutomationDriver.TypeText(box, ", 3");

        outcome.Success.Should().BeTrue(outcome.Error);
        outcome.Detail.Should().EndWith(", replacing the 3 selected char(s)");
        box.Text.Should().Be("Debug.Print 1, 3");
        box.CaretIndex.Should().Be(16);
        texts.Should().Equal(["Debug.Print 1, 3"], "one change, so what is bound to the box sees no text in between");
    }

    [AvaloniaTheory]
    [InlineData(Key.Back)]
    [InlineData(Key.Delete)]
    public void AWordDeleteOverASelectedDescribedDeclarationTakesItsRun(Key key)
    {
        // Ctrl+Backspace and Ctrl+Delete delete a selection as Backspace does, through the library's own word deletes,
        // so the window widens it the same way (#273 task 4.5a review).
        var vm = Open(Procedure);
        var editor = Show(vm);
        var before = vm.Document.Text;
        var declaration = LineWith(editor, "Public Sub Go()");
        editor.Select(declaration.Offset, declaration.Length);

        Press(editor, key, KeyModifiers.Control);

        vm.Document.Text.Should().Be(before.Replace("Public Sub Go()\r\n" + RunText, "\r\n"));
    }

    [AvaloniaTheory]
    [InlineData("Back", null)]
    [InlineData("Delete", null)]
    [InlineData("X", "Ctrl")]
    public void PressKeyLetsADeletionOfAWholeDescribedLineThrough(string key, string? modifiers)
    {
        // The whole line, its line break included, reaches into the run's region, so judged as text written over it
        // the key would be refused. Judged as the deletion it is, it takes the run with the line, as a person's key does
        // (#273 task 4.5a review).
        var vm = Open(Procedure);
        var editor = Show(vm);
        var before = vm.Document.Text;
        var declaration = LineWith(editor, "Public Sub Go()");
        editor.Select(declaration.Offset, declaration.TotalLength);

        // Cut is Cmd+X on macOS, where the IDE binds it as the platform does (ApplicationCommands).
        var pressed = UiAutomationDriver.PressKey(editor, key, modifiers == "Ctrl" && OperatingSystem.IsMacOS() ? "Cmd" : modifiers);

        pressed.Success.Should().BeTrue(pressed.Error);
        vm.Document.Text.Should().Be(before.Replace("Public Sub Go()\r\n" + RunText, string.Empty));
    }

    // ── A line break at the end of a described line (4.5) ───────────────────────────────────────────────────

    [AvaloniaFact]
    public void EnterAtTheEndOfAContinuedDescribedDeclarationRecasesAndReadsAllOfIt()
    {
        // Every physical line of the declaration is committed, from DescribedStart, and read for the closer: the last
        // line alone declares nothing and wants none.
        var vm = Open("Option Explicit\r\n\r\npublic sub Go( _\r\n    ByVal x As Long)\r\nAttribute Go.VB_Description = \"Goes\"\r\n");
        var editor = Show(vm);
        var before = vm.Document.Text;
        PutCaret(editor, LineWith(editor, "    ByVal x As Long)").EndOffset);

        Press(editor, Key.Enter);

        // The closer's indent is the caret's line's, a continuation line's here, as it is with no run (not pinned).
        vm.Document.Text.Should().StartWith(before.Replace("public sub Go(", "Public Sub Go("));
        vm.Document.Text[before.Length..].Should().MatchRegex(@"^ *\r\n *End Sub\r\n$");
    }

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
    public void ShiftEnterAtTheEndOfAContinuedDescribedDeclarationIndentsLikeItsLastLine()
    {
        // The library indents a new line like the line above it, which with no run there is the declaration's last
        // physical line, the continuation, and not its first (#273 task 4.5 review).
        var vm = Open("Option Explicit\r\n\r\nPublic Sub Go( _\r\n    ByVal x As Long)\r\nAttribute Go.VB_Description = \"Goes\"\r\nEnd Sub\r\n");
        var editor = Show(vm);
        var before = vm.Document.Text;
        PutCaret(editor, LineWith(editor, "    ByVal x As Long)").EndOffset);

        Press(editor, Key.Enter, KeyModifiers.Shift);

        vm.Document.Text.Should().Be(before.Replace("\"Goes\"\r\n", "\"Goes\"\r\n    \r\n"));
    }

    [AvaloniaFact]
    public async Task APasteOfSeveralLinesAtTheEndOfADescribedDeclarationPutsAllButTheFirstAfterTheRun()
    {
        // As if the run were not there: the first line joins the declaration, the rest follow the run, and every line
        // break is written in the module's newline, and a tab as spaces, as the library's own paste writes them.
        var vm = Open(Procedure);
        var editor = Show(vm);
        var before = vm.Document.Text;
        await SetClipboard(editor, " ' first\n\tDebug.Print 0");
        PutCaret(editor, LineWith(editor, "Public Sub Go()").EndOffset);

        PressCommand(editor, HexIDE.ApplicationCommands.PasteCommand);
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

        PressCommand(editor, HexIDE.ApplicationCommands.PasteCommand);
        await UntilChanged(vm, before);

        vm.Document.Text.Should().Be(before.Replace("Public Sub Go()\r\n", "Public Sub Go() ' goes\r\n"));
    }

    [AvaloniaFact]
    public async Task APasteTheWindowLeavesToTheLibrarysRuleIsStillWrittenInTheModulesNewline()
    {
        // A selection ending at an anchor that crosses another member's run is carved, so the window does not place the
        // paste and writes it where the carving leaves it, as the library's paste would; and in the module's newline, as
        // the library's paste would too (#273 task 4.5 review).
        var vm = Open(
            "Option Explicit\r\n" +
            "Public Count As Long\r\n" +
            "Attribute Count.VB_VarDescription = \"How many\"\r\n" +
            "\r\n" +
            "Public Sub Go()\r\n" + RunText + "End Sub\r\n");
        var editor = Show(vm);
        var before = vm.Document.Text;
        var from = before.IndexOf("Count As Long", StringComparison.Ordinal);
        editor.Select(from, RunOf(vm, "Go").Anchor - from);
        await SetClipboard(editor, "A\nB");

        PressCommand(editor, HexIDE.ApplicationCommands.PasteCommand);
        await UntilChanged(vm, before);

        vm.Document.Text.Should().Contain("A\r\nB").And.Contain("Attribute Count.VB_VarDescription",
            "the run the selection crossed is carved out of what the paste replaced");
        vm.Document.Text.Replace("\r\n", string.Empty, StringComparison.Ordinal).Should().NotContain("\n",
            "no line break is written as the clipboard had it");
    }

    [AvaloniaFact]
    public void TypeTextOfSeveralLinesAtTheEndOfADescribedDeclarationPutsThemAfterTheRunAndSaysSo()
    {
        var vm = Open(Procedure);
        var editor = Show(vm);
        var before = vm.Document.Text;
        PutCaret(editor, LineWith(editor, "Public Sub Go()").EndOffset);

        const string sent = "\n    Debug.Print 0";
        var typed = UiAutomationDriver.TypeText(editor, sent);

        typed.Success.Should().BeTrue(typed.Error);
        typed.Detail.Should().Contain("Go").And.Contain("after").And.Contain($"offset {editor.TextArea.Caret.Offset}");
        typed.Detail.Should().StartWith($"typed {sent.Length} char(s)",
            "the count is of what was sent, as the reply everywhere else counts it, though its LF was written as CRLF");
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
