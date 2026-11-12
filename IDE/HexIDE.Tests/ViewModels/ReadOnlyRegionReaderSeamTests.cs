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
/// What the code window does with read-only regions a reader other than VB6's reports (#3624), and with those that hang
/// from a line (#3668). The text here has no VB6 in it, so each assertion can only pass by way of the reader.
/// </summary>
public class ReadOnlyRegionReaderSeamTests : IDisposable
{

    private sealed class Rules : IReadOnlyRegionRules
    {
        public string? DescribeRegions(string text, int prefixLength, IReadOnlyList<TextRegion> regions) =>
            "the second heading";
        public bool RefuseRename { get; set; } = true;
        public ContentReplacement Replace(string current, int prefixLength, string incoming) =>
            new(null, "Keep the notes intact.", false);
        public string? RangeEditRefusal(string before, string after, int prefixLength) => "the notes changed";
        public bool RenameMayWrite(string text, int prefixLength, IReadOnlyList<TextChange> changes, TextChange edit) =>
            !RefuseRename;
        public bool HeaderDeclares(string text, int prefixLength, string name) => name == "gamma";
    }

    private sealed class NoRules : IReadOnlyRegionRules { }

    [AvaloniaFact]
    public void ReplacementUsesTheRulesRefusal()
    {
        var vm = NewEditor(null, rules: new Rules()).Initialize(Notes());
        vm.ReplaceContent("new notes").Refusal.Should().Be("Keep the notes intact.");
        vm.Document.Text.Should().Be(Text);
    }

