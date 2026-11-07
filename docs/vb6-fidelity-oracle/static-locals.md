## Static locals (2026-09-02)

Measured before implementing anything, because the design branches on the answer and two plausible
positions were both on the table. Nothing here is implemented yet — `Static x As Long` throws, and
`Static Sub` is silently accepted with the modifier discarded, which is worse (see `interpreter-gaps.md`).

| probe | measured |
|---|---|
| `Static n As Long` inside a class module method | **compiles** — legal, not a VB6 error |
| two instances of that class, `a` bumped 3x and `b` once | **3/1** — storage is PER-INSTANCE |
| `Static Function` with an ordinary `Dim k` inside, called 3x | **3** — the modifier makes ALL locals static |
| plain `Function`, `Static n` + `Dim m`, called 3x, reported as `n * 10 + m` | **31** — n persists, m resets |

### What this overturned

The expectation going in was that VB6 might not permit `Static` in a class module at all — a reasonable
guess, since per-instance static storage is an odd thing for a language to offer and there is no analogue
in most of the family. It permits it, and it does the more expensive thing: each instance gets its own
copy. Had we implemented on the guess, a class with a `Static` counter would have shared one counter
across every instance, which is a silent wrong answer rather than a crash.

The `Static Function` row matters for a different reason: the modifier is not a shorthand, it genuinely
changes the storage class of every local in the procedure. HexIDE currently parses it and throws the
modifier away, so such a procedure runs with ordinary locals and quietly produces wrong numbers.

### What it means for the implementation

Static storage cannot hang off `ProcedureInfo`, because that is shared by every instance. For a class
module method it belongs on the `VbObject`, keyed by procedure and name, and it dies with the instance.
For a standard module the two are indistinguishable (the module is a singleton), so a per-procedure table
is correct there.

The slot model already does the hard part: `ExecutionState` holds slots by id and `ExecutionEnvironment`
binds names to them, so a static local is "look up or create the slot, bind the name to it, and do NOT add
it to `ownedSlots`" — the last part being what keeps scope-exit from releasing it and firing a premature
`Class_Terminate`. Recursion then shares the slot without any extra work, which is what VB6 does.

