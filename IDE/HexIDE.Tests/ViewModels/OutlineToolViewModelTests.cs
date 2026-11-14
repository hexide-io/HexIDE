using HexIDE.Runtime.Serialization;
using Avalonia.Threading;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Events;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Tools;
using Range = HexIDE.Lsp.Messages.Range;

namespace HexIDE.Tests.ViewModels;

public class OutlineToolViewModelTests
{
    private static DocumentSymbol Symbol(string name, SymbolKind kind, int start, int end,
        params DocumentSymbol[] children) => new(name, kind,
            new Range(new Position(start, 0), new Position(end, 0)),
            new Range(new Position(start, 2), new Position(start, 5)), Children: children);

    [Fact]
    public void Tree_PreservesNestingAndFlatReplies()
    {
        var member = Symbol("Value", SymbolKind.Field, 1, 2);
        var type = Symbol("Record", SymbolKind.Struct, 0, 3, member);
        var roots = OutlineToolViewModel.Build([type], null);
        roots.Should().ContainSingle().Which.Children.Should().ContainSingle().Which.Symbol.Should().Be(member);
        OutlineToolViewModel.Build([type with { Children = null }, member], null)
            .Select(n => n.Name).Should().Equal("Record", "Value");
    }

    [Fact]
    public void Filter_KeepsAncestorsOfMatchingDescendants()
    {
        var type = Symbol("Record", SymbolKind.Struct, 0, 4,
            Symbol("Value", SymbolKind.Field, 1, 2), Symbol("Run", SymbolKind.Method, 2, 3));
        var roots = OutlineToolViewModel.Build([type, Symbol("Other", SymbolKind.Function, 5, 6)], SymbolKind.Field);
        roots.Should().ContainSingle().Which.Children.Select(n => n.Name).Should().Equal("Value");
        OutlineToolViewModel.Build([type], SymbolKind.Enum).Should().BeEmpty();
    }

    [Fact]
    public void Caret_FindsInnermostNodeAndExcludesRangeEnd()
    {
        var roots = OutlineToolViewModel.Build([Symbol("Record", SymbolKind.Struct, 0, 4,
            Symbol("Value", SymbolKind.Field, 1, 2))], null);
        OutlineToolViewModel.FindAt(roots, new Position(1, 3))!.Name.Should().Be("Value");
        OutlineToolViewModel.FindAt(roots, new Position(2, 0))!.Name.Should().Be("Record");
        OutlineToolViewModel.FindAt(roots, new Position(4, 0)).Should().BeNull();
    }

    [AvaloniaFact]
    public async Task OpenDiagnostics_FillsProcedureListAndOutlineWithoutSendingChanges()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var lsp = Substitute.For<ILspClient>();
        lsp.IsRunning.Returns(true);
        var symbol = Symbol("Run", SymbolKind.Function, 0, 1);
        lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([symbol]);
        using var editor = CreateEditor(lsp);
        var documents = Substitute.For<IDocumentDockService>();
        documents.ActiveDocument.Returns(editor);
        using var outline = new OutlineToolViewModel(Substitute.For<ILocalizationService>(), documents);
        var uri = editor.GetDocumentUriPublic();
        lsp.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(lsp,
            new PublishDiagnosticsParams(uri, []));
        // Bound the observation even if a regression keeps posting diagnostics forever.
        var until = System.Diagnostics.Stopwatch.StartNew();
        while (until.Elapsed < TimeSpan.FromSeconds(1))
        {
            await Task.Delay(20, cancellationToken);
            Dispatcher.UIThread.RunJobs();
        }
        await lsp.Received(1).RequestDocumentSymbolsAsync(uri, Arg.Any<CancellationToken>());
        await lsp.DidNotReceive().ChangeDocumentAsync(Arg.Any<string>(), Arg.Any<int>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        editor.ProcedureNames.Should().Contain("Run");
        outline.Roots.Select(n => n.Name).Should().Equal("Run");
    }

