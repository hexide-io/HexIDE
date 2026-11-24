using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
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

namespace HexIDE.Integration.Tests.Views;

public sealed class CompletionResolveTests : IDisposable
{
    private readonly List<Window> windows = [];
    private readonly List<CodeEditorViewModel> made = [];
    private readonly ILspClient lsp = Substitute.For<ILspClient>();

    public CompletionResolveTests()
    {
        lsp.IsRunning.Returns(true);
        lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        lsp.RequestFoldingRangesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        lsp.RequestCompletionAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>()).Returns(
        [
            new CompletionItem("Mid", CompletionItemKind.Function),
            new CompletionItem("Min", CompletionItemKind.Function),
        ]);
    }

    public void Dispose()
    {
        foreach (var window in windows) window.Close();
        foreach (var model in made) model.Dispose();
    }

    private (Window Window, TextEditor Editor) Show()
    {
        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe<CreateOrNavigateToSubEvent>(Arg.Any<Action<CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(Arg.Any<Action<ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<FormUnloadedEvent>(Arg.Any<Action<FormUnloadedEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<DocumentSavedEvent>(Arg.Any<Action<DocumentSavedEvent>>()).Returns(Substitute.For<IDisposable>());
        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);
        settings.AutoListMembers.Returns(true);
        settings.ListMembersAsYouType.Returns(true);

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
        return (window, editor);
    }

    private static void Pump()
    {
        for (var i = 0; i < 10; i++)
            Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void KeyboardSelectionShowsTheResolvedDescriptionInTheTooltip()
    {
        const string documentation = "Returns part of a string.";
        var answer = new TaskCompletionSource<CompletionItem?>();
        lsp.ResolveCompletionItemAsync(Arg.Is<CompletionItem>(item => item.Label == "Mid"), Arg.Any<CancellationToken>())
            .Returns(answer.Task);
        var (window, editor) = Show();
        OpenList(window, editor);
        Press(window, Key.Down);
        List(editor).CompletionList.SelectedItem!.Text.Should().Be("Mid");
        TooltipShows(window, documentation).Should().BeFalse();

        answer.SetResult(new CompletionItem("Mid", CompletionItemKind.Function, "Mid detail",
            Documentation: new MarkupContent("plaintext", documentation)));
        Pump();

        TooltipShows(window, documentation).Should().BeTrue();
        TooltipShows(window, "Mid detail").Should().BeTrue();
        List(editor).CompletionList.SelectedItem!.Text.Should().Be("Mid");
    }

    [AvaloniaFact]
    public void MovingSelectionCancelsResolveWithoutShowingALateReplyAndAsksAgainOnReturn()
    {
        var first = new TaskCompletionSource<CompletionItem?>();
        var second = new TaskCompletionSource<CompletionItem?>();
        var again = new TaskCompletionSource<CompletionItem?>();
        var firstToken = CancellationToken.None;
        lsp.ResolveCompletionItemAsync(Arg.Is<CompletionItem>(item => item.Label == "Mid"), Arg.Any<CancellationToken>())
            .Returns(call => { firstToken = call.Arg<CancellationToken>(); return first.Task; }, _ => again.Task);
        lsp.ResolveCompletionItemAsync(Arg.Is<CompletionItem>(item => item.Label == "Min"), Arg.Any<CancellationToken>())
            .Returns(second.Task);
        var (window, editor) = Show();
        OpenList(window, editor);
        Press(window, Key.Down);
        List(editor).CompletionList.SelectedItem!.Text.Should().Be("Mid");
        Press(window, Key.Down);
        List(editor).CompletionList.SelectedItem!.Text.Should().Be("Min");
        firstToken.IsCancellationRequested.Should().BeTrue();
        first.SetResult(new CompletionItem("Mid", CompletionItemKind.Function, "Stale detail"));
        second.SetResult(null);
        Pump();
        TooltipShows(window, "Stale detail").Should().BeFalse();
        Press(window, Key.Up);
        List(editor).CompletionList.SelectedItem!.Text.Should().Be("Mid");
        // The first request was cancelled before it answered, so coming back asks again: an item passed over on the way
        // down a list must still show its documentation when it is selected.
        _ = lsp.Received(2).ResolveCompletionItemAsync(
            Arg.Is<CompletionItem>(item => item.Label == "Mid"), Arg.Any<CancellationToken>());
        TooltipShows(window, "Stale detail").Should().BeFalse();
        again.SetResult(new CompletionItem("Mid", CompletionItemKind.Function, "Fresh detail"));
        Pump();
        TooltipShows(window, "Fresh detail").Should().BeTrue();
    }

    [AvaloniaFact]
    public void AcceptingAnItemClosesAndCancelsResolveWithoutWaiting()
    {
        var answer = new TaskCompletionSource<CompletionItem?>();
        var requestToken = CancellationToken.None;
        lsp.ResolveCompletionItemAsync(Arg.Any<CompletionItem>(), Arg.Any<CancellationToken>())
            .Returns(call => { requestToken = call.Arg<CancellationToken>(); return answer.Task; });
        var (window, editor) = Show();
        OpenList(window, editor);
        Press(window, Key.Down);
        Press(window, Key.Tab);

        editor.Text.Should().Contain("    Mid");
        CodeEditorView.OpenPopupsOf(editor).OfType<CompletionWindow>().Should().BeEmpty();
        requestToken.IsCancellationRequested.Should().BeTrue();
        answer.SetResult(new CompletionItem("Mid", CompletionItemKind.Function, "Late detail"));
        Pump();
        TooltipShows(window, "Late detail").Should().BeFalse();
    }

    private static void OpenList(Window window, TextEditor editor)
    {
        window.KeyTextInput("i");
        Pump();
        List(editor).CompletionList.SelectedItem.Should().BeNull();
    }

    private static CompletionWindow List(TextEditor editor) =>
        CodeEditorView.OpenPopupsOf(editor).OfType<CompletionWindow>().Should().ContainSingle().Subject;

    private static bool TooltipShows(Window window, string text) =>
        UiAutomationDriver.Find(UiAutomationDriver.Dump(window, "Window", 60, interactiveOnly: false), text, 50).Total > 0;

    private static void Press(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        Pump();
    }
}
