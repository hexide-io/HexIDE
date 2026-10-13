using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.Serialization;

/// <summary>
/// Which runs of <c>Attribute</c> lines describe a member, and which line each one describes
/// (hexide-io/HexIDE#273 task 4.1; design.md, "Settled before building phase 4").
/// </summary>
/// <remarks>
/// <para>
/// A run describes a member when the logical line directly above it declares the qualifier of every line in it,
/// read lexically. Anything else is an orphan, which is inert text rather than a read-only region, so every
/// orphan case here also asserts that <see cref="ReadOnlyRegions.Of"/> leaves it out.
/// </para>
/// <para>
/// Offsets are asserted exactly, as in <see cref="ReadOnlyRegionsTests"/>: the folds, Enter and deletion that
/// phase 4 builds on these all start or end at them.
/// </para>
/// </remarks>
public class MemberRunsTests
{
    private const string ClassHeader =
        "VERSION 1.0 CLASS\r\n" +
        "BEGIN\r\n" +
        "  MultiUse = -1  'True\r\n" +
        "END\r\n" +
        "Attribute VB_Name = \"Tide\"\r\n" +
        "Attribute VB_Exposed = False\r\n";

    private static IReadOnlyList<MemberRun> RunsOf(string code) => ReadOnlyRegions.MemberRuns(ClassHeader + code, ClassHeader.Length);

    private static IReadOnlyList<TextRegion> RegionsOf(string code) => ReadOnlyRegions.Of(ClassHeader + code, ClassHeader.Length);

    private static int At(string code, string text) => ClassHeader.Length + code.IndexOf(text, StringComparison.Ordinal);

    // -- Runs that describe a member -------------------------------------------------------------------

    [Fact]
    public void AProceduresRunDescribesItsDeclaration()
    {
        const string code =
            "Option Explicit\r\n" +
            "Public Function Total() As Currency\r\n" +
            "Attribute Total.VB_Description = \"The order total\"\r\n" +
            "Attribute Total.VB_UserMemId = 0\r\n" +
            "    Total = 0\r\n" +
            "End Function\r\n";
        var anchor = At(code, "\r\nAttribute Total.VB_Description");

        var run = RunsOf(code).Should().ContainSingle().Subject;

        run.Qualifiers.Should().Equal("Total", "Total");
        run.DescribedStart.Should().Be(At(code, "Public Function"));
        run.Anchor.Should().Be(anchor);
        run.Region.Should().Be(new TextRegion(At(code, "Attribute Total.VB_Description"), At(code, "    Total = 0"), Anchor: anchor));
        run.LastContentEnd.Should().Be(At(code, "\r\n    Total = 0"));
        run.Declared.Should().Equal(new DeclaredName("Total", At(code, "Total() As")));
        run.DescribesMember.Should().BeTrue();
        run.Key.Should().Be(new MemberRunKey("TOTAL", 0));
        RegionsOf(code).Should().HaveCount(2).And.Contain(run.Region, "a run that describes a member is a read-only region");
    }

    [Fact]
    public void AWithEventsVariablesHelpIdRunDescribesItsDeclaration()
    {
        // VB6 writes a VB_VarHelpID line under a WithEvents variable (the VB98 add-in template's designer has one).
        const string code =
            "Option Explicit\r\n" +
            "Private WithEvents mGame As Game\r\n" +
            "Attribute mGame.VB_VarHelpID = -1\r\n" +
            "\r\n" +
            "Private Sub Class_Initialize()\r\n" +
            "End Sub\r\n";

        var run = RunsOf(code).Should().ContainSingle().Subject;

        run.Declared.Should().Equal(new DeclaredName("mGame", At(code, "mGame As")));
        run.DescribesMember.Should().BeTrue();
        run.Key.Should().Be(new MemberRunKey("MGAME", 0));
        run.Region.End.Should().Be(At(code, "\r\nPrivate Sub"), "the blank line after the run is the developer's");
    }

    [Fact]
    public void ARunUnderADeclarationWithATrailingCommentDescribesIt()
    {
        // The one member run in the VB98 template tree, in ADDIN.DSR: the comment is part of the described line,
        // so the anchor is after it.
        const string code =
            "Public WithEvents MenuHandler As CommandBarEvents          'command bar event handler\r\n" +
            "Attribute MenuHandler.VB_VarHelpID = -1\r\n";

        var run = RunsOf(code).Should().ContainSingle().Subject;

        run.DescribesMember.Should().BeTrue();
        run.Anchor.Should().Be(At(code, "\r\nAttribute"));
        run.Declared.Select(d => d.Name).Should().Equal("MenuHandler");
    }

