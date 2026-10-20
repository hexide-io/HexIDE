using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
}
