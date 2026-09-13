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
    private static string[] FlagNames()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoTree.Root(), "IDE", "HexIDE.Desktop", "ServerOptions.cs"));

        var start = source.IndexOf("Options { get; } =", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "ServerOptions still declares the option table this reads");

        var end = source.IndexOf("];", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, "the option table is closed off where this expects");

        return
        [
            .. Regex.Matches(source[start..end], """new\("(?<name>[a-z][a-z-]*)",""")
                .Select(match => match.Groups["name"].Value)
        ];
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
}