    [Fact]
    public void AContinuedDeclarationIsDescribedFromItsFirstLineAndAnchoredAtItsLast()
    {
        // The trailing spaces after the second continuation are the ones the parity test gives every line, and
        // must not end the logical line.
        const string code =
            "Public Function Total(ByVal a As Long, _\r\n" +
            "                      ByVal b As Long, _  \r\n" +
            "                      ByVal c As Long) As Currency\r\n" +
            "Attribute Total.VB_Description = \"x\"\r\n" +
            "End Function\r\n";

        var run = RunsOf(code).Should().ContainSingle().Subject;

        run.DescribedStart.Should().Be(At(code, "Public Function"), "the described line starts on its first physical line");
        run.Anchor.Should().Be(At(code, "\r\nAttribute"), "and ends on its last");
        run.Declared.Should().Equal(new DeclaredName("Total", At(code, "Total(")));
        run.DescribesMember.Should().BeTrue();
    }

    [Fact]
    public void ANameOnTheSecondLineOfAContinuedDeclarationIsDeclaredThere()
    {
        const string code =
            "Public A As Long, _\r\n" +
            "       B As Long\r\n" +
            "Attribute B.VB_VarDescription = \"b\"\r\n";

        var run = RunsOf(code).Should().ContainSingle().Subject;

        run.Declared.Should().Equal(new DeclaredName("A", At(code, "A As")), new DeclaredName("B", At(code, "B As")));
        run.DescribesMember.Should().BeTrue();
    }

    [Fact]
    public void ATwoDotQualifierIsTheNameBeforeTheFirstDot()
    {
        const string code =
            "Public Property Get Total() As Currency\r\n" +
            "Attribute Total.VB_ProcData.VB_Invoke_Property = \"General\"\r\n" +
            "End Property\r\n";

        var run = RunsOf(code).Should().ContainSingle().Subject;

        run.Qualifiers.Should().Equal("Total");
        run.DescribesMember.Should().BeTrue();
    }

    private const string PropertyPair =
        "Public Property Get Value() As Long\r\n" +
        "Attribute Value.VB_UserMemId = 0\r\n" +
        "    Value = m\r\n" +
        "End Property\r\n" +
        "Public Property Let Value(ByVal v As Long)\r\n" +
        "Attribute value.VB_Description = \"The value\"\r\n" +
        "    m = v\r\n" +
        "End Property\r\n";

    [Fact]
    public void APropertysGetAndLetEachDescribeTheirOwnRunUnderDistinctKeys()
    {
        // A name alone cannot tell a property's procedures apart, so the key carries an ordinal, and the
        // qualifier is compared without case.
        var runs = RunsOf("Private m As Long\r\n" + PropertyPair);

        runs.Should().HaveCount(2).And.OnlyContain(r => r.DescribesMember);
        runs.Select(r => r.Key).Should().Equal(new MemberRunKey("VALUE", 0), new MemberRunKey("VALUE", 1));
        RegionsOf("Private m As Long\r\n" + PropertyPair).Should().HaveCount(3);
    }

    [Fact]
    public void ACommaListDeclaresEveryNameAndOneRunMayDescribeThemAll()
    {
        const string code =
            "Public A As Long, B() As String, C(1 To 3, 2) As Integer, WithEvents D As Timer\r\n" +
            "Attribute A.VB_VarDescription = \"a\"\r\n" +
            "Attribute C.VB_VarDescription = \"c\"\r\n" +
            "Attribute D.VB_VarHelpID = -1\r\n";

        var run = RunsOf(code).Should().ContainSingle().Subject;

        run.Declared.Select(d => d.Name).Should().Equal("A", "B", "C", "D");
        run.Qualifiers.Should().Equal("A", "C", "D");
        run.DescribesMember.Should().BeTrue();
        run.Key.Should().Be(new MemberRunKey("A", 0), "a run is keyed by its first qualifier");
    }

    // -- The declaration reader ------------------------------------------------------------------------

