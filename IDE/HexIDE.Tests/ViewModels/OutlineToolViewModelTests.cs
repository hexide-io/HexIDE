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
    public async Task ActiveDocument_RefreshesFollowsCaretNavigatesAndClears()
    {
        var lsp = Substitute.For<ILspClient>();
        lsp.IsRunning.Returns(true);
        using var editor = new CodeEditorViewModel(Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(), Substitute.For<IEventBus>(), lsp,
            Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(),
            Substitute.For<HexIDE.Bookmarks.IBookmarkService>(), Substitute.For<HexIDE.Debugging.IBreakpointService>(),
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(), Substitute.For<HexIDE.Debugging.IRunScope>(),
            Substitute.For<ILocalizationService>()).Initialize(TestHelpers.CreateForm());
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
        var path = Path.Combine(Path.GetTempPath(), "hexide-outline-" + Guid.NewGuid() + ".txt");
        await File.WriteAllTextAsync(path, "One()\nTwo()\n", TestContext.Current.CancellationToken);
        try
        {
            var lsp = Substitute.For<ILspClient>();
            lsp.IsRunning.Returns(true);
            var localization = Substitute.For<ILocalizationService>();
            using var editor = new RelatedDocumentEditorViewModel(lsp, Substitute.For<IWindowManager>(), localization)
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
                await Task.Delay(50, TestContext.Current.CancellationToken);
                Dispatcher.UIThread.RunJobs();
            }
            sut.Roots.Select(n => n.Name).Should().Equal("Two");
            editor.CaretOffset = editor.Document.GetLineByNumber(2).Offset;
            sut.SelectedNode!.Name.Should().Be("Two");
            sut.SelectedKind = sut.Kinds.Single(k => k.Kind == SymbolKind.Method);
            sut.Roots.Should().ContainSingle();
            using var other = new RelatedDocumentEditorViewModel(lsp, Substitute.For<IWindowManager>(), localization);
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
                Substitute.For<ILocalizationService>()).Initialize(new HexIDE.Runtime.ProjectElements.RelatedDocumentDefinition(
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
