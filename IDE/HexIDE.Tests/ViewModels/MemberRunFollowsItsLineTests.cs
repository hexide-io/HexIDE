using AvaloniaEdit.Document;
using HexIDE.Bookmarks;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.ViewModels;

/// <summary>
/// A member's attribute run follows the line it describes (hexide-io/HexIDE#273 tasks 4.5 and 4.5a): a line break at
/// that line's end goes after the run, and a deletion of that line takes the run with it. These are the rules, asked
/// of the section provider and the view model; <c>MemberRunFollowsItsLineTests</c> in the integration tests drives
/// the keys, the clipboard and automation through a rendered editor.
/// </summary>
public class MemberRunFollowsItsLineTests : IDisposable
{
    private readonly List<CodeEditorViewModel> made = [];
    private readonly IWindowManager windowManager = Substitute.For<IWindowManager>();

    public void Dispose()
    {
        foreach (var vm in made) vm.Dispose();
        GC.SuppressFinalize(this);
    }

    // A procedure with a run VB6 writes, an indented property whose Get has a run, a variable's run, and an orphan
    // under a comment.
    private const string Code =
        "Option Explicit\r\n" +
        "Public Count As Long\r\n" +
        "Attribute Count.VB_VarDescription = \"How many\"\r\n" +
        "\r\n" +
        "Public Sub Go()\r\n" +
        "Attribute Go.VB_Description = \"Goes\"\r\n" +
        "Attribute Go.VB_UserMemId = 0\r\n" +
        "    Debug.Print 1\r\n" +
        "End Sub\r\n" +
        "\r\n" +
        "    Public Property Get Total() As Long\r\n" +
        "Attribute Total.VB_Description = \"The total\"\r\n" +
        "    End Property\r\n" +
        "\r\n" +
        "' Not described\r\n" +
        "Attribute Gone.VB_Description = \"Nothing\"\r\n";

    // A continued declaration, as VB6 accepts one, and its run (oracle probe p11a's shape).
    private const string Continued =
        "Option Explicit\r\n" +
        "\r\n" +
        "Public Sub Go( _\r\n" +
        "    ByVal x As Long)\r\n" +
        "Attribute Go.VB_Description = \"Goes\"\r\n" +
        "End Sub\r\n";

