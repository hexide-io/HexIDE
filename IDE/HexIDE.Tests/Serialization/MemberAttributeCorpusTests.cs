using System.IO;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.Serialization;

/// <summary>
/// Every member run in the files VB6's own IDE wrote through Tools > Procedure Attributes is read as describing the
/// member VB6 gave it to (hexide-io/HexIDE#273 task 4.6; <c>corpus/member-attributes/README.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>VB6 wrote every run here where it belongs, so the answer is known without reading the rule.</b> Its IDE
/// regenerates each run at a save directly under the last physical line of its member's declaration, qualified
/// with the member's declared name in its declared case (IDE probes A, B and B2), and never writes an orphan: a
/// run that reaches nothing it drops, and a variable's it moves under the variable. So in these files each run
/// describes the declaration directly above it, and the table below says which, written from the files and not
/// from <see cref="ReadOnlyRegions.MemberRuns"/>. The accessor matters where the dialog chose one: under the
/// first in the file for a description alone, <c>Let</c> and <c>Set</c> as readily as <c>Get</c>, and under the
/// <c>Get</c> when <c>(Default)</c> was set in the same visit (V6), so each row names the declaration line.
/// </para>
/// <para>
/// <b>What these files cannot check.</b> Every qualifier VB6 writes names the member it sits under, so a reader
/// that consulted a procedure run's qualifier, or required each variable line to name a name its line declares,
/// would pass here. Those rules are <see cref="MemberRunsTests"/>' to pin, on the shapes VB6 reads but never
/// writes; the hand-written ones VB6 was given are kept under <c>corpus/member-attributes/evidence</c>.
/// </para>
/// </remarks>
public class MemberAttributeCorpusTests
{
    /// <summary>
    /// Each run of each file, in order: the first physical line of the declaration it describes, what it describes,
    /// and its key. A file with no run is listed with none, so a file added to the corpus needs a row.
    /// </summary>
    private static readonly Dictionary<string, (string Described, MemberRunKind Kind, string Member, int Ordinal)[]> Expected = new()
    {
        // Session 1, the dialog (IDE probe A, stage 2): procedure, property, event and variable runs, a continued
        // declaration, a comma list, WithEvents with the help id VB6 adds itself, a module, a form and a UserControl.
        ["MemberAttributes/Gauge.ctl"] = [("Public Property Get Level() As Long", MemberRunKind.Procedure, "LEVEL", 0)],
        ["MemberAttributes/Helpers.bas"] = [("Public Sub Announce(ByVal Text As String)", MemberRunKind.Procedure, "ANNOUNCE", 0)],
        ["MemberAttributes/Ledger.cls"] =
        [
            ("Public Event Changed(ByVal Amount As Long)", MemberRunKind.Procedure, "CHANGED", 0),
            // One run for two declarators, grouped by declarator, keyed by the first it names.
            ("Public A As Long, B As Long", MemberRunKind.Variable, "A", 0),
            // Description cleared in the dialog, (Default) kept: VB_UserMemId alone.
            ("Public Function Total() As Long", MemberRunKind.Procedure, "TOTAL", 0),
            // Get before Let in the file: under the Get.
            ("Public Property Get Rate() As Double", MemberRunKind.Procedure, "RATE", 0),
            // Under the declaration's second physical line, described from its first.
            ("Public Function Average(ByVal First As Long, _", MemberRunKind.Procedure, "AVERAGE", 0),
            ("Public Sub Reset()", MemberRunKind.Procedure, "RESET", 0),
        ],
        ["MemberAttributes/Main.frm"] =
        [
            ("Private WithEvents mLedger As Ledger", MemberRunKind.Variable, "MLEDGER", 0),
            ("Public Sub ShowTotal()", MemberRunKind.Procedure, "SHOWTOTAL", 0),
        ],
        ["MemberAttributes/Tariff.cls"] =
        [
            ("Private WithEvents mLedger As Ledger", MemberRunKind.Variable, "MLEDGER", 0),
            // Let first in the file, Description and (Default) in one visit: under the Get, alone, so ordinal 0.
            ("Public Property Get Rate() As Double", MemberRunKind.Procedure, "RATE", 0),
        ],

        // The same project after stage 1, before some runs were cleared and others added: the description with a
        // doubled quote, a one-name variable's run, a Function's description beside (Default), and B's run alone
        // under the comma list. The other four files are byte for byte the ones above.
        ["MemberAttributes-stage1/Ledger.cls"] =
        [
            ("Public Event Changed(ByVal Amount As Long)", MemberRunKind.Procedure, "CHANGED", 0),
            ("Public Owner As String", MemberRunKind.Variable, "OWNER", 0),
            // Keyed by the declarator it names, which is the second the line declares.
            ("Public A As Long, B As Long", MemberRunKind.Variable, "B", 0),
            ("Public Function Total() As Long", MemberRunKind.Procedure, "TOTAL", 0),
            ("Public Property Get Rate() As Double", MemberRunKind.Procedure, "RATE", 0),
            ("Public Function Average(ByVal First As Long, _", MemberRunKind.Procedure, "AVERAGE", 0),
            ("Public Sub Reset()", MemberRunKind.Procedure, "RESET", 0),
        ],
        ["MemberAttributes-stage1/Gauge.ctl"] = [("Public Property Get Level() As Long", MemberRunKind.Procedure, "LEVEL", 0)],
        ["MemberAttributes-stage1/Helpers.bas"] = [("Public Sub Announce(ByVal Text As String)", MemberRunKind.Procedure, "ANNOUNCE", 0)],
        ["MemberAttributes-stage1/Main.frm"] =
        [
            ("Private WithEvents mLedger As Ledger", MemberRunKind.Variable, "MLEDGER", 0),
            ("Public Sub ShowTotal()", MemberRunKind.Procedure, "SHOWTOTAL", 0),
        ],
        ["MemberAttributes-stage1/Tariff.cls"] =
        [
            ("Private WithEvents mLedger As Ledger", MemberRunKind.Variable, "MLEDGER", 0),
            ("Public Property Get Rate() As Double", MemberRunKind.Procedure, "RATE", 0),
        ],

        // Session 1, every option the dialog leaves enabled in a UserControl (IDE probe E).
        ["Extras/Panel.ctl"] =
        [
            ("Public Event Clicked()", MemberRunKind.Procedure, "CLICKED", 0),
            ("Public Spare As Long", MemberRunKind.Variable, "SPARE", 0),
            // B's run under the statement after the colon.
            ("Public A As Long: Public B As Long", MemberRunKind.Variable, "B", 0),
            ("Public Property Get Full() As Long", MemberRunKind.Procedure, "FULL", 0),
            // A Let-only property: under the Let.
            ("Public Property Let WriteOnly(ByVal Value As Long)", MemberRunKind.Procedure, "WRITEONLY", 0),
            // The 254 characters VB6 kept of 1100.
            ("Public Function Longest() As Long", MemberRunKind.Procedure, "LONGEST", 0),
        ],
        ["Extras/Starter.bas"] = [],

        // Session 2, which accessor a description alone goes under (V6): the first in the file, Let and Set included.
        ["Tar2/TarD.cls"] = [("Public Property Let RateD(ByVal Value As Double)", MemberRunKind.Procedure, "RATED", 0)],
        // The Tariff replica: (Default) in the same visit put it under the Get.
        ["Tar2/TarDD.cls"] = [("Public Property Get RateDD() As Double", MemberRunKind.Procedure, "RATEDD", 0)],
        ["Tar2/TarV.cls"] = [("Public Property Let RateV(ByVal Value As Variant)", MemberRunKind.Procedure, "RATEV", 0)],
        // (Default) added to a run already under the Let: it stays there.
        ["Tar2/TarVD.cls"] = [("Public Property Let RateVD(ByVal Value As Variant)", MemberRunKind.Procedure, "RATEVD", 0)],
        ["Tar2/Starter.bas"] = [],

        ["Acc2/GetSetB.cls"] =
        [
            ("Public Property Get PGs() As Variant", MemberRunKind.Procedure, "PGS", 0),
            // The dialog opened on the first Name-list entry with the caret in the Set, and the Sub took the text.
            ("Public Sub OtherGetSetB()", MemberRunKind.Procedure, "OTHERGETSETB", 0),
        ],
        ["Acc2/LetGetA.cls"] =
        [
            ("Public Property Let PLgA(ByVal Value As Variant)", MemberRunKind.Procedure, "PLGA", 0),
            ("Public Sub OtherLetGetA()", MemberRunKind.Procedure, "OTHERLETGETA", 0),
        ],
        ["Acc2/LetGetB.cls"] = [("Public Property Let PLgB(ByVal Value As Variant)", MemberRunKind.Procedure, "PLGB", 0)],
        ["Acc2/LetGetC.cls"] = [("Public Property Let PLgC(ByVal Value As Variant)", MemberRunKind.Procedure, "PLGC", 0)],
        ["Acc2/LetSetB.cls"] = [("Public Property Let PLs(ByVal Value As Variant)", MemberRunKind.Procedure, "PLS", 0)],
        ["Acc2/LetSetC.cls"] = [("Public Property Let PLsC(ByVal Value As Variant)", MemberRunKind.Procedure, "PLSC", 0)],
        ["Acc2/SetGetB.cls"] = [("Public Property Set PSg(ByVal Value As Variant)", MemberRunKind.Procedure, "PSG", 0)],
        ["Acc2/SetGetC.cls"] = [("Public Property Set PSgC(ByVal Value As Variant)", MemberRunKind.Procedure, "PSGC", 0)],
        ["Acc2/SetLetB.cls"] = [("Public Property Set PSl(ByVal Value As Variant)", MemberRunKind.Procedure, "PSL", 0)],
        // A comma list edited (V2): VB6 carried the first declarator's run across its rename and re-qualified it ...
        ["Acc2/MultiP.cls"] = [("Public r As Long, Q As Long", MemberRunKind.Variable, "R", 0)],
        // ... kept the first's when the second was renamed, and dropped the second's ...
        ["Acc2/MultiXY.cls"] = [("Public X As Long, z As Long", MemberRunKind.Variable, "X", 0)],
        // ... and dropped the second's when only the first was renamed.
        ["Acc2/MultiMN.cls"] = [],
        ["Acc2/Starter.bas"] = [],

        ["Probe2/GetSet6.cls"] = [("Public Property Get Target() As Object", MemberRunKind.Procedure, "TARGET", 0)],
        ["Probe2/SetGet6.cls"] = [("Public Property Set Holder(ByVal Value As Object)", MemberRunKind.Procedure, "HOLDER", 0)],
        ["Probe2/LetSet6.cls"] = [("Public Property Let Slot(ByVal Value As Variant)", MemberRunKind.Procedure, "SLOT", 0)],
        ["Probe2/SetLet6.cls"] = [("Public Property Set Spot(ByVal Value As Object)", MemberRunKind.Procedure, "SPOT", 0)],
        // A variable renamed by retyping (V1), and one whose type was retyped (V8): carried, and re-qualified.
        ["Probe2/Var1.cls"] = [("Public Tally As Long", MemberRunKind.Variable, "TALLY", 0)],
        ["Probe2/Retype8.cls"] = [("Public Size As String", MemberRunKind.Variable, "SIZE", 0)],
        // A procedure retyped from Function to Sub and back (V3): the run stayed with it.
        ["Probe2/FuncSub3.cls"] = [("Public Sub Total()", MemberRunKind.Procedure, "TOTAL", 0)],
        ["Probe2/SubFunc3.cls"] = [("Public Function Runner() As Long", MemberRunKind.Procedure, "RUNNER", 0)],
        // Pasted text, reloaded and saved (V4): the dialog's run kept, the pasted duplicate dropped ...
        ["Probe2/Paste4.cls"] = [("Public Function Pasted() As Long", MemberRunKind.Procedure, "PASTED", 0)],
        // ... and in a module the pasted line, which only the reload made a run.
        ["Probe2/PasteMod4.bas"] = [("Public Function Pasted() As Long", MemberRunKind.Procedure, "PASTED", 0)],
        // The declaration line deleted and retyped (V7), and the second declarator renamed (V2): no run comes back.
        ["Probe2/Decl7.cls"] = [],
        ["Probe2/Multi2.cls"] = [],
        ["Probe2/Starter.bas"] = [],
    };

