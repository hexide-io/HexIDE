using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// Holds the design record in <c>openspec/</c> to its own rule that position in the tree is the status.
/// </summary>
/// <remarks>
/// <b>This is the only drift-prone artefact here that was still trusted to a habit.</b> The protocol
/// coverage table is generated and asserted, the language packs are checked at build, licences are enforced
/// in CI — and the design record, which is the thing a contributor reads to learn what the system is
/// supposed to do, relied on somebody remembering to run <c>openspec archive</c>.
///
/// <para>
/// <b>Measured, not imagined.</b> Five changes had shipped their code and never been archived. Because the
/// archive is not scanned for deltas, their requirements were simply absent from <c>specs/</c>: the whole of
/// merged multi-server routing, the only requirement governing the Language &amp; Debug Servers window, and
/// three <c>MODIFIED</c> bodies on the save-safety capability whose headings existed while their content was
/// a version superseded weeks earlier. A contributor reading <c>specs/lsp-client</c> found a single-server
/// client. Four pull requests landed against that seam in a week, each reasoning from behaviour the spec did
/// not contain.
/// </para>
///
/// <para>
/// <b>The <c>MODIFIED</c> case is why heading-presence is not enough on its own and is checked anyway.</b>
/// The survey that found the first two failures grepped for requirement <em>headings</em>, which a
/// <c>MODIFIED</c> delta does not change — so it reported those three changes as tidying rather than drift,
/// and archiving them turned out to rewrite sixty-eight lines of requirement text. A guard cannot compare
/// bodies without re-implementing the merge, so it checks what it can and the completeness check below is
/// what catches the rest: a change that is archived has been merged by the tool, whatever its delta said.
/// </para>
/// </remarks>
public class DesignRecordTests
{
    private static readonly Regex RequirementHeading =
        new(@"^###\s+Requirement:\s*(?<name>.+?)\s*$", RegexOptions.Compiled);