    [Theory]
    [InlineData("Sub Main()", "Main")]
    [InlineData("Private Static Function Twice(ByVal n As Long) As Long", "Twice")]
    [InlineData("Friend Property Set Target(ByVal o As Object)", "Target")]
    [InlineData("public property let value(byval v as long)", "value")]
    [InlineData("Public Event Changed(ByVal Value As Long)", "Changed")]
    [InlineData("Private Declare Function GetTickCount Lib \"kernel32\" () As Long", "GetTickCount")]
    [InlineData("Declare PtrSafe Sub Sleep Lib \"kernel32\" (ByVal ms As Long)", "Sleep")]
    [InlineData("Dim a As Long, b(1 To 3, 4) As String, c", "a,b,c")]
    [InlineData("Global Count%, Name$", "Count,Name")]
    [InlineData("Public Total$ ' isn't it", "Total")]
    [InlineData("Private WithEvents Clock As Timer, WithEvents Alarm As Timer", "Clock,Alarm")]
    [InlineData("Public s As String * 10, t As New Collection", "s,t")]
    [InlineData("Static Count As Long", "Count")]
    [InlineData("Dim [Odd Name] As Long", "Odd Name")]
    [InlineData("Public X As Long: Public Y As Long", "X")]
    public void TheDeclarationReaderFindsTheNamesALineDeclares(string line, string names)
    {
        ReadOnlyRegions.NamesDeclaredOn(line).Select(d => d.Name).Should().Equal(names.Split(','));
    }

    [Theory]
    [InlineData("Total = 0")]
    [InlineData("Call Go")]
    [InlineData("ReDim a(5)")]
    [InlineData("Public Const Rate = 5")]
    [InlineData("Public Enum Colors")]
    [InlineData("Private Type Point")]
    [InlineData("Option Explicit")]
    [InlineData("Implements IShape")]
    [InlineData("End Sub")]
    [InlineData("' Public Sub Go()")]
    [InlineData("Rem Public Sub Go()")]
    [InlineData("")]
    public void ALineThatIsNotADeclarationDeclaresNothing(string line)
    {
        ReadOnlyRegions.NamesDeclaredOn(line).Should().BeEmpty();
    }

    [Fact]
    public void EachNameIsFoundWhereItIsWritten()
    {
        const string line = "Public  Function  Total$ (ByVal a As Long)";

        ReadOnlyRegions.NamesDeclaredOn(line).Should().ContainSingle()
            .Which.Should().Be(new DeclaredName("Total", line.IndexOf("Total", StringComparison.Ordinal)),
                "the type character is not part of the name, and the offset is the name's own");
    }

    // -- Orphans ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Public Sub Go()\r\n\r\nAttribute Go.VB_Description = \"x\"\r\nEnd Sub\r\n", "under a blank line")]
    [InlineData("Public Sub Go()\r\n' describes Go\r\nAttribute Go.VB_Description = \"x\"\r\nEnd Sub\r\n", "under a comment")]
    [InlineData("Public Sub Go()\r\n    Debug.Print 1\r\nAttribute Go.VB_Description = \"x\"\r\nEnd Sub\r\n", "under a body statement")]
    [InlineData("Public Sub Go()\r\nEnd Sub\r\nAttribute Go.VB_Description = \"x\"\r\n", "under the end of the member it names")]
    [InlineData("Public Sub Halt()\r\nAttribute Go.VB_Description = \"x\"\r\nEnd Sub\r\nPublic Sub Go()\r\nEnd Sub\r\n", "under another member")]
    [InlineData("Public Sub Go()\r\nAttribute Go.VB_Description = \"x\"\r\nAttribute Halt.VB_Description = \"y\"\r\nEnd Sub\r\n", "one of its lines names another member")]
    [InlineData("Option Explicit\r\nAttribute VB_Description = \"x\"\r\n", "a line with no qualifier below the code")]
    [InlineData("Public Sub Go()\r\nAttribute VB_Description = \"x\"\r\nEnd Sub\r\n", "a line with no qualifier under a declaration")]
    public void ARunTheLineAboveDoesNotDeclareDescribesNothingAndIsNoRegion(string code, string because)
    {
        var run = RunsOf(code).Should().ContainSingle(because).Subject;

        run.DescribesMember.Should().BeFalse(because);
        run.Key.Should().BeNull("an orphan is not counted among a member's runs");
        RegionsOf(code).Should().ContainSingle("only the header is read-only: " + because)
            .Which.Anchor.Should().BeNull();

        var text = ClassHeader + code;
        var line = text[..run.Region.Start].Count(c => c == '\n') + 1;
        ReadOnlyRegions.IsReadOnlyLine(text, RegionsOf(code), line).Should().BeFalse("its line takes a mark like any other");
    }

    [Fact]
    public void ADotlessLineHasNoQualifier()
    {
        RunsOf("Option Explicit\r\nAttribute VB_Description = \"x\"\r\n").Single().Qualifiers
            .Should().ContainSingle().Which.Should().BeNull();
    }

