## Out of stack space — Error 28 (2026-08-22, #80)

Verified with a `Sub Main` probe (`/make`, run, append each line to a log so nothing is lost to buffering).

| Probe | Result |
|---|---|
| `Sub A(): Call A(): End Sub`, `On Error GoTo` in the caller | **`Err 28`**, `"Out of stack space"` |
| depth reached before it raises | **258,825 frames** |
| the program afterwards | `2 + 2` = `4`, `Err.Number` = 0 — VB6 **does not terminate** |
| `Err.Raise 28` | same number and description |

**258,825 is the number that settles the design.** The fix for an interpreter is a *real stack probe*, not a
depth counter, because any counter a person would choose is two orders of magnitude below VB6's real
capacity — and wrong in the direction that breaks working programs rather than the one that catches broken
ones. HexIDE uses `RuntimeHelpers.TryEnsureSufficientExecutionStack()` at `RunProcedure`, the single sink
every user procedure, method, Property accessor and lifecycle hook flows through.

**Known divergence in depth, not in behaviour.** HexIDE's own frames are far fatter than compiled VB6's — an
async state machine plus an ANTLR visitor walk per call — so on a 1 MB Windows thread stack it reaches about
**202** frames for a bare `Call A(n + 1)` sub and **between 60 and 80** for a recursive Function. Linux
main threads get 8 MB and go several times deeper. That gap is tracked on its own; it is not a regression
from the guard, since the process was already dying at the same depth.

Two probes did **not** complete and their answers are still open:

- a recursive **Function** (`DeepFn = DeepFn()`) had not raised 28 after 120 s, where the equivalent Sub
  raises in well under a second. Whether VB6 gets much deeper in a Function or simply takes far longer is
  unmeasured;
- `On Error Resume Next` **inside** the recursive Sub ran for minutes without settling. Each frame swallows
  the error and carries on, so the unwind re-recurses; whether it terminates at all is unmeasured.

### Variant arithmetic promotes on overflow

First spotted while writing the #80 tests (an untyped `Fact(10)` should be 3628800 as a Long, and raised
Err 6 instead). Superseded by the fuller measurement in *Declared type, Variant subtype, and overflow
promotion* below, which has the whole ladder and the declared-vs-Variant rule that drives it.


