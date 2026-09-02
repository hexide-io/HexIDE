using System.IO;
using System.Text.Json;
using Antlr4.Runtime;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Does HexIDE's grammar agree with real VB6 about what is legal?
///
/// <para>
/// The corpus under <c>/corpus</c> is 319 clean-room cases on line continuations and statement
/// separators, each already compiled by <c>vb6.exe</c> and its verdict recorded in <c>results.json</c>.
/// This turns those recorded facts into a gate: parse every case with the interpreter's own grammar and
/// compare.
/// </para>
///
/// <para>
/// The two directions are not equally bad, which is why they are separate tests. Rejecting code VB6
/// accepts takes down a whole module — nothing in the file runs and the editor cannot open it usefully —
/// so <b>false rejection is the failure that matters</b>. Accepting code VB6 rejects merely means a bad
/// program gets further than it should, and is often the deliberate consequence of a permissive grammar
/// that defers checks to run time.
/// </para>
///
/// <para>
/// This is a PARSE check, not an execution check: it asks whether the module loads, not whether it does
/// the right thing. That is the question the corpus can answer, and it is the one with the largest blast
/// radius.
/// </para>
/// </summary>
public class CorpusConformanceTests
{
    private sealed record Row(string Key, string Area, string Actual, string Error);

    private sealed record Case(string Key, string Scope, string[] Code);

