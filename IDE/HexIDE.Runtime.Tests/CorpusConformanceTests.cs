using System.IO;
using System.Text.Json;
using Antlr4.Runtime;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Does HexIDE's grammar agree with real VB6 about what is legal?
///
/// <para>
/// The corpus under <c>/corpus</c> is 425 clean-room cases, each already compiled by <c>vb6.exe</c> with
/// its verdict recorded in <c>results.json</c>. This turns those recorded facts into a gate: parse every
/// case with the interpreter's own grammar and compare.
/// </para>
///
/// <para>
/// It began as line continuations and statement separators, and has outgrown that name — the directory
/// still carries it while holding Rem forms, line labels, reserved words and Enums. The gate reads that
/// one directory by path, so a new subject has nowhere else to go. Worth splitting the next time this
/// changes.
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
    /// areas collapsed into a single fix once that was seen. These fifty-five rows are nineteen defects.
    /// The largest of them WAS one character in one character class, and it is now gone.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, string> KnownDivergences = new()
    {
        // ===== FALSE REJECTIONS (9) — the damaging direction. =====

        // STRING-CONTINUATION (3)
        //   A trailing underscore INSIDE a string literal. Measured but NOT understood: it continues the
        //   line in a Debug.Print output list and does not in an assignment. Deliberately unimplemented,
        //   because there is no rule here anyone can state, and inventing one is how a wrong
        //   generalisation gets laundered into a fact.
        ["continuation-basics/cont-inside-string-literal"] = "STRING-CONTINUATION",
        ["continuation-illegal/split-string-literal"] = "STRING-CONTINUATION",
        ["continuation-in-strings-comments/string-underscore-at-eol-unterminated"] = "STRING-CONTINUATION",

        // EMPTY-INLINE-IF-BODY (1)
        //   `If True Then:` with the body on the NEXT line. The colon commits VB6 to the single-line form
        //   with an EMPTY body, so the construct is complete on that line and the following statement is
        //   an ordinary one — no End If is wanted or allowed. `inlineIfBody` requires at least one
        //   blockStmt, so HexIDE has no parse: the inline alternative wants a statement it cannot find,
        //   and the block alternative wants an END_IF that is not there.
        //
        //   Found by inspection rather than by the corpus, in the gap between two cases that DO cover the
        //   neighbours — `If x Then: <body on the same line>` (legal, and fixed here by making the
        //   whitespace after THEN optional) and `If x Then:` followed by an End If (illegal, and currently
        //   a false acceptance under COLON-ENDS-THE-THEN-LINE). Two cases either side and the middle one
        //   missing, which is how a false rejection hides.
        //
        //   Not fixed here because the fix — permitting an empty inlineIfBody — is the same edit as
        //   COLON-ENDS-THE-THEN-LINE and conflicts with it if done separately. They ship together.
        ["rem-forms/then-colon-with-the-body-on-the-next-line"] = "EMPTY-INLINE-IF-BODY",

        // OTHER-REJECTION (5) — individually caused; see each case's own why in the corpus.
        ["continuation-basics/cont-after-member-dot"] = "OTHER-REJECTION",
        ["gap-fill/two-file-numbers-across-a-colon"] = "OTHER-REJECTION",
        ["separator-and-continuation-together/continuation-drags-a-label-onto-a-statement"] = "OTHER-REJECTION",
        ["separator-with-declarations/hashconst-value-continued"] = "OTHER-REJECTION",
        ["whitespace-and-eol-edges/eof-mid-continuation-no-trailing-newline"] = "OTHER-REJECTION",

        // ===== FALSE ACCEPTANCES (46) — the mild direction. =====

        // BRACKETED-IDENTIFIER-NOT-VB6 (2)
        //   `ambiguousIdentifier` has a `[name]` alternative. **VB6 has no such syntax.** Measured:
        //   `Dim [q] As Long`, `Dim [Print] As Long` and `Dim [Rem] As Long` are ALL a syntax error at the
        //   Dim. Bracket-escaping a reserved name is a VBA / VB.NET feature that VB6 predates, and the
        //   alternative is an over-acceptance under every bracket case in the corpus.
        //
        //   Worth recording how this was found, because two independent reviewers asserted the opposite
        //   with confidence. The Rem work made `[Rem]` stop parsing — the comment rule fires inside the
        //   brackets and eats the closing one — and that was reported as a REGRESSION on "the documented
        //   escape hatch for a name that collides with a keyword", to be fixed before shipping. Measuring
        //   it instead showed the escape hatch does not exist in this language, so the change had
        //   accidentally moved one case TOWARDS VB6. The right fix is to drop the alternative, which
        //   retires all three; not done here because it is unrelated to Rem and wants its own check that
        //   nothing in the designer-file rules depends on it.
        ["rem-forms/bracketed-plain-identifier"] = "BRACKETED-IDENTIFIER-NOT-VB6",
        ["rem-forms/bracketed-reserved-word"] = "BRACKETED-IDENTIFIER-NOT-VB6",

        // COMPILE-CHECK-DEFERRED-TO-RUN-TIME (5) — every one of these IS refused; just not by the parser.
        //   VB6 compiles a whole module before running it, so it can refuse these at compile time. HexIDE
        //   walks, so the same complaint arrives later — at module load for the two constant-expression
        //   cases (the pre-pass folds Enum members and throws "Constant expression required"), and at the
        //   statement for the other three. That is the translation interpreter-core:40-42 prescribes and it
        //   is the sanctioned answer, not a gap.
        //
        //   They sit here because this gate is a PARSE check and these all parse. Worth keeping visible
        //   rather than filtering out: the day one of them stops being refused at all, nothing else would
        //   notice. EnumTests covers the behaviour these rows cannot.
        ["enum-addressing/assigning-to-an-enum-member"] = "COMPILE-CHECK-DEFERRED-TO-RUN-TIME",
        ["enum-addressing/enum-qualified-by-its-own-enum-twice"] = "COMPILE-CHECK-DEFERRED-TO-RUN-TIME",
        ["enum-expressions/member-forward-reference"] = "COMPILE-CHECK-DEFERRED-TO-RUN-TIME",
        ["enum-expressions/member-references-a-later-const"] = "COMPILE-CHECK-DEFERRED-TO-RUN-TIME",
        ["enum-library-addressing/library-qualified-user-enum"] = "COMPILE-CHECK-DEFERRED-TO-RUN-TIME",

        // RESERVED-WORD-USED-AS-A-NAME (7)
        //   `GoTo End`, `GoTo Stop`, `GoTo Close`, `GoTo Return`, `GoTo Randomize`, `GoTo Resume` — every
        //   one is a syntax error in VB6, because those words are reserved and cannot name anything.
        //   HexIDE accepts them because `ambiguousKeyword` — the rule that lets a keyword stand in for an
        //   identifier — contains all 145 keywords with no distinction between the ones VB6 really does
        //   let you use as a name and the ones it does not.
        //
        //   That list is not derivable and has to be measured word by word. Thirty were measured for this
        //   change (the `labelName` work): NINE are usable as a name and twenty-one are not, and no
        //   structural property separates them — `Reset` is usable and `Randomize` is not, `Beep` is and
        //   `Stop` is not, though each pair is a keyword whose statement form is complete on its own.
        //   Narrowing `ambiguousKeyword` to match is a real fix and a large one, and it wants the other
        //   115 words measured first.
        ["label-name-reserved/keyword-as-a-label-close"] = "RESERVED-WORD-USED-AS-A-NAME",
        ["label-name-reserved/keyword-as-a-label-end"] = "RESERVED-WORD-USED-AS-A-NAME",
        ["label-name-reserved/keyword-as-a-label-randomize"] = "RESERVED-WORD-USED-AS-A-NAME",
        ["label-name-reserved/keyword-as-a-label-resume"] = "RESERVED-WORD-USED-AS-A-NAME",
        ["label-name-reserved/keyword-as-a-label-stop"] = "RESERVED-WORD-USED-AS-A-NAME",
        ["line-labels/return-as-a-label"] = "RESERVED-WORD-USED-AS-A-NAME",
        ["line-labels/stop-as-a-label"] = "RESERVED-WORD-USED-AS-A-NAME",

        // LABEL-OUTSIDE-A-PROCEDURE (1)
        //   `Orphan:` at module level, between two procedures. A label is a procedure-scoped jump target,
        //   so there is nowhere to jump from and vb6.exe refuses it outright: "Only comments may appear
        //   after End Sub, End Function, or End Property". HexIDE's `moduleBody` reaches `block`, which
        //   now carries the line-head, so it takes the label happily. A parser fix — the module body
        //   wants a narrower element list than a procedure body — and unrelated to how labels work
        //   INSIDE a procedure, which is what this change is about.
        ["line-labels/label-at-module-level-between-procedures"] = "LABEL-OUTSIDE-A-PROCEDURE",

        // UNBOUND-PROCEDURE-NAME (7) — PERMANENT, and not a label defect at all.
        //   A name-colon is a label only at the head of a logical line; anywhere else VB6 reads it as a
        //   bare procedure call and rejects it at BIND time ("Sub or Function not defined"). HexIDE
        //   produces the IDENTICAL parse — it agrees with VB6 about where labels are and registers none of
        //   these as labels — so nothing syntactic remains to reject. The residue is name resolution
        //   against a symbol table, which is binding, and binding is permanently outside a CST
        //   (CLAUDE.md limit 1). HexIDE raises VB6's own diagnostic at run time instead, which is the
        //   translation interpreter-core:40-42 prescribes; only the timing differs, and a parse-only gate
        //   can never see it. These entries will never be retired.
        ["gap-fill/two-labels-on-one-line"] = "UNBOUND-PROCEDURE-NAME",
        // Measured directly, and it is what confirms the whole reading of this group: vb6.exe answers
        // `z = 1:: Here: z = 2` with "Sub or Function not defined", not with a syntax error. VB6 is
        // reading `Here` as a CALL, exactly as HexIDE does — the parses agree and only the timing of the
        // complaint differs.
        ["line-labels/label-after-a-double-colon"] = "UNBOUND-PROCEDURE-NAME",
        ["separator-basics/sep-label-mid-line-after-colon"] = "UNBOUND-PROCEDURE-NAME",
        ["separator-in-control-flow/label-in-the-middle-of-a-line"] = "UNBOUND-PROCEDURE-NAME",
        ["separator-vs-label/label-after-continuation-midline"] = "UNBOUND-PROCEDURE-NAME",
        ["separator-vs-label/label-after-then-not-a-label"] = "UNBOUND-PROCEDURE-NAME",
        ["separator-vs-label/second-name-colon-is-not-a-label"] = "UNBOUND-PROCEDURE-NAME",

        // COLON-BEFORE-BLOCK-OPENER (6)
        //   A block-construct OPENER must be first on its physical line: a block `If` header, its
        //   `Else`/`ElseIf`, a procedure or `Declare` header. A LABEL may precede one (`Chk: If True Then`
        //   is legal); a STATEMENT may not. `blockSep : (WS? (NEWLINE | COLON) WS?)+` declares a colon and
        //   a line break to be the same thing, and `block` and `moduleBody` both use it, so every opener is
        //   reachable after a colon exactly as after a newline.
        //
        //   READ THIS BEFORE FIXING: the constraint is on what may FOLLOW a colon, never on what may
        //   precede it. TERMINATORS are not line-initial — `Debug.Print "A" : End If` and
        //   `Debug.Print "A": End Sub` are both LEGAL, measured four separate times in this corpus. An
        //   earlier draft of this comment said a colon may not stand in for the break an `End` requires;
        //   a fix written to that would have converted four accepted cases into false rejections, in the
        //   damaging direction. Openers are line-initial; terminators are not.
        ["separator-basics/sep-block-if-not-first-on-line"] = "COLON-BEFORE-BLOCK-OPENER",
        ["separator-in-control-flow/statement-before-else-block-form"] = "COLON-BEFORE-BLOCK-OPENER",
        ["separator-vs-label/block-if-after-colon"] = "COLON-BEFORE-BLOCK-OPENER",
        ["separator-with-declarations/colon-before-sub-header"] = "COLON-BEFORE-BLOCK-OPENER",
        ["separator-with-declarations/end-sub-colon-next-sub"] = "COLON-BEFORE-BLOCK-OPENER",
        // The corpus PREDICTED this one legal and vb6.exe disagreed. Two readings fit the single data
        // point — a Declare must BEGIN a line, or a Declare must OWN its line — and one probe separates
        // them. Grouped here because it is safe either way; the text asserts only the measured half.
        ["separator-with-declarations/module-declare-colon-declare"] = "COLON-BEFORE-BLOCK-OPENER",

        // REM-IS-TRIVIA-NOT-A-STATEMENT (4)
        //   Rem in VB6 is a STATEMENT that introduces a comment, not lexical trivia. Two consequences the
        //   corpus measures: it needs a colon after a preceding statement on the same line, and — unlike
        //   an apostrophe comment — it counts as "something after Then", forcing the single-line If form.
        //   HexIDE routes it to the hidden channel, so the parser cannot police a position it cannot see.
        //
        //   Deliberately NOT fixed here. The fix is to emit the Rem form on the default channel AND give
        //   it a home in every position a comment may occupy — blockStmt, and the Enum, Type and module
        //   bodies. A miss anywhere there is a FALSE REJECTION on ordinary code, trading the damaging
        //   direction for the mild one. Do NOT instead make the leading `COLON?` mandatory: that rejects a
        //   line-initial `Rem` and `Else Rem nothing`, both measured legal.
        //
        //   What is lost is an error VB6 raises and we do not. The fidelity guardrail permits that and
        //   never permits a wrong value — and note it does not apply here at all, since these are programs
        //   vb6.exe REJECTS, so there is no correct behaviour to diverge from.
        ["continuation-in-strings-comments/rem-after-statement-without-colon"] = "REM-IS-TRIVIA-NOT-A-STATEMENT",
        ["separator-basics/sep-rem-without-colon"] = "REM-IS-TRIVIA-NOT-A-STATEMENT",
        ["separator-in-control-flow/rem-after-then-is-still-block"] = "REM-IS-TRIVIA-NOT-A-STATEMENT",
        ["separator-vs-label/rem-without-colon"] = "REM-IS-TRIVIA-NOT-A-STATEMENT",

        // HIDDEN-CONTINUATION-FAKES-ADJACENCY (3)
        //   A VB6 continuation is WHITESPACE-EQUIVALENT, not a character-level splice: the mandatory space
        //   before the `_` survives the join, so `tot _` / `al` is two names and `x _` / `$` is a name with
        //   whitespace before its type character. HexIDE swallows the whole run, leading whitespace
        //   included, into one hidden token, so the parser sees the flanking tokens as DIRECTLY ADJACENT
        //   and `ambiguousIdentifier : (IDENTIFIER | ambiguousKeyword)+` fuses them. Those rules are
        //   already correct — the same probes with a plain space fail to parse — so this is purely the
        //   invisibility of the token.
        //
        //   Lexer fix: emit the continuation as a visible WS-typed token that absorbs the next line's
        //   indentation. WS-typed, never NEWLINE-typed: `label-after-continuation-midline` is currently
        //   CORRECT precisely because the hidden continuation makes the physical/logical line distinction
        //   unrepresentable, and a NEWLINE would turn a mid-line `Skip:` into a line-head label.
        ["continuation-in-strings-comments/continuation-splitting-a-token"] = "HIDDEN-CONTINUATION-FAKES-ADJACENCY",
        ["continuation-vs-identifier/continuation-splits-identifier"] = "HIDDEN-CONTINUATION-FAKES-ADJACENCY",
        ["gap-fill/continuation-splits-identifier-from-type-character"] = "HIDDEN-CONTINUATION-FAKES-ADJACENCY",

        // COLON-ENDS-THE-THEN-LINE (2)
        //   A colon after `Then` counts as "something other than a comment on the same line", so VB6
        //   commits to the SINGLE-LINE If form with an empty body; a later `End If` then has no block to
        //   close. `ifBlockStmt` uses `blockSep` for the post-`Then` separator, so `:` followed by a
        //   newline is read as the very line break that selects the BLOCK form. The fix must ship together
        //   with the inlineIfBody rewrite below — they edit the same rule and conflict if done apart.
        ["separator-basics/sep-colon-immediately-after-then"] = "COLON-ENDS-THE-THEN-LINE",
        ["separator-in-control-flow/then-colon-then-endif"] = "COLON-ENDS-THE-THEN-LINE",

        // INLINE-IF-BODY-NOT-LINE-BOUNDED (2)
        //   A single-line If owns EVERYTHING after `Then` to the end of the logical line, so in
        //   `For i … : If c Then Exit For : Next i` the `Next` belongs to the branch and the `For` is left
        //   unterminated. `inlineIfBody` ends its trailing loop when the next fragment is not a blockStmt
        //   rather than at end-of-line, and `Next`/`Loop` exist only as tails of their loop rules, so the
        //   branch closes early and the colon is re-read as the loop's own separator. Parser fix:
        //   terminate on NEWLINE/EOF/ELSE rather than on failure to match another statement.
        ["separator-in-control-flow/exit-do-swallows-the-loop"] = "INLINE-IF-BODY-NOT-LINE-BOUNDED",
        ["separator-in-control-flow/exit-for-swallows-the-next"] = "INLINE-IF-BODY-NOT-LINE-BOUNDED",

        // BOUNDED-RESOURCE (2)
        //   vb6.exe enforces scanner budgets a context-free grammar has no notion of: 25 continuations per
        //   logical line (bracketed by the corpus — 24 compiles, 25 does not) and 1023 characters per
        //   physical line. Nothing in the grammar counts anything, so the absence IS the cause and there is
        //   no rule to point at. Properly a pre-lex pass. Refusing to LOAD a module because a line is long
        //   is not obviously a service to anyone.
        ["gap-fill/physical-line-over-1023-characters"] = "BOUNDED-RESOURCE",
        ["gap-fill/twenty-five-consecutive-continuations"] = "BOUNDED-RESOURCE",

        // DIRECTIVE-NOT-LINE-SCOPED (1)
        //   A conditional-compilation directive owns its whole physical line, so `#End If:` is not
        //   recognised as a directive at all — even though a trailing colon is a harmless empty statement
        //   everywhere else in VB6. `macroIfThenElseStmt` ends on the bare MACRO_END_IF with no
        //   end-of-line requirement. One-token parser fix, and the sibling `macroIfBlockStmt` already
        //   models the constraint at the other end of the same construct.
        ["separator-with-declarations/hashendif-trailing-colon"] = "DIRECTIVE-NOT-LINE-SCOPED",

        // ZERO-MEMBER-AGGREGATE-ACCEPTED (1) — NOT a Rem defect.
        //   vb6.exe agrees with HexIDE that a bare `Rem` inside an Enum body is a comment and vanishes.
        //   Its complaint is "Enum without members not allowed", which is arity, not syntax.
        //   `enumerationStmt` uses a Kleene STAR for the member list, so an empty body is accepted. A
        //   one-character fix, `*` -> `+`, and `typeStmt` carries the identical hole. The Rem is only the
        //   vehicle that empties the body; any empty body reaches the same acceptance.
        ["rem-forms/enum-member-named-rem"] = "ZERO-MEMBER-AGGREGATE-ACCEPTED",

        // FILENUMBER-IS-A-GENERAL-LITERAL (1) — NOT a continuation defect.
        //   A continuation can never occur inside a token, so `#1/1/ _` leaves an unterminated date
        //   literal. HexIDE does not splice it either — DATELITERAL correctly fails. It accepts because the
        //   wreckage RELEXES as arithmetic: `#1` matches FILENUMBER, and `literal` lists FILENUMBER as an
        //   alternative, making a file number a legal operand anywhere an expression may appear. Parser
        //   fix: a dedicated rule used only by the file-I/O statements — but any narrowing must keep
        //   `Print #1, "a": Print #2, "b"` green, which reaches `#1` through that same alternative.
        ["gap-fill/date-literal-split-by-continuation"] = "FILENUMBER-IS-A-GENERAL-LITERAL",

        // CONTINUATION-NOT-SUPPRESSED-IN-ENUM-BODY (1) — recommend NOT fixing.
        //   A measured overturn worth reading twice: continuations are honoured in Const, Type, Declare,
        //   Attribute and #Const, but NOT inside an Enum body — `Red = _` / `3` gets "Invalid inside Enum".
        //   And the inverse holds: an Enum body takes COLON separators, which a Type body does not.
        //   Fixing means a lexer mode pushed on ENUM and popped on END_ENUM — context-sensitive lexing
        //   driven by parser state. Do not: only one shape has been probed, and guessing the rule's scope
        //   wide turns a missing error into a false rejection of a whole module.
        ["separator-with-declarations/enum-member-value-continued"] = "CONTINUATION-NOT-SUPPRESSED-IN-ENUM-BODY",

        // MODULE-KIND-NOT-AN-INPUT (1) — nothing to do with underscores, despite the case's name.
        //   The recorded error is "Only valid in object module": the harness compiles the snippet as a
        //   .bas, and `Event` is not permitted in a standard module at all, so the compile dies on the
        //   module-kind rule before any name rule is reached. A control probe with no underscore in the
        //   event name parses identically. Module kind is a property of the FILE, not the token stream —
        //   one grammar serves .bas/.cls/.frm and no token could carry it — so no grammar edit can close
        //   this. Not permanently out of reach either: it needs a load-time check, not binding.
        ["continuation-vs-identifier/event-declaration-with-underscore"] = "MODULE-KIND-NOT-AN-INPUT",
    };

    [Fact]
    public void HexIDE_DoesNotRejectCodeThatVB6Accepts()
    {
        var (raw, _, total, _) = Compare();
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
        var (_, raw, total, _) = Compare();
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
    public void EveryCorpusCaseCarriesARecordedVerdict()
    {
        // A case with no verdict in results.json was silently skipped, which is the quietest way a gate
        // can stop guarding: the count goes up, the gate stays green, and nothing was ever asked of the
        // compiler. Cases that declare `skip` are exempt — those are deliberately undeliverable.
        var (_, _, _, unmeasured) = Compare();

        unmeasured.Should().BeEmpty(
            "a corpus case is a question for vb6.exe, and one with no recorded answer is not evidence of "
          + "anything — run scripts/vb6-legality.ps1 and merge the result, or mark the case `skip` with a "
          + "reason:\n{0}",
            string.Join("\n", unmeasured.Select(k => "    " + k)));
    }

    [Fact]
    public void KnownDivergencesAreStillReal()
    {
        // A stale exemption is as bad as an undocumented one: it permits a future regression under a
        // reason that no longer applies. If a case has been fixed, delete its entry.
        var (falseRejections, falseAcceptances, _, _) = Compare();
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
        var (_, _, total, _) = Compare();
        total.Should().BeGreaterThan(250,
            "the corpus should carry its full set of compiled verdicts; found {0}", total);
    }

    // ------------------------------------------------------------------------------------------------

    private static (List<string> FalseRejections, List<string> FalseAcceptances, int Total,
        List<string> Unmeasured) Compare()
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
        var unmeasured = new List<string>();
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
                // That is deliberate and stays silent.
                if (el.TryGetProperty("skip", out _)) continue;

                // A case with no recorded verdict is NOT deliberate. It contributes nothing while looking
                // exactly like coverage, so someone can add cases, watch the gate stay green, and believe
                // they measured something. Collected rather than skipped.
                if (!verdicts.TryGetValue(key, out var vb6)) { unmeasured.Add(key); continue; }
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
        return (falseRejections, falseAcceptances, total, unmeasured);
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
