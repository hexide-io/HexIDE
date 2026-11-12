using HexIDE.Runtime.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
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

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// The code window's completion list and parameter info reach dump_visual_tree, find and interact (#942). Both are
/// popups parented only logically, to the window, so the tree answered the same whether a list was open or not, and a
/// run concluded that none opened when one had.
/// </summary>
public class CodeEditorPopupTests : IDisposable
{
    private readonly List<Window> windows = [];
    private readonly List<CodeEditorViewModel> made = [];
    private readonly ILspClient lsp = Substitute.For<ILspClient>();

    public CodeEditorPopupTests()
    {
        lsp.IsRunning.Returns(true);
        lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<DocumentSymbol>());
        lsp.RequestFoldingRangesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<FoldingRange>());
        lsp.RequestCompletionAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>()).Returns(
        [
            new CompletionItem("MsgBox", CompletionItemKind.Function),
            new CompletionItem("Mid", CompletionItemKind.Function, "Returns part of a string."),
        ]);
        lsp.RequestSignatureHelpAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>(), Arg.Any<string?>()).Returns(
            new SignatureHelp([new SignatureInformation("MsgBox(Prompt, [Buttons], [Title])", null,
                [new ParameterInformation("Prompt"), new ParameterInformation("[Buttons]"), new ParameterInformation("[Title]")])],
                0, 0));
    }

    public void Dispose()
    {
        foreach (var w in windows) w.Close();
        foreach (var vm in made) vm.Dispose();
        GC.SuppressFinalize(this);
    }

    private (Window Window, CodeEditorView View, TextEditor Editor) Show(ILspClient? client = null)
    {
        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe<CreateOrNavigateToSubEvent>(Arg.Any<Action<CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(Arg.Any<Action<ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<FormUnloadedEvent>(Arg.Any<Action<FormUnloadedEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<DocumentSavedEvent>(Arg.Any<Action<DocumentSavedEvent>>()).Returns(Substitute.For<IDisposable>());
        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);
        settings.AutoListMembers.Returns(true);

        var vm = new CodeEditorViewModel(
            Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(), Substitute.For<IProjectService>(),
            eventBus, client ?? lsp, settings, Substitute.For<IStatusBarService>(), new BookmarkService(), new BreakpointService(),
            Substitute.For<IDebugController>(), new RunScope(), Substitute.For<ILocalizationService>(),
                new Vb6ProjectFileFormat(),
                new Vb6ReadOnlyRegionReader());
        made.Add(vm);
        var module = new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "Module1", ModuleKind.StandardModule);
        module.UpdateCode("Option Explicit\r\n\r\nPrivate Sub Foo()\r\n    M\r\nEnd Sub\r\n");
        vm.Initialize(module);

        var window = new Window { Width = 1200, Height = 800 };
        windows.Add(window);
        window.Show();
        var view = new CodeEditorView { DataContext = vm };
        window.Content = view;
        Dispatcher.UIThread.RunJobs();
        var editor = view.FindControl<TextEditor>("TextEditor")!;
        editor.TextArea.Caret.Offset = editor.Document.Text.IndexOf("    M", StringComparison.Ordinal) + 5;
        editor.TextArea.Focus();
        return (window, view, editor);
    }

    private static void Pump()
    {
        for (var i = 0; i < 10; i++)
            Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData("Ms", 2, 0)]
    [InlineData("Ms", 1, 0)]
    [InlineData("Ms", 0, 0)]
    [InlineData("c.Ms", 4, 2)]
    [InlineData("c.", 2, 2)]
    [InlineData("UBound(Ms", 9, 7)]
    [InlineData("UBound(", 7, 7)]
    public async Task Completion_requests_the_start_of_the_word(string source, int caret, int start)
    {
        var (_, view, editor) = Show();
        editor.Text = "Option Explicit\n" + source;
        editor.CaretOffset = editor.Document.GetOffset(2, caret + 1);
        lsp.ClearReceivedCalls();

        view.TriggerCompletion();
        Pump();

        await lsp.Received(1).RequestCompletionAsync(Arg.Any<string>(), new Position(1, start),
            Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public void An_open_completion_list_is_in_the_tree_under_its_editor_and_its_items_can_be_found_and_selected()
    {
        var (window, view, editor) = Show();
        UiAutomationDriver.Find(UiAutomationDriver.Dump(window, "Window", 60, interactiveOnly: false), "MsgBox", 50)
            .Total.Should().Be(0, "the premise: no list is open yet");

        view.TriggerCompletion();
        Pump();

        var tree = UiAutomationDriver.Dump(window, "Window", 60, interactiveOnly: false);
        var editorPath = UiAutomationDriver.Find(tree, "TextEditor", 50).Matches.Single().Path;
        // Headless hosts a popup in the window's overlay layer, where the walk also meets it; on the desktop the list is
        // a window of its own, which the walk reaches only from its editor, so that copy is the one asserted.
        // The item is named by its text, so its path says which it is (#942 review), rather than ListItem[#0].
        var itemPath = UiAutomationDriver.Find(tree, "MsgBox", 50).Matches
            .Should().ContainSingle(n => n.Path.StartsWith(editorPath + "/", StringComparison.Ordinal) && n.Path.EndsWith("/ListItem[MsgBox]", StringComparison.Ordinal),
                "the list is the editor's, where it opens on screen").Subject.Path;

        var (control, error) = UiAutomationDriver.Resolve(window, itemPath);
        control.Should().NotBeNull(error);
        var selected = UiAutomationDriver.Interact(control!, "select", null);
        selected.Success.Should().BeTrue(selected.Error);
        var list = CodeEditorView.OpenPopupsOf(editor).OfType<CompletionWindow>().Single().CompletionList;
        list.SelectedItem.Should().BeOfType<VbCompletionData>().Which.Text.Should().Be("MsgBox");
    }

    [AvaloniaFact]
    public void Completion_description_is_readable_under_the_list_after_selecting_an_item()
    {
        var (window, view, editor) = Show();
        view.TriggerCompletion();
        Pump();
        var tree = UiAutomationDriver.Dump(window, "Window", 60, interactiveOnly: false);
        var editorPath = UiAutomationDriver.Find(tree, "TextEditor", 50).Matches.Single().Path;
        var itemPath = UiAutomationDriver.Find(tree, "Mid", 50).Matches.Single(n =>
            n.Path.StartsWith(editorPath + "/", StringComparison.Ordinal) &&
            n.Path.EndsWith("/ListItem[Mid]", StringComparison.Ordinal)).Path;
        var (item, error) = UiAutomationDriver.Resolve(window, itemPath);
        item.Should().NotBeNull(error);
        UiAutomationDriver.Interact(item!, "select", null).Success.Should().BeTrue();
        Pump();

        var list = CodeEditorView.OpenPopupsOf(editor).OfType<CompletionWindow>().Single().CompletionList;
        var listPath = UiAutomationDriver.Find(tree, "PART_ListBox", 50).Matches
            .Single(n => n.Path.StartsWith(editorPath + "/", StringComparison.Ordinal)).Path;
        var description = UiAutomationDriver.Find(
            UiAutomationDriver.Dump(window, "Window", 60, interactiveOnly: false),
            "Returns part of a string.", 50).Matches.Should().ContainSingle(n =>
                n.Path.StartsWith(listPath + "/", StringComparison.Ordinal)).Subject;
        var (text, resolveError) = UiAutomationDriver.Resolve(window, description.Path);
        text.Should().NotBeNull(resolveError);
        text.Should().BeOfType<TextBlock>().Which.Text.Should().Be("Returns part of a string.");

        list.SelectedItem = list.CompletionData.Single(i => i.Text == "MsgBox");
        Pump();
        UiAutomationDriver.Find(UiAutomationDriver.Dump(window, "Window", 60, interactiveOnly: false),
            "Returns part of a string.", 50).Total.Should().Be(0, "an item without a description closes the tooltip");
    }

    [AvaloniaFact]
    public void Double_click_commits_the_completion_item_into_the_document()
    {
        var (window, view, editor) = Show();
        view.TriggerCompletion();
        Pump();
        var tree = UiAutomationDriver.Dump(window, "Window", 60, interactiveOnly: false);
        var editorPath = UiAutomationDriver.Find(tree, "TextEditor", 50).Matches.Single().Path;
        var itemPath = UiAutomationDriver.Find(tree, "Mid", 50).Matches.Single(n =>
            n.Path.StartsWith(editorPath + "/", StringComparison.Ordinal) &&
            n.Path.EndsWith("/ListItem[Mid]", StringComparison.Ordinal)).Path;
        var (item, error) = UiAutomationDriver.Resolve(window, itemPath);
        item.Should().NotBeNull(error);

        var outcome = UiAutomationDriver.Interact(item!, "double_click", null);
        outcome.Success.Should().BeTrue(outcome.Error);
        Pump();

        // The item is committed. Whether it replaces the typed M or appends to it, as the list does today, is #941's,
        // so this accepts either rather than pinning that defect.
        editor.Document.Text.ReplaceLineEndings("\n").Should().MatchRegex(@"\n    M?Mid\n");
        CodeEditorView.OpenPopupsOf(editor).Should().BeEmpty();
    }

    [AvaloniaFact]
    public void Collapse_on_the_editor_does_not_call_an_open_completion_list_nothing_to_close()
    {
        // The editor owns a context flyout, so collapse on it answered "the flyout was not open; there is nothing to
        // close" while the dump showed the list open under it (#949 review).
        var (_, view, editor) = Show();
        view.TriggerCompletion();
        Pump();

        var outcome = UiAutomationDriver.Interact(editor, "collapse", null);

        outcome.Success.Should().BeFalse();
        outcome.Error.Should().EndWith("has a popup open that collapse does not close, such as the completion list; press_key Escape closes it");
        CodeEditorView.OpenPopupsOf(editor).Should().NotBeEmpty();
    }

    [AvaloniaFact]
    public void Open_parameter_info_is_in_the_tree_under_its_editor()
    {
        var (window, _, editor) = Show();
        editor.TextArea.Caret.Offset = editor.Document.Text.IndexOf("    M", StringComparison.Ordinal) + 5;
        editor.Document.Insert(editor.TextArea.Caret.Offset, "sgBox");

        editor.TextArea.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "(" });
        Pump();

        var tree = UiAutomationDriver.Dump(window, "Window", 60, interactiveOnly: false);
        var editorPath = UiAutomationDriver.Find(tree, "TextEditor", 50).Matches.Single().Path;
        UiAutomationDriver.Find(tree, "Prompt", 50).Matches.Should().Contain(
            n => n.Path.StartsWith(editorPath + "/", StringComparison.Ordinal), "the signature's text is under its editor");
    }

    [AvaloniaFact]
    public void A_closed_list_leaves_the_tree()
    {
        var (window, view, editor) = Show();
        view.TriggerCompletion();
        Pump();
        UiAutomationDriver.Find(UiAutomationDriver.Dump(window, "Window", 60, interactiveOnly: false), "MsgBox", 50)
            .Total.Should().BeGreaterThan(0, "the premise: the list is open");

        editor.TextArea.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Pump();

        UiAutomationDriver.Find(UiAutomationDriver.Dump(window, "Window", 60, interactiveOnly: false), "MsgBox", 50)
            .Total.Should().Be(0);
    }

    [AvaloniaFact]
    public void Parameter_info_and_the_list_typing_an_argument_opens_are_both_in_the_tree()
    {
        // MsgBox(M: the ( opens the call's parameter info, the M a list, and the parameter info stays while the caret
        // is in the call. Reporting the list alone left the signature unseen (#942 review).
        var (window, _, editor) = Show();
        editor.Document.Insert(editor.TextArea.Caret.Offset, "sgBox");
        editor.TextArea.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "(" });
        Pump();
        editor.TextArea.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "M" });
        Pump();

        CodeEditorView.OpenPopupsOf(editor).Should().HaveCount(2, "the premise: both are open");
        var tree = UiAutomationDriver.Dump(window, "Window", 60, interactiveOnly: false);
        var editorPath = UiAutomationDriver.Find(tree, "TextEditor", 50).Matches.Single().Path;
        bool UnderEditor(string text) => UiAutomationDriver.Find(tree, text, 50).Matches
            .Any(n => n.Path.StartsWith(editorPath + "/", StringComparison.Ordinal));
        UnderEditor("Prompt").Should().BeTrue("the signature is still on screen");
        UnderEditor("MsgBox").Should().BeTrue("and so is the list");
    }

    [AvaloniaFact]
    public void An_item_is_selected_by_its_text()
    {
        var (window, view, editor) = Show();
        view.TriggerCompletion();
        Pump();

        var tree = UiAutomationDriver.Dump(window, "Window", 60, interactiveOnly: false);
        var editorPath = UiAutomationDriver.Find(tree, "TextEditor", 50).Matches.Single().Path;
        var itemPath = UiAutomationDriver.Find(tree, "Mid", 50).Matches
            .Single(n => n.Path.StartsWith(editorPath + "/", StringComparison.Ordinal) && n.Path.EndsWith("/ListItem[Mid]", StringComparison.Ordinal)).Path;
        var listPath = itemPath[..itemPath.IndexOf("/ListItem[", StringComparison.Ordinal)];
        var (list, error) = UiAutomationDriver.Resolve(window, listPath);
        list.Should().NotBeNull(error);

        var selected = UiAutomationDriver.Interact(list!, "select", "Mid");

        selected.Success.Should().BeTrue(selected.Error);
        CodeEditorView.OpenPopupsOf(editor).OfType<CompletionWindow>().Single().CompletionList.SelectedItem
            .Should().BeOfType<VbCompletionData>().Which.Text.Should().Be("Mid");
    }

    [AvaloniaTheory]
    [InlineData("' Typed on Wayland: Hello_1 (ok)! [a+b]")]
    [InlineData("Rem Typed on Wayland")]
    [InlineData("Rem: Typed on Wayland")]
    [InlineData("x = \"Typed on Wayland\"")]
    [InlineData("x = \"a \"\" on Wayland\"")]
    public void Typing_free_text_does_not_accept_completions(string typed)
    {
        lsp.RequestCompletionAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>()).Returns(
            [new CompletionItem("On Error", CompletionItemKind.Keyword)]);
        lsp.RequestSignatureHelpAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(Task.FromResult<SignatureHelp?>(null));
        var (window, view, editor) = Show();
        var vm = (CodeEditorViewModel)view.DataContext!;
        vm.ReplaceBody("");
        editor.CaretOffset = editor.Document.TextLength;
        foreach (var ch in typed)
        {
            if (ch == ' ') window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            window.KeyTextInput(ch.ToString());
            if (ch == ' ') window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            Pump();
        }
        vm.BufferBody.Should().Be(typed);
        CodeEditorView.OpenPopupsOf(editor).Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData("Rem", false)]
    [InlineData("Rem ", true)]
    [InlineData("x.Rem ", false)]
    public void Typing_Rem_only_suppresses_completion_after_a_comment_delimiter(string typed, bool freeText)
    {
        lsp.RequestCompletionAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>()).Returns(
            [new CompletionItem("Remove", CompletionItemKind.Method)]);
        var (window, view, editor) = Show();
        editor.Text = "";
        foreach (var ch in typed)
        {
            window.KeyTextInput(ch.ToString());
            Pump();
        }
        view.TriggerCompletion();
        Pump();
        CodeEditorView.OpenPopupsOf(editor).Any().Should().Be(!freeText);
        editor.Text.Should().Be(typed);
    }

    // Measured: docs/vb6-fidelity-oracle/a-lone-cr-or-lf-in-vb6-source.md and
    // docs/vb6-fidelity-oracle/line-continuations-and-statement-separators.md.
    [AvaloniaTheory]
    [InlineData("' note _\n")]
    [InlineData("' note _\r")]
    [InlineData("' note _\r\n")]
    [InlineData("Rem note _\n")]
    [InlineData("Rem note _\r")]
    [InlineData("Rem note _\r\n")]
    [InlineData("Debug.Print \"note _\n")]
    [InlineData("Debug.Print \"note _\r")]
    [InlineData("Debug.Print \"note _\r\n")]
    public void Typing_in_continued_free_text_preserves_words(string prefix)
    {
        lsp.RequestCompletionAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>()).Returns(
            [new CompletionItem("On Error", CompletionItemKind.Keyword)]);
        var (window, view, editor) = Show();
        var vm = (CodeEditorViewModel)view.DataContext!;
        vm.ReplaceBody(prefix);
        editor.CaretOffset = editor.Document.TextLength;
        foreach (var ch in "on Wayland")
        {
            if (ch == ' ') window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            window.KeyTextInput(ch.ToString());
            if (ch == ' ') window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            Pump();
        }
        vm.BufferBody.ReplaceLineEndings("\n").Should().Be(prefix.ReplaceLineEndings("\n") + "on Wayland");
    }

    [AvaloniaTheory]
    [InlineData("' on")]
    [InlineData("x = \"on")]
    public async Task Late_completion_reply_does_not_open_in_free_text(string freeText)
    {
        var reply = new TaskCompletionSource<CompletionItem[]>();
        lsp.RequestCompletionAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(reply.Task);
        var (window, view, editor) = Show();
        var vm = (CodeEditorViewModel)view.DataContext!;
        vm.ReplaceBody(freeText + "\non");
        editor.CaretOffset = editor.Text.Length;
        window.KeyTextInput("e");
        Pump();
        var pending = CodeEditorView.PendingKeyRequestsOf(editor);
        pending.Should().NotBeNull();
        editor.CaretOffset = vm.BufferPrefixLength + freeText.Length;
        reply.SetResult([new CompletionItem("On Error", CompletionItemKind.Keyword)]);
        await pending!;
        Pump();
        CodeEditorView.OpenPopupsOf(editor).Should().BeEmpty();
    }

    // VB6 SP6 key measurements: #3056, issuecomment-6633933454.
    [AvaloniaTheory]
    [InlineData("Debug.Pr", 8, Key.Return, "Debug.Print\n")]
    [InlineData("Debug.Print", 11, Key.Return, "Debug.Print\n")]
    [InlineData("Debug.Pr", 8, Key.Tab, "Debug.Print")]
    [InlineData("Debug.Pr", 8, Key.Space, "Debug.Print ")]
    [InlineData("Debug.Pr", 8, Key.Escape, "Debug.Pr")]
    [InlineData("Debug.", 6, Key.Return, "Debug.\n")]
    [InlineData("Debug.", 6, Key.Space, "Debug. ")]
    [InlineData("Debug.", 6, Key.Tab, "Debug.Assert")]
    [InlineData("Debug.Przz", 8, Key.Return, "Debug.Pr\nzz")]
    [InlineData("Debug.Przz", 8, Key.Space, "Debug.Pr zz")]
    [InlineData("Debug.Przz", 8, Key.Tab, "Debug.Assert")]
    [InlineData("zet", 3, Key.Return, "zeta1\n")]
    [InlineData("zet", 3, Key.Tab, "zeta1")]
    [InlineData("zet", 3, Key.Space, "zeta1 ")]
    [InlineData("zetzz", 3, Key.Return, "zet\nzz")]
    public void Completion_keys_follow_the_measured_selection(string source, int caret, Key key, string expected)
    {
        lsp.RequestCompletionAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>()).Returns(
            source.StartsWith("Debug.", StringComparison.Ordinal)
                ? [new CompletionItem("Assert", CompletionItemKind.Method), new CompletionItem("Print", CompletionItemKind.Method)]
                : [new CompletionItem("zeta1", CompletionItemKind.Variable), new CompletionItem("zeta2", CompletionItemKind.Variable),
                    new CompletionItem("zebra", CompletionItemKind.Variable)]);
        var (window, view, editor) = Show();
        editor.Text = source;
        editor.CaretOffset = caret;
        if (source.StartsWith("zet", StringComparison.Ordinal))
        {
            var gesture = ApplicationCommands.CompleteWordCommand.Gestures.Single();
            UiAutomationDriver.PressKey(editor, gesture.Key.ToString(), gesture.KeyModifiers.ToString()).Success.Should().BeTrue();
        }
        else
            view.TriggerCompletion();
        Pump();
        CodeEditorView.OpenPopupsOf(editor).Should().ContainSingle();

        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        if (key == Key.Space) window.KeyTextInput(" ");
        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        Pump();

        editor.Text.ReplaceLineEndings("\n").Should().Be(expected);
        CodeEditorView.OpenPopupsOf(editor).Should().BeEmpty();
    }

    [AvaloniaFact]
    public void Typing_after_a_bare_dot_selects_the_matching_item()
    {
        lsp.RequestCompletionAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>()).Returns(
            [new CompletionItem("Assert", CompletionItemKind.Method), new CompletionItem("Print", CompletionItemKind.Method)]);
        var (window, view, editor) = Show();
        editor.Text = "Debug.";
        editor.CaretOffset = 6;
        view.TriggerCompletion();
        Pump();
        window.KeyTextInput("Pr");
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
        window.KeyTextInput(" ");
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
        Pump();
        editor.Text.Should().Be("Debug.Print ");
        CodeEditorView.OpenPopupsOf(editor).Should().BeEmpty();
    }

    [AvaloniaFact]
    public void First_down_highlights_the_outlined_item()
    {
        lsp.RequestCompletionAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>()).Returns(
            [new CompletionItem("Assert", CompletionItemKind.Method), new CompletionItem("Print", CompletionItemKind.Method)]);
        var (window, view, editor) = Show();
        editor.Text = "Debug.";
        editor.CaretOffset = editor.Text.Length;
        view.TriggerCompletion();
        Pump();
        var list = CodeEditorView.OpenPopupsOf(editor).OfType<CompletionWindow>().Single().CompletionList;
        list.SelectedItem.Should().BeNull();
        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        list.SelectedItem.Should().Be(list.CompletionData[0]);
        window.KeyPress(Key.Return, RawInputModifiers.None, PhysicalKey.Enter, null);
        window.KeyRelease(Key.Return, RawInputModifiers.None, PhysicalKey.Enter, null);
        Pump();
        editor.Text.ReplaceLineEndings("\n").Should().Be("Debug.Assert\n");
    }

    [AvaloniaFact]
    public void Complete_word_with_a_unique_prefix_inserts_without_a_list()
    {
        var (_, _, editor) = Show();
        editor.Text = "Msg";
        editor.CaretOffset = 3;
        var gesture = ApplicationCommands.CompleteWordCommand.Gestures.Single();
        UiAutomationDriver.PressKey(editor, gesture.Key.ToString(), gesture.KeyModifiers.ToString()).Success.Should().BeTrue();
        Pump();
        editor.Text.Should().Be("MsgBox");
        editor.CaretOffset.Should().Be(6);
        CodeEditorView.OpenPopupsOf(editor).Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData(Key.Return)]
    [InlineData(Key.Tab)]
    public void Acceptance_keys_accept_a_snippet_choice_without_a_line_break(Key key)
    {
        var (window, editor) = AcceptSnippet("${1|one,two|} $1$0");
        var picker = CodeEditorView.OpenPopupsOf(editor).OfType<CompletionWindow>().Single();
        picker.CompletionList.SelectedItem = picker.CompletionList.CompletionData[1];
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        Pump();
        editor.Text.ReplaceLineEndings("\n").Should().Contain("    two two\n");
        CodeEditorView.OpenPopupsOf(editor).Should().BeEmpty();
        editor.TextArea.StackedInputHandlers.Should().NotBeEmpty();
    }

    private (Window Window, TextEditor Editor) AcceptSnippet(string snippet)
    {
        lsp.RequestCompletionAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>()).Returns(
            [new CompletionItem("zsnip", CompletionItemKind.Text, InsertTextFormat: 2,
                TextEdit: new TextEdit(new HexIDE.Lsp.Messages.Range(new Position(4, 4), new Position(4, 5)), snippet))]);
        var (window, view, editor) = Show();
        view.TriggerCompletion();
        Pump();
        var completion = CodeEditorView.OpenPopupsOf(editor).OfType<CompletionWindow>().Single();
        completion.CompletionList.SelectedItem = completion.CompletionList.CompletionData[0];
        window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        Pump();
        return (window, editor);
    }

    [AvaloniaFact]
    public void Snippet_choice_picker_opens_under_its_editor_and_choosing_updates_linked_occurrences()
    {
        var (window, editor) = AcceptSnippet("${1|one,two|} $1$0");
        var picker = CodeEditorView.OpenPopupsOf(editor).Should().ContainSingle().Subject
            .Should().BeOfType<CompletionWindow>().Subject;
        picker.CompletionList.CompletionData.Select(item => item.Text).Should().Equal("one", "two");
        var tree = UiAutomationDriver.Dump(window, "Window", 60, interactiveOnly: false);
        var editorPath = UiAutomationDriver.Find(tree, "TextEditor", 50).Matches.Single().Path;
        var two = UiAutomationDriver.Find(tree, "two", 50).Matches.Should().ContainSingle(n =>
            n.Path.StartsWith(editorPath + "/", StringComparison.Ordinal) && n.Path.EndsWith("/ListItem[two]", StringComparison.Ordinal)).Subject;
        var (item, error) = UiAutomationDriver.Resolve(window, two.Path);
        item.Should().NotBeNull(error);
        UiAutomationDriver.Interact(item!, "double_click", null).Success.Should().BeTrue();
        editor.Document.Text.ReplaceLineEndings("\n").Should().Contain("    two two\n");
        CodeEditorView.OpenPopupsOf(editor).Should().BeEmpty();
        window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        editor.CaretOffset.Should().Be(editor.Document.Text.IndexOf("two two", StringComparison.Ordinal) + 7);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Tab_after_typing_advances_a_snippet_once_the_completion_list_is_closed(bool closeList)
    {
        var (window, editor) = AcceptSnippet("${1:one} $1$0");
        lsp.RequestCompletionAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>()).Returns(
            [new CompletionItem("accepted", CompletionItemKind.Text)]);
        window.KeyTextInput("two");
        Pump();
        editor.Document.Text.ReplaceLineEndings("\n").Should().Contain("    two two\n");
        var list = CodeEditorView.OpenPopupsOf(editor).OfType<CompletionWindow>().Should().ContainSingle().Subject;
        list.CompletionList.SelectedItem = list.CompletionList.CompletionData[0];
        if (closeList) list.Close();
        window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        Pump();
        if (closeList)
        {
            editor.CaretOffset.Should().Be(editor.Document.Text.IndexOf("two two", StringComparison.Ordinal) + 7);
            editor.TextArea.StackedInputHandlers.Should().BeEmpty();
        }
        else
        {
            editor.Document.Text.Should().Contain("accepted", "Tab accepts the selected completion while its list is open");
            editor.TextArea.StackedInputHandlers.Should().NotBeEmpty();
        }
    }

    [AvaloniaTheory]
    [InlineData(".", "textDocument/completion")]
    [InlineData("x", "textDocument/completion")]
    [InlineData("(", "textDocument/signatureHelp")]
    public async Task Typed_request_follows_the_change_on_the_wire(string typed, string request)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ct = timeout.Token;
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();
        await using var serverStream = serverSide;
        var frames = new List<JsonElement>();
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loop = Task.Run(async () =>
        {
            while (await ReadFrameAsync(serverSide, ct) is { } json)
            {
                var frame = JsonDocument.Parse(json).RootElement.Clone();
                lock (frames) frames.Add(frame);
                var method = frame.GetProperty("method").GetString();
                if (method == "textDocument/didOpen") opened.TrySetResult();
                if (method == request) completed.TrySetResult();
                if (method == "exit") return;
                if (!frame.TryGetProperty("id", out var id)) continue;
                var result = method == "initialize"
                    ? """{"capabilities":{"textDocumentSync":{"openClose":true,"change":1},"completionProvider":{"triggerCharacters":["."]},"signatureHelpProvider":{"triggerCharacters":["("]}}}"""
                    : method is "shutdown" or "textDocument/signatureHelp" ? "null" : "[]";
                await WriteFrameAsync(serverSide,
                    "{\"jsonrpc\":\"2.0\",\"id\":" + id.GetRawText() + ",\"result\":" + result + "}", ct);
            }
        }, ct);
        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>())));
        await using var client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), "vb6");
        var registry = new LspClientRegistry(
            [new LanguageServerRegistration("test", "test", DocumentLanguage.Vb6Extensions, DocumentLanguage.Vb6, () => client)],
            Substitute.For<ILogger<LspClientRegistry>>());
        var (_, _, editor) = Show(registry);
        await opened.Task.WaitAsync(ct);
        editor.TextArea.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = typed });
        var expected = editor.Document.Text;
        expected.Should().Contain("    M" + typed);
        await completed.Task.WaitAsync(ct);
        lock (frames)
        {
            var completion = frames.FindIndex(f => f.GetProperty("method").GetString() == request);
            frames.Take(completion).Should().Contain(f =>
                f.GetProperty("method").GetString() == "textDocument/didChange"
                && f.GetProperty("params").GetProperty("contentChanges")[0].GetProperty("text").GetString() == expected,
                "the server must receive the typed character before answering the typing-triggered request");
        }
        await client.StopAsync();
        await loop.WaitAsync(ct);
    }

    private static async Task<string?> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        var header = new List<byte>(64);
        var one = new byte[1];

        while (true)
        {
            if (await stream.ReadAsync(one.AsMemory(0, 1), ct) == 0) return null;
            header.Add(one[0]);
            if (header.Count >= 4
                && header[^4] == (byte)'\r' && header[^3] == (byte)'\n'
                && header[^2] == (byte)'\r' && header[^1] == (byte)'\n')
            {
                break;
            }
        }

        var length = 0;
        foreach (var line in Encoding.ASCII.GetString([.. header]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                length = int.Parse(line["Content-Length:".Length..].Trim(), CultureInfo.InvariantCulture);
            }
        }

        if (length <= 0) return null;

        var body = new byte[length];
        var read = 0;
        while (read < length)
        {
            var n = await stream.ReadAsync(body.AsMemory(read, length - read), ct);
            if (n == 0) return null;
            read += n;
        }

        return Encoding.UTF8.GetString(body);
    }

    private static async Task WriteFrameAsync(Stream stream, string json, CancellationToken ct)
    {
        // Collapse the interpolated literals' incidental newlines; the header must state the real length.
        var compact = JsonSerializer.Serialize(JsonDocument.Parse(json).RootElement);
        var body = Encoding.UTF8.GetBytes(compact);
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"), ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }
}
