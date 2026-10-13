using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.Serialization;

/// <summary>
/// Where the read-only regions of a code window's buffer are (hexide-io/HexIDE#273 phase 3): the header, and
/// each run of attribute lines describing a member.
/// </summary>
/// <remarks>
/// Every region is asserted as exact offsets, never as "contains". A region one character short leaves the
/// last attribute line's terminator writable, which is exactly how a guarded write would glue the first line
/// of code onto the header.
/// </remarks>
public class ReadOnlyRegionsTests
{
    private const string ClassHeader =
        "VERSION 1.0 CLASS\r\n" +
        "BEGIN\r\n" +
        "  MultiUse = -1  'True\r\n" +
        "END\r\n" +
        "Attribute VB_Name = \"Tide\"\r\n" +
        "Attribute VB_Exposed = False\r\n";

    private const string Designer =
        "VERSION 5.00\r\n" +
        "Object = \"{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0\"; \"MSCOMCTL.OCX\"\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Caption         =   \"Form1\"\r\n" +
        "   BeginProperty Font \r\n" +
        "      Name            =   \"Tahoma\"\r\n" +
        "   EndProperty\r\n" +
        "   Begin VB.CommandButton Command1 \r\n" +
        "      Caption         =   \"OK\"\r\n" +
        "   End\r\n" +
        "End\r\n";

    private const string FormAttributes =
        "Attribute VB_Name = \"Form1\"\r\n" +
        "Attribute VB_PredeclaredId = True\r\n";

    // -- The header ------------------------------------------------------------------------------------

    [Fact]
    public void AClassHeaderIsItsPrefixWhichAlreadyEndsAfterTheAttributeRun()
    {
        var text = ClassHeader + "Option Explicit\r\n";

        ReadOnlyRegions.Of(text, ClassHeader.Length).Should().Equal(new TextRegion(0, ClassHeader.Length));
        ReadOnlyRegions.HeaderEnd(text, ClassHeader.Length).Should().Be(ClassHeader.Length);
    }

    [Fact]
    public void AFormsHeaderStraddlesTheSplitAndRunsThroughTheAttributeLinesInItsCode()
    {
        // The case the design record warns about. The prefix is the designer block alone, because a form's
        // Code already begins with its attribute run -- so a region that stopped at the prefix would leave
        // VB_Name writable, and one built by prepending the run would count it twice.
        var text = Designer + FormAttributes + "Option Explicit\r\n";

        ReadOnlyRegions.Of(text, Designer.Length)
            .Should().Equal(new TextRegion(0, Designer.Length + FormAttributes.Length));
    }

    [Fact]
    public void WithNothingSplitOffTheDesignerHeaderIsFoundFromTheText()
    {
        // An unparseable .ctl keeps its whole file in Code, so the prefix is empty and the buffer still
        // opens with a designer block. The walk goes by nesting: the font's EndProperty and the button's End
        // must not be taken for the form's.
        var text = Designer + FormAttributes + "Option Explicit\r\n";

        ReadOnlyRegions.Of(text, 0)
            .Should().Equal(new TextRegion(0, Designer.Length + FormAttributes.Length));
    }

    [Fact]
    public void AndSoIsAClassHeader()
    {
        var text = ClassHeader + "Option Explicit\r\n";

        ReadOnlyRegions.Of(text, 0).Should().Equal(new TextRegion(0, ClassHeader.Length));
    }

    [Fact]
    public void AModuleWhoseFirstLineIsBlankStillHasItsAttributeLinesProtected()
    {
        // hexide-io/HexIDE#472: the header reader tests only line 0, so a leading blank line leaves the whole
        // file in Code. The blank line goes into the region with the run it precedes.
        const string text = "\r\nAttribute VB_Name = \"Module1\"\r\nOption Explicit\r\n";

        ReadOnlyRegions.Of(text, 0).Should().Equal(new TextRegion(0, text.IndexOf("Option", StringComparison.Ordinal)));
    }

