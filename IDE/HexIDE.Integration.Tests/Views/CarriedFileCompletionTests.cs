using System;
using System.Collections.Generic;
using System.IO;
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
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Views;

public class CarriedFileCompletionTests : IDisposable
{
    private readonly ILspClient client = Substitute.For<ILspClient>();
    private readonly ISettingsService settings = Substitute.For<ISettingsService>();
    private readonly string directory = Path.Combine(Path.GetTempPath(), "hexide-completion-" + Guid.NewGuid().ToString("N"));
    private readonly List<Window> windows = [];
    private readonly List<RelatedDocumentEditorViewModel> models = [];

    public CarriedFileCompletionTests()
    {
        Directory.CreateDirectory(directory);
        client.IsRunning.Returns(true);
        client.SupportsCapability(Arg.Any<string>(), "completionProvider").Returns(true);
        client.SupportsCapability(Arg.Any<string>(), "signatureHelpProvider").Returns(true);
        client.SignatureHelpTriggerCharacters(Arg.Any<string>()).Returns(new[] { ":" });
        client.CompletionTriggerCharacters(Arg.Any<string>()).Returns(new[] { "." });
        client.RequestCompletionAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new CompletionItem("chosen", CompletionItemKind.Text) });
        client.RequestSignatureHelpAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(new SignatureHelp([new SignatureInformation("call: argument")], 0, 0));
        settings.AutoListMembers.Returns(true);
    }

    public void Dispose()
    {
        foreach (var window in windows) window.Close();
        foreach (var model in models) model.Dispose();
        Directory.Delete(directory, true);
        GC.SuppressFinalize(this);
    }

    private (Window Window, RelatedDocumentEditorView View, TextEditor Editor) Show(string text = "")
    {
        var path = Path.Combine(directory, "carried.cpp");
        File.WriteAllText(path, text);
        var model = new RelatedDocumentEditorViewModel(client, Substitute.For<IWindowManager>(),
            Substitute.For<ILocalizationService>(), settings).Initialize(new RelatedDocumentDefinition(
                new ProjectDefinition(VBProjectType.EXE, "P"), "carried.cpp", path));
        models.Add(model);
        var view = new RelatedDocumentEditorView { DataContext = model };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var editor = view.FindControl<TextEditor>("TextEditor")!;
        editor.CaretOffset = text.Length;
        editor.TextArea.Focus();
        client.ClearReceivedCalls();
        return (window, view, editor);
    }

    private static CompletionWindow? List(TextEditor editor) =>
        CodeEditorView.OpenPopupsOf(editor).OfType<CompletionWindow>().FirstOrDefault(w => w.IsOpen);

    private static void Press(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
    }

    private static void Pump()
    {
        for (var i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task TypedLetterOpensCompletionAtCaretAndEnterCommits()
    {
        var (window, _, editor) = Show("ch");
        window.KeyTextInput("o");
        Pump();
        List(editor).Should().NotBeNull();
        await client.Received(1).RequestCompletionAsync(Arg.Any<string>(), new Position(0, 3), Arg.Any<CancellationToken>());
        Press(window, Key.Return);
        editor.Text.Should().Be("chosen");
        List(editor).Should().BeNull();
    }

    [AvaloniaFact]
    public void EnterWithNoSelectedCompletionClosesListAndStartsNewLine()
    {
        var (window, _, editor) = Show("v");
        window.KeyTextInput("l");
        Pump();
        List(editor).Should().NotBeNull();
        List(editor)!.CompletionList.CompletionData.Should().NotBeEmpty();
        List(editor)!.CompletionList.SelectedItem.Should().BeNull();
        Press(window, Key.Return);
        editor.Text.ReplaceLineEndings("\n").Should().Be("vl\n");
        List(editor).Should().BeNull();
    }

    [AvaloniaFact]
    public void SnippetSelectsStopAndTabLeavesIt()
    {
        client.RequestCompletionAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new CompletionItem("chosen", CompletionItemKind.Snippet, InsertText: "${1:chosen}$0", InsertTextFormat: 2) });
        var (window, _, editor) = Show();
        window.KeyTextInput("c");
        Pump();
        List(editor).Should().NotBeNull();
        Press(window, Key.Tab);
        editor.Text.Should().Be("chosen");
        editor.SelectedText.Should().Be("chosen");
        Press(window, Key.Tab);
        editor.SelectionLength.Should().Be(0);
        editor.CaretOffset.Should().Be(6);
    }

    [AvaloniaFact]
    public void EnterOnSnippetSelectsFirstStopWithoutLineBreak()
    {
        client.RequestCompletionAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new CompletionItem("chosen", CompletionItemKind.Snippet, InsertText: "${1:chosen}$0", InsertTextFormat: 2) });
        var (window, _, editor) = Show();
        window.KeyTextInput("c");
        Pump();
        List(editor).Should().NotBeNull();
        Press(window, Key.Return);
        editor.Text.Should().Be("chosen");
        editor.SelectedText.Should().Be("chosen");
        List(editor).Should().BeNull();
        Press(window, Key.Tab);
        editor.SelectionLength.Should().Be(0);
        editor.CaretOffset.Should().Be(6);
    }

    [AvaloniaFact]
    public async Task ServerSignatureTriggerOpensInsight()
    {
        var (window, _, editor) = Show("call");
        window.KeyTextInput(":");
        Pump();
        CodeEditorView.OpenPopupsOf(editor).OfType<OverloadInsightWindow>()
            .Should().ContainSingle(w => w.IsOpen);
        await client.Received(1).RequestSignatureHelpAsync(Arg.Any<string>(), new Position(0, 5), Arg.Any<CancellationToken>(), ":");
    }

    [AvaloniaFact]
    public async Task NoAdvertisedCompletionMakesNoRequest()
    {
        client.SupportsCapability(Arg.Any<string>(), "completionProvider").Returns(false);
        var (window, _, editor) = Show();
        window.KeyTextInput("c");
        Pump();
        List(editor).Should().BeNull();
        await client.DidNotReceive().RequestCompletionAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task AdvertisedCompletionTriggerUsesTriggerRequestAtCaret()
    {
        client.RequestCompletionForTriggerAsync(Arg.Any<string>(), Arg.Any<Position>(), ".", Arg.Any<CancellationToken>())
            .Returns(new[] { new CompletionItem("member", CompletionItemKind.Field) });
        var (window, _, editor) = Show("object");
        window.KeyTextInput(".");
        Pump();
        List(editor).Should().NotBeNull();
        await client.Received(1).RequestCompletionForTriggerAsync(Arg.Any<string>(), new Position(0, 7), ".", Arg.Any<CancellationToken>());
        await client.DidNotReceive().RequestCompletionAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task NoAdvertisedSignatureMakesNoRequest()
    {
        client.SupportsCapability(Arg.Any<string>(), "signatureHelpProvider").Returns(false);
        var (window, _, editor) = Show("call");
        window.KeyTextInput(":");
        Pump();
        CodeEditorView.OpenPopupsOf(editor).OfType<OverloadInsightWindow>().Should().BeEmpty();
        await client.DidNotReceive().RequestSignatureHelpAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>(), Arg.Any<string?>());
    }

    [AvaloniaFact]
    public async Task AutoListMembersOffMakesNoCompletionRequest()
    {
        settings.AutoListMembers.Returns(false);
        var (window, _, editor) = Show();
        window.KeyTextInput("c");
        Pump();
        List(editor).Should().BeNull();
        await client.DidNotReceive().RequestCompletionAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public void SpaceClosesWithoutCommitting()
    {
        var (window, _, editor) = Show();
        window.KeyTextInput("c");
        Pump();
        List(editor).Should().NotBeNull();
        window.KeyTextInput(" ");
        editor.Text.Should().Be("c ");
        List(editor).Should().BeNull();
    }

    [AvaloniaFact]
    public async Task ReattachDoesNotStackCompletionHandlers()
    {
        var (window, view, editor) = Show();
        window.Content = null;
        window.Content = view;
        Pump();
        editor.TextArea.Focus();
        window.KeyTextInput("c");
        Pump();
        List(editor).Should().NotBeNull();
        await client.Received(1).RequestCompletionAsync(Arg.Any<string>(), new Position(0, 1), Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public void DetachClosesPopupsAndEndsSnippetNavigation()
    {
        client.RequestCompletionAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new CompletionItem("chosen", CompletionItemKind.Snippet, InsertText: "${1:chosen}$0", InsertTextFormat: 2) });
        var (window, view, editor) = Show();
        window.KeyTextInput("c");
        Pump();
        List(editor).Should().NotBeNull();
        Press(window, Key.Tab);
        editor.TextArea.StackedInputHandlers.Should().NotBeEmpty();
        window.Content = null;
        editor.TextArea.StackedInputHandlers.Should().BeEmpty();
        window.Content = view;
        Pump();
        CodeEditorView.OpenPopupsOf(editor).Should().BeEmpty();
    }

    [AvaloniaFact]
    public async Task DetachedCompletionAnswerCannotOpenAPopupAfterReattach()
    {
        var answer = new TaskCompletionSource<CompletionItem[]>();
        client.RequestCompletionAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(_ => answer.Task);
        var (window, view, editor) = Show();
        window.KeyTextInput("c");
        Pump();
        var pending = CodeEditorView.PendingKeyRequestsOf(editor);
        pending.Should().NotBeNull();
        window.Content = null;
        window.Content = view;
        answer.SetResult([new CompletionItem("chosen", CompletionItemKind.Text)]);
        await pending!.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Pump();
        List(editor).Should().BeNull();
    }

    [AvaloniaFact]
    public async Task CompletionCommandReachesCarriedView()
    {
        var (window, _, editor) = Show("ch");
        var gesture = HexIDE.ApplicationCommands.CompleteWordCommand.Gestures.Single();
        editor.TextArea.RaiseEvent(new KeyEventArgs
        { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = gesture.Key, KeyModifiers = gesture.KeyModifiers });
        Pump();
        List(editor).Should().NotBeNull();
        await client.Received(1).RequestCompletionAsync(Arg.Any<string>(), new Position(0, 2), Arg.Any<CancellationToken>());
    }
}