    private CodeEditorViewModel Open(string code)
    {
        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe<CreateOrNavigateToSubEvent>(Arg.Any<Action<CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(Arg.Any<Action<ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<FormUnloadedEvent>(Arg.Any<Action<FormUnloadedEvent>>()).Returns(Substitute.For<IDisposable>());
        var vm = new CodeEditorViewModel(
            windowManager, Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(), eventBus, Substitute.For<ILspClient>(),
            Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(),
            Substitute.For<IBookmarkService>(), Substitute.For<HexIDE.Debugging.IBreakpointService>(),
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            Substitute.For<HexIDE.Debugging.IRunScope>(), Substitute.For<ILocalizationService>());
        made.Add(vm);
        var module = TestHelpers.CreateModule(name: "Module1", kind: ModuleKind.StandardModule);
        module.UpdateCode(code);
        return vm.Initialize(module);
    }

    private static int At(CodeEditorViewModel vm, string text, int nth = 0)
    {
        var at = -1;
        for (var i = 0; i <= nth; i++)
            at = vm.Document.Text.IndexOf(text, at + 1, StringComparison.Ordinal);
        return at >= 0 ? at : throw new InvalidOperationException($"'{text}' is not in the buffer");
    }

    private static DocumentLine LineOf(CodeEditorViewModel vm, string text) => vm.Document.GetLineByOffset(At(vm, text));

    private static MemberRun RunOf(CodeEditorViewModel vm, string member) =>
        vm.MemberRunsNow.Single(r => r.Member?.Name == member);

    private static List<(int Offset, int End)> Deletable(CodeEditorViewModel vm, int from, int to) =>
        vm.ReadOnlySections.GetDeletableSegments(new SimpleSegment(from, to - from))
            .Select(s => (s.Offset, s.EndOffset)).ToList();

    private static (int Offset, int End) Widened(CodeEditorViewModel vm, int from, int to)
    {
        var (offset, length) = vm.ReadOnlySections.WidenDeletion(from, to - from);
        return (offset, offset + length);
    }

    // ── How far a deletion reaches (4.5a) ────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void ADeletionOfADescribedLinesTextReachesTheEndOfItsRunsLastLine()
    {
        // The line's text alone: its row is left, ended by the run's last line break, as it would be with no run.
        var vm = OpenCode();
        var declaration = LineOf(vm, "Public Sub Go()");
        var run = RunOf(vm, "Go");

        Widened(vm, declaration.Offset, declaration.EndOffset).Should().Be((declaration.Offset, run.LastContentEnd));
    }

    [AvaloniaFact]
    public void ADeletionOfADescribedLineWithItsBreakReachesPastItsRunsLastBreak()
    {
        // Ctrl+D and a whole-line Cut delete the line with its break, so the run goes with its break too.
        var vm = OpenCode();
        var declaration = LineOf(vm, "Public Sub Go()");
        var run = RunOf(vm, "Go");

        Widened(vm, declaration.Offset, declaration.Offset + declaration.TotalLength)
            .Should().Be((declaration.Offset, run.Region.End));
    }

    [AvaloniaFact]
    public void ADeletionFromTheEndOfTheIndentTakesTheRunToo()
    {
        // Home puts the caret after the indent, so a selection made from there is the whole of the line's text.
        var vm = OpenCode();
        var declaration = LineOf(vm, "Public Property Get Total");
        var text = At(vm, "Public Property Get Total");

        Widened(vm, text, declaration.EndOffset).Should().Be((text, RunOf(vm, "Total").LastContentEnd));
    }

    [AvaloniaFact]
    public void ADeletionEndingBeforeTrailingWhitespaceTakesTheRunToo()
    {
        // What it leaves is whitespace, which declares nothing, so the run would otherwise be left an orphan under it.
        var vm = OpenCode();
        var anchor = RunOf(vm, "Go").Anchor;
        vm.Document.Insert(anchor, "  ");
        var declaration = LineOf(vm, "Public Sub Go()");

        Widened(vm, declaration.Offset, declaration.EndOffset - 2)
            .Should().Be((declaration.Offset, RunOf(vm, "Go").LastContentEnd));
    }

    [AvaloniaFact]
    public void ADeletionLeavingAnyOfTheDeclarationIsNotWidened()
    {
        var vm = OpenCode();
        var declaration = LineOf(vm, "Public Sub Go()");

        Widened(vm, At(vm, "Sub Go()"), declaration.EndOffset).Should().Be((At(vm, "Sub Go()"), declaration.EndOffset));
        Widened(vm, declaration.Offset, declaration.EndOffset - 1).Should().Be((declaration.Offset, declaration.EndOffset - 1));
    }

    [AvaloniaFact]
    public void AVariablesRunGoesWithItsDeclaration()
    {
        var vm = OpenCode();
        var declaration = LineOf(vm, "Public Count As Long");

        Widened(vm, declaration.Offset, declaration.Offset + declaration.TotalLength)
            .Should().Be((declaration.Offset, RunOf(vm, "Count").Region.End));
    }

    [AvaloniaFact]
    public void ADeletionOfTwoDescribedLinesTakesBothRuns()
    {
        var vm = OpenCode();
        var from = LineOf(vm, "Public Count As Long").Offset;
        var to = LineOf(vm, "Public Sub Go()").EndOffset;

        Widened(vm, from, to).Should().Be((from, RunOf(vm, "Go").LastContentEnd));
    }

    [AvaloniaFact]
    public void TheLineAboveAnOrphanTakesNothingWithIt()
    {
        // An orphan is inert text: deleting the comment above it leaves it, and it was never protected.
        var vm = OpenCode();
        var comment = LineOf(vm, "' Not described");

        Widened(vm, comment.Offset, comment.Offset + comment.TotalLength)
            .Should().Be((comment.Offset, comment.Offset + comment.TotalLength));
    }

    [AvaloniaFact]
    public void TheProviderDoesNotCarveOutARunTheDeletionTakes()
    {
        // What the window widens a deletion of the line's text to, the provider must let through whole, or the
        // library would delete the line and leave the run, which is what Ctrl+D did before task 4.5a.
        var vm = OpenCode();
        var declaration = LineOf(vm, "Public Sub Go()");
        var end = RunOf(vm, "Go").LastContentEnd;

        Deletable(vm, declaration.Offset, end).Should().Equal((declaration.Offset, end));
    }

    [AvaloniaFact]
    public void TheProviderLetsADeletionFromTheEndOfTheIndentTakeTheRun()
    {
        // Home puts the caret after the indent, so Home, Shift+End and Backspace delete from there. The window widens
        // that deletion over the run, and the provider must then let it through whole, or the run would be carved out
        // of it and left an orphan under the emptied row (#273 task 4.5a review).
        var vm = OpenCode();
        var text = At(vm, "Public Property Get Total");
        var end = RunOf(vm, "Total").LastContentEnd;

        Deletable(vm, text, end).Should().Equal((text, end));
    }

    [AvaloniaFact]
    public void TheProviderStillCarvesOutARunUnderADeclarationTheDeletionLeavesPartOf()
    {
        var vm = OpenCode();
        var from = At(vm, "Sub Go()");
        var run = RunOf(vm, "Go");

        Deletable(vm, from, run.Region.End).Should().Equal((from, run.Anchor));
    }

    [AvaloniaFact]
    public void ADeletionThatTakesItsRunChangesNoRegionAndOneThatCarvesItDoes()
    {
        // press_key asks this of a key that deletes: what a person's key could not do is refused, and what it does is
        // let through.
        var vm = OpenCode();
        var declaration = LineOf(vm, "Public Sub Go()");
        var run = RunOf(vm, "Go");

        vm.ReadOnlySections.DeletionChangesARegion(declaration.Offset, declaration.TotalLength).Should().BeFalse();
        vm.ReadOnlySections.DeletionChangesARegion(At(vm, "Sub Go()"), run.Region.End - At(vm, "Sub Go()")).Should().BeTrue();
        vm.ReadOnlySections.DeletionChangesARegion(At(vm, "\"Goes\""), 3).Should().BeTrue();
        vm.ReadOnlySections.DeletionChangesARegion(0, 4).Should().BeTrue("the header is never deleted");
    }

    // ── A continued declaration (4.5a, decided) ──────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void DeletingTheLastLineOfAContinuedDeclarationLeavesItsRunUnderWhatIsLeft()
    {
        // The described line is the logical line, from DescribedStart: the physical line above the run, which the
        // provider read before 4.5a, is only its last part. Deleting that part, and with the run selected too, is a
        // rewrite of the declaration under way, and the run stays under what is left, still describing it.
        var vm = OpenContinued();
        var last = LineOf(vm, "    ByVal x As Long)");
        var run = RunOf(vm, "Go");

        Widened(vm, last.Offset, last.Offset + last.TotalLength).Should().Be((last.Offset, last.Offset + last.TotalLength));
        Deletable(vm, last.Offset, run.Region.End).Should().Equal((last.Offset, run.Anchor));

        vm.Document.Remove(last.Offset, last.Length);

        vm.MemberRunsNow.Should().ContainSingle(r => r.DescribesMember).Which.Member!.Value.Name.Should().Be("Go",
            "the line left above the run is still the declaration of Go, continued into the line the deletion emptied");
    }

    [AvaloniaFact]
    public void DeletingEveryLineOfAContinuedDeclarationTakesItsRun()
    {
        var vm = OpenContinued();
        var first = LineOf(vm, "Public Sub Go( _");
        var last = LineOf(vm, "    ByVal x As Long)");
        var run = RunOf(vm, "Go");

        Widened(vm, first.Offset, last.Offset + last.TotalLength).Should().Be((first.Offset, run.Region.End));
        Deletable(vm, first.Offset, run.LastContentEnd).Should().Equal((first.Offset, run.LastContentEnd));
    }

    [AvaloniaFact]
    public void DeletingTheFirstLineOfAContinuedDeclarationLeavesItsRunAnOrphanKeptAsItWas()
    {
        // What is left above the run declares nothing, so the run describes nothing: inert text, kept byte for byte.
        var vm = OpenContinued();
        var first = LineOf(vm, "Public Sub Go( _");
        var runText = "Attribute Go.VB_Description = \"Goes\"\r\n";

        Widened(vm, first.Offset, first.Offset + first.TotalLength).Should().Be((first.Offset, first.Offset + first.TotalLength));
        vm.Document.Remove(first.Offset, first.TotalLength);

        vm.Document.Text.Should().Contain("    ByVal x As Long)\r\n" + runText);
        vm.MemberRunsNow.Should().ContainSingle().Which.DescribesMember.Should().BeFalse();
        vm.ReadOnlyRegionsNow.Should().ContainSingle("only the header is read-only now");
    }

    // ── Where a line break goes (4.5) ────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void ALineBreakAtTheEndOfADescribedLineGoesAfterItsRun()
    {
        var vm = OpenCode();
        var run = RunOf(vm, "Go");

        var placed = vm.ReadOnlySections.PlaceLineBreak(run.Anchor, 0, "\r\n    ");

        placed.Should().NotBeNull();
        placed!.Head.Should().BeEmpty();
        placed.TailAt.Should().Be(run.LastContentEnd);
        placed.Tail.Should().Be("\r\n    ");
    }

    [AvaloniaFact]
    public void TextBeforeTheFirstLineBreakStaysOnTheDeclarationsLine()
    {
        // As if the run were not there: the first line of a pasted block joins the declaration, the rest follows it.
        var vm = OpenCode();
        var run = RunOf(vm, "Go");

        var placed = vm.ReadOnlySections.PlaceLineBreak(run.Anchor, 0, " ' first\r\n    Debug.Print 0")!;

        placed.Head.Should().Be(" ' first");
        placed.Tail.Should().Be("\r\n    Debug.Print 0");
    }

    [AvaloniaTheory]
    [InlineData("\r\n")]
    [InlineData("")]
    public void APastedDeclarationWithItsOwnRunGoesWholeAfterTheExistingRun(string trailingNewline)
    {
        var vm = OpenCode();
        var run = RunOf(vm, "Go");
        const string pasted = "Public Event Opened()\r\nAttribute Opened.VB_Description = \"Opens\"";

        var placed = vm.ReadOnlySections.PlaceLineBreak(run.Anchor, 0, pasted + trailingNewline)!;

        placed.Head.Should().BeEmpty();
        placed.TailAt.Should().Be(run.LastContentEnd);
        placed.Tail.Should().Be("\r\n" + pasted);
    }

    [AvaloniaFact]
    public void EveryLineBreakIsWrittenInTheDescribedLinesNewline()
    {
        var vm = OpenCode();
        var run = RunOf(vm, "Go");

        vm.ReadOnlySections.PlaceLineBreak(run.Anchor, 0, "\nA\rB\r\nC")!.Tail.Should().Be("\r\nA\r\nB\r\nC");
    }

    [AvaloniaFact]
    public void InAnLfModuleEveryLineBreakIsWrittenLf()
    {
        // The described line's own terminator, whatever it is, and not the platform's or the CRLF VB6 writes: a module
        // saved with LF gets LF (#273 task 4.5 review).
        var vm = Open(Code.Replace("\r\n", "\n", StringComparison.Ordinal));
        var run = RunOf(vm, "Go");

        vm.ReadOnlySections.PlaceLineBreak(run.Anchor, 0, "\r\n    Debug.Print 0\r\n")!.Tail
            .Should().Be("\n    Debug.Print 0\n");
    }

    [AvaloniaFact]
    public void ALineBreakBeforeTrailingWhitespaceGoesAfterTheRunToo()
    {
        // Enter takes a caret before trailing whitespace for the end of the line; a line break there would leave the
        // run under a line of nothing but that whitespace.
        var vm = OpenCode();
        var run = RunOf(vm, "Go");
        vm.Document.Insert(run.Anchor, "  ");
        run = RunOf(vm, "Go");

        vm.ReadOnlySections.PlaceLineBreak(run.Anchor - 2, 0, "\r\n")!.TailAt.Should().Be(run.LastContentEnd);
    }

    [AvaloniaFact]
    public void TextReplacingASelectionThatEndsAtTheLinesEndPutsItsFirstLineInThatSelectionsPlace()
    {
        var vm = OpenCode();
        var run = RunOf(vm, "Go");
        var name = At(vm, "Go()");

        var placed = vm.ReadOnlySections.PlaceLineBreak(name, run.Anchor - name, "Halt()\r\n")!;

        (placed.Offset, placed.Length, placed.Head).Should().Be((name, run.Anchor - name, "Halt()"));
    }

    [AvaloniaFact]
    public void APlacedReplacementSaysWhereItsTailWasWrittenAndLeavesTheCaretAfterIt()
    {
        // Where the tail starts once the head has replaced the selection, which is shorter or longer than it: the caret
        // after a paste over a selection ending at the anchor goes to the end of what was pasted (#273 task 4.5 review).
        var vm = OpenCode();
        var run = RunOf(vm, "Go");
        var name = At(vm, "Go()");

        var placed = vm.InsertLineBreakPastRun(name, run.Anchor - name, "Halt()\r\n    x = 1")!;

        vm.Document.Text.Substring(placed.TailStart, placed.Tail.Length).Should().Be("\r\n    x = 1");
        placed.End.Should().Be(At(vm, "    x = 1") + "    x = 1".Length);
    }

    [AvaloniaFact]
    public void TextReplacingADescribedDeclarationWholeThatDeclaresOnItsFirstLineKeepsTheRunForIt()
    {
        // A declaration and the lines after it, pasted over the selected declaration: the first line joins the row, the
        // run stays under it and describes what it declares, as a retype's run does (IDE probe C8).
        var vm = OpenCode();
        var declaration = LineOf(vm, "Public Sub Go()");

        vm.InsertLineBreakPastRun(declaration.Offset, declaration.Length, "Public Sub Leave()\r\n    x = 1")
            .Should().NotBeNull();

        vm.Document.Text.Should().Contain("Public Sub Leave()\r\nAttribute Go.VB_Description");
        RunOf(vm, "Leave").Kind.Should().Be(MemberRunKind.Procedure);
    }

    [AvaloniaTheory]
    [InlineData("' note\nPublic Sub Go()", "Public Sub Go()")]
    [InlineData("\nPublic Sub Go()", "Public Sub Go()")]
    [InlineData("' note\nPublic Sub Go( _\n    ByVal x As Long)\n    Debug.Print x", "    ByVal x As Long)")]
    public void WholeDeclarationReplacementKeepsTheRunUnderTheFirstCompatibleLogicalLine(string text, string lastLine)
    {
        var vm = OpenCode();
        var declaration = LineOf(vm, "Public Sub Go()");

        var placed = vm.InsertLineBreakPastRun(declaration.Offset, declaration.Length, text)!;

        vm.Document.Text.ReplaceLineEndings("\n").Should().Contain(lastLine + "\nAttribute Go.VB_Description");
        RunOf(vm, "Go").Kind.Should().Be(MemberRunKind.Procedure);
        placed.End.Should().Be(text.EndsWith("Debug.Print x", StringComparison.Ordinal)
            ? LineOf(vm, "    Debug.Print x").EndOffset : LineOf(vm, lastLine).EndOffset);
    }

    [AvaloniaTheory]
    [InlineData("Public Count As Long")]
    [InlineData("Public Const Count = 1")]
    public void WholeVariableReplacementSkipsDeclarationsItsRunCannotDescribe(string declarationText)
    {
        var vm = OpenCode();
        var declaration = LineOf(vm, "Public Count As Long");

        vm.InsertLineBreakPastRun(declaration.Offset, declaration.Length,
            "' note\nPublic Event Other()\nPublic Wrong As Long\n" + declarationText + "\n' tail");

        vm.Document.Text.ReplaceLineEndings("\n").Should().Contain(declarationText + "\nAttribute Count.VB_VarDescription");
        RunOf(vm, "Count").Kind.Should().Be(MemberRunKind.Variable);
    }

    [AvaloniaFact]
    public void WholeDeclarationReplacementWithoutADeclarationKeepsTheFirstBreakSplit()
    {
        var vm = OpenCode();
        var declaration = LineOf(vm, "Public Sub Go()");

        vm.InsertLineBreakPastRun(declaration.Offset, declaration.Length, "' note\n    x = 1");

        vm.Document.Text.ReplaceLineEndings("\n").Should().Contain("' note\nAttribute Go.VB_Description");
        vm.MemberRunsNow.Single(r => r.Region.Start == At(vm, "Attribute Go.VB_Description")).Kind.Should().Be(MemberRunKind.Orphan);
    }

    [AvaloniaFact]
    public async Task InsertFileReplacingAWholeDeclarationKeepsItsRun()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hexide-insert-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "' note\nPublic Sub Go()", TestContext.Current.CancellationToken);
        try
        {
            windowManager.OpenFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerOpenOptions>())
                .Returns(Task.FromResult<IReadOnlyList<string>?>([path]));
            var vm = OpenCode();
            var declaration = LineOf(vm, "Public Sub Go()");

            await vm.InsertFileAsync(declaration.Offset, declaration.Length);

            vm.Document.Text.ReplaceLineEndings("\n").Should().Contain("Public Sub Go()\nAttribute Go.VB_Description");
            RunOf(vm, "Go").Kind.Should().Be(MemberRunKind.Procedure);
            vm.CaretOffset.Should().Be(LineOf(vm, "Public Sub Go()").EndOffset);
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public void TheRuleDoesNotApplyAnywhereElse()
    {
        var vm = OpenCode();
        var run = RunOf(vm, "Go");

        vm.ReadOnlySections.PlaceLineBreak(run.Anchor, 0, " As Long").Should().BeNull("no line break, so nothing to place");
        vm.ReadOnlySections.PlaceLineBreak(At(vm, "Go()"), 0, "\r\n").Should().BeNull("mid-line, which leaves Go() above the run");
        vm.ReadOnlySections.PlaceLineBreak(LineOf(vm, "' Not described").EndOffset, 0, "\r\n").Should().BeNull(
            "an orphan is inert text, and a line break above it is the developer's");
        vm.ReadOnlySections.PlaceLineBreak(At(vm, "Option Explicit") - 3, run.Anchor - At(vm, "Option Explicit") + 3, "\r\n")
            .Should().BeNull("a span reaching into the header is carved by the library, and left to it");
        vm.ReadOnlySections.PlaceLineBreak(At(vm, "Count As Long"), run.Anchor - At(vm, "Count As Long"), "\r\n")
            .Should().BeNull("and so is one starting where text may go and crossing another member's run, which it carves");
    }

    [AvaloniaFact]
    public void ALineBreakPlacedPastTheRunIsOneUndoStepAndLeavesTheRunDescribingItsLine()
    {
        var vm = OpenCode();
        var before = vm.Document.Text;
        var run = RunOf(vm, "Go");

        var placed = vm.InsertLineBreakPastRun(run.Anchor, 0, " ' x\r\n    Debug.Print 0");

        placed.Should().NotBeNull();
        vm.Document.Text.Should().Be(before
            .Replace("Public Sub Go()\r\n", "Public Sub Go() ' x\r\n")
            .Replace("VB_UserMemId = 0\r\n", "VB_UserMemId = 0\r\n    Debug.Print 0\r\n"));
        RunOf(vm, "Go").Kind.Should().Be(MemberRunKind.Procedure);
        vm.Document.UndoStack.Undo();
        vm.Document.Text.Should().Be(before);
    }

    [AvaloniaFact]
    public async Task InsertFileAtTheEndOfADescribedLinePutsTheFileAfterTheRun()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hexide-insert-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "\n' from a file\n", TestContext.Current.CancellationToken);
        try
        {
            windowManager.OpenFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerOpenOptions>())
                .Returns(Task.FromResult<IReadOnlyList<string>?>([path]));
            var vm = OpenCode();
            var before = vm.Document.Text;
            var run = RunOf(vm, "Go");

