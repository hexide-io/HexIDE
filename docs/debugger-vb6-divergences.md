# Interpreter debugger — known divergences from VB6

A **living catalogue** of where HexIDE's native interpreter debugger (and its Edit-and-Continue affordance)
deliberately or knowingly diverges from real VB6's debugger/IDE behaviour. Add a row here the moment a divergence
is found — during implementation, review, or live verification — so it's tracked instead of forgotten.

**Scope / sibling docs.** This doc is about *debugger and IDE-during-debug* behaviour. Two neighbours own other
lanes: runtime *language* feature gaps (missing statements/intrinsics) live in [`interpreter-gaps.md`](interpreter-gaps.md);
verified runtime *semantics* (numeric types, errors, coercions — checked against `vb6.exe`) live in
[`vb6-fidelity-oracle.md`](vb6-fidelity-oracle.md); MCP dev-server tooling gaps live in
[`mcp-server-gaps.md`](mcp-server-gaps.md). The debugger's design + phase status is in
[`../openspec/specs/interpreter-debugger/spec.md`](../openspec/specs/interpreter-debugger/spec.md).

**Why these exist.** The in-box interpreter is an *approximation* for out-of-box tyre-kicking, not a fidelity
engine (that is a real language engine's job, behind the replaceable backend seam). Each divergence is a deliberate boundary marker, not a bug backlog — though
some are refinements we may take. Format: **What → VB6 → HexIDE → Why → Status**.

---

## Edit-and-Continue

### D1. True E&C is a permanent wall
- **VB6:** edit the paused line, press F5, keep running with all state intact (many edits apply live).
- **HexIDE:** cannot hot-patch a running program at all.
- **Why:** the tree-walker's execution position IS the live C# async call stack (parked mid-`VisitBlock`), not a
  re-pointable program counter; continuing after a re-parse would need an explicit-stack / continuation VM.
- **Status:** **permanent wall** (true E&C is a compiling debug backend's lane). Softened
  by D2.

### D2. Reset-project prompt stands in for E&C — for EVERY edit
- **VB6:** applied most edits live; showed *"This action will reset your project. Do you want to continue?"* only
  for edits it *couldn't* apply (structural changes). A comment or a simple in-line tweak applied with no reset.
- **HexIDE:** the interpreter can apply *no* edit live, so **every** edit while running/paused pops that same
  prompt (Yes = reset + keep the edit; No = revert). So HexIDE over-prompts relative to VB6 (it resets even for a
  comment).
- **Why:** honest + muscle-memory-recognisable — VB6's own dialog, surfaced for the whole edit set the interpreter
  can't hot-patch. Reframes the wall as a familiar behaviour.
- **Status:** **shipped** (commit 1e52d8f, 2026-08-09), live-verified. A genuine live-apply subset (edits to
  procedures NOT on the current call stack) is a possible later refinement — constrained today because a live run
  loads only the startup form's code.

### D3. E&C prompt fires per-keystroke, not on line-leave
- **VB6:** evaluates an edit when you **leave the line** (or on Run/Continue), so you can finish typing a line /
  comment before it reacts.
- **HexIDE:** prompts on the **first keystroke** of an edit while running/paused.
- **Why:** simpler first cut; the per-keystroke trigger is more eager (nags mid-line).
- **Status:** **PARKED refinement** (user, 2026-08-09). To do: record a *pending edit* (line + snapshot, both
  already captured) on edit and fire the prompt when the caret **leaves that line** (and on Run/Continue). Confirm
  the exact VB6 rule for line-count-changing edits (Enter) and F5-with-a-pending-edit against real `vb6.exe` first.

---

## Stepping

### D4. Step Into past the end of a procedure returns to the caller; a top-level handler disarms
- **VB6:** stepping off the end of a proc returns to the caller (breaking there while stepping); off the end of the
  outermost event handler clears the step and the next event runs free.
- **HexIDE:** matches this. Step is a one-shot: off the end of a NESTED proc it breaks at the caller's next statement
  (Step Into always breaks at the next executed statement); off the end of the OUTERMOST event handler it is
  **disarmed at the event-dispatch boundary** (`VBWindowContext.ExecuteSub`), so it does NOT leak into the next event.
- **History:** from Phase 2 through the first P5 cut the one-shot instead **carried** into the next event's first
  statement (a documented divergence — the faithful proc-end-clear had introduced 7 defects in the Phase-2 review, so
  it was removed). The v2·P5 dispatch-idle disarm restored the VB6 behaviour after both P5 adversarial reviewers
  independently flagged the leak (a Step Over leaving `target=1` fired on *every* subsequent event).
- **Status:** **resolved (v2·P5)**. Matches VB6's "next event runs free."

### D5. Step Over / Step Out (v2·P5)
- **VB6:** Shift+F8 (Step Over — run a called proc to completion, break at the next statement in the current frame),
  Ctrl+Shift+F8 (Step Out — run the rest of the current proc, break in the caller).
- **HexIDE:** implemented as depth-based one-shots using each activation's **captured** call depth (recorded when the
  activation is pushed, NOT the live stack size — so an overlapping re-entrant event frame can't inflate it): Step
  Over breaks when the executing frame's depth ≤ the armed depth (a called Sub runs to completion; a non-call
  statement is same-depth so it behaves like Step Into — matches VB6); Step Out breaks when depth < the armed depth
  (the proc returned). A never-consumed step is disarmed at the event-dispatch boundary (see D4). Module top-level
  code has depth 0 (below any proc), so Step Over from top-level does not descend into a callee and Step Out from a
  top-level-called proc reaches the top-level caller.
- **Why per-frame (not live count):** the first P5 cut read the live `ActivationStack.Count` with a `Max(1, …)`
  clamp, which (a) conflated top-level depth 0 with a first-level proc's depth 1 — so Step Over descended and Step
  Out never fired — and (b) let an overlapping Timer frame started mid-step inflate the count and miss the caller
  break. Capturing depth per-frame fixes both (P5 reviewers, HIGH + MED; regression-guarded in `StepStackTests`).
- **Status:** **implemented + hardened (v2·P5)**.

### D6. Events frozen during a break resume on Continue (not VB6's exact scheduling)
- **VB6:** its own break-mode event scheduling.
- **HexIDE:** a Timer tick / control event that fires while paused is **frozen** (held on the resume gate) and
  resumes when you Continue — HexIDE's cooperative single-thread freeze model, not a byte-for-byte match of VB6's.
- **Why:** the interpreter is UI-thread fire-and-forget async; the freeze is what stops events running while paused.
- **Status:** **by design** (approximation); no known user-visible problem after the Phase-2 fixes.

---

## Locals / inspection

### D7. Locals property surface — live-control + form `Me` properties + child-control tree all shown (P8+) — RESOLVED
- **VB6:** expanding `Me` (or any control) in the Locals window shows its full **property set** (`Caption`, `Width`,
  `BackColor`, `Font`, …) *and* its **child controls**, alongside module-level variables.
- **HexIDE:** (P8) a **live control variable** (`Vb6Value.ValueType.Control`) EXPANDS to its readable VB6
  properties — name-sorted, each read via the same `AvaloniaInteroperability.TryGet` the interpreter uses for
  `Control.Prop` (a new `ReadProperties` enumerator). (residual-1) a **loaded form's `Me` root** does the same:
  because `VBLoader` binds `Me` → `new Vb6Value(window)` (the live `VBFormRuntime` Control), the inspector detects
  the Control-backed `Me` and lists the form window's own properties (`Caption`, `Width`, `Height`, `BackColor`, …)
  *ahead of* the module-level variables. (residual-3, verified) **child controls DO nest under the form `Me`**: they
  live in the form module's `RootEnv` (`VBLoader` `AllocVariable(env, name, control)`), which is the inspector's
  `baseEnv`, so `EnvChildren` enumerates each one under `Me` and the control `ValueNode` expands it to *its* property
  set. Live-verified: a form with `Command0` paused in `Form_Load` → `Me` → `{form properties…, Command0 →
  {Caption="Hi", Left=100, Width=120, …}, mCount=5}`; the control is a child of `Me`, never a top-level sibling.
  Class-instance / object nodes still expand to their declared fields.
- **Genuine residuals (separate, documented elsewhere — NOT this row):** **control arrays** collapse to one variable
  (they aren't modelled as an indexed collection — interpreter-gaps "Runtime forms/controls surface" / E1); the
  **`Controls` collection** isn't modelled; **container-child nesting** (a control inside a Frame/PictureBox) shows
  flat under `Me` rather than under its container — the stated cause, that the runtime spawns all controls flat on
  one form canvas, stopped being true with #84 (a Frame and a PictureBox now host their contents on their own
  canvas), so what remains is that `DebugInspector`'s child provider enumerates the form's controls flat rather
  than walking the containment tree the model records. Same symptom, smaller and purely-Locals-side cause; an
  *unloaded* form's **synthetic `Me` root** (D8 — no backing
  control) has no property surface; and `ICSharpProxy` wrappers don't expand (a non-issue — the only proxied roots,
  `Debug`/`Err`, are `Hidden`). Value formatting is the inspector's approximate `FormatValue` (colours/fonts via
  their value, not `&H…`/font descriptors) — the D9 approximation.
