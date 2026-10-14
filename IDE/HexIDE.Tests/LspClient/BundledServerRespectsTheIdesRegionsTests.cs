using System.Collections.Concurrent;
using HexIDE.Forms.ViewModels;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Runtime.Serialization;
using Microsoft.Extensions.Logging;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// The bundled server keeps off the lines the IDE holds read-only, judged by the IDE's own rule
/// (hexide-io/HexIDE#273 task 3.10).
/// </summary>
/// <remarks>
/// <para>
/// <b>The server has its own copy of the rule, and this is what keeps the two in step.</b> The IDE's is
/// <see cref="ReadOnlyRegions"/>; the server's is <c>VbProtectedRegions</c>, ported rather than shared because
/// the two halves of the repository do not reference each other. The server's own tests pin its copy on
/// fixtures. Only here are the two compared, by driving the built server with the real client and judging
/// every answer against the IDE's regions for the same text.
/// </para>
/// <para>
/// <b>Formatting is the exact check, in both directions.</b> Every line of the text sent is given two trailing
/// spaces, which the formatter removes from any line it is free to touch. So the lines its answer changes
/// must be exactly the non-empty lines outside the IDE's regions: one line fewer means the server protects a
/// line the IDE lets the developer edit, and one more means it rewrote a line the IDE holds.
/// </para>
/// <para>
/// Skipped when the server has not been built, and fatal on CI through <see cref="RequiredServersTests"/>,
/// like every other test that drives it.
/// </para>
/// </remarks>
public class BundledServerRespectsTheIdesRegionsTests : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, TaskCompletionSource<PublishDiagnosticsParams>> _published = new();
    private VBLspClient? _client;
    private int _documents;

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            try { await _client.StopAsync(); } catch { /* teardown is best effort */ }
            try { await _client.DisposeAsync(); } catch { /* ditto */ }
        }
        GC.SuppressFinalize(this);
    }

    // ── Formatting ────────────────────────────────────────────────────────────────────────────────────

    [BundledServerFact]
    public async Task FormattingChangesExactlyTheLinesTheIdeLeavesToTheDeveloper()
    {
        var client = await ConnectAsync();
        var failures = new List<string>();

        foreach (var (name, original) in Inputs())
        {
            var text = WithTrailingSpaces(original);
            var protectedLines = ProtectedLines(text);
            if (!protectedLines.SetEquals(ProtectedLines(original)))
            {
                failures.Add($"{name}: trailing spaces moved the IDE's own regions, so this file proves nothing");
                continue;
            }

            var uri = await OpenAsync(client, text);
            var edits = await client.RequestFormattingAsync(uri, TestContext.Current.CancellationToken);

            var lines = Lines(text);
            var expected = Enumerable.Range(0, lines.Length)
                .Where(i => !protectedLines.Contains(i) && lines[i].Length > 0).ToHashSet();
            var changed = edits.SelectMany(e => Enumerable.Range(e.Range.Start.Line, e.Range.End.Line - e.Range.Start.Line + 1))
                .ToHashSet();

            if (changed.Except(expected).Order().ToList() is { Count: > 0 } over)
                failures.Add($"{name}: formatting changed lines the IDE holds read-only: {string.Join(", ", over)}");
            if (expected.Except(changed).Order().ToList() is { Count: > 0 } under)
                failures.Add($"{name}: formatting left alone lines the IDE lets the developer edit: {string.Join(", ", under)}");
        }

        failures.Should().BeEmpty();
    }

    // ── Diagnostics ───────────────────────────────────────────────────────────────────────────────────

    [BundledServerFact]
    public async Task NoDiagnosticLandsOnALineTheIdeHoldsReadOnly()
    {
        var client = await ConnectAsync();
        var failures = new List<string>();

        var damaged = new[]
        {
            ("damaged form header", DamagedFormHeader),
            ("damaged member attribute", Class.Replace("VB_Description = \"The order total\"", "VB_Description = = \"x\"", StringComparison.Ordinal)),
        };
        foreach (var (name, text) in Inputs().Concat(damaged))
        {
            var protectedLines = ProtectedLines(text);
            var published = await OpenAndPublishAsync(client, text);

            foreach (var d in published.Diagnostics.Where(d => protectedLines.Contains(d.Range.Start.Line)))
                failures.Add($"{name}: line {d.Range.Start.Line}: {d.Message}");
        }

        failures.Should().BeEmpty();
    }

    [BundledServerFact]
    public async Task ADamagedHeaderStillLeavesTheCodesOwnErrorsReported()
    {
        // The positive control for the test above, which an empty publish would otherwise pass.
        var client = await ConnectAsync();

        var published = await OpenAndPublishAsync(client,
            DamagedFormHeader.Replace("dim Caption as string", "dim Caption as", StringComparison.Ordinal));

        published.Diagnostics.Should().ContainSingle().Which.Range.Start.Line.Should().Be(20);
    }

    // ── Rename and highlight ──────────────────────────────────────────────────────────────────────────

    [BundledServerFact]
    public async Task RenameAndHighlightFromALineTheIdeHoldsReadOnlyAnswerNothing()
    {
        var client = await ConnectAsync();
        var failures = new List<string>();
        var probes = 0;

        foreach (var (name, text) in Inputs())
        {
            var uri = await OpenAsync(client, text);
            var lines = Lines(text);
            foreach (var line in ProtectedLines(text).Order())
            {
                foreach (var (_, character) in Words(lines[line]).Take(2))
                {
                    probes++;
                    var at = new Position(line, character);
                    var rename = await client.RequestRenameAsync(uri, at, "Renamed", TestContext.Current.CancellationToken);
                    var highlight = await client.RequestDocumentHighlightAsync(uri, at, TestContext.Current.CancellationToken);

                    if (rename?.Changes is { Count: > 0 })
                        failures.Add($"{name}: rename from {line}:{character} answered with edits");
                    if (highlight is { Length: > 0 })
                        failures.Add($"{name}: highlight from {line}:{character} answered with {highlight.Length} ranges");
                }
            }
        }

        probes.Should().BeGreaterThan(0, "every input has a header with words in it");
        failures.Should().BeEmpty();
    }

    [BundledServerFact]
    public async Task RenamingAWordTheLayoutAlsoUsesEditsNothingTheIdeHoldsExceptTheMembersOwnQualifier()
    {
        // The delta's "Renaming an identifier that also appears in the layout", over every file: each word
        // that appears both on a read-only line and in the code is renamed from the code, and the answer is
        // judged the way the code window judges it before applying (CodeEditorViewModel.ApplyRename). A name
        // the designer block declares has no answer at all, by the IDE's own list of them.
        var client = await ConnectAsync();
        var failures = new List<string>();
        var renamed = 0;
        var declinedDeclared = 0;
        var ownQualifiers = 0;

        foreach (var (name, text) in Inputs())
        {
            var uri = await OpenAsync(client, text);
            var lines = Lines(text);
            var protectedLines = ProtectedLines(text);
            var regions = ReadOnlyRegions.Of(text, 0);
            var declared = ReadOnlyRegions.DeclaredNames(text, 0);
            var lineStarts = LineStarts(text);

            var inLayout = protectedLines.SelectMany(l => Words(lines[l]).Select(w => w.Word))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var firstInCode = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase);
            for (var line = 0; line < lines.Length; line++)
            {
                if (protectedLines.Contains(line))
                    continue;
                foreach (var (word, character) in Words(lines[line]))
                {
                    if (inLayout.Contains(word))
                        firstInCode.TryAdd(word, new Position(line, character));
                }
            }

            foreach (var (word, at) in firstInCode)
            {
                var rename = await client.RequestRenameAsync(uri, at, "Zz" + word, TestContext.Current.CancellationToken);
                if (declared.Contains(word))
                {
                    declinedDeclared++;
                    if (rename?.Changes is { Count: > 0 })
                        failures.Add($"{name}: renaming {word}, which the designer block declares, answered with edits");
                    continue;
                }
                if (rename?.Changes is not { } changes || !changes.TryGetValue(uri, out var edits))
                    continue; // a keyword, or nothing the server will rename
                renamed++;

                // The whole answer at once, because whether a qualifier edit is the rename's own depends on the
                // answer also renaming the member's declaration (#273 task 4.4).
                var answer = edits.Select(edit =>
                {
                    var offset = lineStarts[edit.Range.Start.Line] + edit.Range.Start.Character;
                    var length = lineStarts[edit.Range.End.Line] + edit.Range.End.Character - offset;
                    return new TextChange(offset, length, edit.NewText);
                }).ToList();
                if (CodeEditorViewModel.RefusedRenameEdit(text, 0, answer) is { } refused)
                    failures.Add($"{name}: renaming {word} edits read-only line {LineOf(lineStarts, refused.Offset)}");
                ownQualifiers += answer.Count(c => ReadOnlyRegions.Touches(regions, c.Offset, c.Length));

                var highlight = await client.RequestDocumentHighlightAsync(uri, at, TestContext.Current.CancellationToken);
                foreach (var h in highlight ?? [])
                {
                    if (protectedLines.Contains(h.Range.Start.Line))
                        failures.Add($"{name}: highlighting {word} marks read-only line {h.Range.Start.Line}");
                }
            }
        }

        renamed.Should().BeGreaterThan(0, "the corpus's code uses its controls' properties' names");
        ownQualifiers.Should().BeGreaterThan(0, "the inline files rename members whose runs name them, so the exception is used");
        declinedDeclared.Should().BeGreaterThan(0, "the corpus's code refers to its controls by name");
        failures.Should().BeEmpty();
    }

    [BundledServerFact]
    public async Task RenamingAMemberTakesItsOwnAttributeQualifiersAlong()
    {
        // The exception above, shown to be exercised: the rename answer carries both qualifier edits, and the
        // code window applies them rather than refusing the rename.
        var client = await ConnectAsync();
        var uri = await OpenAsync(client, Class);

        var rename = await client.RequestRenameAsync(uri, new Position(15, 21), "Amount", TestContext.Current.CancellationToken);

        rename!.Changes![uri].Select(e => (e.Range.Start.Line, e.Range.Start.Character))
            .Should().Equal((15, 20), (16, 10), (17, 10), (18, 0));
    }

    [BundledServerFact]
    public async Task TheServerRefusesExactlyTheNewNamesTheIdeRefuses()
    {
        // The rule for a rename's new name has two hand-kept copies too (#273 task 4.4): the IDE's
        // ProjectNaming.IsValidName and IsReservedWord, which CodeEditorViewModel.NewNameRefusal composes, and the
        // server's VbTextHelpers.IsName. The IDE asks first and never sends a name it refuses, so nothing else would
        // notice the two drifting apart; a client that does not ask first would then be answered differently.
        string[] candidates =
        [
            "Amount", "Grand_Total", "RemX", "Rem1", "Stop", "a1",
            "Rem", "rem", "EMPTY", "New", "nothing",
            "1Amount", "_Amount", "Grand Total", "Gr-and", "Amount$", "",
        ];
        var client = await ConnectAsync();
        var uri = await OpenAsync(client, Class);

        var disagreements = new List<string>();
        foreach (var name in candidates)
        {
            var ideRefuses = !ProjectNaming.IsValidName(name) || ProjectNaming.IsReservedWord(name);
            var rename = await client.RequestRenameAsync(uri, new Position(15, 21), name, TestContext.Current.CancellationToken);
            var serverRefuses = rename is null;
            if (ideRefuses != serverRefuses)
                disagreements.Add($"'{name}': the IDE {(ideRefuses ? "refuses" : "accepts")} it, the server {(serverRefuses ? "refuses" : "accepts")} it");
        }

        disagreements.Should().BeEmpty();
        candidates.Count(n => !ProjectNaming.IsValidName(n) || ProjectNaming.IsReservedWord(n))
            .Should().Be(11, "both verdicts are exercised, the reserved words and the shapes among the refusals");
    }

    // ── Harness ───────────────────────────────────────────────────────────────────────────────────────

    private async Task<VBLspClient> ConnectAsync()
    {
        var loggerFactory = LoggerFactory.Create(_ => { });
        var transport = new StdioProcessLspTransport(
            new LspServerInfo(BundledServer.Find()!, "", Path.GetTempPath()),
            loggerFactory.CreateLogger<StdioProcessLspTransport>());

        _client = new VBLspClient(transport, loggerFactory.CreateLogger<VBLspClient>(), "vb6");
        _client.DiagnosticsPublished += (_, p) => Published(p.Uri).TrySetResult(p);
        await _client.StartAsync(TestContext.Current.CancellationToken);
        return _client;
    }

    private TaskCompletionSource<PublishDiagnosticsParams> Published(string uri) =>
        _published.GetOrAdd(uri, _ => new TaskCompletionSource<PublishDiagnosticsParams>(
            TaskCreationOptions.RunContinuationsAsynchronously));

    /// <summary>Opens <paramref name="text"/> as a new document and waits for its first publish.</summary>
    private async Task<string> OpenAsync(VBLspClient client, string text)
    {
        var uri = $"vb6://module/Regions{Interlocked.Increment(ref _documents)}";
        await client.OpenDocumentAsync(uri, text, TestContext.Current.CancellationToken);
        await Published(uri).Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        return uri;
    }

    private async Task<PublishDiagnosticsParams> OpenAndPublishAsync(VBLspClient client, string text) =>
        await Published(await OpenAsync(client, text)).Task;

    [Fact]
    public void TheInlineFilesCarryRunsThatDescribeAMemberAndOrphansBoth()
    {
        // The premise of every comparison above for task 4.1: the two copies of the rule agree on runs only
        // where the inputs have runs of each kind, and of each shape the rule reads -- a continued declaration,
        // a comma list, WithEvents, a colon, constants, a rename's leftover, a property's Get and Let, a
        // procedure's run naming another member, and an orphan of each sort.
        var runs = ReadOnlyRegions.MemberRuns(ClassWithOrphans, 0);

        runs.Where(r => r.DescribesMember).Select(r => (r.Kind, r.Key)).Should().Equal(
            (MemberRunKind.Variable, new MemberRunKey("CLOCK", 0)), (MemberRunKind.Variable, new MemberRunKey("COUNT", 0)),
            (MemberRunKind.Variable, new MemberRunKey("B", 0)), (MemberRunKind.Variable, new MemberRunKey("C2", 0)),
            (MemberRunKind.RenamedVariable, new MemberRunKey("SCORE", 0)), (MemberRunKind.Procedure, new MemberRunKey("TOTAL", 0)),
            (MemberRunKind.Procedure, new MemberRunKey("VALUE", 0)), (MemberRunKind.Procedure, new MemberRunKey("VALUE", 1)),
            (MemberRunKind.Procedure, new MemberRunKey("HALT", 0)));
        runs.Count(r => !r.DescribesMember).Should().Be(8);
        ProtectedLines(ClassWithOrphans).Should().Equal(Enumerable.Range(0, 6).Concat([10, 11, 13, 15, 17, 21, 27, 32, 36, 43]),
            "the lines the server's own tests pin for the same text (WholeFileFixtures.ClassWithOrphans)");

        // And each rule of the reader, and of what a line is, decides one run of the form, so a copy of the rule
        // that breaks one disagrees with the other on that run's lines.
        ProtectedLines(FormWithReaderShapes).Should().Equal(
            Enumerable.Range(0, 9).Concat([11, 23, 25, 28, 30, 36, 44, 46, 47, 50, 53, 54, 55, 56, 76, 80]),
            "the lines the server's own tests pin for the same text (WholeFileFixtures.FormWithReaderShapes)");
    }

    /// <summary>
    /// Every whole VB6 file the repository carries, those <c>HEXIDE_ROUNDTRIP_CORPUS</c> names, and four
    /// inline files, three of them carrying members' attribute lines.
    /// </summary>
    /// <remarks>
    /// The inline files stay although <c>corpus/member-attributes</c> now carries runs VB6's own IDE wrote, because
    /// VB6 writes no orphan and no rename's leftover, and each of the reader's rules is decided by one shape there.
    /// That folder is read whole, its evidence half included: this compares the two copies of the rule and is no
    /// round trip, and the evidence's inputs are the misplaced runs VB6 itself loaded (#273 task 4.6).
    /// </remarks>
    private static IEnumerable<(string Name, string Text)> Inputs()
    {
        yield return ("inline form", Form);
        yield return ("inline class", Class);
        yield return ("inline class with orphans", ClassWithOrphans);
        yield return ("inline form with the reader's shapes", FormWithReaderShapes);

        // The member-attribute corpus is required, where the other roots are taken if found: it is the only whole-file
        // source of runs VB6 wrote and of the misplaced ones it loaded, and without it the comparison would go on
        // passing over the inline files and the demos alone.
        var memberAttributes = FindUpwards(Path.Join("corpus", "member-attributes"));
        memberAttributes.Should().NotBeNull("corpus/member-attributes ships with the repository");
        new[] { "round-trip", "evidence" }.Where(half => !Directory.Exists(Path.Join(memberAttributes!, half)))
            .Should().BeEmpty("both halves are read: the runs VB6 wrote, and the misplaced ones it loaded");
        var roots = new List<string?> { FindUpwards("demo"), FindUpwards(Path.Join("corpus", "designer")), memberAttributes };
        var env = Environment.GetEnvironmentVariable("HEXIDE_ROUNDTRIP_CORPUS");
        if (!string.IsNullOrWhiteSpace(env))
            roots.AddRange(env.Split(';', StringSplitOptions.RemoveEmptyEntries).Where(Directory.Exists));

        string[] extensions = [".frm", ".cls", ".bas", ".ctl", ".pag", ".dob", ".dsr"];
        foreach (var root in roots.OfType<string>())
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                if (extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    yield return (Path.GetRelativePath(root, file), File.ReadAllText(file));
            }
        }
    }

    private static string? FindUpwards(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Join(dir.FullName, relative);
            if (Directory.Exists(candidate))
                return candidate;
        }
        return null;
    }

    /// <summary>The lines of <paramref name="text"/> that the IDE's regions cover, 0-based.</summary>
    private static HashSet<int> ProtectedLines(string text)
    {
        var regions = ReadOnlyRegions.Of(text, 0);
        var starts = LineStarts(text);
        return Enumerable.Range(0, starts.Count)
            .Where(i => regions.Any(r => starts[i] >= r.Start && starts[i] < r.End))
            .ToHashSet();
    }

    /// <summary>The 0-based line <paramref name="offset"/> falls on.</summary>
    private static int LineOf(List<int> lineStarts, int offset) => lineStarts.FindLastIndex(start => start <= offset);

    private static List<int> LineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
                starts.Add(i + 1);
        }
        return starts;
    }

    private static string[] Lines(string text) => text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

    /// <summary><paramref name="text"/> with two spaces after every line that has anything on it.</summary>
    private static string WithTrailingSpaces(string text) =>
        string.Join('\n', text.Split('\n').Select(line =>
        {
            var cr = line.EndsWith('\r') ? "\r" : "";
            var content = line.TrimEnd('\r');
            return content.Trim().Length > 0 ? content + "  " + cr : line;
        }));

    /// <summary>The words of <paramref name="line"/> outside string literals and comments, with where each starts.</summary>
    private static IEnumerable<(string Word, int Character)> Words(string line)
    {
        var inString = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                inString = !inString;
                continue;
            }
            if (inString)
                continue;
            if (c == '\'')
                yield break;
            if (!char.IsLetter(c) || (i > 0 && (char.IsLetterOrDigit(line[i - 1]) || line[i - 1] == '_')))
                continue;

            var end = i;
            while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] == '_'))
                end++;
            yield return (line[i..end], i);
            i = end - 1;
        }
    }

    // ── Inline files (the server's WholeFileFixtures, in the shape VB6 writes them) ──────────────────────

    private const string Form =
        "VERSION 5.00\r\n" +                                                               // 0
        "Object = \"{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0\"; \"MSCOMCTL.OCX\"\r\n" + // 1
        "Begin VB.Form frmOrders \r\n" +                                                  // 2
        "   Caption         =   \"Orders\"\r\n" +                                          // 3
        "   ClientHeight    =   3000\r\n" +                                               // 4
        "   Begin VB.CommandButton cmdOK \r\n" +                                          // 5
        "      Caption         =   \"OK\"\r\n" +                                           // 6
        "      BeginProperty Font \r\n" +                                                 // 7
        "         Name            =   \"MS Sans Serif\"\r\n" +                             // 8
        "      EndProperty\r\n" +                                                         // 9
        "   End\r\n" +                                                                    // 10
        "End\r\n" +                                                                       // 11
        "Attribute VB_Name = \"frmOrders\"\r\n" +                                         // 12
        "Attribute VB_GlobalNameSpace = False\r\n" +                                      // 13
        "Attribute VB_Creatable = False\r\n" +                                            // 14
        "Attribute VB_PredeclaredId = True\r\n" +                                         // 15
        "Attribute VB_Exposed = False\r\n" +                                              // 16
        "Option Explicit\r\n" +                                                           // 17
        "\r\n" +                                                                          // 18
        "private sub cmdOK_Click()\r\n" +                                                 // 19
        "dim Caption as string\r\n" +                                                     // 20
        "Caption = cmdOK.Caption\r\n" +                                                   // 21
        "end sub\r\n";                                                                    // 22

    private const string Class =
        "VERSION 1.0 CLASS\r\n" +                                                         // 0
        "BEGIN\r\n" +                                                                     // 1
        "  MultiUse = -1  'True\r\n" +                                                    // 2
        "  Persistable = 0  'NotPersistable\r\n" +                                        // 3
        "END\r\n" +                                                                       // 4
        "Attribute VB_Name = \"Order\"\r\n" +                                             // 5
        "Attribute VB_GlobalNameSpace = False\r\n" +                                      // 6
        "Attribute VB_Creatable = True\r\n" +                                             // 7
        "Attribute VB_PredeclaredId = False\r\n" +                                        // 8
        "Attribute VB_Exposed = False\r\n" +                                              // 9
        "Option Explicit\r\n" +                                                           // 10
        "\r\n" +                                                                          // 11
        "Public WithEvents Clock As Timer\r\n" +                                          // 12
        "Attribute Clock.VB_VarHelpID = -1\r\n" +                                         // 13
        "\r\n" +                                                                          // 14
        "public property get Total() as currency\r\n" +                                   // 15
        "Attribute Total.VB_Description = \"The order total\"\r\n" +                      // 16
        "Attribute Total.VB_ProcData.VB_Invoke_Property = \"General\"\r\n" +              // 17
        "total = 0\r\n" +                                                                 // 18
        "end property\r\n";                                                               // 19

    /// <summary>
    /// The server's <c>WholeFileFixtures.ClassWithOrphans</c>, written by hand in shapes the bundled grammar reads:
    /// runs VB6 gives to the line above, of each kind and shape the rule reads, and an orphan of each sort.
    /// Its remarks there number every run.
    /// </summary>
    private const string ClassWithOrphans =
        "VERSION 1.0 CLASS\r\n" +                                                         // 0
        "BEGIN\r\n" +                                                                     // 1
        "  MultiUse = -1  'True\r\n" +                                                    // 2
        "END\r\n" +                                                                       // 3
        "Attribute VB_Name = \"Ledger\"\r\n" +                                            // 4
        "Attribute VB_Exposed = False\r\n" +                                              // 5
        "Option Explicit\r\n" +                                                           // 6
        "Attribute VB_Description = \"dotless, below the code\"\r\n" +                    // 7  orphan
        "\r\n" +                                                                          // 8
        "Public WithEvents Clock As Timer, Alarm As Timer\r\n" +                          // 9
        "Attribute Clock.VB_VarHelpID = -1\r\n" +                                         // 10
        "Attribute Alarm.VB_VarDescription = \"The alarm\"\r\n" +                         // 11
        "public rate as currency, count as long ' totals\r\n" +                           // 12
        "Attribute count.VB_VarDescription = \"How many\"\r\n" +                          // 13
        "Public A As Long: Public B As Long\r\n" +                                        // 14
        "Attribute B.VB_VarDescription = \"After a colon.\"\r\n" +                        // 15
        "Private Const C1 = 1, C2 = 2\r\n" +                                              // 16
        "Attribute C2.VB_VarDescription = \"Second of two consts.\"\r\n" +                // 17
        "Public Tally As Long\r\n" +                                                      // 18
        "Attribute Tally.VB_Description = \"a procedure attribute\"\r\n" +                // 19 orphan
        "Private Score As Long\r\n" +                                                     // 20
        "Attribute Points.VB_VarDescription = \"left by a rename\"\r\n" +                 // 21
        "Private Spare As Long\r\n" +                                                     // 22
        "Attribute Clock.VB_VarDescription = \"another variable's\"\r\n" +                // 23 orphan
        "\r\n" +                                                                          // 24
        "public function total(byval a as long, _\r\n" +                                  // 25
        "    byval b as long) as currency\r\n" +                                          // 26
        "Attribute total.VB_Description = \"The sum\"\r\n" +                              // 27
        "total = a + b\r\n" +                                                             // 28
        "Attribute total.VB_UserMemId = 0\r\n" +                                          // 29 orphan
        "end function\r\n" +                                                              // 30
        "public property get Value() as long\r\n" +                                       // 31
        "Attribute Value.VB_UserMemId = 0\r\n" +                                          // 32
        "Value = 1\r\n" +                                                                 // 33
        "end property\r\n" +                                                              // 34
        "public property let Value(byval v as long)\r\n" +                                // 35
        "Attribute Value.VB_Description = \"The value\"\r\n" +                            // 36
        "end property\r\n" +                                                              // 37
        "\r\n" +                                                                          // 38
        "Attribute Value.VB_HelpID = 1\r\n" +                                             // 39 orphan
        "' the next one describes nothing either\r\n" +                                   // 40
        "Attribute Value.VB_MemberFlags = \"40\"\r\n" +                                   // 41 orphan
        "public sub Halt()\r\n" +                                                         // 42
        "Attribute Total.VB_Description = \"named for another member\"\r\n" +             // 43
        "end sub\r\n" +                                                                   // 44
        "public sub Ring()\r\n" +                                                         // 45
        "Attribute Ring.VB_Description = \"mixed\"\r\n" +                                 // 46 orphan
        "Attribute Ring.VB_VarHelpID = -1\r\n" +                                          // 47 orphan
        "end sub\r\n" +                                                                   // 48
        "public sub Chime()\r\n" +                                                        // 49
        "Attribute Chime.VB_VarHelpID = -1\r\n" +                                         // 50 orphan
        "end sub\r\n";                                                                    // 51

    /// <summary>
    /// The server's <c>WholeFileFixtures.FormWithReaderShapes</c>: one run for each rule of the declaration reader
    /// and of what a line is, decided by that rule alone, in shapes VB6 or the bundled grammar refuses. Its
    /// remarks there say which rule decides which run.
    /// </summary>
    private const string FormWithReaderShapes =
        "VERSION 5.00\r\n" +                                                                       // 0
        "Begin VB.Form frmShapes \r\n" +                                                          // 1
        "   Caption         =   \"Shapes\"\r\n" +                                                  // 2
        "   Begin VB.CommandButton cmdGo \r\n" +                                                  // 3
        "      Caption         =   \"Go\"\r\n" +                                                   // 4
        "   End\r\n" +                                                                            // 5
        "End\r\n" +                                                                               // 6
        "Attribute VB_Name = \"frmShapes\"\r\n" +                                                 // 7
        "Attribute VB_Exposed = False\r\n" +                                                      // 8
        "Option Explicit\r\n" +                                                                   // 9
        "Private Declare PtrSafe Function GetTickCount Lib \"kernel32\" () As Long\r\n" +         // 10
        "Attribute GetTickCount.VB_Description = \"PtrSafe is VBA7's Declare\"\r\n" +             // 11
        "Public Event Rang()\r\n" +                                                               // 12
        "Public Enum Sizes\r\n" +                                                                 // 13
        "Attribute Sizes.VB_VarDescription = \"an Enum declares nothing\"\r\n" +                  // 14 orphan
        "    Small = 1\r\n" +                                                                     // 15
        "Attribute Small.VB_VarDescription = \"nor does an enum's member\"\r\n" +                 // 16 orphan
        "End Enum\r\n" +                                                                          // 17
        "Private Type Point\r\n" +                                                                // 18
        "Attribute Point.VB_VarDescription = \"nor does a Type\"\r\n" +                           // 19 orphan
        "    X As Long\r\n" +                                                                     // 20
        "End Type\r\n" +                                                                          // 21
        "Private [Total] As Long\r\n" +                                                           // 22
        "Attribute Total.VB_VarDescription = \"a bracketed name\"\r\n" +                          // 23
        "Public a1 As Long, b1(1 To 3, 4) As String, c1 As Long\r\n" +                            // 24
        "Attribute c1.VB_VarDescription = \"past a comma in bounds\"\r\n" +                       // 25
        "Public d1 As Long, f1 As Long, _\r\n" +                                                  // 26
        "       e1 As Long\r\n" +                                                                 // 27
        "Attribute e1.VB_VarDescription = \"past a continuation\"\r\n" +                          // 28
        "Public g1 As Long, k1 As Long: Public h1 As Long\r\n" +                                  // 29
        "Attribute h1.VB_VarDescription = \"after a colon\"\r\n" +                                // 30
        "Public m1 As Long, p1 As Long ' x, n1\r\n" +                                             // 31
        "Attribute n1.VB_VarDescription = \"in a comment\"\r\n" +                                 // 32 orphan
        "Public q1 As Long, r1 As Long: rem x: Dim s1 As Long\r\n" +                              // 33
        "Attribute s1.VB_VarDescription = \"after Rem\"\r\n" +                                    // 34 orphan
        "Private Const Lf = 10, Sep = \":\", Tb = 9\r\n" +                                        // 35
        "Attribute Tb.VB_VarDescription = \"past a string\"\r\n" +                                // 36
        "Public Spare1 As Long\r\n" +                                                             // 37
        "Attribute cmdGo.VB_VarDescription = \"names a control\"\r\n" +                           // 38 orphan
        "Public Spare2 As Long\r\n" +                                                             // 39
        "Attribute Inner.VB_VarDescription = \"names a local\"\r\n" +                             // 40 orphan
        "Public Spare3 As Long\r\n" +                                                             // 41
        "Attribute Lone.VB_VarDescription = \"names a procedure\"\r\n" +                          // 42 orphan
        "Public Spare4 As Long\r\n" +                                                             // 43
        "Attribute Gone.VB_VarDescription = \"names nothing: a rename left it\"\r\n" +            // 44
        "Public Spare5 As Long\r\n" +                                                             // 45
        "Attribute Moved.VB_VarDescription = \"a rename left it, in two cases\"\r\n" +            // 46
        "Attribute moved.VB_VarHelpID = 5\r\n" +                                                  // 47
        "Private m_\r\n" +                                                                        // 48
        "Public Function Foo() As Long\r\n" +                                                     // 49
        "Attribute Foo.VB_Description = \"under a name ending in an underscore\"\r\n" +           // 50
        "End Function\r\n" +                                                                      // 51
        "Public Sub Wrapped()\r\n" +                                                              // 52
        "Attribute Wrapped.VB_Description = _\r\n" +                                              // 53
        "\"continued\"\r\n" +                                                                     // 54
        "Attribute _\r\n" +                                                                       // 55
        "Wrapped.VB_HelpID = 3\r\n" +                                                             // 56
        "End Sub\r\n" +                                                                           // 57
        "Public Sub Split(ByVal a As Long, _\r\n" +                                               // 58
        "Attribute Split.VB_Description = \"part of the declaration\"\r\n" +                      // 59
        "ByVal b As Long)\r\n" +                                                                  // 60
        "End Sub\r\n" +                                                                           // 61
        "Public Sub Lone(ByVal a As Long, _\r\n" +                                                // 62
        "_\r\n" +                                                                                 // 63
        "ByVal b As Long)\r\n" +                                                                  // 64
        "Attribute Lone.VB_Description = \"under the declaration's last line\"\r\n" +             // 65 orphan
        "End Sub\r\n" +                                                                           // 66
        "Public Sub Body()\r\n" +                                                                 // 67
        "    Dim Inner As Long\r\n" +                                                             // 68
        "Attribute Inner.VB_VarDescription = \"a local's\"\r\n" +                                 // 69 orphan
        "End Sub\r\n" +                                                                           // 70
        "Public Sub Reserved()\r\n" +                                                             // 71
        "Attribute Reserved.VB_Description = \"beside a line naming nothing\"\r\n" +              // 72 orphan
        "Attribute(1) = 2\r\n" +                                                                  // 73 orphan
        "End Sub\r\n" +                                                                           // 74
        "Public Sub Prefixed()\r\n" +                                                             // 75
        "Attribute Prefixed.VB_Description = \"beside a line of another word\"\r\n" +             // 76
        "AttributeCount = 2\r\n" +                                                                // 77
        "End Sub\r\n" +                                                                           // 78
        "Public Sub Lowered()\r\n" +                                                              // 79
        "attribute Lowered.VB_Description = \"the word in lower case\"\r\n" +                     // 80
        "End Sub\r\n";                                                                            // 81

    /// <summary>The inline form with a designer line the grammar cannot read.</summary>
    private static readonly string DamagedFormHeader =
        Form.Replace("ClientHeight    =   3000", "ClientHeight    =   = 3000", StringComparison.Ordinal);
}