    private static readonly string[] SourceExtensions = [".frm", ".bas", ".cls", ".ctl", ".pag"];

    [Fact]
    public void EveryRunVb6WroteIsReadAsDescribingTheMemberItWroteItUnder()
    {
        var root = Corpus("round-trip");
        var files = SourceFilesUnder(root);
        files.Select(Relative).Should().BeEquivalentTo(Expected.Keys,
            "every VB6 source file of the round-trip half has a row, and every row a file");

        foreach (var path in files)
        {
            var name = Relative(path);
            var text = Vb6TextFile.ReadDocument(File.ReadAllBytes(path)).Text;
            var runs = ReadOnlyRegions.MemberRuns(text, 0);

            runs.Select(r => (Described: FirstLineAt(text, r.DescribedStart), r.Kind, Member: r.Key?.Member, Ordinal: r.Key?.Ordinal ?? -1))
                .Should().Equal(Expected[name].Select(e => (e.Described, e.Kind, (string?)e.Member, e.Ordinal)),
                    $"{name}'s runs describe the members VB6 wrote them under");

            foreach (var run in runs)
            {
                run.Member.Should().NotBeNull($"{name}: a run that describes a member names it");
                run.Member!.Value.Name.ToUpperInvariant().Should().Be(run.Key!.Value.Member,
                    $"{name}: the key is the member's declared name");
                foreach (var line in run.Lines)
                {
                    // VB6 qualifies every line with the declared name, in its declared case, and what 4.4's save
                    // re-writes is read at the offset alone, so the offset has to land on the name in VB6's files.
                    line.Qualifier.Should().NotBeNull($"{name}: VB6 writes no dotless line below the header");
                    text.Substring(line.QualifierOffset, line.Qualifier!.Length).Should().Be(line.Qualifier,
                        $"{name}: the qualifier is where the line says it is");
                    run.Declared.Select(d => d.Name).Should().Contain(line.Qualifier,
                        $"{name}: VB6 qualified the line with a name its declaration spells exactly so");
                }
            }

            ReadOnlyRegions.Of(text, 0).Skip(1).Should().Equal(runs.Select(r => r.Region),
                $"{name}: after the header, its read-only regions are exactly its runs");
        }
    }

