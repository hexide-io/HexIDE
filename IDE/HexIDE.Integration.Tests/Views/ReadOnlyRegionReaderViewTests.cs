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
using HexIDE.Automation;
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
            // The view also queries regions before the fixture's document is bound, on an empty buffer.
            // Omit ranges absent from that buffer; the tests below assert them on the bound document.
            return ranges.Where(r => r.Last < starts.Count)
                .Select(r => new TextRegion(starts[r.First - 1], starts[r.Last])).ToList();
        }
    }

    private TextEditor Show(IReadOnlyRegionReader reader, IReadOnlyRegionRules? rules = null)
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
            regionReader: reader, regionRules: rules);
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

    private sealed class Words : IReadOnlyRegionRules
    {
        public string? DescribeRegions(string text, int prefixLength, IReadOnlyList<TextRegion> regions)
        {
            text.Should().StartWith("alpha");
            prefixLength.Should().Be(0);
            regions.Should().Equal(new TextRegion(7, 13), new TextRegion(27, 36));
            return "the second and fifth headings";
        }
    }

    private sealed class NoWords : IReadOnlyRegionRules;

    private static string ExpectedWords(int provider) => provider == 0 ? "the second and fifth headings" : "read-only text";
    private TextEditor ShowWords(int provider) => Show(new FixedLines((2, 2), (5, 5)),
        provider == 0 ? new Words() : provider == 1 ? new NoWords() : null);

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CaretNoteUsesTheFormatsWords(int provider)
    {
        var single = Show(new FixedLines((2, 2)), provider == 0 ? new SingleWords() : provider == 1 ? new NoWords() : null);
        single.CaretOffset = 8;
        var note = CaretPlacement.Where((CodeEditorViewModel)single.DataContext!, 2, 2);
        note.Should().Contain("That is in " + (provider == 0 ? "the second heading" : "read-only text"));
        note.Should().NotContain("Attribute").And.NotContain("header").And.NotContain("designer").And.NotContain("VB6");
    }

    private sealed class SingleWords : IReadOnlyRegionRules
    {
        public string? DescribeRegions(string text, int prefixLength, IReadOnlyList<TextRegion> regions)
        {
            regions.Select(r => (r.Start, r.End)).Should().Equal((7, 13));
            return "the second heading";
        }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TypingRefusalUsesTheFormatsWords(int provider)
    {
        var editor = Show(new FixedLines((2, 2)), provider == 0 ? new SingleWords() : provider == 1 ? new NoWords() : null);
        editor.CaretOffset = 8;
        var result = UiAutomationDriver.PressKey(editor, "X", null);
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("is in " + (provider == 0 ? "the second heading" : "read-only text"));
        result.Error.Should()
            .NotContain("Attribute").And.NotContain("header").And.NotContain("designer").And.NotContain("VB6");
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void PartialSelectionUsesTheFormatsWordsForTheSet(int provider)
    {
        var editor = ShowWords(provider);
        editor.Select(8, 31);
        var result = UiAutomationDriver.PressKey(editor, "Delete", null);
        result.Success.Should().BeTrue(result.Error);
        result.Detail.Should().Contain("in " + ExpectedWords(provider) + ", which only the IDE changes");
        result.Detail.Should().NotContain("Attribute").And.NotContain("header").And.NotContain("designer").And.NotContain("VB6");
    }

    private sealed class HangingHeading : IReadOnlyRegionReader
    {
        public IReadOnlyList<TextRegion> Regions(string text, int prefixLength) =>
            HangingRegions(text, prefixLength).Select(h => h.Region).ToList();

        public IReadOnlyList<HangingRegion> HangingRegions(string text, int prefixLength) =>
            text.Length < 20 ? [] :
            [new(new TextRegion(7, 13, Anchor: 5), 0, 5, 11, new HangingRegionKey("One", 0), "One")];
    }

    [AvaloniaFact]
    public void LineBreakNoteUsesTheFormatsSingularPhrase()
    {
        var editor = Show(new HangingHeading(), new SingleWords());
        editor.CaretOffset = 5;

        var result = UiAutomationDriver.TypeText(editor, "\r\nx");

        result.Success.Should().BeTrue(result.Error);
        result.Detail.Should().Contain("The edit ended on the line above the second heading");
        result.Detail.Should().NotContain("Attribute").And.NotContain("header").And.NotContain("designer").And.NotContain("VB6");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeletionHintUsesTheRegionsAnchor(bool hanging)
    {
        var editor = Show(hanging ? new HangingHeading() : new FixedLines((2, 2)), new SingleWords());
        editor.CaretOffset = 13;

        var result = UiAutomationDriver.PressKey(editor, "Back", null);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("the second heading").And.Contain(hanging
            ? "Deleting the whole of the line above that read-only text"
            : "Edit after that read-only text instead.");
        result.Error.Should().NotContain("Attribute").And.NotContain("header").And.NotContain("designer").And.NotContain("VB6");
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