    [AvaloniaFact]
    public void RangeEditUsesTheRulesFindings()
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(c => "⟦" + c.Arg<string>() + "⟧");
        var vm = NewEditor(null, localization, new Rules()).Initialize(Notes());
        vm.ApplyEdits([new TextChange(0, 5, "ALPHA")]).Should().Be(
            "Edits refused: the notes changed. Nothing was changed.");
        vm.Document.Text.Should().Be(Text);
    }

    [AvaloniaFact]
    public void RegionEditUsesTheRulesDescription()
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(c => "⟦" + c.Arg<string>() + "⟧");
        var vm = NewEditor(new FixedLines((2, 2)), localization, new Rules()).Initialize(Notes());
        vm.ApplyEdits([new TextChange(At(vm, "beta"), 1, "B")]).Should().Be(
            "The edit at line 2, column 1 would change the second heading, which only the IDE changes. Nothing was changed.");
        vm.Document.Text.Should().Be(Text);
    }

    [AvaloniaFact]
    public void EmptyRegionsUseReadOnlyTextWithoutAskingTheRules()
    {
        var rules = Substitute.For<IReadOnlyRegionRules>();
        rules.DescribeRegions(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<IReadOnlyList<TextRegion>>())
            .Returns("the second heading");
        var vm = NewEditor(null, rules: rules).Initialize(Notes());

        vm.DescribeRegions([]).Should().Be("read-only text");

        rules.DidNotReceive().DescribeRegions(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<IReadOnlyList<TextRegion>>());
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegionEditWithoutWordsUsesReadOnlyText(bool defaults)
    {
        var vm = NewEditor(new FixedLines((2, 2)), rules: defaults ? new NoRules() : null).Initialize(Notes());
        vm.ApplyEdits([new TextChange(At(vm, "beta"), 1, "B")]).Should().Be(
            "The edit at line 2, column 1 would change read-only text, which only the IDE changes. Nothing was changed.");
        vm.Document.Text.Should().Be(Text);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RenameUsesBothAnswersFromTheRules(bool workspace)
    {
        var rules = new Rules();
        var vm = NewEditor(new FixedLines((2, 2)), new LocalizationService(), rules).Initialize(Notes());
        TextEdit[] edits = [new(new LspRange(new Position(1, 0), new Position(1, 4)), "BETA")];
        string? Rename() => workspace
            ? vm.ApplyRename(new WorkspaceEdit(new Dictionary<string, TextEdit[]> { [vm.GetDocumentUriPublic()] = edits }), "beta", takeFocus: false)
            : vm.ApplyRename(edits, "beta");
        Rename().Should().NotBeNull();
        vm.Document.Text.Should().Be(Text);
        rules.RefuseRename = false;
        Rename().Should().BeNull();
        vm.Document.Text.Should().Be(Text.Replace("beta", "BETA"));
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RenameWithoutPermissionLeavesTheReadersRegionAlone(bool workspace, bool defaults)
    {
        var vm = NewEditor(new FixedLines((2, 2)), new LocalizationService(),
            defaults ? new NoRules() : null).Initialize(Notes());
        TextEdit[] edits = [new(new LspRange(new Position(1, 0), new Position(1, 4)), "BETA")];

        var refusal = workspace
            ? vm.ApplyRename(new WorkspaceEdit(new Dictionary<string, TextEdit[]> { [vm.GetDocumentUriPublic()] = edits }), "beta", takeFocus: false)
            : vm.ApplyRename(edits, "beta");

        refusal.Should().NotBeNull();
        vm.Document.Text.Should().Be(Text);
    }

    [AvaloniaFact]
    public void HeaderDeclarationUsesTheRulesAnswer()
    {
        var vm = NewEditor(null, new LocalizationService(), new Rules()).Initialize(Notes());
        vm.RenameRefusalAt(At(vm, "gamma"), "gamma").Should().NotBeNull();
        vm.RenameRefusalAt(At(vm, "delta"), "delta").Should().BeNull();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingAndDefaultRulesAllowWritesOnVb6Text(bool defaults)
    {
        var module = new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "Notes", ModuleKind.StandardModule);
        module.UpdateCode("Attribute VB_Name = \"Notes\"\r\nPublic Sub Total()\r\nAttribute Total.VB_Description = \"sum\"\r\nEnd Sub\r\n");
        var vm = NewEditor(null, rules: defaults ? new NoRules() : null).Initialize(module);
        vm.RenameRefusalAt(0, "Notes").Should().BeNull();
        vm.ApplyEdits([new TextChange(0, 1, "x")]).Should().BeNull();
        vm.ApplyRename([new TextEdit(new LspRange(new Position(0, 0), new Position(0, 1)), "y")], "x").Should().BeNull();
        vm.ReplaceContent("new notes").Should().Be(new ContentReplacement("new notes", null, false));
        vm.Document.Text.Should().Be("new notes");
    }

    [Fact]
    public void ClosedDocumentUsesTheRulesRefusal()
    {
        var module = Notes();
        DocumentContent.Write(DocumentIdentity.For(module), null, "new notes", new Rules())
            .Refusal.Should().Be("Keep the notes intact.");
        module.Code.Should().Be(Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosedDocumentWithoutRulesAcceptsIncomingText(bool defaults)
    {
        var module = new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "Notes", ModuleKind.StandardModule);
        DocumentContent.Write(DocumentIdentity.For(module), null, "new notes", defaults ? new NoRules() : null)
            .Should().Be(new ContentReplacement("new notes", null, false));
        module.Code.Should().Be("new notes");
    }

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

    private CodeEditorViewModel NewEditor(IReadOnlyRegionReader? reader, ILocalizationService? localization = null, IReadOnlyRegionRules? rules = null)
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
            Substitute.For<HexIDE.Debugging.IRunScope>(), localization ?? Substitute.For<ILocalizationService>(),
            regionReader: reader, regionRules: rules);
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

    // ── Regions that hang from a line (#3668) ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Notes in which lines starting <c>~</c> tuck under the <c>heading</c> line directly above them, and hang from it.
    /// A heading's name is its second word; <c>#</c> starts a comment.
    /// </summary>
    private const string Tucked =
        "title Notes\r\n" +
        "heading One # first\r\n" +
        "  ~ tucked under One\r\n" +
        "  ~ also under One\r\n" +
        "plain line\r\n" +
        "~ under a plain line, so it hangs from nothing\r\n" +
        "heading Two\r\n" +
        "  ~ under Two\r\n" +
        "end\r\n";

    /// <summary>A format whose read-only regions are the tucked lines under a heading, and nothing else.</summary>
    private sealed class Headings : IReadOnlyRegionReader
    {
        public IReadOnlyList<TextRegion> Regions(string text, int prefixLength) =>
            HangingRegions(text, prefixLength).Select(h => h.Region).ToList();

        public IReadOnlyList<HangingRegion> HangingRegions(string text, int prefixLength)
        {
            var lines = LinesOf(text);
            var hanging = new List<HangingRegion>();
            var seen = new Dictionary<string, int>();
            for (var i = 1; i < lines.Count; i++)
            {
                if (!IsTucked(text, lines[i]) || IsTucked(text, lines[i - 1]))
                    continue;
                var above = lines[i - 1];
                if (NameOf(text[above.Start..above.End]) is not { } name)
                    continue;
                var last = i;
                while (last + 1 < lines.Count && IsTucked(text, lines[last + 1]))
                    last++;
                seen.TryGetValue(name, out var ordinal);
                seen[name] = ordinal + 1;
                hanging.Add(new HangingRegion(
                    new TextRegion(lines[i].Start, lines[last].Next, OpenEnded: lines[last].End == text.Length, Anchor: above.End),
                    above.Start, above.End, lines[last].End, new HangingRegionKey(name, ordinal), name));
            }
            return hanging;
        }

        public int CommentStart(string text, HangingRegion region)
        {
            var hash = text.IndexOf('#', region.LineStart, region.Anchor - region.LineStart);
            return hash < 0 ? region.Anchor : hash;
        }

        public bool StartsHangingRegion(ReadOnlySpan<char> line) => line.TrimStart().StartsWith("~");

        public int? LineToHangFrom(string text, HangingRegion region, string inserted)
        {
            foreach (var line in LinesOf(inserted))
            {
                if (NameOf(inserted[line.Start..line.End]) is not null)
                    return line.End;
            }
            return null;
        }

        private static string? NameOf(string line)
        {
            var words = line.Split('#')[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words is ["heading", var name, ..] ? name : null;
        }

        private static bool IsTucked(string text, (int Start, int End, int Next) line) =>
            text.AsSpan(line.Start, line.End - line.Start).TrimStart().StartsWith("~");

        private static List<(int Start, int End, int Next)> LinesOf(string text)
        {
            var lines = new List<(int, int, int)>();
            for (var start = 0; start <= text.Length;)
            {
                var end = text.IndexOfAny(['\r', '\n'], start);
                if (end < 0)
                {
                    lines.Add((start, text.Length, text.Length));
                    break;
                }
                var next = text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? end + 2 : end + 1;
                lines.Add((start, end, next));
                start = next;
            }
            return lines;
        }
    }

    private CodeEditorViewModel OpenTucked(ILocalizationService? localization = null, IReadOnlyRegionRules? rules = null)
    {
        var module = new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "Notes", ModuleKind.StandardModule);
        module.RecordOriginalHeader("");
        module.UpdateCode(Tucked);
        var vm = NewEditor(new Headings(), localization, rules).Initialize(module);
        vm.Document.Text.Should().Be(Tucked, "the premise: nothing VB6 is in the buffer");
        return vm;
    }

    private static HangingRegion RegionNamed(CodeEditorViewModel vm, string name) =>
        vm.HangingRegionsNow.Single(h => h.Name == name);

    private static int EndOf(CodeEditorViewModel vm, string text) => At(vm, text) + text.Length;

    [AvaloniaFact]
    public void TheWindowsHangingRegionsAreTheReadersAndEachIsOneOfItsRegions()
    {
        var vm = OpenTucked();

        vm.HangingRegionsNow.Select(h => (h.Name, h.Key)).Should().Equal(
            ("One", new HangingRegionKey("One", 0)), ("Two", new HangingRegionKey("Two", 0)));
        var one = RegionNamed(vm, "One");
        one.LineStart.Should().Be(At(vm, "heading One"));
        one.Anchor.Should().Be(EndOf(vm, "heading One # first"));
        one.LastContentEnd.Should().Be(EndOf(vm, "also under One"));
        vm.ReadOnlyRegionsNow.Should().Equal(vm.HangingRegionsNow.Select(h => h.Region),
            "every hanging region is one of the reader's regions, and the line under a plain line is neither");
    }

    [AvaloniaFact]
    public void AHangingRegionFoldsIntoTheLineItHangsFrom()
    {
        var vm = OpenTucked();

        var folds = Vb6FoldingAdapter.HangingRegions(vm.HangingRegionsNow);

        folds.Select(f => (f.StartOffset, f.EndOffset)).Should().Equal(
            (EndOf(vm, "heading One # first"), EndOf(vm, "also under One")),
            (EndOf(vm, "heading Two"), EndOf(vm, "under Two")));
    }

    [AvaloniaFact]
    public void ALineBreakAtTheEndOfTheLineGoesPastItsRegion()
    {
        var vm = OpenTucked();
        var before = vm.Document.Text;

        var placed = vm.InsertLineBreakPastRun(EndOf(vm, "heading Two"), 0, "\r\nnew line");

        placed.Should().NotBeNull();
        vm.Document.Text.Should().Be(before.Replace("  ~ under Two\r\n", "  ~ under Two\r\nnew line\r\n"));
        RegionNamed(vm, "Two").LineStart.Should().Be(At(vm, "heading Two"), "the region still hangs from its heading");
    }

    [AvaloniaFact]
    public void DeletingTheWholeLineTakesItsRegionWithIt()
    {
        var vm = OpenTucked();
        var two = RegionNamed(vm, "Two");

        var (offset, length) = vm.ReadOnlySections.WidenDeletion(two.LineStart, two.Anchor - two.LineStart);

        (offset, offset + length).Should().Be((two.LineStart, two.LastContentEnd));
        vm.ReadOnlySections.GetDeletableSegments(new SimpleSegment(offset, length))
            .Select(s => (s.Offset, s.EndOffset)).Should().Equal((offset, offset + length));
        vm.ReadOnlySections.DeletionChangesARegion(two.LineStart, two.Anchor - two.LineStart).Should().BeFalse();
    }

    [AvaloniaFact]
    public void AnEditThatWouldLeaveARegionUnderAnotherLineIsRefusedByTheRegionsName()
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(c => "⟦" + c.Arg<string>() + "⟧");
        var vm = OpenTucked(localization, new Rules());
        var before = vm.Document.Text;

        var refusal = vm.ApplyEdits([new HexIDE.Language.TextChange(EndOf(vm, "heading Two"), 0, "\r\nx")]);

        refusal.Should().Be("The edit at line 7, column 12 would put a line break in the line above the second heading or in that line's trailing comment, parting the line from the read-only text below it. Insert new lines after that read-only text instead. Nothing was changed.");
        vm.Document.Text.Should().Be(before);
    }

    [AvaloniaFact]
    public void ALineBreakBeforeTheReadersCommentTakesTheCommentPastTheRegion()
    {
        var vm = OpenTucked();
        var at = At(vm, "# first");

        var placed = vm.InsertLineBreakPastRun(at, 0, "\r\n");

        placed.Should().NotBeNull();
        vm.Document.Text.Should().Contain("heading One \r\n  ~ tucked under One\r\n  ~ also under One\r\n# first\r\nplain line");
        RegionNamed(vm, "One").Anchor.Should().Be(EndOf(vm, "heading One "));
    }

    [AvaloniaFact]
    public void PastedTextWhoseSecondLineStartsARegionGoesWholePastTheRegion()
    {
        var vm = OpenTucked();

        vm.InsertLineBreakPastRun(EndOf(vm, "heading Two"), 0, "heading Three\r\n  ~ under Three\r\n");

        vm.Document.Text.Should().EndWith("heading Two\r\n  ~ under Two\r\nheading Three\r\n  ~ under Three\r\nend\r\n");
        vm.HangingRegionsNow.Select(h => h.Name).Should().Equal("One", "Two", "Three");
    }

    [AvaloniaFact]
    public void AWholeLineReplacedKeepsItsRegionUnderTheLineTheReaderPicks()
    {
        var vm = OpenTucked();
        var two = RegionNamed(vm, "Two");

        vm.InsertLineBreakPastRun(two.LineStart, two.Anchor - two.LineStart, "a note\r\nheading Deux\r\nafter");

        vm.Document.Text.Should().EndWith("a note\r\nheading Deux\r\n  ~ under Two\r\nafter\r\nend\r\n");
        RegionNamed(vm, "Deux").Key.Should().Be(new HangingRegionKey("Deux", 0));
    }

    [AvaloniaFact]
    public void AReaderWithNoHangingRegionsLeavesTheWindowWithNone()
    {
        OpenTopAndEpsilon(this).HangingRegionsNow.Should().BeEmpty("6a's reader implements Regions alone");
        NewEditor(null).Initialize(Notes()).HangingRegionsNow.Should().BeEmpty();
    }
}
