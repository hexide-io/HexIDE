using HexIDE.IDE;

namespace HexIDE.Tests.IDE;

// Existing request rules moved intact by #4139; CommonDialog measurements: #1084 and
// docs/vb6-fidelity-oracle/in-box-controls-the-commondialog.md. No new VB6 measurements.
public class VbFileDialogRequestTests
{
    private static VbFileDialogRequest Request(int index = 1, string name = "") =>
        new(false, null, [("Text", " *.txt ; ; *.log "), ("Basic", "*.bas")], index, "", name, "", false);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 0)]
    public void First_pair_position(int index, int expected) =>
        Request(index).FirstFilterPosition.Should().Be(expected);

    [Fact]
    public void No_pairs_have_no_first_position() =>
        (Request() with { Filters = [] }).FirstFilterPosition.Should().BeNull();

    [Theory]
    [InlineData(@"C:\folder\picked.txt", "picked.txt")]
    [InlineData("/folder/picked.txt", "picked.txt")]
    [InlineData("", null)]
    public void Suggested_name_is_the_last_part_or_null(string name, string? expected) =>
        Request(name: name).SuggestedName.Should().Be(expected);

    [Fact]
    public void Patterns_trim_spaces_and_remove_empty_entries() =>
        Request().Patterns(0).Should().Equal("*.txt", "*.log");

    [Theory]
    [InlineData("Text", "*.txt", "*.log", 1)]
    [InlineData("Other", "*.txt", "*.log", null)]
    [InlineData("Text", "*.log", "*.txt", null)]
    public void Answered_pair_matches_description_and_patterns(string description, string first, string second, int? expected) =>
        Request().IndexOf(description, [first, second]).Should().Be(expected);

    [Fact]
    public void Answered_pair_position_counts_from_one() =>
        Request().IndexOf("Basic", ["*.bas"]).Should().Be(2);
}
