using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Folding;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Language;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A real code window showing read-only regions that a reader other than VB6's reports (#3624): the first folds as the
/// header and every one is drawn in the read-only colour, on text with no VB6 in it.
/// </summary>
public class ReadOnlyRegionReaderViewTests : IDisposable
{
    private static readonly Color ClassicGrey = Color.Parse("#707070");

    private readonly List<Window> windows = [];
    private readonly List<CodeEditorViewModel> made = [];
    private readonly ILspClient lsp = Substitute.For<ILspClient>();

    public ReadOnlyRegionReaderViewTests()
    {
        lsp.IsRunning.Returns(true);
        lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<DocumentSymbol>());
        lsp.RequestFoldingRangesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<FoldingRange>());
    }

    public void Dispose()
    {
        foreach (var w in windows) w.Close();
        foreach (var vm in made) vm.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Reports the 1-based line ranges it was given, whatever the text says.</summary>
    private sealed class FixedLines(params (int First, int Last)[] ranges) : IReadOnlyRegionReader
    {
        public IReadOnlyList<TextRegion> Regions(string text, int prefixLength)
        {
            var starts = new List<int> { 0 };
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                    starts.Add(i + 1);
            }
            return ranges.Select(r => new TextRegion(starts[r.First - 1], starts[r.Last])).ToList();
        }
    }

    private TextEditor Show(IReadOnlyRegionReader reader)
    {
        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe<CreateOrNavigateToSubEvent>(Arg.Any<Action<CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(Arg.Any<Action<ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<FormUnloadedEvent>(Arg.Any<Action<FormUnloadedEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<DocumentSavedEvent>(Arg.Any<Action<DocumentSavedEvent>>()).Returns(Substitute.For<IDisposable>());
        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);
        var vm = new CodeEditorViewModel(
            Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(), Substitute.For<IProjectService>(),
            eventBus, lsp, settings, Substitute.For<IStatusBarService>(), new BookmarkService(), new BreakpointService(),
            Substitute.For<IDebugController>(), new RunScope(), Substitute.For<ILocalizationService>(),
            regionReader: reader);
        made.Add(vm);

        var module = new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "Notes", ModuleKind.StandardModule);
        module.RecordOriginalHeader("");
        module.UpdateCode("alpha\r\nbeta\r\ngamma\r\ndelta\r\nepsilon\r\nzeta\r\n");
        vm.Initialize(module);

        var window = new Window { Width = 1200, Height = 800 };
        windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var view = new CodeEditorView { DataContext = vm };
        window.Content = view;
        Dispatcher.UIThread.RunJobs();
        return view.FindControl<TextEditor>("TextEditor")!;
    }

    private static List<Color?> ColoursOn(TextEditor editor, int line) =>
        editor.TextArea.TextView.GetOrConstructVisualLine(editor.Document.GetLineByNumber(line)).Elements
            .Where(e => e.DocumentLength > 0)
            .Select(e => (e.TextRunProperties.ForegroundBrush as ISolidColorBrush)?.Color)
            .ToList();

    [AvaloniaFact]
    public void TheReadersFirstRegionOpensFoldedAsTheHeader()
    {
        var editor = Show(new FixedLines((1, 2), (5, 5)));

        var folding = (FoldingManager)editor.TextArea.TextView.GetService(typeof(FoldingManager))!;
        var header = folding.GetFoldingsAt(0).Single();

        header.IsFolded.Should().BeTrue();
        header.EndOffset.Should().Be(editor.Document.GetLineByNumber(2).EndOffset);
    }

    [AvaloniaFact]
    public void TheReadersRegionsAreDrawnInTheReadOnlyColour()
    {
        var editor = Show(new FixedLines((1, 2), (5, 5)));

        ColoursOn(editor, 5).Should().NotBeEmpty().And.OnlyContain(c => c == ClassicGrey);
        ColoursOn(editor, 3).Should().NotBeEmpty().And.Contain(c => c != ClassicGrey);
    }
}