    /// <summary>Cases the interpreter is KNOWN to disagree on, each with the reason. A row here is a
    /// documented divergence, not a licence to drift — the second test fails if one is silently fixed,
    /// so the list can only shrink deliberately.</summary>
    private static readonly Dictionary<string, string> KnownDivergences = new()
    {
        // Grouped by CAUSE, because they are five defects rather than 49. The tag is the cause; the
        // reasons are below. This list may only shrink, and never silently: KnownDivergencesAreStillReal
        // fails if an entry is left behind after its case starts passing.
        //
        //  COLON-SEPARATOR     The lexer treats a colon as a statement separator only when a SPACE follows
        //                      (NEWLINE : ... | COLON ' '), so every tight or trailing colon is refused.
        //                      By far the largest cluster. Not a one-line fix: deleting COLON from NEWLINE
        //                      removes the very token lineLabel needs, so it wants a parser-level change.
        //  SPLIT-KEYWORD       VB6 lets a continuation split a MULTI-WORD keyword (End _ Sub, End _ If,
        //                      Select _ Case). Those are single lexer tokens here, so the split breaks them.
        //  STRING-CONTINUATION A trailing underscore INSIDE a string literal. VB6's behaviour is measured
        //                      but NOT understood - it continues the line in a Debug.Print output list and
        //                      does not in an assignment (see the oracle doc). Not worth implementing to a
        //                      rule nobody can state.
        //  FRX-OFFSET          FRX_OFFSET (COLON [0-9A-F]+) is a DESIGNER-FILE token live in ordinary code,
        //                      so a colon followed by hex digits lexes as ONE token and swallows the
        //                      separator. Predicted by reading the lexer before it was ever compiled.
        //  REM-FORM            COMMENT requires REM to be followed by a space, so a bare Rem, a Rem
        //                      separated by a tab, and Rem immediately followed by a colon are all refused.
        //  OTHER               Individually caused; see each case's own why in the corpus.
        ["gap-fill/two-file-numbers-across-a-colon"] = "COLON-SEPARATOR",
        ["gap-fill/with-block-colon-tight-against-dot-and-end"] = "COLON-SEPARATOR",
        ["separator-and-continuation-together/continuation-drags-a-label-onto-a-statement"] = "COLON-SEPARATOR",
        ["separator-and-continuation-together/continuation-line-is-a-bare-colon"] = "COLON-SEPARATOR",
        ["separator-and-continuation-together/single-line-if-colon-then-continuation-then-else"] = "COLON-SEPARATOR",
        ["separator-basics/sep-colon-before-else-single-line"] = "COLON-SEPARATOR",
        ["separator-basics/sep-double-colon-tight"] = "COLON-SEPARATOR",
        ["separator-basics/sep-line-of-only-colons"] = "COLON-SEPARATOR",
        ["separator-basics/sep-mixed-chain-with-empty-statement"] = "COLON-SEPARATOR",
        ["separator-basics/sep-no-space-around-colon"] = "COLON-SEPARATOR",
        ["separator-basics/sep-numeric-line-number-with-colon"] = "COLON-SEPARATOR",
        ["separator-basics/sep-trailing-colon-end-of-line"] = "COLON-SEPARATOR",
        ["separator-in-control-flow/both-branches-colon-joined-module"] = "COLON-SEPARATOR",
        ["separator-in-control-flow/colon-immediately-after-then"] = "COLON-SEPARATOR",
        ["separator-in-control-flow/colon-immediately-before-else"] = "COLON-SEPARATOR",
        ["separator-in-control-flow/double-colon-empty-statement-in-then"] = "COLON-SEPARATOR",
        ["separator-in-control-flow/multi-then-statements-before-else"] = "COLON-SEPARATOR",
        ["separator-in-control-flow/then-tail-colon-no-spaces"] = "COLON-SEPARATOR",
        ["separator-in-control-flow/trailing-colon-at-end-of-then-branch"] = "COLON-SEPARATOR",
        ["separator-vs-label/double-colon-empty-statement"] = "COLON-SEPARATOR",
        ["separator-vs-label/label-colon-no-space-before-stmt"] = "COLON-SEPARATOR",
        ["separator-vs-label/label-named-reserved-word"] = "COLON-SEPARATOR",
        ["separator-vs-label/label-named-soft-keyword"] = "COLON-SEPARATOR",
        ["separator-vs-label/numeric-label-alone-on-line"] = "COLON-SEPARATOR",
        ["separator-vs-label/numeric-label-with-colon"] = "COLON-SEPARATOR",
        ["separator-vs-label/separator-colon-no-surrounding-space"] = "COLON-SEPARATOR",
        ["separator-vs-label/trailing-colon-end-of-line"] = "COLON-SEPARATOR",
        ["whitespace-and-eol-edges/colon-only-line-at-module-level"] = "COLON-SEPARATOR",
        ["whitespace-and-eol-edges/empty-proc-body-colon-only"] = "COLON-SEPARATOR",
        ["whitespace-and-eol-edges/tab-around-colon-separator"] = "COLON-SEPARATOR",

        ["continuation-basics/cont-splitting-end-sub"] = "SPLIT-KEYWORD",
        ["continuation-illegal/continuation-splitting-end-sub"] = "SPLIT-KEYWORD",
        ["continuation-vs-identifier/continuation-splits-end-sub"] = "SPLIT-KEYWORD",
        ["gap-fill/end-if-split-by-continuation"] = "SPLIT-KEYWORD",
        ["gap-fill/select-case-and-end-select-split-by-continuation"] = "SPLIT-KEYWORD",
        ["separator-with-declarations/end-type-split-by-continuation"] = "SPLIT-KEYWORD",

        ["continuation-basics/cont-inside-string-literal"] = "STRING-CONTINUATION",
        ["continuation-illegal/split-string-literal"] = "STRING-CONTINUATION",
        ["continuation-in-strings-comments/string-underscore-at-eol-unterminated"] = "STRING-CONTINUATION",

        ["gap-fill/colon-tight-before-hex-digit-name"] = "FRX-OFFSET",
        ["gap-fill/colon-tight-before-uppercase-hex-identifier"] = "FRX-OFFSET",
        ["gap-fill/hex-literal-then-tight-colon-then-hex-name"] = "FRX-OFFSET",

        ["gap-fill/bare-rem-with-no-text"] = "REM-FORM",
        ["gap-fill/line-number-then-rem-without-a-colon"] = "REM-FORM",
        ["gap-fill/rem-immediately-followed-by-colon"] = "REM-FORM",
        ["gap-fill/rem-separated-by-a-tab"] = "REM-FORM",

        ["continuation-basics/cont-after-member-dot"] = "OTHER",
        ["separator-with-declarations/hashconst-value-continued"] = "OTHER",
        ["whitespace-and-eol-edges/eof-mid-continuation-no-trailing-newline"] = "OTHER",
    };

    [Fact]
    public void HexIDE_DoesNotRejectCodeThatVB6Accepts()
    {
        var (raw, _, total) = Compare();
        var falseRejections = raw
            .Where(f => !KnownDivergences.ContainsKey(f.Split(' ')[0]))
            .ToList();

        falseRejections.Should().BeEmpty(
            "the interpreter must not refuse to parse code the real compiler accepts — a parse failure "
          + "takes down the whole module, not one statement, so this is the most damaging kind of gap "
          + "there is. {0} of {1} corpus cases were rejected:\n{2}",
            falseRejections.Count, total, string.Join("\n", falseRejections.Select(f => "    " + f)));
    }

