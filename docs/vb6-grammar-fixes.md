# VB6 grammar fixes — clean-room findings & upstream log

> A running log of clean-room fixes and open gaps for the **VB6 ANTLR grammar** underpinning the planned
> MIT LSP server (see `openspec/specs/language-server/spec.md`). Each landed fix is a candidate to
> **upstream to [`antlr/grammars-v4`](https://github.com/antlr/grammars-v4) `vb6/`** — the maintained, MIT
> home of Ulrich Wolffgang's ("proleap") VB6 grammar, where the living copy is developed (his personal
> `uwol/proleap-vb6-parser` has been frozen since 2018).
>
> **Upstreaming is deferred for now.** This file accumulates fixes so they can be PR'd cleanly later.

## File-number hashes and date literals (#1146)

Both lexers formerly matched any text between two hashes as `DATELITERAL`, consuming file statements such as
`Line Input #1, s: Print #2, s` and close lists such as `Close #1, #2`. The token now consists of numeric or named
date components separated by date/time punctuation or whitespace, with an optional AM/PM (or A/P) suffix.
A separator cannot end the literal. This is token delimiting, not calendar validation.

The oracle's "File-number hashes and date literals" records native and p-code compile probes, including the
surprise that `Print #1, 2#` really does contain a date in VB6. There is no statement-context override that
would turn it into a file operation. Both test projects carry the same token/parse cases, and the runtime
suite also copies a file with multiple file numbers on each source line.

## Clean-room rule (hard — never relax)

