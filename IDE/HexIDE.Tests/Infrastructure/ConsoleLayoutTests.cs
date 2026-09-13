namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// The composition <c>--help</c> uses to put the mark beside or above its text, and the one measurement it
/// depends on: a line's width on screen with its colour codes discounted.
/// </summary>
public class ConsoleLayoutTests
{
    private const string Esc = "\x1b";

    // A "mark" three rows tall and four cells wide, coloured so the widths have something to discount.
    private static readonly string[] Mark =
    [
        $"{Esc}[38;2;255;0;0m▀▀▀▀{Esc}[0m",
        $"{Esc}[48;2;0;0;255m{Esc}[38;2;255;255;255m▀▀{Esc}[0m  {Esc}[0m",
        $"{Esc}[0m    {Esc}[0m",
    ];

    [Fact]
    public void VisibleWidth_DiscountsColourAndCountsGlyphs()
    {
        ConsoleLayout.VisibleWidth(Mark[0]).Should().Be(4);
        ConsoleLayout.VisibleWidth(Mark[1]).Should().Be(4);
        ConsoleLayout.VisibleWidth(Mark[2]).Should().Be(4);
        ConsoleLayout.VisibleWidth("plain").Should().Be(5);
        ConsoleLayout.VisibleWidth("").Should().Be(0);
    }

    [Fact]
    public void Beside_PutsEachTextRowAfterTheMarkRowAndAGutter()
    {
        var lines = ConsoleLayout.Beside(Mark, ["one", "two", "three"], markWidth: 4);

        lines.Should().HaveCount(3);
        lines[0].Should().Be(Mark[0] + "  one");
        lines[1].Should().Be(Mark[1] + "  two");
        lines[2].Should().Be(Mark[2] + "  three");
    }

    [Fact]
    public void Beside_IndentsTextThatOutrunsTheMark_SoTheColumnStaysStraight()
    {
        var lines = ConsoleLayout.Beside(Mark, ["a", "b", "c", "d", "e"], markWidth: 4);

        lines.Should().HaveCount(5);
        lines[3].Should().Be("      d", "four cells of mark-width blank, then the gutter");
        lines[4].Should().Be("      e");
    }

    [Fact]
    public void Beside_EndsWithTheMark_WhereThereIsNoTextForARow()
    {
        var lines = ConsoleLayout.Beside(Mark, ["only"], markWidth: 4);

        lines.Should().HaveCount(3);
        lines[0].Should().Be(Mark[0] + "  only");
        // No gutter dangling after a mark row, and no trailing whitespace on a plain one — but a colour row's
        // reset is not whitespace, and stays.
        lines[1].Should().Be(Mark[1]);
        lines[2].Should().Be(Mark[2]);
    }

    [Fact]
    public void Beside_TreatsABlankTextRowAsAbsent()
    {
        var lines = ConsoleLayout.Beside(["....", "...."], ["", "x"], markWidth: 4);

        lines[0].Should().Be("....", "a blank right-hand row must not leave a gutter behind");
        lines[1].Should().Be("....  x");
    }

    [Fact]
    public void Stacked_PutsTheMarkAboveTheText_WithARowBetween()
    {
        var lines = ConsoleLayout.Stacked(["####  ", "#  # "], ["one", "two"]);

        lines.Should().Equal("####", "#  #", "", "one", "two");
    }

    [Fact]
    public void Stacked_DoesNotPileUpBlankRowsMeantForTheBesideLayout()
    {
        // A mark padded to a fixed box has blank rows at the bottom, and the text starts blank so its
        // title lands on the mark's second row when beside. Stacked, those would make a four-row gap.
        var lines = ConsoleLayout.Stacked(["    ", "####", "    ", "    "], ["", "one"]);

        lines.Should().Equal("", "####", "", "one");
    }

    [Theory]
    [InlineData(80, 24, 54, true)]   // 24 + 2 + 54 = 80: exactly fits
    [InlineData(79, 24, 54, false)]  // one short wraps
    [InlineData(int.MaxValue, 24, 200, true)]
    public void FitsBeside_NeedsMarkGutterAndLongestLine(int consoleWidth, int markWidth, int longest, bool fits)
    {
        string[] text = ["short", new string('x', longest), $"{Esc}[1m" + new string('y', longest) + $"{Esc}[0m"];

        ConsoleLayout.FitsBeside(consoleWidth, markWidth, text).Should().Be(fits);
    }

    [Fact]
    public void FitsBeside_WithNoText_NeedsOnlyTheMark()
    {
        ConsoleLayout.FitsBeside(26, 24, []).Should().BeTrue();
        ConsoleLayout.FitsBeside(25, 24, []).Should().BeFalse();
    }
}
