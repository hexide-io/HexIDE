using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// Keeps the command line, its own <c>--help</c>, and the page that documents it from drifting apart.
/// </summary>
/// <remarks>
/// <para>
/// <b>A program that documents itself incorrectly is worse than one that does not document itself.</b>
/// A reader who finds no `--help` goes and looks; a reader handed a `--help` that omits a flag concludes
/// the flag does not exist. The same goes for the page: `docs/command-line.md` is the only user-facing
/// account, so a flag missing from it is a flag nobody outside this repository can discover.
/// </para>
/// <para>
/// The help text cannot drift on its own — it is rendered from the same list the parser reads, which is
/// the structural half of this. What a shared list cannot enforce is the prose, so that is what these
/// assert. This is the guard <c>docs/lsp-client.md</c> has against the LSP metamodel and
/// <c>en.json</c> has against the AXAML, applied to the third surface with the same failure mode.
/// </para>
/// <para>
/// <b>Read from the source, not by reflecting over the built assembly</b>, and that is not a stylistic
/// preference. Nothing in the tree references <c>HexIDE.Desktop</c>, so its <c>bin/</c> holds an assembly
/// only once somebody has built that project: on CI the step that does so runs two steps AFTER this
/// suite, and in a fresh clone running the documented <c>dotnet test HexIDE.Tests/</c> it never runs at
/// all. The reflecting version therefore failed three tests on first contact and passed only on a
/// machine that happened to have built the IDE — a guard reporting on the state of a working directory
/// rather than on the thing it was written to check. Both halves of the question are claims about
/// source anyway, so nothing is lost by reading it.
/// </para>
/// </remarks>
public class CommandLineDocumentationTests
{
    private static readonly string[] Flags = FlagNames();

    /// <summary>
    /// The flag names, read out of the desktop project's own option table.
    /// </summary>
    /// <remarks>
    /// Read from the table rather than restated here, so this test cannot itself become the stale copy it
    /// exists to prevent. Scoped to the table's own bounds, so an unrelated <c>new("…")</c> elsewhere in
    /// the file cannot be mistaken for an option.
    /// </remarks>
    private static string[] FlagNames() =>
    [
        .. Regex.Matches(OptionTableSource(), """new\("(?<name>[a-z][a-z-]*)",""")
            .Select(match => match.Groups["name"].Value)
    ];

