using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using AvaloniaEdit;
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
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// Edit-and-Continue's reset prompt against the header the code window now holds
/// (hexide-io/HexIDE#273 task 3.13).
/// </summary>
/// <remarks>
/// Against a rendered editor, because the prompt is raised from the text area's own input path, and whether a
/// keystroke writes anything is decided there by the section provider and the Enter handler.
/// </remarks>
public class ResetPromptTests : IDisposable
{
    private readonly List<Window> windows = [];
    private readonly List<CodeEditorViewModel> made = [];
    private readonly IWindowManager windowManager = Substitute.For<IWindowManager>();
    private readonly IDebugController debugController = Substitute.For<IDebugController>();
    private TaskCompletionSource<MessageBoxResult> answer = new();

    public ResetPromptTests()
    {
        // A run is in progress, so an edit asks whether to reset it, and the answer waits until a test gives it:
        // the prompt stays open for as long as the test needs, as a modal dialog would.
        debugController.IsSessionActive.Returns(true);
        windowManager.MessageBox(Arg.Any<string>(), Arg.Any<string?>(), MessageBoxButtons.YesNo, Arg.Any<MessageBoxIcon>())
            .Returns(_ => answer.Task);
    }

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

    private CodeEditorViewModel OpenForm()
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
            debugController, new RunScope(), Substitute.For<ILocalizationService>());
        made.Add(vm);
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        return vm.Initialize(new FormDeserializer().Deserialize(project, Frm, NullSink.Instance)!);
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

    /// <summary>Typed text as the input system delivers it: a TextInput event on the focused text area.</summary>
    private static void Type(TextEditor editor, string text)
    {
        editor.TextArea.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text });
        Dispatcher.UIThread.RunJobs();
    }

    private static void Press(TextEditor editor, Key key)
    {
        editor.TextArea.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = KeyModifiers.None,
        });
        Dispatcher.UIThread.RunJobs();
    }

    private void Answer(MessageBoxResult result)
    {
        answer.SetResult(result);
        Dispatcher.UIThread.RunJobs();
    }

    private int Prompts() =>
        windowManager.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IWindowManager.MessageBox));

    private static int InHeader(CodeEditorViewModel vm) =>
        vm.Document.Text.IndexOf("\"Orders\"", StringComparison.Ordinal);

    private static int InCode(CodeEditorViewModel vm) =>
        vm.Document.Text.IndexOf("Unload Me", StringComparison.Ordinal);

    // ── Only an edit that lands asks ────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void TypingInCodeWhileRunningAsksToReset()
    {
        // The control: proves the run is in progress and the prompt reachable in this harness.
        var vm = OpenForm();
        var editor = Show(vm);
        editor.CaretOffset = InCode(vm);

        Type(editor, "x");

        Prompts().Should().Be(1);
        vm.Document.Text.Should().Contain("xUnload Me", "VB6 lets the edit appear while it asks");
    }

    [AvaloniaFact]
    public void TypingInTheHeaderWhileRunningDoesNotAsk()
    {
        // The header refuses the keystroke, so nothing was edited and there is nothing to reset the run for.
        var vm = OpenForm();
        var editor = Show(vm);
        var before = vm.Document.Text;
        editor.CaretOffset = InHeader(vm);

        Type(editor, "x");

        vm.Document.Text.Should().Be(before, "the fixture must actually be refused");
        Prompts().Should().Be(0);
    }

    [AvaloniaFact]
    public void EnterInTheHeaderWhileRunningDoesNotAsk()
    {
        var vm = OpenForm();
        var editor = Show(vm);
        var before = vm.Document.Text;
        editor.CaretOffset = InHeader(vm);

        Press(editor, Key.Enter);

        vm.Document.Text.Should().Be(before);
        Prompts().Should().Be(0);
    }

    [AvaloniaFact]
    public void BackspaceInCodeWhileRunningAsksToReset()
    {
        // The control for the key route: without it, the refusals below would pass for a key that never arrived.
        var vm = OpenForm();
        var editor = Show(vm);
        editor.CaretOffset = InCode(vm) + 1;

        Press(editor, Key.Back);

        vm.Document.Text.Should().Contain("nload Me").And.NotContain("Unload Me");
        Prompts().Should().Be(1);
    }

    [AvaloniaFact]
    public void BackspaceAtTheStartOfTheCodeWhileRunningDoesNotAsk()
    {
        // The first character of code: Backspace would delete the header's last line break, which is refused.
        var vm = OpenForm();
        var editor = Show(vm);
        var before = vm.Document.Text;
        editor.CaretOffset = vm.Document.Text.IndexOf("Option Explicit", StringComparison.Ordinal);

        Press(editor, Key.Back);

        vm.Document.Text.Should().Be(before);
        Prompts().Should().Be(0);
    }

    [AvaloniaFact]
    public async Task PastingInCodeWhileRunningAsksToReset()
    {
        // A paste can land after the key that asked for it has been handled, because the clipboard is read
        // asynchronously; asking only while that key is being handled would let it in without a prompt.
        var vm = OpenForm();
        var editor = Show(vm);
        var clipboard = TopLevel.GetTopLevel(editor)!.Clipboard!;
        await clipboard.SetTextAsync("Pasted ");
        Dispatcher.UIThread.RunJobs();
        editor.CaretOffset = InCode(vm);

        editor.TextArea.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent, Key = Key.V, KeyModifiers = HexIDE.ApplicationCommands.PasteCommand.Gestures[0].KeyModifiers,
        });
        for (var i = 0; i < 50 && !vm.Document.Text.Contains("Pasted ", StringComparison.Ordinal); i++)
        {
            await Task.Delay(10, CancellationToken.None);
            Dispatcher.UIThread.RunJobs();
        }

        vm.Document.Text.Should().Contain("Pasted Unload Me", "the paste must actually land for this to test anything");
        Prompts().Should().Be(1);
    }

    [AvaloniaFact]
    public async Task PastingInTheHeaderWhileRunningDoesNotAsk()
    {
        var vm = OpenForm();
        var editor = Show(vm);
        var clipboard = TopLevel.GetTopLevel(editor)!.Clipboard!;
        await clipboard.SetTextAsync("Pasted ");
        Dispatcher.UIThread.RunJobs();
        var before = vm.Document.Text;
        editor.CaretOffset = InHeader(vm);

        editor.TextArea.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent, Key = Key.V, KeyModifiers = HexIDE.ApplicationCommands.PasteCommand.Gestures[0].KeyModifiers,
        });
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(10, CancellationToken.None);
            Dispatcher.UIThread.RunJobs();
        }

        vm.Document.Text.Should().Be(before);
        Prompts().Should().Be(0);
    }

    [AvaloniaFact]
    public void DeletingASelectionInCodeWhileRunningAsksToReset()
    {
        // Delete is bound to the code window's Delete command, which takes the key before the text area sees it,
        // so the key handler that asked never ran for it. The command deletes a selection; with nothing selected
        // it deletes nothing at all, so the test selects.
        var vm = OpenForm();
        var editor = Show(vm);
        editor.Select(InCode(vm), "Unload ".Length);

        Press(editor, Key.Delete);

        vm.Document.Text.Should().NotContain("Unload Me", "the delete must actually land for this to test anything");
        Prompts().Should().Be(1);
    }

    [AvaloniaFact]
    public void DeletingASelectionInTheHeaderWhileRunningDoesNotAsk()
    {
        var vm = OpenForm();
        var editor = Show(vm);
        var before = vm.Document.Text;
        editor.Select(InHeader(vm), "\"Orders\"".Length);

        Press(editor, Key.Delete);

        vm.Document.Text.Should().Be(before);
        Prompts().Should().Be(0);
    }

    [AvaloniaFact]
    public async Task CuttingCodeWhileRunningAsksToReset()
    {
        var vm = OpenForm();
        var editor = Show(vm);
        editor.Select(InCode(vm), "Unload ".Length);

        editor.TextArea.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent, Key = Key.X, KeyModifiers = HexIDE.ApplicationCommands.CutCommand.Gestures[0].KeyModifiers,
        });
        for (var i = 0; i < 50 && vm.Document.Text.Contains("Unload Me", StringComparison.Ordinal); i++)
        {
            await Task.Delay(10, CancellationToken.None);
            Dispatcher.UIThread.RunJobs();
        }

        vm.Document.Text.Should().NotContain("Unload Me", "the cut must actually land for this to test anything");
        Prompts().Should().Be(1);
    }

    [AvaloniaFact]
    public async Task CuttingHeaderTextWhileRunningDoesNotAsk()
    {
        var vm = OpenForm();
        var editor = Show(vm);
        var before = vm.Document.Text;
        editor.Select(InHeader(vm), "\"Orders\"".Length);

        editor.TextArea.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent, Key = Key.X, KeyModifiers = HexIDE.ApplicationCommands.CutCommand.Gestures[0].KeyModifiers,
        });
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(10, CancellationToken.None);
            Dispatcher.UIThread.RunJobs();
        }

        vm.Document.Text.Should().Be(before);
        Prompts().Should().Be(0);
    }

    [AvaloniaFact]
    public void ChangingAFormsLayoutWhilePaused()
    {
        // The code-editor delta's scenario: a designer change made while a run is paused shows no prompt. It reaches
        // the window the way a designer commit does, through the header refresher, which re-renders the form and
        // replaces the header in the open window.
        var vm = OpenForm();
        Show(vm);
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns(new List<BaseEditorWindowViewModel> { vm });
        using var refresher = new HeaderRefresher(new EventBus(), dock, new BreakpointService(), new BookmarkService());
        var form = vm.FormDefinition!;
        var code = vm.BufferBody;

        form.Components.Single(c => c.BaseClass.VBTypeName == "VB.Form")
            .SetProperty(VBProperties.CaptionProperty, "Orders and more");
        refresher.LayoutChanged(form);
        Dispatcher.UIThread.RunJobs();

        vm.Document.Text.Should().Contain("\"Orders and more\"", "the premise: the change reached the window's header");
        vm.BufferBody.Should().Be(code);
        Prompts().Should().Be(0);
    }

    [AvaloniaFact]
    public void ARefusedKeystrokeDoesNotLeaveThePromptWaitingForTheNextChange()
    {
        // The keystroke arms the prompt and writes nothing. If it stayed armed, the next change of any kind would
        // ask to reset the run on its behalf, a designer change included.
        var vm = OpenForm();
        var editor = Show(vm);
        editor.CaretOffset = InHeader(vm);
        Type(editor, "x");

        vm.RefreshPrefix(FormCodeText.Prefix(vm.FormDefinition!).Replace("\"Orders\"", "\"Orders and more\""));
        Dispatcher.UIThread.RunJobs();

        Prompts().Should().Be(0);
    }

    // ── What "No" puts back ─────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void NoRevertsTheEdit()
    {
        var vm = OpenForm();
        var editor = Show(vm);
        var before = vm.Document.Text;
        editor.CaretOffset = InCode(vm);

        Type(editor, "x");
        Answer(MessageBoxResult.No);

        vm.Document.Text.Should().Be(before);
    }

    [AvaloniaFact]
    public void NoLeavesWhatTheEditDidNotTouchWhereItWas()
    {
        // Only the span that differs is replaced. A whole-body replace would also restore the text, and would move
        // every anchor in the code, the folds and the caret among them, to the start of the replaced span.
        var vm = OpenForm();
        var editor = Show(vm);
        var endSub = vm.Document.Text.LastIndexOf("End Sub", StringComparison.Ordinal);
        var anchor = vm.Document.CreateAnchor(endSub);
        editor.CaretOffset = InCode(vm);

        Type(editor, "x");
        Answer(MessageBoxResult.No);

        anchor.IsDeleted.Should().BeFalse();
        anchor.Offset.Should().Be(endSub, "the revert must not have replaced text the edit never touched");
    }

    [AvaloniaFact]
    public void YesKeepsTheEdit()
    {
        var vm = OpenForm();
        var editor = Show(vm);
        editor.CaretOffset = InCode(vm);

        Type(editor, "x");
        Answer(MessageBoxResult.Yes);

        vm.Document.Text.Should().Contain("xUnload Me");
    }

    [AvaloniaFact]
    public void NoAfterAHeaderRefreshKeepsTheNewHeaderAndRevertsOnlyTheEdit()
    {
        // The defect: the revert restored a whole-buffer snapshot holding the OLD header while the prefix held
        // the new one, and the split is by the prefix's length, so the next flush took the body from the
        // wrong offset. A designer commit or a save landing while the prompt is open does exactly that.
        var vm = OpenForm();
        var editor = Show(vm);
        var code = vm.BufferBody;
        editor.CaretOffset = InCode(vm);

        Type(editor, "x");
        var grown = FormCodeText.Prefix(vm.FormDefinition!).Replace("\"Orders\"", "\"Orders and more\"");
        vm.RefreshPrefix(grown);
        Answer(MessageBoxResult.No);

        vm.Document.Text.Should().Be(grown + code, "the header the designer wrote stays, and the edit goes");
        vm.BufferBody.Should().Be(code, "the split must agree with the text, or the next flush cuts the code");
    }

    [AvaloniaFact]
    public void NoAfterAHeaderRefreshPutsTheCaretBackOnTheSameCode()
    {
        // Measured live before this was fixed: the caret came back at its old offset, which a longer header had
        // moved to the line above.
        var vm = OpenForm();
        var editor = Show(vm);
        editor.CaretOffset = InCode(vm);

        Type(editor, "x");
        vm.RefreshPrefix(FormCodeText.Prefix(vm.FormDefinition!).Replace("\"Orders\"", "\"Orders and more\""));
        Answer(MessageBoxResult.No);

        editor.CaretOffset.Should().Be(InCode(vm));
    }

    [AvaloniaFact]
    public void NoAfterAHeaderThatShrankStillSplitsInTheRightPlace()
    {
        // The other direction: a header that got shorter, where a stale split takes header text into the body.
        var vm = OpenForm();
        var editor = Show(vm);
        var code = vm.BufferBody;
        editor.CaretOffset = InCode(vm);

        Type(editor, "x");
        var shrunk = FormCodeText.Prefix(vm.FormDefinition!).Replace("   Caption         =   \"Orders\"\r\n", "");
        vm.RefreshPrefix(shrunk);
        Answer(MessageBoxResult.No);

        vm.Document.Text.Should().Be(shrunk + code);
        vm.BufferBody.Should().Be(code);
    }
}
