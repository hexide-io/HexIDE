using System.IO;
using System.Text.Json;
using Antlr4.Runtime;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Does HexIDE's grammar agree with real VB6 about what is legal?
///
/// <para>
/// The corpus under <c>/corpus</c> is 329 clean-room cases on line continuations and statement
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
    /// documented divergence, not a licence to drift — KnownDivergencesAreStillReal fails if one is
    /// silently fixed, so the list can only shrink deliberately.</summary>
    ///
    /// <remarks>
    /// Both directions live here, and they are NOT equally serious. The first block is the false
    /// REJECTIONS — code vb6.exe accepts and HexIDE refuses, which takes a whole module down. The rest are
    /// false ACCEPTANCES, where a bad program merely gets further than it should.
    ///
    /// <para>
    /// Grouped by CAUSE, because this corpus has already taught that lesson the expensive way — a bucket
    /// labelled LABEL turned out to be mostly one over-broad lexer token, and nine cases across three
    /// areas collapsed into a single fix once that was seen. These fifty-three rows are nine defects.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, string> KnownDivergences = new()
    {
        // ===== FALSE REJECTIONS (10) — the damaging direction. =====

        // STRING-CONTINUATION (3)
        //   A trailing underscore INSIDE a string literal. Measured but NOT understood: it continues the
        //   line in a Debug.Print output list and does not in an assignment. Deliberately unimplemented,
        //   because there is no rule here anyone can state, and inventing one is how a wrong
        //   generalisation gets laundered into a fact.
        ["continuation-basics/cont-inside-string-literal"] = "STRING-CONTINUATION",
        ["continuation-illegal/split-string-literal"] = "STRING-CONTINUATION",
        ["continuation-in-strings-comments/string-underscore-at-eol-unterminated"] = "STRING-CONTINUATION",

        // LABEL-NAME (2)
        //   A line label named after a keyword. `lineLabel` takes an `ambiguousIdentifier`, which does not
        //   reach every reserved word, so `Error:` and friends are refused as labels.
        ["separator-vs-label/label-named-reserved-word"] = "LABEL-NAME",
        ["separator-vs-label/label-named-soft-keyword"] = "LABEL-NAME",

        // OTHER-REJECTION (5) — individually caused; see each case's own why in the corpus.
        ["continuation-basics/cont-after-member-dot"] = "OTHER-REJECTION",
        ["gap-fill/two-file-numbers-across-a-colon"] = "OTHER-REJECTION",
        ["separator-and-continuation-together/continuation-drags-a-label-onto-a-statement"] = "OTHER-REJECTION",
        ["separator-with-declarations/hashconst-value-continued"] = "OTHER-REJECTION",
        ["whitespace-and-eol-edges/eof-mid-continuation-no-trailing-newline"] = "OTHER-REJECTION",

        // ===== FALSE ACCEPTANCES (43) — the mild direction, newly gated. =====

        // UNDERSCORE-IDENTIFIER (16) — the largest lever by far.
        //   LETTER includes the underscore, so a lone `_` is a legal IDENTIFIER here and is not in VB6,
        //   where an identifier may CONTAIN an underscore but never BEGIN with one. Every malformed
        //   continuation below therefore completes as arithmetic against a variable named `_` instead of
        //   failing: `x = 1 +_` becomes `1 + _`.
        //
        //   A lexer fix, and it was TRIED and reverted in this change. `_` alone on a line is legal VB6,
        //   and NEWLINE has already eaten the space that LINE_CONTINUATION needs to recognise it, so
        //   removing the underscore from the identifier start refuses two legal cases to fix one illegal.
        //   Settling how NEWLINE and LINE_CONTINUATION share that whitespace is the real fix, and it
        //   belongs to its own change.
        ["continuation-basics/cont-comment-after-underscore"] = "UNDERSCORE-IDENTIFIER",
        ["continuation-basics/cont-no-space-before-underscore"] = "UNDERSCORE-IDENTIFIER",
        ["continuation-illegal/comment-after-underscore"] = "UNDERSCORE-IDENTIFIER",
        ["continuation-illegal/double-underscore-at-line-end"] = "UNDERSCORE-IDENTIFIER",
        ["continuation-illegal/letter-after-underscore"] = "UNDERSCORE-IDENTIFIER",
        ["continuation-illegal/no-space-before-underscore-after-operator"] = "UNDERSCORE-IDENTIFIER",
        ["continuation-illegal/underscore-only-line-at-column-one"] = "UNDERSCORE-IDENTIFIER",
        ["continuation-in-strings-comments/continuation-splitting-a-token"] = "UNDERSCORE-IDENTIFIER",
        ["continuation-vs-identifier/bracketed-leading-underscore"] = "UNDERSCORE-IDENTIFIER",
        ["continuation-vs-identifier/comment-after-continuation"] = "UNDERSCORE-IDENTIFIER",
        ["continuation-vs-identifier/continuation-splits-identifier"] = "UNDERSCORE-IDENTIFIER",
        ["continuation-vs-identifier/continuation-without-preceding-space"] = "UNDERSCORE-IDENTIFIER",
        ["continuation-vs-identifier/leading-underscore-name"] = "UNDERSCORE-IDENTIFIER",
        ["continuation-vs-identifier/lone-underscore-name"] = "UNDERSCORE-IDENTIFIER",
        ["gap-fill/continuation-splits-identifier-from-type-character"] = "UNDERSCORE-IDENTIFIER",
        ["separator-and-continuation-together/colon-immediately-before-underscore-no-space"] = "UNDERSCORE-IDENTIFIER",

        // COLON-POSITION (9)
        //   `blockSep` treats a colon and a newline as interchangeable, and VB6 does not. A colon may join
        //   STATEMENTS; it may not stand in for the line break that a block construct's header, its Else,
        //   its End, or a procedure declaration requires. A parser fix, and not a small one — it means
        //   those constructs stop accepting `blockSep` and start requiring a newline specifically.
        ["separator-basics/sep-block-if-not-first-on-line"] = "COLON-POSITION",
        ["separator-basics/sep-colon-immediately-after-then"] = "COLON-POSITION",
        ["separator-in-control-flow/statement-before-else-block-form"] = "COLON-POSITION",
        ["separator-in-control-flow/then-colon-then-endif"] = "COLON-POSITION",
        ["separator-vs-label/block-if-after-colon"] = "COLON-POSITION",
        ["separator-with-declarations/colon-before-sub-header"] = "COLON-POSITION",
        ["separator-with-declarations/end-sub-colon-next-sub"] = "COLON-POSITION",
        ["separator-with-declarations/hashendif-trailing-colon"] = "COLON-POSITION",
        ["separator-with-declarations/module-declare-colon-declare"] = "COLON-POSITION",

        // LABEL-POSITION (6)
        //   `lineLabel` is an ordinary blockStmt alternative, so the grammar accepts a label anywhere a
        //   statement may go. VB6 allows one only at the START of a line. A parser fix: the label belongs
        //   in the line prefix, beside lineNumber, rather than in the statement list.
        ["gap-fill/two-labels-on-one-line"] = "LABEL-POSITION",
        ["separator-basics/sep-label-mid-line-after-colon"] = "LABEL-POSITION",
        ["separator-in-control-flow/label-in-the-middle-of-a-line"] = "LABEL-POSITION",
        ["separator-vs-label/label-after-continuation-midline"] = "LABEL-POSITION",
        ["separator-vs-label/label-after-then-not-a-label"] = "LABEL-POSITION",
        ["separator-vs-label/second-name-colon-is-not-a-label"] = "LABEL-POSITION",

        // REM-POSITION (5)
        //   Rem is a STATEMENT in VB6, not a lexical comment — which is exactly why the reference says it
        //   needs a colon when it follows another statement, and why `x = 1 Rem foo` is a syntax error
        //   while `x = 1 ' foo` is fine. HexIDE sends both to the hidden channel, so the Rem form is
        //   accepted anywhere the apostrophe is.
        //
        //   Deliberately NOT fixed. Making Rem visible to the parser means finding it a home in every
        //   position a comment may occupy — module declarations, Type and Enum bodies, between procedures
        //   — and a miss anywhere there is a FALSE REJECTION on ordinary code, trading the damaging
        //   direction for the mild one. What is lost is an error VB6 raises and we do not; the fidelity
        //   guardrail permits a missing error and never a wrong value, and no value is wrong here.
        ["continuation-in-strings-comments/rem-after-statement-without-colon"] = "REM-POSITION",
        ["rem-forms/enum-member-named-rem"] = "REM-POSITION",
        ["separator-basics/sep-rem-without-colon"] = "REM-POSITION",
        ["separator-in-control-flow/rem-after-then-is-still-block"] = "REM-POSITION",
        ["separator-vs-label/rem-without-colon"] = "REM-POSITION",

        // BOUNDED-RESOURCE (2)
        //   VB6 enforces hard limits the grammar has no notion of: at most 25 continuations per logical
        //   line, and 1023 characters per physical line. Neither is a structural question, so a grammar is
        //   the wrong place for either — they belong to a length check, if they are worth having at all.
        //   Refusing to load a long line is not obviously a service to anyone.
        ["gap-fill/physical-line-over-1023-characters"] = "BOUNDED-RESOURCE",
        ["gap-fill/twenty-five-consecutive-continuations"] = "BOUNDED-RESOURCE",

        // NAME-SHAPE (1)
        //   `Public Event Data_Ready()` is refused by VB6 because an event name may not contain an
        //   underscore — it would collide with the `Object_Event` handler naming convention. A rule about
        //   the shape of a name rather than the shape of the program.
        ["continuation-vs-identifier/event-declaration-with-underscore"] = "NAME-SHAPE",

        // OTHER (4) — individually caused; see each case's own why in the corpus.
        ["gap-fill/date-literal-split-by-continuation"] = "OTHER",
        ["separator-in-control-flow/exit-do-swallows-the-loop"] = "OTHER",
        ["separator-in-control-flow/exit-for-swallows-the-next"] = "OTHER",
        ["separator-with-declarations/enum-member-value-continued"] = "OTHER",
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
    public void HexIDE_DoesNotAcceptCodeThatVB6Rejects()
    {
        // The other direction, and the reason it is now gated: this half of Compare() was computed and
        // then consumed by nothing, so a fix that over-reached could widen the grammar past VB6 and the
        // corpus would stay green. That is exactly the failure mode the Rem work risks — a rule that
        // starts a comment too eagerly turns `RemX = 5` into a comment and DELETES the assignment. Silent
        // wrong behaviour, which the project ranks worse than a late error.
        //
        // It is the milder direction in general: a permissive grammar that defers a check to run time is
        // often deliberate here, and several entries below are exactly that. Hence a gate with an
        // exemption list rather than a prohibition.
        var (_, raw, total) = Compare();
        var falseAcceptances = raw
            .Where(f => !KnownDivergences.ContainsKey(f.Split(' ')[0]))
            .ToList();

        falseAcceptances.Should().BeEmpty(
            "the interpreter parsed code the real compiler rejects. That is milder than the reverse — a "
          + "bad program merely gets further than it should — but an unexplained one usually means the "
          + "grammar is wider than VB6 rather than deliberately permissive. {0} of {1} corpus cases:\n{2}",
            falseAcceptances.Count, total, string.Join("\n", falseAcceptances.Select(f => "    " + f)));
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