    [Fact]
    public void TextThatOpensWithCodeHasNoHeader()
    {
        const string text = "\r\n\r\nPrivate Sub Form_Load()\r\nEnd Sub\r\n";

        ReadOnlyRegions.Of(text, 0).Should().BeEmpty();
        ReadOnlyRegions.HeaderEnd(text, 0).Should().Be(0);
    }

    [Fact]
    public void BlankLinesAfterTheRunAreTheDevelopersNotTheHeaders()
    {
        var text = ClassHeader + "\r\n\r\nOption Explicit\r\n";

        ReadOnlyRegions.HeaderEnd(text, ClassHeader.Length).Should().Be(ClassHeader.Length);
    }

    [Fact]
    public void LfTextGivesTheSameRegionsAtItsOwnOffsets()
    {
        // A buffer carries whatever line ending it was composed with. The offsets must be the text's own.
        var lf = (Designer + FormAttributes + "Option Explicit\r\n").Replace("\r\n", "\n");
        var designerLf = Designer.Replace("\r\n", "\n");

        ReadOnlyRegions.Of(lf, designerLf.Length)
            .Should().Equal(new TextRegion(0, designerLf.Length + FormAttributes.Replace("\r\n", "\n").Length));
    }

    // -- Member attribute runs -------------------------------------------------------------------------

    [Fact]
    public void AProceduresAttributeLinesAreARegionOfTheirOwn()
    {
        var code =
            "Option Explicit\r\n" +
            "Public Property Get Value() As Long\r\n" +
            "Attribute Value.VB_UserMemId = 0\r\n" +
            "Attribute Value.VB_Description = \"The value\"\r\n" +
            "    Value = 1\r\n" +
            "End Property\r\n";
        var text = ClassHeader + code;
        var start = text.IndexOf("Attribute Value.VB_UserMemId", StringComparison.Ordinal);
        var end = text.IndexOf("    Value = 1", StringComparison.Ordinal);

        ReadOnlyRegions.Of(text, ClassHeader.Length)
            .Should().Equal(new TextRegion(0, ClassHeader.Length),
                new TextRegion(start, end, Anchor: start - 2));
    }

    [Fact]
    public void AVariablesAttributeLineIsOneToo()
    {
        var text = ClassHeader + "Public Handle As Long\r\nAttribute Handle.VB_VarUserMemId = 0\r\n";
        var start = text.IndexOf("Attribute Handle", StringComparison.Ordinal);

        ReadOnlyRegions.Of(text, ClassHeader.Length)[1]
            .Should().Be(new TextRegion(start, text.Length, Anchor: start - 2));
    }

    [Theory]
    [InlineData("' Attribute X.VB_Description = \"x\"")]
    [InlineData("AttributeCount = 3")]
    [InlineData("Attribute = 5")]            // Attribute is a reserved word, so this is an attribute line naming nothing
    [InlineData("Attribute(2) = 5")]         // and so is this (MemberRunsTests)
    public void CodeThatMerelyStartsWithTheWordIsNoRegion(string line)
    {
        var text = ClassHeader + "Sub Main()\r\n" + line + "\r\nEnd Sub\r\n";

        ReadOnlyRegions.Of(text, ClassHeader.Length).Should().ContainSingle();
    }

    [Fact]
    public void AnAttributeRunOnTheLastLineWithNoTerminatorIsOpenEnded()
    {
        var text = ClassHeader + "Public Handle As Long\r\nAttribute Handle.VB_VarUserMemId = 0";
        var start = text.IndexOf("Attribute Handle", StringComparison.Ordinal);

        ReadOnlyRegions.Of(text, ClassHeader.Length)[1]
            .Should().Be(new TextRegion(start, text.Length, OpenEnded: true, Anchor: start - 2));
    }

    // -- What counts as touching one -------------------------------------------------------------------

