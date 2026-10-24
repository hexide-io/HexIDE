using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
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
/// type_text's keyboard mode types as a person does, a key at a time (#943). The document mode inserts through the
/// document, by design, and press_key raises only KeyDown and KeyUp, which AvaloniaEdit writes no text for, so nothing
/// on the surface raised TextInput: the completion list that opens as you type, parameter info, and the block a line
/// break closes were out of reach.
/// </summary>
public class TypeKeysTests : IDisposable
{
    private readonly List<Window> windows = [];
    private readonly List<CodeEditorViewModel> made = [];
    private readonly ILspClient lsp = Substitute.For<ILspClient>();

    public TypeKeysTests()
    {
        lsp.IsRunning.Returns(true);
        lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<DocumentSymbol>());
        lsp.RequestFoldingRangesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<FoldingRange>());
        lsp.RequestCompletionAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CompletionItem>());
    }

    public void Dispose()
    {
        foreach (var w in windows) w.Close();
        foreach (var vm in made) vm.Dispose();
        GC.SuppressFinalize(this);
    }

    private TextEditor Show(string code)
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
            eventBus, lsp, settings, Substitute.For<IStatusBarService>(), new BookmarkService(), new BreakpointService(),
            Substitute.For<IDebugController>(), new RunScope(), Substitute.For<ILocalizationService>());
        made.Add(vm);
        var module = new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "Module1", ModuleKind.StandardModule);
        module.UpdateCode(code);
        vm.Initialize(module);

        var window = new Window { Width = 1200, Height = 800 };
        windows.Add(window);
        window.Show();
        var view = new CodeEditorView { DataContext = vm };
        window.Content = view;
        Dispatcher.UIThread.RunJobs();
        return view.FindControl<TextEditor>("TextEditor")!;
    }

    private static void PutCaretAfter(TextEditor editor, string text) =>
        editor.TextArea.Caret.Offset = editor.Document.Text.IndexOf(text, StringComparison.Ordinal) + text.Length;

    private const string Body = "Option Explicit\r\n\r\nPrivate Sub Foo()\r\n    \r\nEnd Sub\r\n";

    private const string WithRun = "Option Explicit\r\n\r\nPublic Function Total()\r\n"
        + "Attribute Total.VB_UserMemId = 0\r\n    Total = 1\r\nEnd Function\r\n";

    [AvaloniaFact]
    public void Delete1055_Partial_run_selection_deletes_code_and_names_what_stays()
    {
        var editor = Show(WithRun);
        var start = editor.Text.IndexOf("Total()", StringComparison.Ordinal);
        var end = editor.Text.IndexOf("VB_UserMemId", StringComparison.Ordinal);
        var before = editor.Text;
        var removed = editor.Document.GetLineByOffset(start).EndOffset - start;
        editor.Select(start, end - start);

        var result = UiAutomationDriver.PressKey(editor, "Delete", null);

        result.Success.Should().BeTrue(result.Error);
        editor.Text.Should().Be(before.Remove(start, removed));
        result.Detail.Should().Contain("deleted 7 char(s)")
            .And.Contain("left").And.Contain("the Attribute lines describing Total")
            .And.Contain("which only the IDE changes").And.Contain($"caret is now at offset {editor.CaretOffset}")
            .And.Contain("no selection remains");
        editor.SelectionLength.Should().Be(0);
    }

    [AvaloniaFact]
    public void Delete1055_Backspace_selection_reaching_header_deletes_only_code()
    {
        var editor = Show(WithRun);
        var end = editor.Text.IndexOf("Option Explicit", StringComparison.Ordinal) + "Option".Length;
        var code = editor.Text.IndexOf("Option Explicit", StringComparison.Ordinal);
        var before = editor.Text;
        editor.Select(3, end - 3);

        var result = UiAutomationDriver.PressKey(editor, "Back", null);

        result.Success.Should().BeTrue(result.Error);
        editor.Text.Should().Be(before.Remove(code, end - code));
        result.Detail.Should().Contain("deleted 6 char(s)").And.Contain("the file's header")
            .And.Contain("which only the IDE changes").And.Contain($"caret is now at offset {editor.CaretOffset}")
            .And.Contain("no selection remains");
        editor.SelectionLength.Should().Be(0);
    }

    [AvaloniaFact]
    public void Delete1055_Selection_wholly_in_run_is_refused_and_reports_remaining_selection()
    {
        var editor = Show(WithRun);
        var start = editor.Text.IndexOf("Attribute Total.", StringComparison.Ordinal) + 2;
        editor.Select(start, 5);
        var before = editor.Text;

        var result = UiAutomationDriver.PressKey(editor, "Delete", null);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("the Attribute lines describing Total").And.Contain("Nothing was written.")
            .And.Contain($"selection remains from offset {start} to {start + 5}");
        editor.Text.Should().Be(before);
        editor.SelectionLength.Should().Be(5);
    }

    [AvaloniaFact]
    public async Task Delete1055_Typing_over_partial_selection_matches_real_text_input()
    {
        foreach (var header in new[] { false, true })
        {
            var editor = Show(WithRun);
            var person = Show(WithRun);
            var start = header ? 3 : editor.Text.IndexOf("Total()", StringComparison.Ordinal);
            var end = header ? editor.Text.IndexOf("Option Explicit", StringComparison.Ordinal) + 6
                : editor.Text.IndexOf("VB_UserMemId", StringComparison.Ordinal);
            editor.Select(start, end - start);
            person.Select(start, end - start);

            var typed = UiAutomationDriver.TypeText(editor, "x");
            person.TextArea.RaiseEvent(new TextInputEventArgs
            {
                RoutedEvent = InputElement.TextInputEvent, Text = "x",
            });

            typed.Success.Should().BeTrue(typed.Error);
            editor.Text.Should().Be(person.Text);
            editor.CaretOffset.Should().Be(person.CaretOffset);
            editor.SelectionLength.Should().Be(person.SelectionLength).And.Be(0);
            typed.Detail.Should().Contain(header ? "the file's header" : "the Attribute lines describing Total");

            var keyboard = Show(WithRun);
            keyboard.Select(start, end - start);
            var keys = await UiAutomationDriver.TypeKeysAsync(keyboard, "x");
            keys.Success.Should().BeTrue(keys.Error);
            keyboard.Text.Should().Be(person.Text);
        }
    }

    [AvaloniaFact]
    public void Refusal1042_Backspace_names_the_member_without_sending_the_caret_back_to_itself()
    {
        var editor = Show(WithRun);
        PutCaretAfter(editor, "Attribute Total.VB_UserMemId = 0\r\n");
        var caret = editor.CaretOffset;
        var before = editor.Text;

        var refused = UiAutomationDriver.PressKey(editor, "Back", null);

        refused.Success.Should().BeFalse();
        refused.Error.Should().Contain("text before the caret belongs to the Attribute lines describing Total")
            .And.Contain("Deleting the member's whole declaration")
            .And.NotContain($"CaretOffset={caret}").And.NotContain("nearest place");
        editor.Text.Should().Be(before);

        var declaration = editor.Document.GetLineByOffset(editor.Text.IndexOf("Public Function", StringComparison.Ordinal));
        editor.Select(declaration.Offset, declaration.Length);
        UiAutomationDriver.PressKey(editor, "Delete", null).Success.Should().BeTrue();
        editor.Text.Should().NotContain("Public Function Total").And.NotContain("Attribute Total.")
            .And.Contain("    Total = 1");
    }

    [AvaloniaFact]
    public void Refusal1042_Header_key_names_the_header_unambiguously()
    {
        var editor = Show(WithRun);
        editor.CaretOffset = 3;
        var before = editor.Text;

        var refused = UiAutomationDriver.PressKey(editor, "Delete", null);

        refused.Success.Should().BeFalse();
        refused.Error.Should().Contain("is in the file's header, which only the IDE changes")
            .And.NotContain("or a member");
        editor.Text.Should().Be(before);
    }

    [AvaloniaFact]
    public void Refusal1042_Insertion_in_a_run_names_it_and_gives_a_new_editable_offset()
    {
        var editor = Show(WithRun);
        PutCaretAfter(editor, "Attribute Total.");
        var caret = editor.CaretOffset;
        var past = editor.Text.IndexOf("    Total = 1", StringComparison.Ordinal);
        var before = editor.Text;

        var refused = UiAutomationDriver.TypeText(editor, "x");

        refused.Success.Should().BeFalse();
        refused.Error.Should().Contain("is in the Attribute lines describing Total, which only the IDE changes")
            .And.Contain($"nearest place that can be edited starts at offset {past}")
            .And.Contain($"CaretOffset={past}").And.NotContain($"CaretOffset={caret}");
        editor.Text.Should().Be(before);
    }

    [AvaloniaFact]
    public async Task Letters_typed_as_keys_ask_for_completion_as_a_person_s_do()
    {
        var editor = Show(Body);
        PutCaretAfter(editor, "Foo()\r\n    ");
        var entered = new List<string?>();
        editor.TextArea.TextEntered += (_, e) => entered.Add(e.Text);

        var typed = await UiAutomationDriver.TypeKeysAsync(editor, "Ms");
        Dispatcher.UIThread.RunJobs();

        typed.Success.Should().BeTrue(typed.Error);
        typed.Mechanism.Should().Be("keyboard");
        typed.Detail.Should().StartWith("typed 2 key(s) into TextEditor as a person types them; the caret is at offset ");
        entered.Should().Equal("M", "s");
        editor.Document.Text.Should().Contain("    Ms\r\nEnd Sub");
        // Line 4, counted from 0: the module's Attribute VB_Name header is its first line.
        await lsp.Received().RequestCompletionAsync(Arg.Any<string>(), new Position(4, 5), Arg.Any<CancellationToken>());
        await lsp.Received().RequestCompletionAsync(Arg.Any<string>(), new Position(4, 6), Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public void The_document_mode_asks_for_nothing_which_is_why_the_keyboard_mode_exists()
    {
        var editor = Show(Body);
        PutCaretAfter(editor, "Foo()\r\n    ");

        UiAutomationDriver.TypeText(editor, "Ms").Success.Should().BeTrue();
        Dispatcher.UIThread.RunJobs();

        lsp.DidNotReceive().RequestCompletionAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task A_line_break_typed_as_Enter_closes_the_block_it_opens()
    {
        var editor = Show("Option Explicit\r\n\r\n");
        editor.TextArea.Caret.Offset = editor.Document.TextLength;

        var typed = await UiAutomationDriver.TypeKeysAsync(editor, "private sub Bar()\nx = 1");

        typed.Success.Should().BeTrue(typed.Error);
        editor.Document.Text.ReplaceLineEndings("\n").Should().EndWith("Private Sub Bar()\n    x = 1\nEnd Sub",
            "Enter re-cases the line, adds its End Sub and indents the body, as a person's does; a plain Enter writes \\n (#530)");
    }

    [AvaloniaFact]
    public async Task A_key_in_the_header_is_refused_before_anything_is_typed()
    {
        var editor = Show(Body);
        editor.TextArea.Caret.Offset = 3;
        var before = editor.Document.Text;

        var typed = await UiAutomationDriver.TypeKeysAsync(editor, "x");

        typed.Success.Should().BeFalse();
        typed.Mechanism.Should().Be("keyboard");
        typed.Error.Should().Contain("Nothing was written");
        editor.Document.Text.Should().Be(before);
    }

    [AvaloniaFact]
    public async Task A_TextBox_takes_the_keys_as_its_text()
    {
        var box = new TextBox { Width = 200 };
        var window = new Window { Width = 300, Height = 100, Content = box };
        windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var typed = await UiAutomationDriver.TypeKeysAsync(window, "Ab 1");

        typed.Success.Should().BeTrue(typed.Error);
        typed.Detail.Should().Contain("TextBox").And.NotContain("caret");
        box.Text.Should().Be("Ab 1");
    }

    // ── as a person's keys arrive: after the list a letter asked for, to the focused control, bindings first ──────

    private static CompletionWindow? OpenList(TextEditor editor) =>
        typeof(CodeEditorView).GetField("_completionWindow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(editor.FindLogicalAncestorOfType<CodeEditorView>()) as CompletionWindow is { IsOpen: true } open ? open : null;

    [AvaloniaFact]
    public async Task The_list_a_letter_asks_for_is_open_before_the_reply_as_a_person_sees_it_before_the_next_key()
    {
        // The server answers off the UI thread, after a later key had already gone in: the list opened after the typing.
        lsp.RequestCompletionAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            await Task.Delay(150);
            return new[] { new CompletionItem("MsgBox", CompletionItemKind.Function) };
        });
        var editor = Show(Body);
        PutCaretAfter(editor, "Foo()\r\n    ");

        var typed = await UiAutomationDriver.TypeKeysAsync(editor, "M");

        typed.Success.Should().BeTrue(typed.Error);
        typed.Detail.Should().NotContain("had not answered");
        OpenList(editor).Should().NotBeNull("the list the letter asked for is open when the reply comes");
    }

    [AvaloniaFact]
    public async Task A_server_slower_than_the_wait_is_named_in_the_reply()
    {
        var gate = new TaskCompletionSource<CompletionItem[]>();
        lsp.RequestCompletionAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>()).Returns(_ => gate.Task);
        var editor = Show(Body);
        PutCaretAfter(editor, "Foo()\r\n    ");
        var wait = UiAutomationDriver.PopupWait;
        UiAutomationDriver.PopupWait = TimeSpan.FromMilliseconds(100);
        try
        {
            var typed = await UiAutomationDriver.TypeKeysAsync(editor, "M");

            typed.Detail.Should().EndWith("; the language server had not answered 1 key(s)' completion or parameter-info "
                                          + "request within 100 ms, so a list may still open");
        }
        finally
        {
            UiAutomationDriver.PopupWait = wait;
            gate.SetResult([]);
        }
    }

    private Window ShowBoxes(params Control[] controls)
    {
        var panel = new StackPanel();
        foreach (var c in controls) panel.Children.Add(c);
        var window = new Window { Width = 400, Height = 300, Content = panel };
        windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public async Task A_Tab_that_moves_focus_sends_the_keys_after_it_where_focus_went()
    {
        var (a, b) = (new TextBox { Width = 200 }, new TextBox { Width = 200 });
        ShowBoxes(a, b);

        var typed = await UiAutomationDriver.TypeKeysAsync(a, "x\ty");

        typed.Success.Should().BeTrue(typed.Error);
        (a.Text, b.Text).Should().Be(("x", "y"), "a person's y goes to the box the Tab moved to");
        typed.Detail.Should().Contain("focus moved on to");
    }

    [AvaloniaFact]
    public async Task An_Enter_bound_to_a_command_runs_it_as_a_person_s_does()
    {
        var searched = 0;
        var box = new TextBox { Width = 200 };
        box.KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Enter), Command = new RelayCommand(() => searched++) });
        ShowBoxes(box);

        (await UiAutomationDriver.TypeKeysAsync(box, "foo\n")).Success.Should().BeTrue();
        searched.Should().Be(1, "the binding on the box takes the Enter, as the Object Browser's search box's does");

        UiAutomationDriver.PressKey(box, "Enter", null).Success.Should().BeTrue();
        searched.Should().Be(2, "press_key delivers a key the same way");
    }

    private sealed class RelayCommand(Action run) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => run();
    }

    [AvaloniaFact]
    public async Task A_dialog_opening_over_the_window_stops_the_typing_and_says_where()
    {
        var box = new TextBox { Width = 200 };
        var window = ShowBoxes(box);
        Window? prompt = null;
        box.TextChanged += (_, _) =>
        {
            if (prompt is not null) return;
            prompt = new Window { Title = "Reset?", Width = 100, Height = 50 };
            _ = prompt.ShowDialog(window);
        };
        try
        {
            var typed = await UiAutomationDriver.TypeKeysAsync(box, "abc");

            typed.Success.Should().BeFalse();
            typed.Error.Should().StartWith("typed 1 of 3 key(s) into TextBox, then stopped: 'Reset?' opened over the window");
            box.Text.Should().Be("a", "the rest would have gone to the dialog");
        }
        finally { prompt?.Close(); }
    }

    // The reset prompt a key opens during a run is owned by whichever window is active, often a running form, and the
    // typing looked only at its own window's dialogs: the next key went into the code under a prompt a person's key would
    // have gone to, and the stop blamed focus (#1257). A dialog over any window stops it, named with where it opened.
    [AvaloniaFact]
    public async Task A_dialog_opening_over_another_window_stops_the_typing_and_names_that_window()
    {
        var box = new TextBox { Width = 200 };
        ShowBoxes(box);
        var form = new Window { Title = "Form1", Width = 200, Height = 100 };
        form.Show();
        Window? prompt = null;
        box.TextChanged += (_, _) =>
        {
            if (prompt is not null) return;
            prompt = new Window { Title = "Reset?", Width = 100, Height = 50 };
            _ = prompt.ShowDialog(form);
        };
        try
        {
            var typed = await UiAutomationDriver.TypeKeysAsync(box, "abc");

            typed.Success.Should().BeFalse();
            typed.Error.Should().StartWith("typed 1 of 3 key(s) into TextBox, then stopped: 'Reset?' opened over 'Form1', "
                                           + "and a person's next key would go to it");
            box.Text.Should().Be("a", "the rest would have gone to the dialog");
        }
        finally
        {
            prompt?.Close();
            form.Close();
        }
    }

    // A dialog open over the window disables it, and a person's keys go to the dialog; the window's focus stays where it
    // was, so the keys went in under the reset prompt (#1290). Refused before a key is sent, naming the dialog, and the
    // reply's note leaves it out. press_key is refused the same way.
    [AvaloniaFact]
    public async Task A_dialog_open_over_the_window_refuses_the_typing_and_names_it()
    {
        var box = new TextBox { Width = 200 };
        var window = ShowBoxes(box);
        window.Title = "Project1 - HexIDE";
        var open = new Window { Title = "Reset?", Width = 100, Height = 50 };
        _ = open.ShowDialog(window);
        Dispatcher.UIThread.RunJobs();
        try
        {
            var watch = OpenDialogs.Watch(OpenDialogs.Among(windows.Append(open)), window);
            OpenDialogs.During(watch);
            var typed = await UiAutomationDriver.TypeKeysAsync(box, "abc");

            typed.Error.Should().Be("'Reset?', a modal dialog over 'Project1 - HexIDE', is open and waits for an answer, so a "
                                    + "person's keys would go to it, not to TextBox; nothing was typed. Answer it first: "
                                    + "dump_visual_tree with 'window' set to its title shows it, and interact answers it");
            box.Text.Should().BeNullOrEmpty();
            OpenDialogs.Note(watch, windows.Append(open)).Should().BeNull("the refusal names it");

            var pressed = UiAutomationDriver.PressKey(box, "A", null);
            pressed.Error.Should().StartWith("'Reset?', a modal dialog over 'Project1 - HexIDE', is open and waits for an "
                                             + "answer, so a person's keys would go to it, not to TextBox; nothing was pressed.");
            box.Text.Should().BeNullOrEmpty();
        }
        finally { open.Close(); }
    }

    // Dialogs stacked over the window: the one on top takes the keys, and the one under it cannot be answered first, so the
    // refusal names the top one (#1290 review).
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Under_stacked_dialogs_the_refusal_names_the_one_on_top(bool press)
    {
        var box = new TextBox { Width = 200 };
        var window = ShowBoxes(box);
        window.Title = "Main";
        var outer = new Window { Title = "Outer", Width = 200, Height = 100 };
        _ = outer.ShowDialog(window);
        Dispatcher.UIThread.RunJobs();
        var inner = new Window { Title = "Inner", Width = 100, Height = 50 };
        _ = inner.ShowDialog(outer);
        Dispatcher.UIThread.RunJobs();
        try
        {
            var watch = OpenDialogs.Watch([window, outer, inner], window);
            OpenDialogs.During(watch);
            var error = press
                ? UiAutomationDriver.PressKey(box, "A", null).Error
                : (await UiAutomationDriver.TypeKeysAsync(box, "abc")).Error;
            error.Should().StartWith("'Inner', a modal dialog over 'Outer', is open");
            var note = OpenDialogs.Note(watch, [window, outer, inner]);
            note.Should().BeNull("the refusal speaks for every blocking dialog");
            var reply = error + note;
            reply.Should().NotContain("reached past");
            reply.Split("'Inner'").Length.Should().Be(2);
            reply.Split("'Outer'").Length.Should().Be(2);
        }
        finally
        {
            inner.Close();
            outer.Close();
        }
    }

    // Under a dialog a key goes to the dialog, whatever the control would have done with it: a read-only box is not the
    // reason given (#1290 review).
    [AvaloniaFact]
    public async Task A_dialog_over_the_window_is_named_ahead_of_what_the_control_would_refuse()
    {
        var box = new TextBox { Width = 200, IsReadOnly = true };
        var window = ShowBoxes(box);
        window.Title = "Main";
        var open = new Window { Title = "Reset?", Width = 100, Height = 50 };
        _ = open.ShowDialog(window);
        Dispatcher.UIThread.RunJobs();
        try
        {
            (await UiAutomationDriver.TypeKeysAsync(box, "abc")).Error.Should().StartWith("'Reset?', a modal dialog over 'Main'");
        }
        finally { open.Close(); }
    }

    // Typing in a dialog that has a dialog of its own open over it: the one over it takes the keys.
    [AvaloniaFact]
    public async Task A_dialog_over_the_dialog_typed_in_refuses_the_typing()
    {
        var host = new Window { Title = "Host", Width = 300, Height = 200 };
        windows.Add(host);
        host.Show();
        var box = new TextBox { Width = 200 };
        var dialog = new Window { Title = "Options", Width = 250, Height = 150, Content = box };
        _ = dialog.ShowDialog(host);
        var over = new Window { Title = "Sure?", Width = 100, Height = 50 };
        Dispatcher.UIThread.RunJobs();
        _ = over.ShowDialog(dialog);
        Dispatcher.UIThread.RunJobs();
        try
        {
            var typed = await UiAutomationDriver.TypeKeysAsync(box, "abc");

            typed.Error.Should().StartWith("'Sure?', a modal dialog over 'Options', is open");
            box.Text.Should().BeNullOrEmpty();
        }
        finally
        {
            over.Close();
            dialog.Close();
        }
    }

    // A dialog already open when the typing starts is not one the typing opened, wherever it is. Headless, the dialog takes
    // focus once shown, so the typing stops on focus after the first key, without naming the dialog as opened by it.
    [AvaloniaFact]
    public async Task A_dialog_open_before_the_typing_is_not_named_as_opened_by_it()
    {
        var box = new TextBox { Width = 200 };
        ShowBoxes(box);
        var form = new Window { Title = "Form1", Width = 200, Height = 100 };
        form.Show();
        var open = new Window { Title = "Already", Width = 100, Height = 50 };
        _ = open.ShowDialog(form);
        try
        {
            var typed = await UiAutomationDriver.TypeKeysAsync(box, "abc");

            typed.Error.Should().Be("typed 1 of 3 key(s) into TextBox, then stopped: focus has left the window, so a "
                                    + "person's next key would not reach it");
            box.Text.Should().Be("a");
        }
        finally
        {
            open.Close();
            form.Close();
        }
    }

    // The stop names the dialog, so the reply's note of open dialogs leaves it out rather than naming it twice, and in
    // other words (#1257 review).
    [AvaloniaFact]
    public async Task A_dialog_the_typing_stopped_for_is_left_out_of_the_reply_s_note()
    {
        var box = new TextBox { Width = 200 };
        var window = ShowBoxes(box);
        var form = new Window { Title = "Form1", Width = 200, Height = 100 };
        form.Show();
        Window? prompt = null;
        box.TextChanged += (_, _) =>
        {
            if (prompt is not null) return;
            prompt = new Window { Title = "Reset?", Width = 100, Height = 50 };
            _ = prompt.ShowDialog(form);
        };
        try
        {
            var watch = OpenDialogs.Watch([window, form], window);
            OpenDialogs.During(watch);

            var typed = await UiAutomationDriver.TypeKeysAsync(box, "abc");

            typed.Error.Should().Contain("'Reset?' opened over 'Form1'");
            OpenDialogs.Note(watch, [window, form, prompt!]).Should().BeNull();
        }
        finally
        {
            prompt?.Close();
            form.Close();
        }
    }

    // A program's MsgBox shown with no form to sit over has a window of its own, and "over no window" said nothing.
    [AvaloniaFact]
    public async Task A_box_in_a_window_of_its_own_is_named_so()
    {
        var box = new TextBox { Width = 200 };
        ShowBoxes(box);
        Window? shown = null;
        box.TextChanged += (_, _) =>
        {
            if (shown is not null) return;
            shown = new Window { Title = "Project1", Content = new HexIDE.Runtime.Dialogs.MessageBox() };
            shown.Show();
        };
        try
        {
            var typed = await UiAutomationDriver.TypeKeysAsync(box, "abc");

            typed.Error.Should().StartWith("typed 1 of 3 key(s) into TextBox, then stopped: 'Project1' opened in a window "
                                           + "of its own, and a person's next key would go to it");
        }
        finally { shown?.Close(); }
    }

    // A MsgBox whose form is hidden moves to a window of its own: the same box in a new window, not a new dialog.
    [AvaloniaFact]
    public async Task A_box_moving_to_a_new_window_does_not_stop_the_typing()
    {
        var box = new TextBox { Width = 200 };
        ShowBoxes(box);
        var message = new HexIDE.Runtime.Dialogs.MessageBox();
        var first = new Window { Title = "Project1", Content = message };
        first.Show();
        Window? second = null;
        box.TextChanged += (_, _) =>
        {
            if (second is not null) return;
            first.Content = null;
            first.Close();
            second = new Window { Title = "Project1", Content = message };
            second.Show();
            box.Focus();
        };
        try
        {
            var typed = await UiAutomationDriver.TypeKeysAsync(box, "abc");

            (typed.Error ?? "").Should().NotContain("'Project1' opened");
        }
        finally
        {
            first.Close();
            second?.Close();
        }
    }

    [AvaloniaFact]
    public async Task A_character_beyond_the_basic_plane_is_one_key_and_a_lone_surrogate_takes_nothing_with_it()
    {
        var box = new TextBox { Width = 200 };
        ShowBoxes(box);

        var typed = await UiAutomationDriver.TypeKeysAsync(box, "a\U0001F600b\uD800c");

        typed.Detail.Should().StartWith("typed 5 key(s)", "a, the emoji, b, the lone surrogate, c");
        box.Text.Should().EndWith("c");
    }
}
