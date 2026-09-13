using System.Reflection;

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
/// Reflection rather than a project reference: <c>HexIDE.Desktop</c> is an executable nothing in the tree
/// references, and adding a reference to it from a test project would pull the automation server and its
/// AspNetCore dependency into every test run.
/// </para>
/// </remarks>
public class CommandLineDocumentationTests
{
    private static readonly string[] Flags = FlagNames();

    /// <summary>
    /// The flag names, read out of the built desktop assembly's own option table.
    /// </summary>
    /// <remarks>
    /// Read from the table rather than restated here, so this test cannot itself become the stale copy it
    /// exists to prevent.
    /// </remarks>
    private static string[] FlagNames()
    {
        var assembly = Assembly.LoadFrom(DesktopAssemblyPath());
        var options = assembly.GetType("HexIDE.Desktop.ServerOptions", throwOnError: true)!;
        var table = (System.Collections.IEnumerable)options
            .GetProperty("Options", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

        var names = new List<string>();
        foreach (var option in table)
        {
            names.Add((string)option.GetType().GetProperty("Name")!.GetValue(option)!);
        }

        return [.. names];
    }

    private static string DesktopAssemblyPath()
    {
        var here = AppContext.BaseDirectory;
        var configuration = here.Contains("Release", StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug";

        var candidate = Path.GetFullPath(Path.Combine(
            RepoRoot(), "IDE", "HexIDE.Desktop", "bin", configuration, "net10.0", "HexIDE.Desktop.dll"));

        File.Exists(candidate).Should().BeTrue(
            $"the desktop assembly is what carries the option table; expected it at {candidate}");

        return candidate;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "IDE")) &&
                Directory.Exists(Path.Combine(dir.FullName, "LspServer")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("no ancestor holds both IDE/ and LspServer/");
    }

    private static string Reference() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "docs", "command-line.md"));

    [Fact]
    public void TheTableIsNotEmpty()
    {
        // Everything below passes vacuously if the reflection quietly returned nothing, which is exactly
        // how a guard like this fails open.
        Flags.Should().NotBeEmpty("the option table was read from the desktop assembly");
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

        var documented = System.Text.RegularExpressions.Regex
            .Matches(reference, @"`--(?<name>[a-z][a-z-]*)`")
            .Select(m => m.Groups["name"].Value)
            .Distinct();

        foreach (var name in documented)
        {
            Flags.Should().Contain(name,
                $"docs/command-line.md documents '--{name}', so the parser has to accept it");
        }
    }
}
