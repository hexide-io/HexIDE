using AvaloniaEdit.Document;
using HexIDE.Bookmarks;
using HexIDE.Controls;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Language;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using LspRange = HexIDE.Lsp.Messages.Range;

namespace HexIDE.Tests.ViewModels;

/// <summary>
/// What the code window does with read-only regions a reader other than VB6's reports (#3624). The text here has no VB6
/// in it, so each assertion can only pass by way of the reader.
/// </summary>
public class ReadOnlyRegionReaderSeamTests : IDisposable
{
    private const string Text = "alpha\r\nbeta\r\ngamma\r\ndelta\r\nepsilon\r\nzeta\r\n";

    private readonly List<CodeEditorViewModel> made = [];

    public void Dispose()
    {
        foreach (var vm in made) vm.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Reports the 1-based line ranges it was given, whatever the text says.</summary>
    private sealed class FixedLines(params (int First, int Last)[] ranges) : IReadOnlyRegionReader
    {
        public int Reads { get; private set; }

        public IReadOnlyList<TextRegion> Regions(string text, int prefixLength)
        {
            Reads++;
            var starts = new List<int> { 0 };
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                    starts.Add(i + 1);
            }
            return ranges.Select(r => new TextRegion(starts[r.First - 1], starts[r.Last])).ToList();
        }
    }