    /// <summary>
    /// The text of the option table itself, sliced out of <c>ServerOptions.cs</c>.
    /// </summary>
    /// <remarks>
    /// Bounded by the table's own declaration and its closing bracket, so an unrelated <c>new("...")</c>
    /// elsewhere in the file cannot be mistaken for an option. One definition of those bounds, shared by
    /// everything here that reads the table.
    /// </remarks>
    private static string OptionTableSource()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoTree.Root(), "IDE", "HexIDE.Desktop", "ServerOptions.cs"));

        var start = source.IndexOf("Options { get; } =", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "ServerOptions still declares the option table this reads");

        var end = source.IndexOf("];", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, "the option table is closed off where this expects");

        return source[start..end];
    }

    private static string Reference() =>
        File.ReadAllText(Path.Combine(RepoTree.Root(), "docs", "command-line.md"));

    [Fact]
    public void TheTableIsNotEmpty()
    {
        // Everything below passes vacuously if the scan quietly matched nothing, which is exactly how a
        // guard like this fails open.
        Flags.Should().NotBeEmpty("the option table was read from ServerOptions.cs");
        Flags.Should().Contain("help", "the flag these tests exist because of");
    }

    [Fact]
    public void EveryFlagAppearsInTheUserFacingReference()
    {
        var reference = Reference();

        foreach (var flag in Flags)
        {
            // A ROW IN THE OPTIONS TABLE, not a mention anywhere on the page. The first version of this
            // asserted the substring and passed against the sentence "There is no `--help`" — a line
            // saying the flag does not exist satisfied the test that it was documented.
            reference.Should().Contain($"| `--{flag}` |",
                $"'--{flag}' is accepted by the parser, so it needs a row in docs/command-line.md's "
              + "options table — that page is the only account of the command line anyone outside this "
              + "repository can read");
        }
    }

    [Fact]
    public void TheReferenceInventsNoFlags()
    {
        // The other direction, and the one that rots quietly: a flag removed from the parser leaves its
        // paragraph behind, and the page goes on promising something the program no longer does.
        var reference = Reference();

        var documented = Regex.Matches(reference, "`--(?<name>[a-z][a-z-]*)`")
            .Select(m => m.Groups["name"].Value)
            .Distinct();

        foreach (var name in documented)
        {
            Flags.Should().Contain(name,
                $"docs/command-line.md documents '--{name}', so the parser has to accept it");
        }
    }

    /// <summary>
    /// Every option as <c>--help</c> will render it: the flag, its value name, and its summary.
    /// </summary>
    /// <remarks>
    /// The name alone is enough to check the documentation; the width budget needs the whole row, so this
    /// reads the first three arguments of every <c>CommandLineOption</c> in the table.
    /// </remarks>
    private static (string Name, string? Value, string Summary)[] Table()
    {
        var table = OptionTableSource();

        // name, then either 'null' or a quoted value name, then the quoted summary.
        var entry = new Regex(
            @"new\(""(?<name>[a-z][a-z-]*)"",\s*(?:null|""(?<value>[^""]*)""),\s*""(?<summary>[^""]*)""");

        return
        [
            .. entry.Matches(table).Select(m => (
                m.Groups["name"].Value,
                m.Groups["value"].Success ? m.Groups["value"].Value : null,
                m.Groups["summary"].Value))
        ];
    }

    /// <summary>The mark's width, read from <c>HelpMark.cs</c> rather than restated here.</summary>
    private static int MarkWidth()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoTree.Root(), "IDE", "HexIDE.Desktop", "HelpMark.cs"));

        var match = Regex.Match(source, @"public const int Width = (?<w>\d+);");
        match.Success.Should().BeTrue("HelpMark still declares the width this reads");

        return int.Parse(match.Groups["w"].Value);
    }

    /// <summary>
    /// The widest option row has to leave the mark room to sit beside it in a default console.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the invariant a comment used to assert and nothing checked.</b> The mark sits to the left
    /// of the option list only while the widest rendered row, plus the mark and the gutter, fits the
    /// console. Miss it by one column and every default-sized window silently falls back to the stacked
    /// layout, which is the arrangement the whole beside path exists to avoid - with a green build, because
    /// nothing composed the help text and measured it.
    /// </para>
    /// <para>
    /// It had already happened. The comment stating the rule said 58 characters, the real limit was 60, and
    /// an option three lines below it was sitting at exactly 60 - so the rule was false in the same list
    /// that stated it, and the widest row was 94 rather than the 84 believed, leaving the layout fitting a
    /// 120-column console by exact equality with no margin at all.
    /// </para>
    /// <para>
    /// Composed the way <c>ServerOptions.HelpText</c> composes it, from the same source, so the two cannot
    /// disagree: the value column is the longest spelling in the table, and each row is two spaces, that
    /// column, two more, then the summary.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheWidestOptionRowLeavesRoomForTheMark()
    {
        var table = Table();
        table.Should().NotBeEmpty("the option table was read from ServerOptions.cs");

        var column = table.Max(o => Spelling(o).Length);
        var rows = table
            .Select(o => (Option: o, Width: 2 + column + 2 + o.Summary.Length))
            .OrderByDescending(r => r.Width)
            .ToArray();

        var budget = DefaultConsoleWidth - MarkWidth() - HexIDE.IDE.ConsoleLayout.Gutter.Length;
        var widest = rows[0];

        widest.Width.Should().BeLessThanOrEqualTo(budget,
            $"'--{widest.Option.Name}' renders a {widest.Width}-column row, and a row may not exceed "
          + $"{budget} columns if the {MarkWidth()}-cell mark and the "
          + $"{HexIDE.IDE.ConsoleLayout.Gutter.Length}-cell gutter are to fit beside it in a "
          + $"{DefaultConsoleWidth}-column console. Shorten its summary, or narrow the value column - it is "
          + $"currently {column} cells, set by '{table.MaxBy(o => Spelling(o).Length).Name}'");
    }

    /// <summary>
    /// The default width of a Windows console and of Windows Terminal, and so the width the beside layout
    /// is designed to fit. Not a limit the code enforces at runtime - there it measures the real console.
    /// </summary>
    private const int DefaultConsoleWidth = 120;

    /// <summary>Exactly as <c>ServerOptions.Spelling</c> builds it.</summary>
    private static string Spelling((string Name, string? Value, string Summary) option) =>
        option.Value is null ? $"--{option.Name}" : $"--{option.Name} {option.Value}";
}