    [Fact]
    public void ABlankLineInsideAMembersRunLeavesTheHalfAfterItAnOrphan()
    {
        // A blank line ends a run, so the second half hangs from the blank line and describes nothing. Whether
        // VB6 honours such a half is an oracle question (design.md); until it is answered it is inert text.
        const string code =
            "Public Function Total() As Currency\r\n" +
            "Attribute Total.VB_Description = \"x\"\r\n" +
            "\r\n" +
            "Attribute Total.VB_UserMemId = 0\r\n" +
            "End Function\r\n";

        var runs = RunsOf(code);

        runs.Select(r => r.DescribesMember).Should().Equal(true, false);
        RegionsOf(code).Should().HaveCount(2).And.Contain(runs[0].Region).And.NotContain(runs[1].Region);
    }

    [Fact]
    public void ARunThatDescribesAndAnOrphanSitSideBySide()
    {
        const string code =
            "Public Sub Go()\r\n" +
            "Attribute Go.VB_Description = \"Goes\"\r\n" +
            "    Debug.Print 1\r\n" +
            "Attribute Go.VB_UserMemId = 0\r\n" +
            "End Sub\r\n";

        var runs = RunsOf(code);

        runs.Select(r => r.DescribesMember).Should().Equal(true, false);
        RegionsOf(code).Skip(1).Should().Equal(runs[0].Region);
    }

    // -- Keys ------------------------------------------------------------------------------------------

    [Fact]
    public void TheKeysOutliveAnEditAboveAndAnOrphanBeforeThem()
    {
        // What 4.3's remembered fold state and 4.5b's comparison stand on: offsets move, keys do not. An orphan
        // naming the same member is free text, so it does not shift the ordinals either.
        var before = RunsOf("Private m As Long\r\n" + PropertyPair);
        var after = RunsOf(
            "' a new comment\r\n" +
            "Private m As Long\r\n" +
            "\r\n" +
            "Attribute Value.VB_Description = \"stray\"\r\n" +
            PropertyPair);

        after.Where(r => r.DescribesMember).Select(r => r.Key).Should().Equal(before.Select(r => r.Key));
        after.Where(r => r.DescribesMember).Select(r => r.Region.Start)
            .Should().NotEqual(before.Select(r => r.Region.Start), "the premise: the runs did move");
    }

    // -- The header ------------------------------------------------------------------------------------

    [Fact]
    public void TheHeaderIsNeverAMemberRunAndAnOrphanDoesNotMoveIt()
    {
        // An attribute line straight after the header's own run is the header's, whatever it names; and an
        // orphan below the code leaves the header where it was.
        var absorbed = ClassHeader + "Attribute Total.VB_Description = \"x\"\r\nOption Explicit\r\n";
        ReadOnlyRegions.MemberRuns(absorbed, ClassHeader.Length).Should().BeEmpty();
        ReadOnlyRegions.HeaderEnd(absorbed, ClassHeader.Length).Should().Be(absorbed.IndexOf("Option", StringComparison.Ordinal));

        var orphaned = ClassHeader + "Option Explicit\r\nAttribute VB_Description = \"x\"\r\n";
        ReadOnlyRegions.HeaderEnd(orphaned, ClassHeader.Length).Should().Be(ClassHeader.Length);
        ReadOnlyRegions.HeaderEnd(orphaned, 0).Should().Be(ClassHeader.Length, "found from the text alone too");
        ReadOnlyRegions.Of(orphaned, 0).Should().Equal(new TextRegion(0, ClassHeader.Length));
    }

    [Fact]
    public void AFormsRunsAreFoundBelowItsHeaderWhichStraddlesThePrefix()
    {
        const string designer =
            "VERSION 5.00\r\n" +
            "Begin VB.Form Form1 \r\n" +
            "   Caption         =   \"Form1\"\r\n" +
            "End\r\n";
        var text = designer +
                   "Attribute VB_Name = \"Form1\"\r\n" +
                   "Option Explicit\r\n" +
                   "Public Sub Go()\r\n" +
                   "Attribute Go.VB_Description = \"Goes\"\r\n" +
                   "End Sub\r\n";

        var run = ReadOnlyRegions.MemberRuns(text, designer.Length).Should().ContainSingle().Subject;

        run.DescribesMember.Should().BeTrue();
        run.Region.Start.Should().BeGreaterThan(ReadOnlyRegions.HeaderEnd(text, designer.Length));
    }
}