    private static readonly Regex OperationHeading =
        new(@"^##\s+(?<op>ADDED|MODIFIED|REMOVED|RENAMED)\s+Requirements\s*$", RegexOptions.Compiled);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "IDE"))
                             && Directory.Exists(Path.Combine(dir.FullName, "LspServer"))))
            dir = dir.Parent;

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root from the test output.");
    }

    private static string ChangesDirectory() => Path.Combine(RepoRoot(), "openspec", "changes");

    private static string ArchiveDirectory() => Path.Combine(ChangesDirectory(), "archive");

    /// <summary>A delta's requirement headings, grouped by the operation they sit under.</summary>
    private static Dictionary<string, List<string>> Operations(string deltaPath)
    {
        var byOperation = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var current = "ADDED";

        foreach (var line in File.ReadAllLines(deltaPath))
        {
            if (OperationHeading.Match(line) is { Success: true } op)
            {
                current = op.Groups["op"].Value;
                continue;
            }

            if (RequirementHeading.Match(line) is { Success: true } req)
            {
                if (!byOperation.TryGetValue(current, out var names))
                    byOperation[current] = names = [];
                names.Add(req.Groups["name"].Value);
            }
        }

        return byOperation;
    }

    private static HashSet<string> RequirementsIn(string specPath) =>
        File.Exists(specPath)
            ? [.. File.ReadAllLines(specPath)
                .Select(l => RequirementHeading.Match(l))
                .Where(m => m.Success)
                .Select(m => m.Groups["name"].Value)]
            : [];

    /// <summary>Every delta in the archive, as (change id, capability, path).</summary>
    private static List<(string Change, string Capability, string Path)> ArchivedDeltas()
    {
        var deltas = new List<(string, string, string)>();
        if (!Directory.Exists(ArchiveDirectory())) return deltas;

        foreach (var change in Directory.EnumerateDirectories(ArchiveDirectory()))
        {
            var specs = Path.Combine(change, "specs");
            if (!Directory.Exists(specs)) continue;

            foreach (var capability in Directory.EnumerateDirectories(specs))
            {
                var delta = Path.Combine(capability, "spec.md");
                if (File.Exists(delta))
                    deltas.Add((Path.GetFileName(change), Path.GetFileName(capability), delta));
            }
        }

        return deltas;
    }

    [Fact]
    public void AChangeWhoseTasksAreAllDoneHasBeenArchived()
    {
        // The failure this exists for, in its simplest form. A change sitting in changes/ says the work is
        // in flight; specs/ says what the system does today. A completed change left in place makes both
        // statements false about the same thing, and the archive is not scanned for deltas, so nothing
        // downstream ever notices.
        var stranded = new List<string>();

        foreach (var change in Directory.EnumerateDirectories(ChangesDirectory()))
        {
            if (Path.GetFileName(change) == "archive") continue;

            var tasks = Path.Combine(change, "tasks.md");
            if (!File.Exists(tasks)) continue;

            var text = File.ReadAllText(tasks);
            var done = Regex.Matches(text, @"^\s*-\s*\[x\]", RegexOptions.Multiline).Count;
            var open = Regex.Matches(text, @"^\s*-\s*\[ \]", RegexOptions.Multiline).Count;

            if (done > 0 && open == 0) stranded.Add(Path.GetFileName(change));
        }

        stranded.Should().BeEmpty(
            "a change with every task ticked is finished, and a finished change belongs in the archive. Run "
          + "`openspec archive <id> -y`, then check each merged spec's `## Purpose` survived it. Leaving it "
          + "in `changes/` is not a delay — the deltas never merge into `specs/` at all, which is how five "
          + "changes' requirements went missing at once");
    }

    [Fact]
    public void EveryArchivedRequirementReachedTheSpecItTargeted()
    {
        // The other half: archived, but the merge did not happen or did not survive. A hand-placed change
        // produces exactly this — the archive holds the delta, the capability never gained the requirement,
        // and the record looks complete from every angle except the one that matters.
        var deltas = ArchivedDeltas();

        // Requirements a later change deliberately took away or renamed. Read across the whole archive
        // rather than in date order, which is a deliberate simplification: a requirement removed before it
        // was ever added is not a shape this workflow can produce, and ordering by the id's date prefix
        // would be a second thing to get wrong.
        var retired = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, _, path) in deltas)
        {
            var ops = Operations(path);
            foreach (var op in (string[])["REMOVED", "RENAMED"])
            {
                if (ops.TryGetValue(op, out var names)) retired.UnionWith(names);
            }
        }

        var specs = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var missing = new List<string>();
        var checkedCount = 0;

        foreach (var (change, capability, path) in deltas)
        {
            if (!specs.TryGetValue(capability, out var present))
            {
                specs[capability] = present =
                    RequirementsIn(Path.Combine(RepoRoot(), "openspec", "specs", capability, "spec.md"));
            }

            var ops = Operations(path);
            foreach (var op in (string[])["ADDED", "MODIFIED"])
            {
                if (!ops.TryGetValue(op, out var names)) continue;

                foreach (var name in names)
                {
                    if (retired.Contains(name)) continue;
                    checkedCount++;
                    if (!present.Contains(name)) missing.Add($"{capability}: {name}  ({change}, {op})");
                }
            }
        }

        // A guard that examines nothing passes for the wrong reason, and this one reads the filesystem by
        // path — a moved directory or a renamed heading format would leave it silently vacuous.
        deltas.Should().NotBeEmpty("the archive holds deltas, so failing to find any means this stopped "
                                 + "looking in the right place");
        checkedCount.Should().BeGreaterThan(20,
            "the archive holds dozens of requirements; a handful would mean the heading pattern stopped "
          + "matching");

        missing.Should().BeEmpty(
            "an archived change's requirements must be present in the capability it targeted. The archive is "
          + "not scanned for deltas, so a change that reached it by hand rather than through "
          + "`openspec archive` never merges and nothing says so");
    }

    [Fact]
    public void EveryDeltaTargetsASpecThatExists()
    {
        // A capability folder is created by the first change that adds to it, so this cannot be checked
        // before archiving — but afterwards, a delta against a capability with no spec file means the merge
        // wrote nowhere.
        var orphaned = ArchivedDeltas()
            .Select(d => (d.Change, d.Capability,
                          Spec: Path.Combine(RepoRoot(), "openspec", "specs", d.Capability, "spec.md")))
            .Where(d => !File.Exists(d.Spec))
            .Select(d => $"{d.Capability} ({d.Change})")
            .ToList();

        orphaned.Should().BeEmpty("a delta names the capability it changes, and that capability should have "
                               + "a spec once the change is archived");
    }
}