    private CodeEditorViewModel NewEditor(IReadOnlyRegionReader? reader)
    {
        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe<CreateOrNavigateToSubEvent>(Arg.Any<Action<CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(Arg.Any<Action<ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<FormUnloadedEvent>(Arg.Any<Action<FormUnloadedEvent>>()).Returns(Substitute.For<IDisposable>());
        var vm = new CodeEditorViewModel(
            Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(), eventBus, Substitute.For<ILspClient>(),
            Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(),
            Substitute.For<IBookmarkService>(), Substitute.For<HexIDE.Debugging.IBreakpointService>(),
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            Substitute.For<HexIDE.Debugging.IRunScope>(), Substitute.For<ILocalizationService>(),
            regionReader: reader);
        made.Add(vm);
        return vm;
    }

    /// <summary>A module read from a file with nothing split off, so the buffer is <see cref="Text"/> and nothing else.</summary>
    private static ModuleDefinition Notes()
    {
        var module = new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "Notes", ModuleKind.StandardModule);
        module.RecordOriginalHeader("");
        module.UpdateCode(Text);
        return module;
    }

    private CodeEditorViewModel Open(IReadOnlyRegionReader? reader) => NewEditor(reader).Initialize(Notes());

    private static CodeEditorViewModel OpenTopAndEpsilon(ReadOnlyRegionReaderSeamTests tests) =>
        tests.Open(new FixedLines((1, 2), (5, 5)));

    private static int At(CodeEditorViewModel vm, string text) => vm.Document.Text.IndexOf(text, StringComparison.Ordinal);

    [AvaloniaFact]
    public void TheWindowsRegionsAreTheReadersAnswer()
    {
        var vm = OpenTopAndEpsilon(this);

        vm.Document.Text.Should().Be(Text, "the premise: nothing VB6 is in the buffer");
        vm.ReadOnlyRegionsNow.Should().Equal(
            new TextRegion(0, At(vm, "gamma")),
            new TextRegion(At(vm, "epsilon"), At(vm, "zeta")));
    }

    [AvaloniaFact]
    public void TypingIsRefusedInARegionAndAllowedOutsideOne()
    {
        var vm = OpenTopAndEpsilon(this);

        vm.ReadOnlySections.CanInsert(At(vm, "beta")).Should().BeFalse();
        vm.ReadOnlySections.CanInsert(At(vm, "gamma")).Should().BeTrue("a region's end is the start of the next line");
        vm.ReadOnlySections.CanInsert(At(vm, "epsilon") + 2).Should().BeFalse();
        vm.ReadOnlySections.CanInsert(At(vm, "zeta")).Should().BeTrue();
    }

    [AvaloniaFact]
    public void ADeletionLeavesARegionsLines()
    {
        var vm = OpenTopAndEpsilon(this);

        var deletable = vm.ReadOnlySections.GetDeletableSegments(
                new SimpleSegment(At(vm, "gamma"), At(vm, "zeta") - At(vm, "gamma")))
            .Select(s => (s.Offset, s.EndOffset)).ToList();

        deletable.Should().Equal((At(vm, "gamma"), At(vm, "epsilon")));
    }

    [AvaloniaFact]
    public void TheGuardedWritersAskTheReader()
    {
        var vm = OpenTopAndEpsilon(this);

        vm.IsReadOnlyRegion(At(vm, "alpha"), 1).Should().BeTrue();
        vm.IsReadOnlyRegion(At(vm, "delta"), 1).Should().BeFalse();
        vm.RefusesTyping(At(vm, "epsilon"), 1).Should().BeTrue();
        vm.SnapshotReadOnlyRegions()(At(vm, "beta"), 1).Should().BeTrue();
        vm.SnapshotReadOnlyRegions()(At(vm, "zeta"), 1).Should().BeFalse();
    }

    [AvaloniaFact]
    public void TheFirstRegionIsTheHeadersFold()
    {
        var vm = OpenTopAndEpsilon(this);

        var fold = Vb6FoldingAdapter.Header(vm.Document, vm.ReadOnlyRegionsNow);

        fold.Should().NotBeNull();
        fold!.StartOffset.Should().Be(0);
        fold.EndOffset.Should().Be(vm.Document.GetLineByNumber(2).EndOffset);
    }

    [AvaloniaFact]
    public void AMarkIsRefusedOnARegionsLine()
    {
        var vm = OpenTopAndEpsilon(this);

        vm.ToggleBookmark(2).Should().BeFalse();
        vm.ToggleBreakpoint(5).Should().BeFalse();
        vm.ToggleBookmark(3).Should().BeTrue();
        vm.ToggleBreakpoint(6).Should().BeTrue();
    }

    [AvaloniaFact]
    public void FormattingLeavesARegionAlone()
    {
        var vm = OpenTopAndEpsilon(this);
        var whole = new LspRange(new Position(0, 0), new Position(6, 0));

        var reduction = vm.ApplyFormatting([new TextEdit(whole, Text.ToUpperInvariant())]);

        vm.Document.Text.Should().Be("alpha\r\nbeta\r\nGAMMA\r\nDELTA\r\nepsilon\r\nZETA\r\n");
        reduction.DroppedLines.Should().Be(3);
    }

    [AvaloniaFact]
    public void AnUnopenedDocumentIsGuardedByTheReader()
    {
        var module = Notes();
        module.Owner.AddModule(module);
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([module.Owner]);
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns(new List<BaseEditorWindowViewModel>());
        var documents = new FindProjectDocuments(dock, projects, Substitute.For<IEditorService>(), () => module.Owner,
            new FixedLines((2, 2)));

        var document = documents.GetDocuments(allProjects: true).Single();

        document.IsReadOnlyRegion(Text.IndexOf("beta", StringComparison.Ordinal), 1).Should().BeTrue();
        document.IsReadOnlyRegion(Text.IndexOf("alpha", StringComparison.Ordinal), 1).Should().BeFalse();
    }

    [AvaloniaFact]
    public void WithNoReaderNothingIsReadOnlyEvenAVb6Header()
    {
        var module = new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "Notes", ModuleKind.StandardModule);
        module.UpdateCode("Option Explicit\r\n");

        var vm = NewEditor(null).Initialize(module);

        vm.Document.Text.Should().StartWith("Attribute VB_Name", "the premise: the buffer opens with a VB6 header");
        vm.ReadOnlyRegionsNow.Should().BeEmpty();
        vm.ReadOnlySections.CanInsert(0).Should().BeTrue();
        vm.IsReadOnlyRegion(0, 1).Should().BeFalse();
        vm.ToggleBookmark(1).Should().BeTrue();
    }

    [AvaloniaFact]
    public void TheReaderIsAskedOncePerVersionOfTheBuffer()
    {
        var reader = new FixedLines((1, 1));
        var vm = Open(reader);
        _ = vm.ReadOnlyRegionsNow;
        var once = reader.Reads;

        _ = vm.ReadOnlyRegionsNow;
        vm.ReadOnlySections.CanInsert(At(vm, "gamma"));
        vm.IsReadOnlyRegion(At(vm, "alpha"), 1);
        reader.Reads.Should().Be(once, "the answer is kept for the buffer as it stands");

        vm.Document.Insert(At(vm, "gamma"), "x");
        _ = vm.ReadOnlyRegionsNow;
        reader.Reads.Should().Be(once + 1);
    }
}