    [Fact]
    public void KnownDivergencesAreStillReal()
    {
        // A stale exemption is as bad as an undocumented one: it permits a future regression under a
        // reason that no longer applies. If a case has been fixed, delete its entry.
        var (falseRejections, falseAcceptances, _) = Compare();
        var stillWrong = falseRejections.Concat(falseAcceptances)
            .Select(f => f.Split(' ')[0]).ToHashSet(StringComparer.Ordinal);

        foreach (var (key, why) in KnownDivergences)
            stillWrong.Should().Contain(key,
                "KnownDivergences lists '{0}' ({1}), but the interpreter now agrees with VB6 — remove the entry",
                key, why);
    }

    [Fact]
    public void TheCorpusAndItsRecordedVerdictsAreBothPresent()
    {
        // Without this the two tests above pass vacuously, which is the failure mode a corpus gate dies
        // of: it goes quiet and everyone assumes it is still guarding something.
        var (_, _, total) = Compare();
        total.Should().BeGreaterThan(250,
            "the corpus should carry its full set of compiled verdicts; found {0}", total);
    }

    // ------------------------------------------------------------------------------------------------

    private static (List<string> FalseRejections, List<string> FalseAcceptances, int Total) Compare()
    {
        var root = RepoRoot();
        var dir = Path.Combine(root, "corpus", "continuation-and-separator");
        Directory.Exists(dir).Should().BeTrue("the corpus should be at {0}", dir);

        var resultsPath = Path.Combine(dir, "results.json");
        File.Exists(resultsPath).Should().BeTrue(
            "the compiled verdicts should be at {0} — regenerate with scripts/vb6-legality.ps1", resultsPath);

        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var verdicts = JsonSerializer.Deserialize<List<Row>>(File.ReadAllText(resultsPath), opts)!
            // audit.json is a findings REPORT that reuses the case schema, not runnable cases.
            .Where(r => r.Area != "audit")
            .ToDictionary(r => r.Key, r => r.Actual, StringComparer.Ordinal);

        var falseRejections = new List<string>();
        var falseAcceptances = new List<string>();
        var total = 0;

        foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => f))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name is "results" or "audit") continue;

            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var el in doc.RootElement.GetProperty("cases").EnumerateArray())
            {
                var id = el.GetProperty("id").GetString()!;
                var key = $"{name}/{id}";
                // A case may declare itself undeliverable by the compile harness (the line-ending cases).
                // With no compiler verdict there is nothing to compare against.
                if (el.TryGetProperty("skip", out _) || !verdicts.TryGetValue(key, out var vb6)) continue;
                if (vb6 is not ("legal" or "illegal")) continue;   // timeouts prove nothing either way

                var scope = el.TryGetProperty("scope", out var s) ? s.GetString() : "statement";
                var code = el.GetProperty("code").EnumerateArray().Select(x => x.GetString()!).ToArray();
                var module = scope == "module"
                    ? string.Join("\r\n", code)
                    : "Sub Main()\r\n" + string.Join("\r\n", code) + "\r\nEnd Sub";

                total++;
                var parses = Parses(module);
                if (vb6 == "legal" && !parses) falseRejections.Add($"{key} (VB6 accepts it)");
                else if (vb6 == "illegal" && parses) falseAcceptances.Add($"{key} (VB6 rejects it)");
            }
        }

        // Returned UNFILTERED. Subtracting the known divergences here would make them invisible to the
        // staleness check as well, so it could never see one that had started passing — the check would
        // report every entry stale, which is exactly what it did until this was separated.
        return (falseRejections, falseAcceptances, total);
    }

    /// <summary>Parse with the interpreter's own grammar, reporting only whether it succeeded.</summary>
    private static bool Parses(string source)
    {
        var lexer = new VB6Lexer(new AntlrInputStream(new StringReader(source)));
        var parser = new VB6Parser(new CommonTokenStream(lexer));
        lexer.RemoveErrorListeners();
        parser.RemoveErrorListeners();
        var listener = new CountingErrorListener();
        lexer.AddErrorListener(listener);
        parser.AddErrorListener(listener);
        try
        {
            parser.startRule();
        }
        catch (Exception)
        {
            return false;
        }
        return listener.Errors == 0;
    }

    private sealed class CountingErrorListener : IAntlrErrorListener<IToken>, IAntlrErrorListener<int>
    {
        public int Errors { get; private set; }

        public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol,
            int line, int charPositionInLine, string msg, RecognitionException e) => Errors++;

        public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol,
            int line, int charPositionInLine, string msg, RecognitionException e) => Errors++;
    }

    /// <summary>The monorepo root — the directory holding both halves. Not located by solution file:
    /// there is a HexIDE.slnx in the root AND in IDE/, so that search stops one level too early.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "IDE"))
                             && Directory.Exists(Path.Combine(dir.FullName, "LspServer"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the repository root should be findable from {0}", AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