            await vm.InsertFileAsync(run.Anchor, 0);

            vm.Document.Text.Should().Be(before.Replace("VB_UserMemId = 0\r\n", "VB_UserMemId = 0\r\n' from a file\r\n\r\n"),
                "the file's line breaks are written as the module's, after the run");
            vm.CaretOffset.Should().Be(run.LastContentEnd + "\r\n' from a file\r\n".Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void AnAddInsEditPuttingALineBreakAtTheEndOfADescribedLineIsRefused()
    {
        var vm = OpenCode();
        var before = vm.Document.Text;
        var run = RunOf(vm, "Go");

        var refusal = vm.ApplyEdits([new TextChange(run.Anchor, 0, "\r\n    Debug.Print 0")]);

        refusal.Should().Contain("line break").And.Contain("Go").And.Contain("Nothing was changed");
        vm.Document.Text.Should().Be(before);
    }

    [AvaloniaFact]
    public void AnAddInsReplacementEndingAtTheEndOfADescribedLineWithALineBreakIsRefused()
    {
        // Asked of where the edit ends, which is where its text meets the anchor: a new name and a line break written
        // over the old name would leave the run under the line break's line (#273 task 4.5 review).
        var vm = OpenCode();
        var before = vm.Document.Text;
        var run = RunOf(vm, "Go");
        var name = At(vm, "Go()");

        var refusal = vm.ApplyEdits([new TextChange(name, run.Anchor - name, "Halt()\r\n")]);

        refusal.Should().Contain("line break").And.Contain("Go");
        vm.Document.Text.Should().Be(before);
    }

    [AvaloniaFact]
    public void AnAddInsEditAtTheEndOfADescribedLineWithoutALineBreakIsApplied()
    {
        var vm = OpenCode();
        var run = RunOf(vm, "Go");

        vm.ApplyEdits([new TextChange(run.Anchor, 0, " ' goes")]).Should().BeNull();
        vm.ApplyEdits([new TextChange(LineOf(vm, "    Debug.Print 1").Offset, 0, "    Stop\r\n")]).Should().BeNull(
            "a line break anywhere a run does not hang from is the add-in's to put");

        vm.Document.Text.Should().Contain("Public Sub Go() ' goes\r\nAttribute Go.VB_Description");
    }

    private CodeEditorViewModel OpenCode() => Open(Code);

    private CodeEditorViewModel OpenContinued() => Open(Continued);
}