- **Status:** **RESOLVED for the ordinary case** — form/control property surface + the child-control tree all render
  (P8 + residual-1/3, 2026-08-10). The lazy child-provider model made each surface purely additive. The
  `Me.Tag`-leaked-`TaskCompletionSource` defect this surfaced is FIXED (see interpreter-gaps). The remaining items
  above are separate runtime-model gaps, not a Locals-tree deficiency.

### D8. A form's `Me` root is synthetic (forms are singleton Standard modules)
- **VB6:** a form is a class with a default instance; `Me` in a form event handler is a real object with the form's
  properties + module-level variables under it.
- **HexIDE:** the interpreter models a form as the **primary Standard module** (a singleton), so a form event
  handler runs with **no backing `Me` object**. The Locals inspector still shows a root labelled `Me` over the form
  module's module-level variables (a class instance method, by contrast, has a real `Me` = its `VbObject`). A plain
  `.bas` module frame labels the root with the module name instead. The form's own name — its self-reference
  binding in module scope (HexIDE seeds `Form1` → the runtime instance) — is hidden under `Me`, since VB6 never
  lists `Me` as a field of itself.
- **Why:** consequence of the form-as-singleton runtime model; keeps a recognisable `Me` root without inventing a
  fake object. Ties to D7 (no property/control surface under it).
