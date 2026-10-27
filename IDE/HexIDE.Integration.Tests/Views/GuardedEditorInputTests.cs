using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
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
/// The code window's own input paths that write the document directly, and the automation tools that drive
/// them, held off the header (hexide-io/HexIDE#273 task 3.9).
/// </summary>
/// <remarks>
/// These run against a real rendered editor, because the Enter handler is a tunnel handler on the text area
/// and the automation refusal reads the view model the text area inherits: neither is visible from a
/// view-model test. The second needs AvaloniaEdit's control theme, which the test app did not load until
/// these were written — without a template the text area is never parented, inherits nothing, and every
/// refusal passed as "not a code window".
/// </remarks>
public class GuardedEditorInputTests : IDisposable
{
    private readonly List<Window> windows = [];
    private readonly List<CodeEditorViewModel> made = [];

    public void Dispose()
    {
        foreach (var w in windows) w.Close();
        foreach (var vm in made) vm.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    private const string Frm =
        "VERSION 5.00\r\n" +
        "Begin VB.Form frmOrders \r\n" +
        "   Caption         =   \"Orders\"\r\n" +
        "   Begin VB.CommandButton cmdOK \r\n" +
        "      Caption         =   \"OK\"\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"frmOrders\"\r\n" +
        "Attribute VB_PredeclaredId = True\r\n" +
        "Option Explicit\r\n" +
        "\r\n" +
        "Private Sub cmdOK_Click()\r\n" +
        "    Unload Me\r\n" +
        "End Sub\r\n";

    private CodeEditorViewModel OpenForm(bool heldReadOnly = false, IWindowManager? windowManager = null,
        ILspClient? client = null, ILocalizationService? localization = null)
    {
        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe<CreateOrNavigateToSubEvent>(Arg.Any<Action<CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(Arg.Any<Action<ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<FormUnloadedEvent>(Arg.Any<Action<FormUnloadedEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<DocumentSavedEvent>(Arg.Any<Action<DocumentSavedEvent>>()).Returns(Substitute.For<IDisposable>());

        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);
        var lsp = client ?? Substitute.For<ILspClient>();
        lsp.IsRunning.Returns(false);
        lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<DocumentSymbol>());

        var vm = new CodeEditorViewModel(
            windowManager ?? Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(), Substitute.For<IProjectService>(),
            eventBus, lsp, settings, Substitute.For<IStatusBarService>(), Substitute.For<IBookmarkService>(),
            Substitute.For<IBreakpointService>(), Substitute.For<IDebugController>(), new RunScope(),
            localization ?? Substitute.For<ILocalizationService>());
        made.Add(vm);
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, Frm, NullSink.Instance)!;
        // Held read-only as a whole, as the deserializer holds a form whose .frx content did not all arrive.
        if (heldReadOnly)
            form.MarkUnfaithfulToSave(UnfaithfulSaveCause.UnreproducibleBinaryContent,
                "it references companion binary content HexIDE cannot re-emit (0 of 1 blob(s) reached the model)");
        return vm.Initialize(form);
    }

    private (Window Window, TextEditor Editor) Show(CodeEditorViewModel vm)
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
        return (window, editor);
    }

