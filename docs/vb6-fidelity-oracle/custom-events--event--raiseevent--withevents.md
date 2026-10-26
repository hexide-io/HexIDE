## Custom events — `Event` / `RaiseEvent` / `WithEvents` (interpreter-advanced Phase 5)

Verified with a `Clock` source class (declares `Event Tick(ByRef Cancel As Boolean)` + `Event Plain()`, raises
them from `DoTick`/`DoPlain`) and a `Listener` class (`Private WithEvents src As Clock`, `src_Tick` handler, an
`Attach`/`Detach` to `Set src`), driven from `Sub Main`.

| Behaviour | `vb6.exe` result |
|---|---|
| `WithEvents` in a **standard `.bas`** module | **compile error: "Only valid in object module"** — `WithEvents` (and the event-sink pattern) is **class/form-only**. The sink is therefore always a class **instance**; the handler `{var}_{event}` is a method of that instance and runs **on it** (with `Me` = the listener instance). |
| `RaiseEvent` with **no sink** bound (`WithEvents` var is `Nothing`) | **silent no-op** — the raiser's pre/post lines bracket nothing; a `ByRef Cancel` stays unchanged. |
| `RaiseEvent` of an event with **no matching `{var}_{event}` handler** | **silent no-op** (handlers are optional; e.g. `Plain` with no `src_Plain`). |
| handler present | runs **synchronously between** the raiser's surrounding statements (blocking); a **`ByRef Cancel` written by the handler is seen by the raiser** after `RaiseEvent` returns. |
| **Multiple** `WithEvents` vars bound to one source (multicast) | **ALL fire, in *attachment* order** (first-`Set` fires first). The event's `ByRef` args are **shared across handlers** — a later handler sees an earlier one's write-back (e.g. `Cancel` left `True` by the first handler is `True` when the second runs, and to the raiser). |
| `Set src = Nothing` | unbinds **that** sink only — other sinks on the same source still fire. |
| Rebind `Set src = otherSource` | the sink **moves** — raising on the *old* source no longer reaches this handler; raising on the *new* source does. |
| Advised listener whose **own** references drop (source stays alive elsewhere) | the listener **terminates** at scope-exit — a source does **NOT** hold a strong back-reference to its listeners, so there is **no leaked cycle**. VB6 then auto-unadvises, so a later raise on the still-alive source does **not** reach the (now-dead) handler. *(Overturned the initial "strong back-ref → leak" guess — the oracle says the opposite.)* |
| `RaiseEvent Foo(SideEffect())` with **no listener** bound | the **argument expressions are still evaluated** (a side-effecting arg runs — a counter incremented to 1) — then dispatch is a no-op. VB6 evaluates `RaiseEvent` args regardless of whether any connection is advised. |
| `Set src = Nothing` where the old source raises an event in its **own `Class_Terminate`** (source held only by that `WithEvents` field) | VB6 **unadvises before releasing** — the detaching listener is disconnected first, so the source's `Class_Terminate`-time `RaiseEvent` does **not** reach it. (Unadvise-then-release ordering.) |

**`Set` slot ordering — assign the new reference BEFORE releasing the old.** Probed with a `CThing` whose
`Class_Terminate` reports what the global `g` points to across `Set g = New CThing` (twice) then `Set g = Nothing`:
result **`0;N;`**. So during the reassign, the release-triggered `Class_Terminate` of the *old* instance already sees
`g` pointing at the *new* instance (whose `Id` is still 0), and the final `Set g = Nothing`'s terminate sees `g` as
Nothing. Consequence for the interpreter: `Set` must write the slot/field/element **then** `ReleaseRef` the old
occupant (not the reverse) — otherwise a terminate reads the dying object, and a reassignment made inside that
terminate is clobbered by a late slot-write. (In the current interpreter this is defense-in-depth: a `Class_Terminate`
can't yet read an outer-scope slot, so the scenario isn't reachable — but the ordering is now correct.)

**Design consequence:** VB6 events are **multicast** with deterministic attach-order dispatch and shared ByRef
args. A "single-sink" MVP would visibly diverge whenever ≥2 listeners bind one source (only one would fire). The
sink is a **(listener-instance, WithEvents-var-name)** pair; a source keeps the set of currently-bound sinks and
dispatches to `{varName}_{eventName}` on each listener instance, synchronously, in attach order, passing the same
(ByRef-aliased) args to each.

**`WithEvents` belongs to each declarator, not to the statement** (2026-10-14, hexide-io/HexIDE#1021). Measured with
`scripts/vb6-oracle.ps1`, one headless `/make` and run per row. `Class2` is the source (`Public Event Ev()`, and
`Public Sub Fire()` raising it); `Class1` holds the row's declaration line, a `Hits` string, `Private Sub A_Ev()` and
`Private Sub B_Ev()` each appending their letter, and `Test()`, which sets `A` and `B` to new `Class2`s, fires each,
and returns `"[" & Hits & "]"`.

| Row | `Class1`'s declaration | `/make` | `Test()` |
|---|---|---|---|
| R1 | `Public WithEvents A As Class2, WithEvents B As Class2` | compiles | `[AB]` |
| R2 | `Private WithEvents A As Class2, WithEvents B As Class2` | compiles | `[AB]` |
| R3 | `Dim WithEvents A As Class2, WithEvents B As Class2` | compiles | `[AB]` |
| R4 | `Public WithEvents A As Class2, B As Class2` | compiles | `[A]` |
| R5 | `Public A As Class2, WithEvents B As Class2` | compiles | `[B]` |
| R6 | the two declarations on two lines (control) | compiles | `[AB]` |
| N1 | `Public WithEvents A As Class2, WithEvents B As Long` | `Expected: identifier` | |
| N2 | `Public WithEvents B As Long` (control) | `Expected: identifier` | |

So a `WithEvents` may stand before any declarator of a comma list, makes that declarator alone a sink, and is held
to the same rule behind a comma as at the front (N1 against N2). HexIDE previously treated it as a statement
modifier: both grammars took it once after the visibility or `Dim`,
refusing R1–R3 and R5 and making both names in R4 sinks. Both grammars now take it per declarator, and the
interpreter records and seeds only marked names as sinks (#1021). Both compile errors report `Line 0`, which nothing
here explains. The non-object-type refusals (N1/N2) remain unimplemented; no new type validation is added here.
Whether VB6 accepts a variable named `WithEvents` is unmeasured; HexIDE's existing parse is preserved.