    [Fact]
    public void NothingVb6SavedIsAnOrphanOrARenamesLeftover()
    {
        // The property behind the table above, over every file VB6 saved: the round-trip half, and each evidence
        // project's saved output, where VB6 had just dropped what reached nothing and moved what it bound by name
        // (IDE probes B, B2 and V5). A run VB6 writes is always directly under its member and always names it, so an
        // orphan or a stale name in one of these is the reader's mistake and never the file's.
        var files = SourceFilesUnder(Corpus("round-trip"))
            .Concat(Directory.EnumerateDirectories(Corpus("evidence"), "saved*", SearchOption.AllDirectories)
                .SelectMany(SourceFilesUnder))
            .ToList();
        files.Should().HaveCountGreaterThan(Expected.Count, "the evidence's saved output is read too");

        var runs = 0;
        foreach (var path in files)
        {
            var text = Vb6TextFile.ReadDocument(File.ReadAllBytes(path)).Text;
            foreach (var run in ReadOnlyRegions.MemberRuns(text, 0))
            {
                run.Kind.Should().BeOneOf([MemberRunKind.Procedure, MemberRunKind.Variable],
                    $"{Path.GetFileName(path)}: the run under '{FirstLineAt(text, run.DescribedStart)}' is one VB6 wrote");
                runs++;
            }
        }
        runs.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Where HexIDE knowingly reads a hand-written run otherwise than VB6's save treated it, by the evidence file and
    /// the line the run sits under. Each is recorded in design.md, "Settled before building phase 4".
    /// </summary>
    private static readonly Dictionary<(string File, string Above), string> KnownDivergences = new()
    {
        [("ConstClsPub.cls", "Public Const Outer = 5")] =
            "a Public Const in a class is a declaration VB6 rejects, and drops its run (V5); the reader does not know "
            + "the module's kind, so it reads the run as the constant's, protected text either way",
        [("DeclShapes.cls", "Public Const K As Long = 1")] = "the same shape, in the first session's class (IDE probe B2)",
    };

    [Fact]
    public void AHandWrittenRunDescribesAMemberExactlyWhereVb6KeptIt()
    {
        // The evidence half made executable. Each input there is a shape written by hand and loaded by VB6's IDE,
        // and its saved/ twin is what VB6 wrote back (IDE probes B, B2 and V5): a run VB6 bound to the declaration
        // above it is still under that declaration, re-qualified and respaced perhaps, and one it bound to nothing
        // or to a member elsewhere is gone from there, dropped or moved. HexIDE does not drop or move anything, so
        // the comparison is the verdict: a run describes a member here exactly when VB6 kept a line of it in place,
        // which is judged from the two texts alone, the physical line above each run and the values its lines carry.
        var compared = 0;
        var kept = 0;
        foreach (var project in Directory.EnumerateDirectories(Corpus("evidence")).Order(StringComparer.Ordinal))
        {
            foreach (var path in SourceFilesUnder(Path.Join(project, "input")))
            {
                var name = Path.GetFileName(path);
                var text = Vb6TextFile.ReadDocument(File.ReadAllBytes(path)).Text;
                var saved = Vb6TextFile.ReadDocument(File.ReadAllBytes(Path.Join(project, "saved", name))).Text;
                var vb6Runs = BlocksBelowTheHeader(saved);

                foreach (var run in ReadOnlyRegions.MemberRuns(text, 0))
                {
                    var above = LineAbove(text, run.Region.Start);
                    var values = run.Lines.Select(l => ValueOf(text[l.Start..l.End])).ToHashSet(StringComparer.Ordinal);
                    var vb6KeptIt = vb6Runs.Any(b => b.Above == above && b.Values.Overlaps(values));
                    var expected = KnownDivergences.ContainsKey((name, above)) ? !vb6KeptIt : vb6KeptIt;

                    run.DescribesMember.Should().Be(expected,
                        $"{name}: VB6 {(vb6KeptIt ? "kept" : "did not keep")} the run under '{above}'"
                        + (KnownDivergences.TryGetValue((name, above), out var why) ? ", and HexIDE diverges: " + why : ""));
                    compared++;
                    if (vb6KeptIt) kept++;
                }
            }
        }

        compared.Should().Be(43, "every hand-written run of the four evidence projects is compared");
        kept.Should().BeInRange(1, compared - 1, "and VB6 kept some and not others, or the comparison decides nothing");
    }

    /// <summary>
    /// Each block of <c>Attribute</c> lines below the header of <paramref name="text"/>, as the physical line above it
    /// and the values its lines carry: found from the text, not by the reader under test.
    /// </summary>
    private static List<(string Above, HashSet<string> Values)> BlocksBelowTheHeader(string text)
    {
        var lines = text[ReadOnlyRegions.HeaderEnd(text, 0)..].Split("\r\n");
        var blocks = new List<(string Above, HashSet<string> Values)>();
        for (var i = 1; i < lines.Length; i++)
        {
            if (!IsAttributeLine(lines[i]) || IsAttributeLine(lines[i - 1]))
                continue;
            var values = new HashSet<string>(StringComparer.Ordinal);
            for (var j = i; j < lines.Length && IsAttributeLine(lines[j]); j++)
                values.Add(ValueOf(lines[j]));
            blocks.Add((lines[i - 1], values));
        }
        return blocks;

        static bool IsAttributeLine(string line) => line.TrimStart().StartsWith("Attribute ", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>What an attribute line sets, after its first <c>=</c>, without the spacing around it.</summary>
    private static string ValueOf(string line) => line[(line.IndexOf('=') + 1)..].Trim();

    /// <summary>The physical line of <paramref name="text"/> that ends just before <paramref name="lineStart"/>.</summary>
    private static string LineAbove(string text, int lineStart)
    {
        var end = lineStart - 2; // the CRLF VB6 writes
        var start = text.LastIndexOf('\n', end - 1) + 1;
        return text[start..end];
    }

    private static string Corpus(string half)
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            var candidate = Path.Join(at.FullName, "corpus", "member-attributes", half);
            if (Directory.Exists(candidate))
                return candidate;
        }
        throw new DirectoryNotFoundException("corpus/member-attributes/" + half + " ships with the repository");
    }

    private static List<string> SourceFilesUnder(string root) =>
        Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => SourceExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal).ToList();

    /// <summary>A round-trip file as the table names it: its project's folder and its name.</summary>
    private static string Relative(string path) =>
        Path.GetFileName(Path.GetDirectoryName(path)) + "/" + Path.GetFileName(path);

    /// <summary>The physical line of <paramref name="text"/> that starts at <paramref name="start"/>, without its terminator.</summary>
    private static string FirstLineAt(string text, int start)
    {
        var end = text.IndexOfAny(['\r', '\n'], start);
        return text[start..(end < 0 ? text.Length : end)];
    }
}
