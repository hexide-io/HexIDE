using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
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

namespace HexIDE.Integration.Tests.Automation;

public sealed class LinkedEditingTests : IDisposable
{
    private Window? window;
    private CodeEditorViewModel? vm;
    private readonly ILspClient lsp = Substitute.For<ILspClient>();
    private readonly ISettingsService settings = Substitute.For<ISettingsService>();
    private readonly IStatusBarService statusBar = Substitute.For<IStatusBarService>();
    private const string Code = "Attribute VB_Name = \"Module1\"\nSub Foo()\nDim x\nx = x\nEnd Sub\n";

    public void Dispose()
    {
        window?.Close();
        vm?.Dispose();
    }

    private TextEditor Show(bool enabled = true, string? pattern = null, string? response = null, bool available = true)
    {
        settings.LinkedEditing.Returns(enabled);
        settings.TabWidth.Returns(4);
        lsp.IsRunning.Returns(true);
        lsp.SupportsCapability(Arg.Any<string>(), "linkedEditingRangeProvider").Returns(available);
        lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<DocumentSymbol>());
        lsp.RequestFoldingRangesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<FoldingRange>());
        lsp.RequestLinkedEditingRangesAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(response is null ? new LinkedEditingRanges([
                new(new(2, 4), new(2, 5)), new(new(3, 0), new(3, 1)), new(new(3, 4), new(3, 5))], pattern)
                : JsonSerializer.Deserialize<LinkedEditingRanges>(response));
        var bus = Substitute.For<IEventBus>();
        vm = new CodeEditorViewModel(Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(), bus, lsp, settings, statusBar,
            new BookmarkService(), new BreakpointService(), Substitute.For<IDebugController>(), new RunScope(),
            Substitute.For<ILocalizationService>());
        var module = new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "Module1", ModuleKind.StandardModule);
        module.UpdateCode(Code[Code.IndexOf("Sub Foo", StringComparison.Ordinal)..]);
        vm.Initialize(module);
        window = new Window { Width = 1000, Height = 700, Content = new CodeEditorView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var editor = ((CodeEditorView)window.Content).FindControl<TextEditor>("TextEditor")!;
        editor.CaretOffset = editor.Document.Text.IndexOf("x =", StringComparison.Ordinal) + 1;
        editor.TextArea.Focus();
        return editor;
    }

    private static async Task Rest()
    {
        await Task.Delay(750, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task TypingMirrorsAndOneUndoRestoresAllThree()
    {
        var editor = Show();
        await Rest();
        editor.TextArea.TextView.BackgroundRenderers.OfType<HexIDE.Controls.DocumentHighlightRenderer>()
            .SelectMany(renderer => renderer.Segments).Should().HaveCount(3);
        window!.KeyTextInput("y");
        editor.Text.ReplaceLineEndings("\n").Should().Be(Code.Replace("x", "xy"));
        await Rest();
        await lsp.Received().RequestLinkedEditingRangesAsync(Arg.Any<string>(), new Position(3, 2), Arg.Any<CancellationToken>());
        editor.Undo();
        editor.Text.ReplaceLineEndings("\n").Should().Be(Code);
    }

    [AvaloniaTheory]
    [InlineData("caret")]
    [InlineData("outside")]
    [InlineData("escape")]
    [InlineData("pattern")]
    public async Task EndingKeepsTypedTextAndStopsMirroring(string reason)
    {
        var editor = Show(pattern: "[a-z]+");
        await Rest();
        window!.KeyTextInput("y");
        editor.Text.ReplaceLineEndings("\n").Should().Contain("Dim xy");
        if (reason == "caret") { editor.CaretOffset = 0; editor.CaretOffset = editor.Document.Text.IndexOf("xy =", StringComparison.Ordinal) + 2; }
        if (reason == "outside") editor.Document.Insert(editor.Document.TextLength, " ");
        if (reason == "escape") window!.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        if (reason == "pattern") window!.KeyTextInput("1");
        window!.KeyTextInput("z");
        editor.Text.ReplaceLineEndings("\n").Should().Contain("Dim xy\n");
        editor.Text.ReplaceLineEndings("\n").Should().Contain(reason == "pattern" ? "xy1z = xy" : "xyz = xy");
    }

    [AvaloniaFact]
    public async Task CaretRequestsAreDebouncedAndNonWordsAreNotAsked()
    {
        var editor = Show();
        var offset = editor.CaretOffset;
        editor.CaretOffset = offset - 1;
        editor.CaretOffset = offset;
        await lsp.DidNotReceive().RequestLinkedEditingRangesAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>());
        await Rest();
        await lsp.Received(1).RequestLinkedEditingRangesAsync(Arg.Any<string>(), new Position(3, 1), Arg.Any<CancellationToken>());
        lsp.ClearReceivedCalls();
        editor.CaretOffset = editor.Document.Text.IndexOf(" = ", StringComparison.Ordinal) + 1;
        await Rest();
        await lsp.DidNotReceive().RequestLinkedEditingRangesAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task CrossingTheCurrentRangeDoesNotMirror()
    {
        var editor = Show();
        await Rest();
        var offset = editor.Document.Text.IndexOf("x =", StringComparison.Ordinal);
        var expected = editor.Text.Remove(offset - 1, 2).Insert(offset - 1, "_");
        editor.Document.Replace(offset - 1, 2, "_");
        editor.Text.ReplaceLineEndings("\n").Should().Be(expected.ReplaceLineEndings("\n"));
        editor.TextArea.TextView.BackgroundRenderers.OfType<HexIDE.Controls.DocumentHighlightRenderer>()
            .SelectMany(renderer => renderer.Segments).Should().BeEmpty();
    }

    [AvaloniaFact]
    public async Task AStaleAnswerCannotStartLinking()
    {
        var editor = Show();
        var answer = new TaskCompletionSource<LinkedEditingRanges?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lsp.RequestLinkedEditingRangesAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>()).Returns(answer.Task);
        await Rest();
        await lsp.Received().RequestLinkedEditingRangesAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>());
        editor.Document.Insert(editor.Document.TextLength, " ");
        lsp.RequestLinkedEditingRangesAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>()).Returns((LinkedEditingRanges?)null);
        answer.SetResult(new LinkedEditingRanges([
            new(new(2, 4), new(2, 5)), new(new(3, 0), new(3, 1)), new(new(3, 4), new(3, 5))]));
        await Rest();
        window!.KeyTextInput("y");
        editor.Text.ReplaceLineEndings("\n").Should().Be(Code.Replace("x =", "xy =") + " ");
    }

    [AvaloniaFact]
    public async Task OffMakesNoRequestAndTypesOnlyAtCaret()
    {
        var editor = Show(enabled: false);
        await Rest();
        window!.KeyTextInput("y");
        await Rest();
        editor.Text.ReplaceLineEndings("\n").Should().Be(Code.Replace("x =", "xy ="));
        await lsp.DidNotReceive().RequestLinkedEditingRangesAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task AProtectedAnswerIsRejectedWithoutATypingRefusalNotice()
    {
        Show(response: "{\"ranges\":[{\"start\":{\"line\":0,\"character\":0},\"end\":{\"line\":0,\"character\":1}},{\"start\":{\"line\":3,\"character\":0},\"end\":{\"line\":3,\"character\":1}}]}");
        statusBar.ClearReceivedCalls();
        await Rest();
        statusBar.DidNotReceive().SetTemporaryMessage(Arg.Any<string>(), Arg.Any<TimeSpan>());
    }

    [AvaloniaFact]
    public async Task UnavailableServerIsNotAsked()
    {
        var editor = Show(available: false);
        await Rest();
        window!.KeyTextInput("y");
        await Rest();
        editor.Text.ReplaceLineEndings("\n").Should().Contain("xy = x");
        await lsp.DidNotReceive().RequestLinkedEditingRangesAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task AnEditRequestWaitsForTheCurrentTextToReachTheServer()
    {
        var editor = Show();
        lsp.RequestLinkedEditingRangesAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns((LinkedEditingRanges?)null);
        await Rest();
        lsp.ClearReceivedCalls();
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lsp.ChangeDocumentAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(sent.Task);
        try
        {
            window!.KeyTextInput("y");
            await Rest();
            await lsp.Received().ChangeDocumentAsync(Arg.Any<string>(), Arg.Any<int>(),
                Arg.Is<string>(text => text.Contains("xy = x", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
            await lsp.DidNotReceive().RequestLinkedEditingRangesAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>());
            sent.SetResult();
            await Rest();
            await lsp.Received().RequestLinkedEditingRangesAsync(Arg.Any<string>(), new Position(3, 2), Arg.Any<CancellationToken>());
        }
        finally { sent.TrySetResult(); }
    }

    [AvaloniaFact]
    public async Task EditAtAWordAsksAgainAfterDebounce()
    {
        var editor = Show();
        lsp.RequestLinkedEditingRangesAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns((LinkedEditingRanges?)null);
        await Rest();
        lsp.ClearReceivedCalls();
        window!.KeyTextInput("y");
        await Rest();
        await lsp.Received().RequestLinkedEditingRangesAsync(Arg.Any<string>(), new Position(3, 2), Arg.Any<CancellationToken>());
    }

    [AvaloniaTheory]
    [InlineData("{\"ranges\":[{\"start\":{\"line\":3,\"character\":0},\"end\":{\"line\":3,\"character\":1}}]}")]
    [InlineData("{\"ranges\":[{\"start\":{\"line\":1,\"character\":0},\"end\":{\"line\":1,\"character\":1}},{\"start\":{\"line\":3,\"character\":0},\"end\":{\"line\":3,\"character\":1}}]}")]
    [InlineData("{\"ranges\":[{\"start\":{\"line\":2,\"character\":4},\"end\":{\"line\":2,\"character\":5}},{\"start\":{\"line\":3,\"character\":4},\"end\":{\"line\":3,\"character\":5}}]}")]
    public async Task UnusableAnswerDoesNotMirror(string response)
    {
        var editor = Show(response: response);
        await Rest();
        editor.TextArea.TextView.BackgroundRenderers.OfType<HexIDE.Controls.DocumentHighlightRenderer>()
            .SelectMany(renderer => renderer.Segments).Should().BeEmpty();
        window!.KeyTextInput("y");
        editor.Text.ReplaceLineEndings("\n").Should().Be(Code.Replace("x =", "xy ="));
    }
}