- **Status:** **by design** (approximation). Exact VB6 Locals labelling (`Me` vs module name, root ordering) is
  worth confirming against the real VB6 IDE at some point; recorded so it isn't mistaken for a bug.

### D9. Locals show only already-executed `Dim`s; value formatting is approximate
- **VB6:** the Locals window lists **every** declared local from procedure entry (unset ones show as `Empty`/0/""),
  and formats values per the VBA locale conventions.
- **HexIDE:** locals appear **only once their `Dim` statement has executed** (the interpreter allocates a local's
  slot lazily when its `Dim` runs), so a local declared later in the proc is absent until stepped past. Value
  formatting is a close-but-not-identical approximation (strings quoted; `True`/`False`; `#yyyy-MM-dd HH:mm:ss#`
  dates in the invariant culture; `Nothing`/`Empty`/`Null`; objects/arrays/UDTs show their type and expand). An
  object- or UDT-typed array labels its header as `Variant()` (the element class/type name isn't recoverable from
  the array's stored element type) while its populated elements label correctly.
- **Why:** lazy `Dim` allocation is the interpreter's execution model; exact VBA value formatting is a rabbit hole
  not worth chasing for a tyre-kicking debugger.
- **Status:** **by design** (approximation). Arrays are capped at 500 shown elements with a "… N more" marker (a
  visible, non-silent cap).

### D10. Locals tree expansion is preserved across a step (P8) — RESOLVED
- **VB6:** expanded nodes stay expanded as you step, so you can watch one field evolve.
- **HexIDE:** (P8) the tree is still rebuilt on every `Stopped`, but `LocalsToolViewModel.Refresh` now SNAPSHOTS the
  expanded node paths (the chain of `Expression` names, walking only already-realized subtrees) before the rebuild
  and re-expands matching paths after — so expansion survives a step. `TreeViewItem.IsExpanded` binds TwoWay to a new
  `LocalsVariableNode.IsExpanded`.
- **Residual:** a node whose path changes (renamed/re-shaped) between breaks isn't matched (collapses) — acceptable,
  since its identity genuinely changed.
- **Status:** **resolved** (P8). Regression-guarded in `LocalsToolViewModelTests`.

### D11. Module-level `Const`s appear under the root as variables
- **VB6:** the Locals window does not list module-level `Const`s (or `Enum` members).
- **HexIDE:** a `Const` "is just a slot" in the module env (`StatementExecutor.VisitConstStmt`), and each `Enum`
  member is likewise seeded as a module-env slot so a bare-name reference resolves (`PrePass` — `rootEnv.DefineVariable`),
  so **both** module `Const`s and `Enum` members show under the Me/module root as ordinary variables.
- **Why:** the slot-based partition can't tell a `Const` slot from a `Dim` slot without the pre-pass's declaration
  metadata, which the debug frame doesn't carry.
- **Status:** **by design** (approximation) — arguably even useful (you see the constant's value). Found by the
  cross-model (Fable) red-team pass.

### D12. A ByRef parameter that aliases a same-named module var / field is shown only under the root
- **VB6:** a ByRef parameter shows at procedure level even when its name matches a module variable / instance field.
- **HexIDE:** the partition classifies a `currentEnv` entry as base-scope (root-only) when its slot equals the base
  scope's slot for that name. A ByRef param that BOTH shares a name with a module var/field AND is passed that
  exact variable aliases the same slot, so it appears only under the root, not at proc level. (The common cases —
  a differently-named param, or a different variable passed — are correct and tested.)
- **Why:** when the slots genuinely coincide the heuristic can't separate "the param aliasing X" from "X" without
  the procedure's parameter list (semantic metadata the frame doesn't carry).
- **Status:** **by design** (very narrow edge). Found by the Fable red-team.

### D13. A break during class field-initialization has a degenerate partition
- **VB6:** n/a in the same shape.
- **HexIDE:** if a breakpoint lands during a class instance's field initialization (`New` → field-init blocks,
  before `Me` is bound), the frame's base scope is the class TEMPLATE env while `currentEnv` is the fresh instance
  env, so fields can split oddly between top-level and the root, and the root may show stale template slots.