Every fix here is authored **only** from the **VB6 Language Reference** (MSDN aa338033) and, where relevant,
**[MS-VBAL]**, and validated against a **real `vb6.exe` compiler**. The **Rubberduck grammar is quarantined**
(GPLv3) — it is never opened while authoring these rules, and each fix deliberately diverges from any RD
shape (e.g. `lineNumber` not RD's `lineNumberLabel`; no `MINUS?`). This provenance is what keeps the fixes
MIT-cleanly upstreamable. Preserve it for every future entry.

## Why the corpus is authored rather than borrowed (2026-09-02)

Rubberduck's VB6 test suite is a well-known body of language torture tests, and it is **permanently
unavailable to this project**: RD2 has no CLA, and previous relicensing discussions established that
tracing every contributor is impractical. So this is settled rather than pending — there is no version of
"ask again later".

That is not only a licence problem. This repository has already paid the cost once: the GPLv3 LSP server
that was replaced carried `RubberduckGrammarTests.cs`, whose inputs were ported from that suite, and
`lsp-parity-matrix.md` names it as one of the two things creating the GPL obligation the swap existed to
shed. Porting them back would re-acquire it.

**The replacement is better, not merely legal.** Those tests encode what their authors believed VB6
accepts. A corpus generated here and compiled by `vb6.exe` encodes what VB6 *actually* accepts — the
legality oracle (`scripts/vb6-legality.ps1`) is what makes that difference real. The first such corpus,
319 cases on line continuations and statement separators, corrected 16 of its own predictions and resolved
60 questions nobody could answer from documentation.

Because it is authored here and validated by the compiler, it is also publishable — which makes it
something this project can offer outward rather than something it must ask for.

## Method / local artifacts

- **Corpus** — a `vb6-corpus/` checkout beside this repo: clean-room legal-VB6 conformance files (a
  "should-parse" oracle), grown by AI adversarial ("Thunderframe") generation, fanned out across the
  language surface.
- **Runner** — a `vb6-grammar-runner/` checkout beside this repo: a ~30-line C# ANTLR harness (no Java) that
  parses the corpus (pass/fail) and runs a partial-input robustness soak. Vendors the grammar under test.
- **Grammar under test** — grammars-v4 `vb6` (`VisualBasic6Lexer.g4` + `VisualBasic6Parser.g4`); pristine in
  a `grammars-v4/vb6/` checkout beside this repo, working copy with the fixes in the runner's `Grammar/`.
- **Legality oracle** — files verified against a real `vb6.exe` (the authoritative "is this legal VB6?").

## Fixes landed (candidate upstream PRs)

| # | Construct | Layer | Rule change |
|---|---|---|---|
| 1 | Numeric line numbers (`10 x = 1`, `On n GoTo 10, 20`) | parser | add `lineNumber : INTEGERLITERAL;` + an optional `(lineNumber WS?)?` prefix in `block` |
| 2 | `%` Integer suffix on integer literals (`42%`) | lexer | add `PERCENT` to `INTEGERLITERAL`'s type-declaration suffix set |
| 3 | Single-line `If`: empty-Then (`If x Then Else …`), line-number target, nested | parser | restructure `inlineIfThenElse` to allow an empty Then / a bare `lineNumber` (implicit GoTo) / a single-or-nested body on each arm |
| 4 | Typed function names (`Function MakeName$()`) | parser | add `typeHint?` after the name in `functionStmt` (matching `propertyGetStmt`) |
| 5 | `D`-exponent doubles (`1.5D10`, `2.5D-5`) | lexer | `DOUBLELITERAL` exponent `'E'` → `('E' | 'D')` |
| 6 | Hex / octal `%` suffix (`&H1F%`) | lexer | add `PERCENT` to `COLORLITERAL` (hex) and `OCTALLITERAL` suffixes |
| 7 | Trailing comment after a header or designer property value (`MultiUse = -1  'True`, `Enabled = 0   'False`) | parser | add `WS?` before `NEWLINE` in `moduleConfigElement` and `cp_SingleProperty` |
| 8 | A member call in a `While...Wend` body (`k.Bump`, `Debug.Print x`) | parser | `whileWendStmt`'s body `block* NEWLINE*` → `(block blockSep)?`, as `doLoopStmt` has it |
| 9 | A Print output list of separators alone (`Debug.Print ,`, `Debug.Print ;`) | parser | a second `iCS_B_MemberProcedureCall` alternative, `implicitCallStmt_InStmt? DOT PRINT WS? (COMMA \| SEMICOLON) (WS? (COMMA \| SEMICOLON))*`; `argsCall` is unchanged |
| 10 | A lone CR as a line end (`x = 1` CR `x = x + 10`, `x = 1 + _` CR `10`) | lexer | `NEWLINE`, its continuation tail and `LINE_CONTINUATION`: `'\r'? '\n'` → `('\r'? '\n' | '\r')` |

Provenance: VB6 Language Reference ("Line numbers", "If…Then…Else", "Type Declaration Characters"), each
verified against `vb6.exe`; deliberately divergent from the Rubberduck grammar.

**Fix 7 has a different provenance, and a wider blast radius than its size suggests (2026-09-20).** It was
not read out of the language reference at all — it was measured against files VB6 itself wrote: the VB98
Template tree (22 designer files) plus every `.cls` header in this repository's demos. VB6 decodes an
enumerated property value into a comment when it saves — `MultiUse = -1  'True`, `Enabled = 0   'False`,
`StartUpPosition = 3  'Windows Default` — and `COMMENT` is on the hidden channel, so what reaches the
parser is the **whitespace in front of the comment**. `NEWLINE` swallows leading whitespace itself, but
only when a newline actually follows, so those spaces lex as `WS` and the property rules, which went
straight from the value to `NEWLINE`, rejected them.

The effect was that **every `.cls` VB6 has ever written failed to parse at line 3**, and 30 of 46 corpus
files failed somewhere in their header — in both grammars, at the same line of the same file, because both
carry the proleap shape. It went unnoticed because nothing ever parsed a whole file: the IDE splits the
header off at load and hands the parser only the body. `WholeFileGrammarTests`, in both halves, is what
now asks the whole-file question, and hexide-io/HexIDE#273 is why it is being asked.

This one is worth upstreaming ahead of the others: it is two optional tokens, it cannot change what the
grammar accepts anywhere else, and without it the grammar cannot read the files its own subject produces.

**Fix 8 is an ambiguity, not a rejection (2026-10-13).** `block*` let two blocks sit side by side with no
separator, so `k.Bump` had two parses: one statement, or a call of a procedure named `k` followed by `.Bump`,
a leading-dot With member call. ANTLR took the second. The grammar accepted the text either way, so the LSP
server reported nothing; the interpreter, which shares the rule's shape, executed the wrong tree and raised
"Sub or Function not defined (k)" (hexide-io/HexIDE#882). `doLoopStmt` already took one optional block
followed by a separator, and never had the problem. Provenance: the VB6 Language Reference's `While...Wend`
syntax (a condition, optional statements, `Wend`), and `vb6.exe`, native and in the IDE, running each
member-call shape in a `While` body, one-line and nested forms included.

Error recovery inside a `While` body now cascades as it does in `Do`, `For` and `If`. A half-typed `MsgBox ("hi"` in
a `While` body can drop the procedures after it from the document symbols, where `block*` happened to resynchronise
sooner. That is the behaviour of every other block rule here, not a regression of this one; do not revert toward
`block*` to soften it, because that brings back the wrong tree for every valid member call.

**Fix 9 is narrow on purpose (2026-10-22).** `argsCall` needs at least one argument, and widening it would have been
the one-line fix. It would also have accepted `Foo ,` for an ordinary procedure, which `vb6.exe` refuses as "Syntax
error", native and p-code, while `Foo , 2` builds (hexide-io/HexIDE#1409). Separators alone belong to Print's
output list, which VB6 reads as `Print #` reads its own, so the alternative is keyed on the `PRINT` token after the
dot. It admits `Form1.Print ,` too, whose VB6 behaviour is not measured and which HexIDE cannot run, since a form's
`Print` method is not implemented. Provenance: measured against
`vb6.exe` and its IDE's Immediate window (the oracle's "Sequential files", under "Separators with no item").

**Fix 10 is the interpreter's grammar only (2026-10-22).** VB6 ends a line at CRLF, a lone LF or a lone CR, for a
statement, a continuation and a comment alike, measured with `vb6.exe /make` on byte-exact files (the oracle's "A lone
CR or LF in VB6 source", hexide-io/HexIDE#1020). The rule took only the first two, so a form whose code section has a
lone CR, which reaches the interpreter as it was read, was a syntax error to F5 (hexide-io/HexIDE#1426). `COMMENT`
already stopped at a lone CR and continues through `LINE_CONTINUATION`, so it needed nothing of its own. The bundled
server's `NEWLINE` still takes only CRLF and LF, and belongs with hexide-io/HexIDE#1052, where its whole position model
is LF-only. `GrammarParityTests` compares rule inventories, not bodies, so it doesn't see the two disagree.

## Open gaps (found, not yet fixed)

| Construct | Why it fails | Assessment |
|---|---|---|
| **Colon-family**: `If x Then a=1: b=2 Else c=3`, trailing `stmt:`, colon-chain cascades | `:` (colon+space) and `\n` are the **same** `NEWLINE` token, so a statement-separator `:` can't be distinguished from a line end | **the big one** — needs a lexer-level split (mode/predicate). Also RD's signature strength |
| Combined `Next j, i` (one `Next`, nested loops) | grammar couples each `For` to its own `Next` | needs For/Next decoupling; low frequency — deferred |
| Date literal in a `Write` / output list (`Write #1, #1/1/2000#`) | `DATELITERAL: HASH (~[#\r\n])* HASH` greedily eats from the file-number `#1` to the date's closing `#` | needs `#N` (file number) vs `#…#` (date) lexer disambiguation |
| `Err.Raise expr, a, b` (no-paren call, complex first arg + args) | member-call statement fails on `obj.Method <expr>, a, b` (works for `Debug.Print`) | look at the no-paren call/arg rule — possibly common (`Err.Raise`, `Collection.Add`) |
| `Print #n,` (empty / trailing output list) | Print output-list edge (trailing comma → blank line) | Print-statement edge |

## Corpus-fidelity follow-ups (do NOT affect the grammar)

- Self-referential `WithEvents` (`Private WithEvents m As <the declaring class's own type>`) is accepted by
  the lenient grammar but rejected by `vb6.exe` ("Cannot handle events for the object specified") — retarget
  to a companion class in the corpus.
- `.cls` corpus files should use **CRLF** (vb6.exe refuses to *load* an LF-ending class file).

## Status (2026-07-26)

Phase 1 (grammar parity) of the LSP swap: corpus **27 files, 20/27 pass**; **6 clean-room fixes landed**.
Robustness: base-grammar soak green (0 crashes / 0 timeouts / p99 ≈ 126 ms over 9,577 partial/broken inputs);
the 6 fixes are trivially additive (no new recursion/ambiguity), so robustness is preserved by construction.
Upstreaming to grammars-v4 is **deferred** — accumulate more fixes here first.
