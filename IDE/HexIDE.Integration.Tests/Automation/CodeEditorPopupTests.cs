using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
            new CompletionItem("Mid", CompletionItemKind.Function),
        ]);
        lsp.RequestSignatureHelpAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>()).Returns(
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

    private (Window Window, CodeEditorView View, TextEditor Editor) Show()
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
}
