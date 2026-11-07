## `Sub Main` as a startup object (2026-09-04, #210)

| case | VB6 |
|---|---|
| `Sub Main` in the only module | runs |
| `Sub Main` in Module2, none in Module1 | **runs** — the lookup is project-wide |
| `Private Sub Main` in the startup module | **runs** |
| **`Private Sub Main` in a FOREIGN module** | **runs** — visibility is ignored entirely |
| Two modules both declaring `Main` | **illegal** — "Ambiguous name detected: Main" |
| One `Private` + one `Public` `Main`, different modules | **illegal** — same, still ambiguous |
| `Sub Main(ByVal n As Long)` | **illegal** — "Must have startup form or Sub Main()" |
| `Sub Main(Optional ByVal n As Long = 7)` | **illegal** — same |
| `Function Main() As Long` | **illegal** — same |
| No `Main` declared anywhere | **illegal** — same, at compile time |
| An executable statement outside any procedure | **illegal** — "Invalid outside procedure" |

**The startup lookup is not ordinary procedure resolution**, and the two rows that establish it are the
ones that had to be measured rather than reasoned:

- **A `Private Sub Main` in a foreign module is a valid startup.** Ordinary resolution sees another
  module's `Public` only, so delegating to it would have refused a project VB6 runs. A `Private` Main in
  the *primary* module proves nothing — own-module visibility explains that anyway — which is why the
  foreign-module case is the decisive one.
- **`Private` + `Public` in two modules is still ambiguous.** Ordinary resolution would not call that a
  clash at all, since a `Private` procedure is module-local. The startup search does, because it is
  choosing between candidates rather than resolving a reference.

And the parameter rule is "declares none", not "callable with none": an **all-`Optional`** parameter list
still disqualifies. One probe of the required-argument form alone would have suggested the weaker rule.

### VB6 has no top-level statements

`Debug.Print "x"` outside any procedure is **"Invalid outside procedure"**. So HexIDE's execution of
top-level statements — the entry point every statement-scope corpus case uses, and what the test fixture's
`Run()` is built on — is a deliberate **extension**, not a VB6 feature. Worth stating plainly: after a few
hundred corpus cases that depend on it, it reads like one. It costs nothing on real VB6 source, which
cannot contain such a statement, so no valid program is affected by accepting it. Recorded in the parse
gate as `TOP-LEVEL-STATEMENTS-ARE-AN-EXTENSION` rather than as a missing diagnostic.

### Two harness defects this exposed, both of the same kind

The first measurement of these rules produced **three wrong answers**, and the cause was the harness rule
whose own comment warns about exactly this: it appends a `Sub Main` when the case declares none, because
the .vbp names one as the startup.

1. It scanned **only the primary source** for an existing `Main`. A case whose `Main` lives in Module2 got
   a second one appended to Module1, so *"is the startup found project-wide"* came back
   illegal-because-ambiguous — an answer about the harness, not VB6.
2. It matched `Sub +Main` only, so a `Function Main` did not count as already-declared and collided with
   an appended `Sub Main`.
3. And the question *"what happens when no `Main` exists"* was **unaskable**, because the harness
   guaranteed one did. It now takes a `noAutoMain` opt-out.

Then fixing it appeared not to work, because the same decision was **derived twice** — once for the
legality module and again, differently, inside the behaviour-capture assembly. The legality verdict came
out right while the probe still failed as ambiguous. The decision is now computed once and reused.

The lesson is not "check the harness" but something narrower: **a harness that must synthesise part of the
program can only be trusted for questions that do not touch the synthesised part.** Every one of these
three questions was about `Sub Main`, which is precisely what the harness was inventing.