- **Why:** the field-init frame runs over the template/instance env pair with no `Me`; the partition heuristic
  degenerates there.
- **Status:** **by design** (narrow — needs a breakpoint on a class field-declaration line during construction).
  Found by the Fable red-team; recorded for a future refinement.

---

## Immediate window

### D14. The Immediate window executes assignment / Set, but not other statements (v2·P7c)
- **VB6:** the Immediate window runs *any* statement — assignment (`x = 5`), `Set`, `Debug.Print`, calling `Sub`s
  (`Foo`), etc.
- **HexIDE:** (P7c) a **bare assignment or `Set`** (`count = 5`, `Set obj = Nothing`) is now EXECUTED against the
  paused frame and mutates its state — `count = 5` **assigns**, whereas `?count = 5` still **compares** (prints
  `True`/`False`). `?expr` / `Print expr` / `Debug.Print expr` evaluate + print; a bare expression (`count`,
  `count + 8`) still evaluates. Every OTHER bare statement (a bare intrinsic statement like `Erase`/`Beep`,
  control-flow, …) falls back to expression evaluation — so a non-let/set statement that isn't also a valid
  expression is a "Syntax error", not executed. User calls stay rejected (D15).
- **Why the narrowing:** `letStmt`/`setStmt` are unambiguously statements; a bare identifier parses as an implicit
  call, so restricting execution to let/set keeps a bare `count` **evaluating** to its value (backward-compatible)
  instead of being run as a call. Broader bare-statement execution needs disambiguation not worth its edge value.
- **Status:** **by design** (P7c scope — decided "assignment now, user calls later"). Whole-input strictness still
  means an incomplete expression (`count +`) is a syntax error, not a truncated evaluation.

### D15. All user-code invocation is rejected in the Immediate window (v1)
- **VB6:** you can call user procedures (and `New` objects) from the Immediate window, with their side effects.
- **HexIDE:** any user-code invocation in an Immediate expression is rejected — a bare call, an instance method
  (`?obj.Method()`), a **Property Get** (`?obj.Prop`), a Module-qualified call (`?Module1.Foo()`), and `New`
  (`?New Thing`). Intrinsics (`Len`, `Mid`, `UCase`, …) and direct field reads (`?obj.Field`) still work.
- **Why:** the interpreter runs on the UI thread and is parked at the paused statement gate; running any user
  statements would hit that same gate on the same thread — a **deadlock** — and mutate program state mid-debug.
- **Status:** **by design** (v1). The `SuppressUserProcedureCalls` guard sits at the two sinks EVERY user body flows
  through — `RunProcedure` (calls/methods/Property Get) and `NewObject` (`New`) — and is scoped to `State==Paused`
  so it can never affect the resumed program walk if it leaks across an async intrinsic's UI-thread yield. (The
  original guard sat only in `CallProcedure` and missed the method/Property-Get/qualified/`New` paths + could leak —
  both found by the cross-model adversarial review.)

---

## Call Stack

### D16. The Call Stack window shows a Line column (VB6's dialog had neither columns nor line numbers)
- **VB6:** the Call Stack was a **modal dialog** — a plain single-column list of `Project.Module.Procedure` frames
  (current at top) with **Show** and **Close** buttons, and **no line numbers** (VB6 code had no line numbers by
  default).