    /// <summary>
    /// Enter as the input system delivers it: raised on the focused text area, where the editor's tunnel
    /// handler and then AvaloniaEdit's own see it. Deliberately not through the automation driver, which
    /// refuses the key before it is raised and so would never exercise the handler.
    /// </summary>
    private static void PressEnter(TextEditor editor)
    {
        editor.TextArea.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, KeyModifiers = KeyModifiers.None,
        });
        Dispatcher.UIThread.RunJobs();
    }

    private static int InHeader(CodeEditorViewModel vm) =>
        vm.Document.Text.IndexOf("\"Orders\"", StringComparison.Ordinal);

    private static int InCode(CodeEditorViewModel vm) =>
        vm.Document.Text.IndexOf("Unload Me", StringComparison.Ordinal) + "Unload Me".Length;

    [AvaloniaFact]
    public void RenameUsesLocalizedPromptAndRefusesAReservedNameBeforeAskingTheServer()
    {
        var windows = Substitute.For<IWindowManager>();
        windows.InputBox(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns("Rem");
        var lsp = Substitute.For<ILspClient>();
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => "translated:" + call.Arg<string>());
        var vm = OpenForm(windowManager: windows, client: lsp, localization: localization);
        var (window, editor) = Show(vm);
        editor.TextArea.Caret.Offset = vm.Document.Text.IndexOf("cmdOK_Click", StringComparison.Ordinal) + 2;

        ((CodeEditorView)window.Content!).RenameSymbol();
        Dispatcher.UIThread.RunJobs();

        windows.Received(1).InputBox("translated:Str.CodeEditor.Dialog.RenamePrompt",
            "translated:Str.CodeEditor.Dialog.RenameTitle", "cmdOK_Click");
        lsp.DidNotReceive().RequestRenameAsync(Arg.Any<string>(), Arg.Any<Position>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        windows.Received(1).MessageBox(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>());
    }

    // ── Enter, taken ahead of AvaloniaEdit by a tunnel handler ──────────────────────────────────────────

    [AvaloniaFact]
    public void EnterWithTheCaretInTheHeaderWritesNothing()
    {
        var vm = OpenForm();
        var (_, editor) = Show(vm);
        var before = vm.Document.Text;
        editor.TextArea.Caret.Offset = InHeader(vm);

        PressEnter(editor);
        Dispatcher.UIThread.RunJobs();

        vm.Document.Text.Should().Be(before,
            "the header is written by the designer, a save or a rename, never by a key in the code window");
    }

    [AvaloniaFact]
    public void EnterStillRecasesADeclarationThatAnAttributeRunDescribes()
    {
        // Re-casing rewrites the committed line's text and never its line break, so it is asked about the
        // text alone (#273 task 3.11). Asked about the line with its break, it would count as changing the
        // attribute run hanging from that break, and every described declaration would keep its casing.
        var vm = OpenForm();
        var (_, editor) = Show(vm);
        vm.Document.Insert(vm.Document.TextLength,
            "public function total() as currency\r\nAttribute total.VB_Description = \"x\"\r\nend function\r\n");
        var declaration = vm.Document.GetLineByOffset(vm.Document.Text.IndexOf("public function", StringComparison.Ordinal));
        editor.TextArea.Caret.Offset = declaration.EndOffset;

        PressEnter(editor);

        vm.Document.Text.Should().Contain("Public Function total() As Currency");
    }

    [AvaloniaFact]
    public void EnterInTheCodeStillBreaksTheLine()
    {
        // The control: the same key, delivered the same way, does reach the handler.
        var vm = OpenForm();
        var (_, editor) = Show(vm);
        var before = vm.Document.Text;
        editor.TextArea.Caret.Offset = InCode(vm);

        PressEnter(editor);
        Dispatcher.UIThread.RunJobs();

        vm.Document.Text.Should().NotBe(before);
        vm.Document.Text[..InHeader(vm)].Should().Be(before[..InHeader(vm)]);
    }

    [AvaloniaFact]
    public void EnterInAFileHeldReadOnlyAsAWholeWritesNothing()
    {
        // The banner says typing is refused anywhere in such a file. The section provider refuses AvaloniaEdit's
        // own typing, but Enter is taken by the tunnel handler first and wrote the line break and indent itself.
        var vm = OpenForm(heldReadOnly: true);
        vm.IsReadOnly.Should().BeTrue();
        var (_, editor) = Show(vm);
        var before = vm.Document.Text;
        editor.TextArea.Caret.Offset = InCode(vm);

        PressEnter(editor);
        Dispatcher.UIThread.RunJobs();

        vm.Document.Text.Should().Be(before, "the file will not be saved, so a line break typed into it would be lost");
    }

    // ── Automation: type_text and press_key ─────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void TypeTextAndPressKeyInAFileHeldReadOnlyAsAWholeAreRefusedAndSayWhy()
    {
        var vm = OpenForm(heldReadOnly: true);
        var (_, editor) = Show(vm);
        var before = vm.Document.Text;
        editor.TextArea.Caret.Offset = InCode(vm);

        var typed = UiAutomationDriver.TypeText(editor, "x");
        var pressed = UiAutomationDriver.PressKey(editor, "Enter", null);

        typed.Success.Should().BeFalse();
        typed.Error.Should().Contain("will not be saved").And.Contain("companion binary content");
        pressed.Success.Should().BeFalse("before, the key was reported as pressed and its handler wrote the line break");
        pressed.Error.Should().Contain("will not be saved");
        vm.Document.Text.Should().Be(before);
        UiAutomationDriver.PressKey(editor, "Down", null).Success.Should().BeTrue("moving the caret edits nothing");
    }

    [AvaloniaFact]
    public void TypeTextWithTheCaretInTheHeaderIsRefusedAndSaysWhereTheCodeStarts()
    {
        var vm = OpenForm();
        var (_, editor) = Show(vm);
        var before = vm.Document.Text;
        editor.TextArea.Caret.Offset = InHeader(vm);

        var outcome = UiAutomationDriver.TypeText(editor, "x");

        outcome.Success.Should().BeFalse();
        var codeStart = before.IndexOf("Option Explicit", StringComparison.Ordinal);
        outcome.Error.Should().Contain("header").And.Contain($"CaretOffset={codeStart}");
        vm.Document.Text.Should().Be(before);
    }

    [AvaloniaFact]
    public void TypeTextInTheCodeIsApplied()
    {
        var vm = OpenForm();
        var (_, editor) = Show(vm);
        editor.TextArea.Caret.Offset = InCode(vm);

        UiAutomationDriver.TypeText(editor, " ' bye").Success.Should().BeTrue();

        vm.Document.Text.Should().Contain("Unload Me ' bye");
    }

    [AvaloniaFact]
    public void PressKeyThatWouldEditTheHeaderIsRefused()
    {
        var vm = OpenForm();
        var (_, editor) = Show(vm);
        var before = vm.Document.Text;
        editor.TextArea.Caret.Offset = InHeader(vm);

        UiAutomationDriver.PressKey(editor, "Enter", null).Success.Should().BeFalse();
        UiAutomationDriver.PressKey(editor, "Back", null).Success.Should().BeFalse();
        UiAutomationDriver.PressKey(editor, "Delete", null).Success.Should().BeFalse();
        // Paste is Cmd+V on macOS, where the IDE binds it as the platform does (ApplicationCommands).
        UiAutomationDriver.PressKey(editor, "V", OperatingSystem.IsMacOS() ? "Cmd" : "Ctrl").Success.Should().BeFalse();

        vm.Document.Text.Should().Be(before);
    }

    [AvaloniaFact]
    public void BackspaceAtTheStartOfTheCodeIsRefusedBecauseItWouldJoinTheHeader()
    {
        var vm = OpenForm();
        var (_, editor) = Show(vm);
        var before = vm.Document.Text;
        editor.TextArea.Caret.Offset = before.IndexOf("Option Explicit", StringComparison.Ordinal);

        UiAutomationDriver.PressKey(editor, "Back", null).Success.Should().BeFalse();

        vm.Document.Text.Should().Be(before);
    }

    [AvaloniaFact]
    public void PressKeyThatOnlyMovesTheCaretIsNeverRefused()
    {
        var vm = OpenForm();
        var (_, editor) = Show(vm);
        editor.TextArea.Caret.Offset = InHeader(vm);

        UiAutomationDriver.PressKey(editor, "Down", null).Success.Should().BeTrue();
        UiAutomationDriver.PressKey(editor, "C", "Ctrl").Success.Should().BeTrue();
    }

    /// <summary>
    /// AvaloniaEdit's own search panel is not a second Find surface (#273 task 3.11). Its Replace All writes
    /// straight to the document, header included, and it opens on Ctrl+F wherever MainView does not claim
    /// the key first, which is this harness: a code window in a plain window.
    /// </summary>
    [AvaloniaFact]
    public void CtrlFInACodeWindowDoesNotOpenAvaloniaEditsOwnSearchPanel()
    {
        var vm = OpenForm();
        var (_, editor) = Show(vm);

        editor.TextArea.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent, Key = Key.F, KeyModifiers = KeyModifiers.Control,
        });
        Dispatcher.UIThread.RunJobs();

        (editor.SearchPanel?.IsOpened ?? false).Should().BeFalse();
        editor.TextArea.DefaultInputHandler.NestedInputHandlers
            .Should().NotContain(h => h.GetType().Name == "SearchInputHandler");
    }
}
