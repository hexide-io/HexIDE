using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using HexIDE.Controls;
using HexIDE.Lsp.Messages;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.Controls;

/// <summary>
/// The code window's folds: a server's, the header's, and the two merged into one list the folding manager
/// accepts (hexide-io/HexIDE#273 task 3.14).
/// </summary>
public class Vb6FoldingAdapterTests
{
    // Header: lines 1-9, 0-based 0-8. Code from line 10.
    private const string Frm =
        "VERSION 5.00\r\n" +
        "Begin VB.Form frmOrders \r\n" +
        "   Caption         =   \"Orders\"\r\n" +
        "   Begin VB.CommandButton cmdOK \r\n" +
        "      Caption         =   \"OK\"\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"frmOrders\"\r\n" +
        "Attribute VB_PredeclaredId = True\r\n" +
        "Option Explicit\r\n" +
        "\r\n" +
        "Private Sub cmdOK_Click()\r\n" +
        "    Unload Me\r\n" +
        "End Sub\r\n";

    /// <summary>Where the designer half ends and the code section starts, as the model splits them.</summary>
    private static readonly int FrmPrefix = Frm.IndexOf("Attribute VB_Name", StringComparison.Ordinal);

    private static NewFolding? HeaderOf(string text, int prefixLength) =>
        Vb6FoldingAdapter.Header(new TextDocument(text), ReadOnlyRegions.Of(text, prefixLength));

    private static NewFolding Fold(int start, int end) => new(start, end);

    // ── The header's fold ──────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void AFormsHeaderFoldEndsWithTheHeadersLastLineNotItsLineBreak()
    {
        var fold = HeaderOf(Frm, FrmPrefix);

        fold.Should().NotBeNull();
        fold!.StartOffset.Should().Be(0);
        var lastLine = "Attribute VB_PredeclaredId = True";
        fold.EndOffset.Should().Be(Frm.IndexOf(lastLine, StringComparison.Ordinal) + lastLine.Length,
            "folded, the header is one line and the first line of code stays a line of its own");
    }

    [AvaloniaFact]
    public void TheHeaderFoldIsLabelledWithItsFirstLine()
    {
        HeaderOf(Frm, FrmPrefix)!.Name.Should().Be("VERSION 5.00");
    }