- **HexIDE:** the Call Stack is a **dockable tool window** (a `Document`/tool pane, not a modal dialog), and each
  frame carries a right-aligned **Line** column (the 1-based statement line of that activation). Current frame is
  marked with an arrow; there are no Show/Close buttons (the dock owns close; double-click-to-navigate is a later
  nice-to-have).
- **Why:** a non-modal, always-available pane fits the dock-based IDE better than a modal dialog, and the interpreter
  *does* track a per-frame current line, so surfacing it is a cheap, useful modern-additive (muscle-memory:
  recognisable list, plus more info). Project name is omitted (single-project interpreter).
- **Status:** **by design** (additive evolution). User-recognisable; strictly more information than VB6's list.

### D17. The Call Stack is anchored at the paused frame; a frozen re-entrant event's frames are excluded
- **VB6:** its own break-mode call-stack presentation for re-entrant (event-during-event) situations.
- **HexIDE:** the chain is built from the **paused activation** down through its callers — `GetCallStack` finds the
  paused frame's own position in the activation stack and walks from there toward the root. A newcomer event (Timer
  tick / control event) that fires and freezes on the pause-gate while you are stopped is pushed **above** the paused
  frame; because the walk is anchored at the paused frame, those frozen newcomer frames are **excluded** — the paused
  frame stays the "current" (arrowed, top) row and the real caller chain is shown, not the unrelated frozen handler.
- **History:** the first P5 cut walked the whole activation stack top-down, so a frozen newcomer appeared as the
  "current" frame with the actually-paused frame pushed down the list (and, for a top-level break, the paused frame
  could vanish). Both P5 reviewers flagged this; anchoring at the paused frame fixed it.
- **Limitation:** a break in **module top-level** code isn't on the activation stack, so its Call Stack is just that
  single frame — the entry frame of an `A→B→C` chain called from top-level code is the deepest shown row (its
  top-level caller isn't a procedure and isn't listed). Real VB6 has no top-level code, so this only surfaces via the
  test harness / VBScript-style top-level.
- **Status:** **by design** (approximation); consistent with the cooperative single-thread freeze model.

---

## Execution control

### D18. Set Next Statement is TOP-LEVEL-BODY granularity only (v2·P7b)
- **VB6:** you can drag the yellow arrow to — or `Ctrl+F9` on — **any** executable line in the current procedure,
  including a line nested inside an `If`/`For`/`Do`/`Select` block. VB6 is compiled to a flat instruction stream with
  a real instruction pointer, so every line is an addressable jump target.
- **HexIDE:** Set Next Statement works only to a **top-level statement of the currently paused procedure**, and only
  while paused **at** a top-level statement. A target nested inside a block — or a move requested while paused inside
  one — is **refused** with a message. Reason: the top-level body runs through a `pc`-addressable loop
  (`ExecuteProcedureBody`, the same mechanism `GoTo`/`Resume` use), but statements nested in a block run via
  recursive C# descent (`VisitBlock`'s `foreach`) that can't be jumped into from outside.
- **Not a VB6 limitation — an INTERPRETER one.** Making any line a jump target needs a linearized control-flow graph /
  bytecode instead of tree-walking — a large rewrite that edges into compiler territory (the CST-not-AST /
  approximation-only boundary). Parked as the "CFG-rewrite wall."
- **Status:** **by design** (a real tree-walker limit, not an artificial cap). Run To Cursor (P7a) has no such limit —
  it's a one-shot breakpoint, matched by line at any depth.

---

## Deferred debugger surfaces (not divergences yet — just not built)

Immediate USER-CALL execution (calling a user Sub/Function from the Immediate window — the deadlock-prone part, D15;
assignment/Set landed in P7c, D14); modern per-line conditional breakpoints / hit counts (deferred additive Evolution
item — VB6's watch-based Break-When-True/Changed shipped in P6b). Tracked in
[`MISSING_FEATURES.md`](MISSING_FEATURES.md) and the spec's v2 list (P8). Serious/compiled debugging belongs to a
compiled backend over DAP. *(Landed since this list was written: Watches + Break-When-True/Changed + Data Tips (P6), Run To Cursor
+ Set Next Statement + Immediate assignment/Set (P7), the Locals property/control surface incl. the form-`Me` +
child-control tree (P8 + D7 residual-1/3, D7 now RESOLVED).)*
