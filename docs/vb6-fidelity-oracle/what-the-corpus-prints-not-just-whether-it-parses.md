## What the corpus PRINTS, not just whether it parses (2026-09-03)

The conformance corpus has been measured against `vb6.exe` for legality since it was built, and every one of
its five gate assertions bottomed out in a single predicate: *does this parse*. That is one bit per case. It
caught real defects and drove false rejections from 21 to 9 — and it was structurally unable to see four
defects that shipped in one session, all of which parse correctly and then do the wrong thing: an `Else:`
that swallowed its own branch, a line label that ate the statement beside it, `Dim p As Module2.Point`, and
the whole of module scope.

**The corpus was never the gap.** 296 of its 300 legal cases already called `Debug.Print`; 640 prints in all.
Every one of the four missed defects already had cases that printed the value that was wrong — 5 of 5 for the
`Else:` family, 69 of 69 for labels, 15 of 25 for module scope. The cases were sitting there. Nothing was
asking them what they produced.

### Method

`Debug.Print` is **inert in a compiled exe** — there is no Immediate window to receive it — which is why
this went unnoticed for so long. But a compiled exe can `Print #` to a file, which `vb6-oracle.ps1` already
relied on. `scripts/vb6-legality.ps1 -CaptureOutput` therefore builds a *second* module per case with
`Debug.Print` rewritten to a helper that appends `TypeName(v) & vbTab & CStr(v)`, compiles it, runs it, and
records what the file received. Open/append/close per call, so no startup or shutdown hook is needed and a
case defining its own `Sub Main` needs no special handling.

The rewrite changes the program, and some cases are *about* the construct being rewritten, so the probe's
legality is compared with the original's; a disagreement is recorded as `rewrite-broke-case` and **no
behaviour is kept**. Five cases hit that, all of them about unterminated or continued string literals —
exactly where a `Debug.Print` rewrite would be expected to interfere.

### Results (502 cases)

| | |
|---|---|
| legal / illegal | 356 / 146 |
| ran cleanly, output recorded | **331** |
| hung (killed on timeout) | **0** |
| non-zero exit | 0 |
| rewrite broke the case | 5 |
| no print to observe | 32 |

**Zero hangs was not the expected result.** The legality oracle compiles and never runs, deliberately,
because an unhandled VB6 runtime error in a compiled exe raises a modal and waits forever. That risk is real
but did not materialise once across 331 executions of this corpus — worth knowing before anyone else decides
running is too dangerous to attempt.

Types actually observed across 424 printed lines: `String` 304, `Long` 102, `Integer` 10, `Boolean` 5,
`Empty` 2, `Double` 1. The type is recorded alongside the value on purpose — a gate diffing rendered text
alone passes an Integer where VB6 gives a Long, which is precisely where a wrong value hides.

**Thirteen legal cases contain a `Debug.Print` and print nothing.** `second-name-colon-runs-as-call`,
`end-sub-after-colon`, `label-immediately-before-end-sub`, `colon-only-line-at-module-level` and others.
"VB6 prints nothing here" is a real expectation, and an interpreter that prints *something* at one of them is
diverging in a direction no parse check can detect.

### What it found immediately

216 of the captured cases are statement-scope and run under the interpreter today. **21 diverge.** All 21
parse cleanly, so the existing gate reported every one of them as conformant.

The largest is a single cause with seven cases: **a `Select Case` whose parts are colon-joined, split by a
line continuation, or separated by a blank line selects the wrong branch, or none at all.** Silently, with no
error raised:

| case | vb6.exe | HexIDE |
|---|---|---|
| `select-case-entirely-on-one-line` | `A` | `B` |
| `case-body-colon-joined` | `A`, `B` | `C` |
| `sep-whole-select-one-line` | `ONE` | *(nothing)* |
| `blank-line-before-first-case` | `two` | *(nothing)* |

Two further silent wrong values, unrelated to each other:

- **A line continuation inside an identifier does not join it.** `continuation-illegal/split-identifier`
  prints `Long 0` under VB6. The recorded probe is `Dim value As Long`, then `val _` ⏎ `ue = 5`, then
  `Debug.Print value`: it splits the **assignment**, leaving the declared Long untouched. It does not
  assign 5 first and split the read, as #224's illustrative snippet does. HexIDE joined the names,
  assigned to `value`, and printed `Long 5`. **#224 fixed:** both grammar copies emit a continuation between word fragments as
  whitespace, preserving the name boundary; multi-word keyword tokens still absorb their separator.
- **`""` inside a string literal is not collapsed.** VB6 prints `he said "hi" _`; HexIDE prints
  `he said ""hi"" _`.

### What this overturned

The belief that a corpus measured against the real compiler *is* a conformance gate. It is only a gate for
the question it asks. Ours asked the cheapest one available — and because false rejections are the most
visible failure and parsing is where they live, nobody noticed that "wrong value", which CLAUDE.md ranks as
the one thing never acceptable, had no gate at all. The corpus had carried the evidence the whole time.

**A gap this turned up on its own:** the interpreter has **no `Sub Main` startup object**.
`ProjectSerializer` reads `Startup="Sub Main"` from the .vbp, and `BasicInterpreter` still describes it as
"a future `Sub Main`". So a Standard EXE that starts at `Sub Main` — the default for a code-only project —
does not run. It is also why 82 measured cases cannot be gated yet: their entry point is never called.