    [AvaloniaFact]
    public void ATwoLineHeaderIsFolded()
    {
        // The shortest header a fold can hide anything of.
        var text = "Attribute VB_Name = \"Module1\"\r\nAttribute VB_Description = \"Helpers\"\r\nOption Explicit\r\n";

        var fold = HeaderOf(text, text.IndexOf("Option", StringComparison.Ordinal));

        fold.Should().NotBeNull();
        fold!.EndOffset.Should().Be(text.IndexOf("\r\nOption", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void AOneLineHeaderIsNotFolded()
    {
        var text = "Attribute VB_Name = \"Module1\"\r\nOption Explicit\r\n";

        HeaderOf(text, text.IndexOf("Option", StringComparison.Ordinal)).Should().BeNull();
    }

    [AvaloniaFact]
    public void AFileWithNoHeaderHasNoHeaderFold()
    {
        HeaderOf("Option Explicit\r\n\r\nSub Main()\r\nEnd Sub\r\n", 0).Should().BeNull();
    }

    [AvaloniaFact]
    public void AMembersAttributeRunIsNotTheHeader()
    {
        // The first region is a member's when the file has no header, and it is phase 4's to fold, not this one's.
        var text = "Option Explicit\r\nPublic Sub Go()\r\nAttribute Go.VB_Description = \"Goes\"\r\nAttribute Go.VB_UserMemId = 0\r\nEnd Sub\r\n";
        var regions = ReadOnlyRegions.Of(text, 0);
        regions.Should().NotBeEmpty();
        regions[0].Anchor.Should().NotBeNull("the test needs a member's run first");

        Vb6FoldingAdapter.Header(new TextDocument(text), regions).Should().BeNull();
    }

    [AvaloniaFact]
    public void AHeaderWithNoFinalLineBreakEndsAtTheEndOfTheFile()
    {
        // A file that is nothing but its header, and ends without a line break.
        var text = "Attribute VB_Name = \"Module1\"\r\nAttribute VB_Description = \"Helpers\"";

        var fold = HeaderOf(text, text.Length);

        fold.Should().NotBeNull();
        fold!.EndOffset.Should().Be(text.Length);
    }

    // ── A member's attribute run (task 4.3) ─────────────────────────────────────────────────────────────

    private static List<NewFolding> RunFoldsOf(string text) => Vb6FoldingAdapter.MemberRuns(ReadOnlyRegions.MemberRuns(text, 0));

    private static int EndOf(string text, string line) => text.IndexOf(line, StringComparison.Ordinal) + line.Length;

    [Fact]
    public void ARunsFoldRunsFromTheEndOfTheLineItDescribesToTheEndOfItsLastLine()
    {
        var text = "Option Explicit\r\nPublic Sub Go()\r\nAttribute Go.VB_Description = \"Goes\"\r\n" +
                   "Attribute Go.VB_UserMemId = 0\r\n    Debug.Print 1\r\nEnd Sub\r\n";

        var fold = RunFoldsOf(text).Should().ContainSingle().Subject;

        fold.StartOffset.Should().Be(EndOf(text, "Public Sub Go()"), "it starts part-way along the declaration's line, at its end");
        fold.EndOffset.Should().Be(EndOf(text, "Attribute Go.VB_UserMemId = 0"), "and ends before the run's last line break");
        fold.Name.Should().BeNull("no title: the library draws its own box, and the run's text is what is being hidden");
    }

    [Fact]
    public void AnOrphanHasNoFold()
    {
        // Under a statement VB6 gives the run to nothing, so it is free text, shown as code.
        RunFoldsOf("Option Explicit\r\nPublic Sub Go()\r\n    Debug.Print 1\r\nAttribute Go.VB_Description = \"Goes\"\r\nEnd Sub\r\n")
            .Should().BeEmpty();
    }

    [Fact]
    public void EachRunOfAPropertyHasItsOwnFold()
    {
        var text = "Public Property Get Total() As Long\r\nAttribute Total.VB_Description = \"The total\"\r\nEnd Property\r\n" +
                   "Public Property Let Total(ByVal Value As Long)\r\nAttribute Total.VB_Description = \"Sets it\"\r\nEnd Property\r\n";

        RunFoldsOf(text).Select(f => (f.StartOffset, f.EndOffset)).Should().Equal(
            (EndOf(text, "Get Total() As Long"), EndOf(text, "\"The total\"")),
            (EndOf(text, "(ByVal Value As Long)"), EndOf(text, "\"Sets it\"")));
    }

    [Fact]
    public void AVariablesRunFoldsFromTheEndOfItsDeclaration()
    {
        var text = "Option Explicit\r\nPublic Total As Long\r\nAttribute Total.VB_VarDescription = \"The total\"\r\n";

        RunFoldsOf(text).Select(f => (f.StartOffset, f.EndOffset)).Should().Equal(
            (EndOf(text, "Public Total As Long"), EndOf(text, "\"The total\"")));
    }

    [Fact]
    public void ARunUnderAContinuedDeclarationFoldsFromTheEndOfTheDeclarationsLastPhysicalLine()
    {
        // The line directly above the run, which is the one the fold's marker sits at the end of.
        var text = "Option Explicit\r\nPublic Function Total( _\r\n    ByVal Rate As Double) As Double\r\n" +
                   "Attribute Total.VB_Description = \"The total\"\r\nEnd Function\r\n";

        RunFoldsOf(text).Select(f => f.StartOffset).Should().Equal(EndOf(text, "ByVal Rate As Double) As Double"));
    }

    [AvaloniaFact]
    public void ARunsFoldNestsInsideAServersFoldOfTheProcedureAndOneCrossingItIsDropped()
    {
        var text = "Option Explicit\r\nPublic Sub Go()\r\nAttribute Go.VB_Description = \"Goes\"\r\n    Debug.Print 1\r\nEnd Sub\r\n";
        var document = new TextDocument(text);
        var run = RunFoldsOf(text).Single();
        var procedure = Vb6FoldingAdapter.FromServer(document, [new FoldingRange(1, 4)]).Single();
        var crossing = Vb6FoldingAdapter.FromServer(document, [new FoldingRange(2, 3, StartCharacter: 0)]).Single();

        Vb6FoldingAdapter.Merge([run], [procedure, crossing]).Should().Equal(procedure, run);
    }

    // ── A server's folds ──────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void AServerFoldDefaultsToTheEndsOfItsFirstAndLastLines()
    {
        var document = new TextDocument(Frm);

        var folds = Vb6FoldingAdapter.FromServer(document, [new FoldingRange(11, 13)]);

        folds.Should().ContainSingle();
        folds[0].StartOffset.Should().Be(document.GetLineByNumber(12).EndOffset);
        folds[0].EndOffset.Should().Be(document.GetLineByNumber(14).EndOffset);
        folds[0].Name.Should().BeNull();
    }

    [AvaloniaFact]
    public void AServerFoldOnOneLineOrPastTheEndIsSkipped()
    {
        var document = new TextDocument(Frm);

        Vb6FoldingAdapter.FromServer(document, [new FoldingRange(3, 3), new FoldingRange(11, 99), new FoldingRange(5, 4)])
            .Should().BeEmpty();
    }

    // ── Merging ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheHeaderFoldIsKeptWhenTheServerSendsNothing()
    {
        // The defect this merge exists for: applying a server's answer replaces every fold, and no answer at all
        // is the commonest one.
        var header = Fold(0, 50);

        Vb6FoldingAdapter.Merge([header], []).Should().Equal(header);
    }

    [Fact]
    public void TheMergeIsSortedByStartWithTheLongerFirstOnATie()
    {
        // The manager throws on a list out of start order. On a tie it pairs folds with sections in list order.
        var header = Fold(0, 50);
        var late = Fold(200, 260);
        var outer = Fold(100, 180);
        var inner = Fold(100, 140);

        Vb6FoldingAdapter.Merge([header], [late, inner, outer]).Should().Equal(header, outer, inner, late);
    }

    [Fact]
    public void AServerFoldCrossingTheHeaderIsDropped()
    {
        var header = Fold(0, 50);
        var crossing = Fold(40, 90);
        var after = Fold(60, 90);

        Vb6FoldingAdapter.Merge([header], [crossing, after]).Should().Equal(header, after);
    }

    [Fact]
    public void AServerFoldInsideTheHeaderIsKept()
    {
        var header = Fold(0, 50);
        var inside = Fold(10, 40);
        var endingWithIt = Fold(20, 50);

        Vb6FoldingAdapter.Merge([header], [inside, endingWithIt]).Should().Equal(header, inside, endingWithIt);
    }

    [Fact]
    public void AServerFoldThatSharesAnOwnFoldsStartIsKept()
    {
        var own = Fold(10, 50);
        var server = Fold(10, 120);

        Vb6FoldingAdapter.Merge([own], [server]).Should().Equal(server, own);
    }

    [AvaloniaFact]
    public void AServerFoldHonoursCharactersOnBothLines()
    {
        var document = new TextDocument("  int area() {\r\n    return 1;\r\n  }\r\n");
        var fold = Vb6FoldingAdapter.FromServer(document,
            [new FoldingRange(0, 2, StartCharacter: 14, EndCharacter: 2)]).Single();

        fold.StartOffset.Should().Be(14);
        fold.EndOffset.Should().Be(document.GetLineByNumber(3).Offset + 2);
        fold.Name.Should().BeNull();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnInnerFoldKeepsItsStateWhenTheOuterVanishes(bool innerFolded)
    {
        var manager = new FoldingManager(new TextDocument(new string('x', 100)));
        Vb6FoldingAdapter.Update(manager, [Fold(10, 90), Fold(10, 50)]);
        manager.AllFoldings.Single(f => f.EndOffset == 90).IsFolded = !innerFolded;
        manager.AllFoldings.Single(f => f.EndOffset == 50).IsFolded = innerFolded;

        Vb6FoldingAdapter.Update(manager, [Fold(10, 50)]);

        manager.AllFoldings.Single().IsFolded.Should().Be(innerFolded);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void AReturningFoldTakesItsDefaultRatherThanTheSurvivorsState(bool defaultClosed)
    {
        var manager = new FoldingManager(new TextDocument(new string('x', 100)));
        Vb6FoldingAdapter.Update(manager, [Fold(10, 90), Fold(10, 50)]);
        manager.AllFoldings.Single(f => f.EndOffset == 90).IsFolded = !defaultClosed;
        manager.AllFoldings.Single(f => f.EndOffset == 50).IsFolded = !defaultClosed;
        Vb6FoldingAdapter.Update(manager, [Fold(10, 50)]);
        Vb6FoldingAdapter.Update(manager, [new NewFolding(10, 90) { DefaultClosed = defaultClosed }, Fold(10, 50)]);

        manager.AllFoldings.Single(f => f.EndOffset == 90).IsFolded.Should().Be(defaultClosed);
        manager.AllFoldings.Single(f => f.EndOffset == 50).IsFolded.Should().Be(!defaultClosed);
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ASameStartPairKeepsStatesAcrossUpdates(bool editAbove, bool reverseOrder)
    {
        var document = new TextDocument(new string('x', 100));
        var manager = new FoldingManager(document);
        Vb6FoldingAdapter.Update(manager, [Fold(10, 90), Fold(10, 50)]);
        manager.AllFoldings.Single(f => f.EndOffset == 50).IsFolded = true;
        var shift = editAbove ? 7 : 0;
        if (editAbove) document.Insert(0, "header\n");
        var outer = Fold(10 + shift, 90 + shift);
        var inner = Fold(10 + shift, 50 + shift);

        Vb6FoldingAdapter.Update(manager, reverseOrder ? [inner, outer] : [outer, inner]);

        manager.AllFoldings.Should().HaveCount(2);
        manager.AllFoldings.Single(f => f.EndOffset == outer.EndOffset).IsFolded.Should().BeFalse();
        manager.AllFoldings.Single(f => f.EndOffset == inner.EndOffset).IsFolded.Should().BeTrue();
    }

    [Fact]
    public void AServerFoldContainingTheHeaderFromFurtherDownIsKept()
    {
        // Not reachable from the top of the file, where the header always starts, but the rule is containment,
        // and phase 4's attribute folds start further down.
        var own = Fold(30, 50);
        var containing = Fold(20, 90);

        Vb6FoldingAdapter.Merge([own], [containing]).Should().Equal(containing, own);
    }

    [Fact]
    public void AZeroLengthFoldIsDropped()
    {
        var kept = Fold(70, 80);

        Vb6FoldingAdapter.Merge([Fold(10, 10)], [Fold(60, 60), kept]).Should().Equal(kept);
    }

    [Fact]
    public void WithNoOwnFoldsAServersAreKeptAndSorted()
    {
        // The related-document window, which has no header.
        Vb6FoldingAdapter.Merge([], [Fold(50, 60), Fold(0, 20)])
            .Select(f => f.StartOffset).Should().Equal(0, 50);
    }
}
