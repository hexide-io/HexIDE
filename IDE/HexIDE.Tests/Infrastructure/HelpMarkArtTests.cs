using System.Text;
using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// The contract the checked-in colour mark has to keep for <c>--help</c> to compose it beside text.
/// </summary>
/// <remarks>
/// <para>
/// <c>tools/hexlogo/hexide-logo-24.ans</c> is generated, embedded into <c>HexIDE.Desktop</c> at build,
/// and laid out by <see cref="ConsoleLayout"/> on the assumption that every row is exactly 24 visible
/// cells. Nothing at build time checks that: an embedded resource is bytes, and a regeneration at the
/// wrong width, without <c>--pad</c>, or saved by an editor that trimmed trailing spaces would compose
/// into a help screen with a ragged text column. This reads the file as source, the way
/// <see cref="CommandLineDocumentationTests"/> reads the option table, because the desktop assembly is
/// not built when this suite runs.
/// </para>
/// <para>
/// The numbers here are the ones <c>HelpMark</c> declares. They are restated rather than referenced for
/// the same reason: nothing in the tree references <c>HexIDE.Desktop</c>.
/// </para>
/// </remarks>
public class HelpMarkArtTests
{
    private const int Width = 24;
    private const int Rows = 12;
    private const string Reset = "\x1b[0m";

    private static string ArtPath() => Path.Combine(RepoTree.Root(), "tools", "hexlogo", "hexide-logo-24.ans");

    private static string[] ArtRows() =>
        File.ReadAllText(ArtPath(), Encoding.UTF8).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(row => row.TrimEnd('\r')).ToArray();

    [Fact]
    public void TheArtIsTwelveRowsOfTwentyFourCells()
    {
        var rows = ArtRows();

        rows.Should().HaveCount(Rows);
        foreach (var row in rows)
        {
            ConsoleLayout.VisibleWidth(row).Should().Be(Width,
                "every row must be padded to the mark's width or the text column beside it goes ragged; "
              + "regenerate with --pad (see tools/hexlogo/README.md)");
        }
    }

    [Fact]
    public void EveryRowEndsByResettingColour()
    {
        // A row that leaves colour set bleeds it into the gutter and the text after it.
        foreach (var row in ArtRows())
        {
            row.Should().EndWith(Reset);
        }
    }

    [Fact]
    public void TheArtUsesOnlyColourSequences_NeverCursorMovement()
    {
        // SGR ("m") is the only kind of escape that is safe to concatenate with text and safe to write to a
        // file. Anything else — cursor moves, erases, mode switches — would be a picture that is only right
        // on a live terminal, and would be wrong beside the text.
        var text = File.ReadAllText(ArtPath(), Encoding.UTF8);
        var escapes = Regex.Matches(text, "\x1b\\[[^m]*.");

        escapes.Should().NotBeEmpty("the file is colour art, so it carries escape sequences");
        foreach (Match escape in escapes)
        {
            escape.Value.Should().MatchRegex("^\x1b\\[[0-9;]*m$");
        }
    }

    [Fact]
    public void TheArtIsUtf8WithoutABom_AndLfLineEndings()
    {
        var bytes = File.ReadAllBytes(ArtPath());

        bytes.Take(3).Should().NotEqual([0xEF, 0xBB, 0xBF], "a BOM would print as a stray glyph before the first row");
        bytes.Should().NotContain((byte)'\r', "the file is pinned to LF in .gitattributes; CR would be doubled on Windows by WriteLine");
    }

    [Fact]
    public void EveryRowCarriesTwentyFourBitColour()
    {
        // The file is the truecolour rendering; a 256-colour or monochrome one saved over it would keep the
        // row contract and still be the wrong picture. A changed colour inside the right depth is beyond
        // this test and is what the README's "regenerate and diff" instruction is for.
        ArtRows().Should().OnlyContain(row => row.Contains("\x1b[38;2;", StringComparison.Ordinal),
            "every row of the mark carries at least one 24-bit foreground colour");
    }
}
