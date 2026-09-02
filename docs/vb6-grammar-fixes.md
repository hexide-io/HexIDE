# VB6 grammar fixes — clean-room findings & upstream log

> A running log of clean-room fixes and open gaps for the **VB6 ANTLR grammar** underpinning the planned
> MIT LSP server (see `openspec/specs/language-server/spec.md`). Each landed fix is a candidate to
> **upstream to [`antlr/grammars-v4`](https://github.com/antlr/grammars-v4) `vb6/`** — the maintained, MIT
> home of Ulrich Wolffgang's ("proleap") VB6 grammar, where the living copy is developed (his personal
> `uwol/proleap-vb6-parser` has been frozen since 2018).
>
> **Upstreaming is deferred for now.** This file accumulates fixes so they can be PR'd cleanly later.

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

Provenance: VB6 Language Reference ("Line numbers", "If…Then…Else", "Type Declaration Characters"), each
verified against `vb6.exe`; deliberately divergent from the Rubberduck grammar.

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