    [Fact]
    public void AReplacementOverlappingARegionTouchesIt()
    {
        var region = new TextRegion(10, 20);

        region.Touches(5, 6).Should().BeTrue("the last character replaced is the region's first");
        region.Touches(19, 5).Should().BeTrue("the first character replaced is the region's last");
        region.Touches(5, 5).Should().BeFalse("it ends where the region starts");
        region.Touches(20, 5).Should().BeFalse("it starts where the region ends");
    }

    [Fact]
    public void AnInsertionAtARegionsFirstCharacterTouchesItAndOneAtItsEndDoesNot()
    {
        // At the start it would push the region away from the line above: text above the top of the file,
        // or a line between a declaration and the attribute run that describes it. At the end it is simply
        // the start of the next line.
        var region = new TextRegion(10, 20);

        region.Touches(10, 0).Should().BeTrue();
        region.Touches(15, 0).Should().BeTrue();
        region.Touches(20, 0).Should().BeFalse();
        region.Touches(9, 0).Should().BeFalse();
    }

    [Fact]
    public void TheNamesADesignerBlockDeclaresAreTheFormsAndItsControls()
    {
        var names = ReadOnlyRegions.DeclaredNames(Designer + FormAttributes + "Option Explicit\r\n", 0);

        names.Should().BeEquivalentTo("Form1", "Command1");
        names.Should().Contain("COMMAND1", "VB6 compares names ignoring case");
        ReadOnlyRegions.DeclaredNames(ClassHeader + "Option Explicit\r\n", 0)
            .Should().BeEmpty("a class's BEGIN block declares nothing");
    }

    [Fact]
    public void AnInsertionAtTheEndOfAnOpenEndedRegionJoinsItsLastLine()
    {
        new TextRegion(10, 20, OpenEnded: true).Touches(20, 0).Should().BeTrue();
    }

    // -- What counts as changing one (#273 task 3.11) --------------------------------------------------

    private const string DescribedMember =
        "Public Function Total() As Currency\r\n" +
        "Attribute Total.VB_Description = \"The order total\"\r\n" +
        "    Total = 0\r\n" +
        "End Function\r\n";

    private static (TextRegion Run, int DeclarationEnd) TheRun()
    {
        var text = ClassHeader + DescribedMember;
        return (ReadOnlyRegions.Of(text, ClassHeader.Length)[1], text.IndexOf("\r\nAttribute Total", StringComparison.Ordinal));
    }

    [Fact]
    public void RemovingTheLineBreakARunHangsFromChangesTheRunThoughItDoesNotTouchIt()
    {
        // Joining the attribute line onto the declaration destroys the run: the line no longer starts with
        // Attribute. The line break is outside the run, so Touches says no; Changes is what a writer asks.
        var (run, declarationEnd) = TheRun();

        run.Anchor.Should().Be(declarationEnd);
        run.Touches(declarationEnd, 2).Should().BeFalse();
        run.Changes(declarationEnd, 2).Should().BeTrue();
        run.Changes(declarationEnd - "Currency".Length, "Currency\r\n".Length).Should().BeTrue();
        ReadOnlyRegions.Changes([run], declarationEnd, 1).Should().BeTrue("even half a CRLF");
    }

    [Fact]
    public void RewritingADeclarationsTextOrTypingAtItsEndDoesNotChangeItsRun()
    {
        var (run, declarationEnd) = TheRun();
        var declarationStart = declarationEnd - "Public Function Total() As Currency".Length;

        run.Changes(declarationStart, declarationEnd - declarationStart).Should().BeFalse(
            "re-casing or re-indenting a declaration leaves its line break alone");
        run.Changes(declarationEnd, 0).Should().BeFalse("text typed at the end of the line lands before its break");
        run.Changes(run.Start, 0).Should().BeTrue("text between the declaration and its run separates them");
    }

    [Fact]
    public void TheHeaderHasNoAnchor()
    {
        var header = ReadOnlyRegions.Of(ClassHeader + DescribedMember, ClassHeader.Length)[0];

        header.Anchor.Should().BeNull();
        header.EditStart.Should().Be(header.Start);
    }
}
