using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// Holds <c>docs/lsp-client.md</c>'s coverage table to the specification's own model of itself.
///
/// <para>
/// <b>Why this exists.</b> The document it guards replaced one that had drifted into fiction: it listed six
/// binding-dependent features as <i>Planned</i> and <i>Future</i> after the architecture had ruled them out,
/// described an <c>Option Explicit</c> check that is compiled out under "what works", and overstated three
/// table counts. None of that was noticed for months, because nothing could notice it. A table of ninety-odd
/// protocol methods is the most drift-prone artefact this repository could own, and shipping one without a
/// guard would be repeating the mistake at greater length.
/// </para>
///
/// <para>
/// So the table is generated from <c>metaModel.json</c> and then checked against it. The check is the point:
/// generation happens once, by hand, and is exactly as forgettable as any other manual step.
/// </para>
/// </summary>
public class ProtocolCoverageDocTests
{
    private static readonly Regex Row = new(
        @"^\|\s*(?<mark>[✅◐○])\s*\|\s*`(?<method>[^`]+)`\s*\|\s*(?<dir>[→←↔])\s*\|",
        RegexOptions.Compiled);

    private sealed record DocumentedRow(string Method, string Mark, string Direction);

    private static string DocumentPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "IDE"))
                             && Directory.Exists(Path.Combine(dir.FullName, "LspServer"))))
            dir = dir.Parent;

        return dir is null
            ? throw new InvalidOperationException("Could not locate the repository root from the test output.")
            : Path.Combine(dir.FullName, "docs", "lsp-client.md");
    }

    private static List<DocumentedRow> DocumentedRows()
    {
        var rows = new List<DocumentedRow>();
        foreach (var line in File.ReadAllLines(DocumentPath()))
        {
            if (Row.Match(line) is { Success: true } m)
            {
                rows.Add(new DocumentedRow(
                    m.Groups["method"].Value, m.Groups["mark"].Value, m.Groups["dir"].Value));
            }
        }
        return rows;
    }

    private static string Expected(LspSpecificationModel.Direction direction) => direction switch
    {
        LspSpecificationModel.Direction.ClientToServer => "→",
        LspSpecificationModel.Direction.ServerToClient => "←",
        _ => "↔",
    };

    [LspModelFact]
    public void TheTableListsEveryMessageTheProtocolDefines()
    {
        // The assertion that makes the document's denominator real. A method added to a future protocol
        // version, or one quietly dropped from the table during an edit, fails here rather than being
        // discovered by a reader who trusted the table to be exhaustive.
        var specification = LspSpecificationModel.All()!;
        var documented = DocumentedRows().Select(r => r.Method).ToHashSet(StringComparer.Ordinal);

        var missing = specification.Select(m => m.Method)
            .Where(method => !documented.Contains(method))
            .OrderBy(method => method, StringComparer.Ordinal)
            .ToArray();

        missing.Should().BeEmpty(
            $"docs/lsp-client.md must list every message in LSP {LspSpecificationModel.Version}; "
          + "regenerate the table from the metaModel rather than adding rows by hand");
    }

    [LspModelFact]
    public void TheTableInventsNothingTheProtocolDoesNotDefine()
    {
        // The other direction, and the one a typo trips. A misspelled method reads as a real row and would
        // otherwise sit there indefinitely being quoted.
        var specification = LspSpecificationModel.All()!;
        var known = specification.Select(m => m.Method).ToHashSet(StringComparer.Ordinal);

        var invented = DocumentedRows().Select(r => r.Method)
            .Where(method => !known.Contains(method))
            .OrderBy(method => method, StringComparer.Ordinal)
            .ToArray();

        invented.Should().BeEmpty(
            $"every row must name a message LSP {LspSpecificationModel.Version} actually defines. "
          + "HexIDE's own custom methods are documented in prose, deliberately outside this table");
    }

    [LspModelFact]
    public void EachMessageAppearsExactlyOnce()
    {
        var duplicates = DocumentedRows()
            .GroupBy(r => r.Method, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} ({g.Count()}×)")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        duplicates.Should().BeEmpty(
            "a method listed twice can carry two different statuses, and then the table says both");
    }

    [LspModelFact]
    public void EveryRowRecordsTheDirectionTheSpecificationGivesIt()
    {
        // Direction is the half of this table a reader cannot sanity-check by eye, and it is what makes a
        // circle on a server-to-client row mean something quite different from one on a client-to-server
        // row: the first is a message we ignore when a server sends it, the second one we simply never send.
        var specification = LspSpecificationModel.All()!
            .ToDictionary(m => m.Method, m => m.Direction, StringComparer.Ordinal);
        var documented = DocumentedRows();

        var wrong = documented
            .Where(r => specification.ContainsKey(r.Method))
            .Where(r => r.Direction != Expected(specification[r.Method]))
            .Select(r => $"{r.Method}: table says {r.Direction}, specification says "
                       + Expected(specification[r.Method]))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        wrong.Should().BeEmpty("direction is taken from the metaModel, not decided here");
    }

    [LspModelFact]
    public void TheHeadlineCountsMatchTheTableBeneathThem()
    {
        // The prose above the table states two numbers, and prose is exactly what stops being true first.
        var specification = LspSpecificationModel.All()!;
        var rows = DocumentedRows();
        var text = File.ReadAllText(DocumentPath());

        var implemented = rows.Count(r => r.Mark is "✅" or "◐");

        text.Should().Contain($"**{specification.Count} messages**",
            "the stated total must be the specification's, and it changes between protocol versions");
        text.Should().Contain($"implements {implemented} of the {specification.Count}",
            $"the table currently marks {implemented} of {specification.Count} as implemented or partial");
    }

    /// <summary>
    /// Every section heading's "N of M" must match the rows beneath it.
    /// </summary>
    /// <remarks>
    /// The headline total was guarded and the section headings were not, so they drifted separately and
    /// silently: three of them were wrong at once — `textDocument/*` understated by one, `workspace/*` said
    /// 0 while two rows were ticked, and `codeLens/*` said 0 of 1 with its single row ticked. A reader
    /// scanning for where the gaps are reads these headings, not the ninety rows, so a wrong one is worse
    /// than a wrong total. Nothing noticed, which is the same failure the whole guard exists to prevent.
    /// </remarks>
    [LspModelFact]
    public void EverySectionHeadingMatchesItsOwnRows()
    {
        var text = File.ReadAllText(DocumentPath());
        var lines = text.Split((char)10);   // newline, spelled so no escape survives editing

        string? section = null;
        var stated = 0;
        var statedTotal = 0;
        var counted = 0;
        var rows = 0;
        var problems = new List<string>();

        void Close()
        {
            if (section is null) return;
            if (counted != stated || rows != statedTotal)
                problems.Add($"{section}: heading says {stated} of {statedTotal}, rows give {counted} of {rows}");
        }

        foreach (var line in lines)
        {
            var heading = Regex.Match(line, @"^### `?([^`—]+?)`?\s*—\s*(\d+) of (\d+)");
            if (heading.Success)
            {
                Close();
                section = heading.Groups[1].Value.Trim();
                stated = int.Parse(heading.Groups[2].Value);
                statedTotal = int.Parse(heading.Groups[3].Value);
                counted = 0;
                rows = 0;
                continue;
            }

            var row = Regex.Match(line, @"^\|\s*(?<mark>[✅◐○])\s*\|");
            if (!row.Success || section is null) continue;
            rows++;
            if (row.Groups["mark"].Value is "✅" or "◐") counted++;
        }
        Close();

        problems.Should().BeEmpty("each section heading states its own coverage and must not drift from it");
    }
}

/// <summary>
/// A <see cref="FactAttribute"/> that skips — <b>visibly</b> — when the protocol model cannot be obtained.
///
/// <para>
/// Same bargain as <see cref="ForeignServerFactAttribute"/>, for the same reason: an offline machine should
/// not fail a suite over a fixture it could not fetch, and CI must not be allowed to lose the check
/// silently. A returned-early test passes while asserting nothing, which is the failure this whole approach
/// exists to prevent.
/// </para>
/// </summary>
public sealed class LspModelFactAttribute : FactAttribute
{
    public LspModelFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
            : base(sourceFilePath, sourceLineNumber)
    {
        if (LspSpecificationModel.All() is not null) return;

        // Enforcement lives in RequiredServersTests; see the note there and in
        // ForeignServerFactAttribute about why an attribute constructor must not throw under v3.
        Skip = "The LSP metaModel is unavailable. It is normally downloaded on demand; set "
             + $"{LspSpecificationModel.PathVariable} to a local copy to work offline.";
    }
}