    [AvaloniaFact]
    public async Task Refresh_FlushesUnsentEditBeforeSymbolsAndDoesNotResendIt()
    {
        var lsp = Substitute.For<ILspClient>();
        lsp.IsRunning.Returns(true);
        using var editor = CreateEditor(lsp);
        editor.Document.Insert(editor.Document.TextLength, "' edit");
        var text = editor.Document.Text;
        lsp.ClearReceivedCalls();

        await editor.RefreshSymbolsAsync();
        await editor.RefreshSymbolsAsync();

        Received.InOrder(() =>
        {
            _ = lsp.ChangeDocumentAsync(editor.GetDocumentUriPublic(), Arg.Any<int>(), text, Arg.Any<CancellationToken>());
            _ = lsp.RequestDocumentSymbolsAsync(editor.GetDocumentUriPublic(), Arg.Any<CancellationToken>());
            _ = lsp.RequestDocumentSymbolsAsync(editor.GetDocumentUriPublic(), Arg.Any<CancellationToken>());
        });
        await lsp.Received(1).ChangeDocumentAsync(editor.GetDocumentUriPublic(), Arg.Any<int>(), text,
            Arg.Any<CancellationToken>());
    }

    private static CodeEditorViewModel CreateEditor(ILspClient lsp) =>
        new CodeEditorViewModel(Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(), Substitute.For<IEventBus>(), lsp,
            Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(),
            Substitute.For<HexIDE.Bookmarks.IBookmarkService>(), Substitute.For<HexIDE.Debugging.IBreakpointService>(),
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(), Substitute.For<HexIDE.Debugging.IRunScope>(),
            Substitute.For<ILocalizationService>(), new Vb6ProjectFileFormat(), new Vb6ReadOnlyRegionReader()).Initialize(TestHelpers.CreateForm());

    [AvaloniaFact]
    public async Task ActiveDocument_RefreshesFollowsCaretNavigatesAndClears()
    {
        var lsp = Substitute.For<ILspClient>();
        lsp.IsRunning.Returns(true);
        using var editor = new CodeEditorViewModel(Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(), Substitute.For<IEventBus>(), lsp,
            Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(),
            Substitute.For<HexIDE.Bookmarks.IBookmarkService>(), Substitute.For<HexIDE.Debugging.IBreakpointService>(),
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(), Substitute.For<HexIDE.Debugging.IRunScope>(),
            Substitute.For<ILocalizationService>(), new Vb6ProjectFileFormat(), new Vb6ReadOnlyRegionReader()).Initialize(TestHelpers.CreateForm());
        var documents = Substitute.For<IDocumentDockService>();
        documents.ActiveDocument.Returns(editor);
        using var sut = new OutlineToolViewModel(Substitute.For<ILocalizationService>(), documents);
        var first = Symbol("First", SymbolKind.Function, 0, 1);
        lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([first]);
        editor.Document.Text = "First()\nSecond()\n";
        await editor.RefreshSymbolsAsync();
        Dispatcher.UIThread.RunJobs();
        sut.Roots.Select(n => n.Name).Should().Equal("First");
        editor.CaretOffset = 1;
        sut.SelectedNode!.Name.Should().Be("First");
        editor.CaretOffset = editor.Document.TextLength;
        sut.SelectedNode.Should().BeNull();
        var focused = 0;
        editor.FocusWindowRequest += () => focused++;
        sut.Navigate(sut.Roots[0]);
        editor.CaretOffset.Should().Be(2);
        focused.Should().Be(1);
        var second = Symbol("Second", SymbolKind.Method, 1, 2);
        lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([second]);
        editor.Document.Insert(editor.Document.TextLength, "' edit");
        await editor.RefreshSymbolsAsync();
        Dispatcher.UIThread.RunJobs();
        sut.Roots.Select(n => n.Name).Should().Equal("Second");
        documents.ActiveDocument.Returns((BaseEditorWindowViewModel?)null);
        documents.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(documents,
            new PropertyChangedEventArgs(nameof(IDocumentDockService.ActiveDocument)));
        sut.Roots.Should().BeEmpty();
        editor.CaretOffset = 0;
        sut.SelectedNode.Should().BeNull();
    }
    [AvaloniaFact]
    public async Task CarriedFile_RefreshesAfterEditWithoutDiagnosticsAndSwitchesDocuments()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = Path.Combine(Path.GetTempPath(), "hexide-outline-" + Guid.NewGuid() + ".txt");
        await File.WriteAllTextAsync(path, "One()\nTwo()\n", cancellationToken);
        try
        {
            var lsp = Substitute.For<ILspClient>();
            lsp.IsRunning.Returns(true);
            var localization = Substitute.For<ILocalizationService>();
            using var editor = new RelatedDocumentEditorViewModel(lsp, Substitute.For<IWindowManager>(), localization, Substitute.For<HexIDE.IDE.ISettingsService>())
                .Initialize(new HexIDE.Runtime.ProjectElements.RelatedDocumentDefinition(
                    TestHelpers.CreateProject("P"), "outline.txt", path));
            var documents = Substitute.For<IDocumentDockService>();
            documents.ActiveDocument.Returns(editor);
            using var sut = new OutlineToolViewModel(localization, documents);
            var first = Symbol("One", SymbolKind.Function, 0, 1);
            lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([first]);
            await ((IOutlineDocument)editor).RefreshSymbolsAsync();
            Dispatcher.UIThread.RunJobs();
            sut.Roots.Select(n => n.Name).Should().Equal("One");
            sut.Navigate(sut.Roots[0]);
            editor.CaretOffset.Should().Be(2);
            var second = Symbol("Two", SymbolKind.Method, 1, 2);
            lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([second]);
            editor.Document.Insert(editor.Document.TextLength, "' edit");
            // The refresh is debounced: poll for it, bounded, rather than sleeping a fixed time a loaded machine can miss.
            for (var i = 0; i < 100 && sut.Roots.FirstOrDefault()?.Name != "Two"; i++)
            {
                await Task.Delay(50, cancellationToken);
                Dispatcher.UIThread.RunJobs();
            }
            sut.Roots.Select(n => n.Name).Should().Equal("Two");
            editor.CaretOffset = editor.Document.GetLineByNumber(2).Offset;
            sut.SelectedNode!.Name.Should().Be("Two");
            sut.SelectedKind = sut.Kinds.Single(k => k.Kind == SymbolKind.Method);
            sut.Roots.Should().ContainSingle();
            using var other = new RelatedDocumentEditorViewModel(lsp, Substitute.For<IWindowManager>(), localization, Substitute.For<HexIDE.IDE.ISettingsService>());
            documents.ActiveDocument.Returns(other);
            documents.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(documents,
                new PropertyChangedEventArgs(nameof(IDocumentDockService.ActiveDocument)));
            sut.Roots.Should().BeEmpty();
            editor.CaretOffset = 0;
            sut.SelectedNode.Should().BeNull();
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public async Task CarriedFile_DiscardsReplyForOldBuffer()
    {
        var path = Path.Combine(Path.GetTempPath(), "hexide-outline-" + Guid.NewGuid() + ".txt");
        await File.WriteAllTextAsync(path, "One()", TestContext.Current.CancellationToken);
        try
        {
            var lsp = Substitute.For<ILspClient>();
            var pending = new TaskCompletionSource<DocumentSymbol[]>();
            lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(pending.Task);
            using var editor = new RelatedDocumentEditorViewModel(lsp, Substitute.For<IWindowManager>(),
                Substitute.For<ILocalizationService>(), Substitute.For<HexIDE.IDE.ISettingsService>()).Initialize(new HexIDE.Runtime.ProjectElements.RelatedDocumentDefinition(
                    TestHelpers.CreateProject("P"), "outline.txt", path));
            var request = ((IOutlineDocument)editor).RefreshSymbolsAsync();
            editor.Document.Insert(0, "' newer\n");
            pending.SetResult([Symbol("Old", SymbolKind.Function, 0, 1)]);
            await request;
            Dispatcher.UIThread.RunJobs();
            editor.OutlineSymbols.Should().BeEmpty();
        }
        finally { File.Delete(path); }
    }

}
