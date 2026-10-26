# Interpreter debugger — known divergences from VB6

A **living catalogue** of where HexIDE's native interpreter debugger (and its Edit-and-Continue affordance)
deliberately or knowingly diverges from real VB6's debugger/IDE behaviour. Add a row here the moment a divergence
is found — during implementation, review, or live verification — so it's tracked instead of forgotten.

**Scope / sibling docs.** This doc is about *debugger and IDE-during-debug* behaviour. Two neighbours own other
lanes: runtime *language* feature gaps (missing statements/intrinsics) live in [`interpreter-gaps.md`](interpreter-gaps.md);
verified runtime *semantics* (numeric types, errors, coercions — checked against `vb6.exe`) live in
[`vb6-fidelity-oracle/README.md`](vb6-fidelity-oracle/README.md); MCP dev-server tooling gaps live in
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
- **VB6:** applies most edits live. A comment, a body line, even a new module-level `Dim` apply with no prompt. For an
  edit it can't apply live, it asks as the caret leaves the line, and it has two prompts (measured, #1329; the oracle's
  "The reset prompt" section):
  - **"This action will reset your project, proceed anyway?"** (OK, Cancel, Help), for retyping a module variable or
    changing a procedure's parameters. Cancel puts the line back and stays in break mode; OK keeps the edit and ends
    the run.
  - **"This edit requires a Reset"** (OK, Help), which can't be declined, for a duplicate declaration.
  - In run mode the code window takes no typing at all, so neither prompt arises there.
  - This bullet used to quote the prompt as "…Do you want to continue?" with Yes and No. That was never measured, and
    it was HexIDE's wording, not VB6's. Since #1360 HexIDE uses the measured wording and OK/Cancel, without Help
    because it has no help topic.
- **Which windows the prompt holds (measured, #1329):**
  - **VB6:** the running form owns it, and every top-level window of the process is disabled while it waits: the IDE
    and the program together. The program is paused in break mode anyway, and run mode offers no prompt.
  - **HexIDE (#1366):** the code window's own window owns it when visible and not minimized (#1298), with the
    running form as the fallback. `WindowManager` now disables all desktop windows for the prompt's lifetime and
    restores their enabled states afterwards. Headless tests cover both owners and Yes/No; native enabled-state
    verification on the running IDE remains required.
  - **Remaining differences:** ownership still differs from VB6, and the program isn't paused for the prompt in
    run mode, so Timers keep firing. Floating a code window currently hides it instead (#1361).
- **HexIDE:** the interpreter can apply *no* edit live, so **every** edit while running/paused pops that same
  prompt (OK = reset + keep the edit; Cancel = revert). So HexIDE over-prompts relative to VB6 (it resets even for a
  comment).
- **Why:** honest + muscle-memory-recognisable — VB6's own dialog, surfaced for the whole edit set the interpreter
  can't hot-patch. Reframes the wall as a familiar behaviour.
- **Status:** **shipped** (commit 1e52d8f, 2026-08-09), live-verified. A genuine live-apply subset (edits to
  procedures NOT on the current call stack) is a possible later refinement. A form's run loads the project's
  standard and class modules too (#258); stored breakpoints in both stop through `ProjectRunnerService`
  (`DebuggerLinesAreFileLinesTests`, #10). `.ctl` and `.pag` are not loaded as free-standing modules by that path.
- **Since #273 task 3.13 (2026-10-02):** the prompt asks only about an edit that lands. A keystroke in the
  file's header or a member's `Attribute` lines writes nothing, and no longer asks to reset the run for it; nor
  does a designer change or a save, each of which rewrites the header as the IDE's own edit (a designer change
  has a cost of its own during a run: D26), nor a reload, which the file watcher holds back until the run ends
  (`FileWatcherService`). Paste, cut and Delete with a selection now ask: their keys are
  bound to code-window commands that took them before the old handler saw them, so until then a paste while
  paused landed with no prompt at all, by the key, the menus and the toolbar alike. Answering Cancel restores the
  code only, under whatever header the window shows by then.
- **Since #1025:** keystroke arming follows AvaloniaEdit's editing bindings and its Delete Line and Indent
  Selection gestures. Delete Line and word deletion previously landed without asking; headless regression
  tests now cover all three, No restoring the deletion, and Delete Line refusing the header without asking.
  This fixes HexIDE's reset affordance; it does not establish new VB6 behaviour. Undo and Redo remain
  unmeasured and unchanged (#710, #513).
- **"Every edit" is unmeasured for the routes that are not keystrokes.** Replace All, formatting, Insert File,
  rename, an add-in's `ApplyEdits`/`SetContent` and automation's `set_file_content` while paused have not been
  checked against the prompt. Paste, cut and Delete were assumed covered until they were tested, and were not.
  Tracked as [#710](https://github.com/hexide-io/HexIDE/issues/710).

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

### D8. A form's `Me` root is synthetic (forms are singleton Standard modules) — RESOLVED (#822, first slice)
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
- **Resolved by #822's first slice (#1010):** a running form is an instance of its module, so `Me` in its event
  handlers is a real `VbObject`, as a class method's is. Locals shows it as `Me` with the form's name as its type: the
  window's VB6 properties first, then the instance's variables and controls (D7's surface, unchanged). The form's name
  is now seeded into every module, as `Debug` and `Err` are, so it is hidden from a module frame's Locals as well as
  under `Me`. The labelling question above is still unmeasured.

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
  (`?obj.Method()`), a **Property Get** (`?obj.Prop`), an object's **default member** (`?obj`, #840), a
  Module-qualified call (`?Module1.Foo()`), and `New` (`?New Thing`). An object with no default member is 438, which
  VB6's is inferred to be from `Debug.Print k` (measured, 438 in the IDE), and `?Err` is Err.Number. Intrinsics (`Len`, `Mid`, `UCase`, …) and direct field reads (`?obj.Field`) still work.
- **Why:** the interpreter runs on the UI thread and is parked at the paused statement gate; running any user
  statements would hit that same gate on the same thread — a **deadlock** — and mutate program state mid-debug.
- **Status:** **by design** (v1). The `SuppressUserProcedureCalls` guard sits at the two sinks EVERY user body flows
  through — `RunProcedure` (calls/methods/Property Get) and `NewObject` (`New`) — and is scoped to `State==Paused`
  so it can never affect the resumed program walk if it leaks across an async intrinsic's UI-thread yield. (The
  original guard sat only in `CallProcedure` and missed the method/Property-Get/qualified/`New` paths + could leak —
  both found by the cross-model adversarial review.)

### D28. A `?` line ending in `;` or `,` leaves no line open (deliberate, #1386)
- **VB6:** `? 6;` prints ` 6 ` and leaves the caret after it, on the same line. What is typed next joins that line, so
  `? 7` there becomes ` 6 ? 7`, and Enter answers "Compile error: Invalid in Immediate pane" (measured).
- **HexIDE:** the items print as VB6 prints them, ` 6 `, and the next line typed starts a line of its own.
- **Why:** the joined line is a trap rather than a use. Nothing typed after an open line can be evaluated, since VB6 reads
  the whole line, output included. The Fidelity Principle reproduces what VB6 means to do, not where it trips.
- **Status:** **by design.** A `?` line otherwise prints exactly as `Debug.Print` does (the oracle's "A `?` line in the
  Immediate window").

---

## Call Stack

### D16. The Call Stack window shows a Line column (VB6's dialog had neither columns nor line numbers)
- **VB6:** the Call Stack was a **modal dialog** — a plain single-column list of `Project.Module.Procedure` frames
  (current at top) with **Show** and **Close** buttons, and **no line numbers** (VB6 code had no line numbers by
  default).
- **HexIDE:** the Call Stack is a **dockable tool window** (a `Document`/tool pane, not a modal dialog), and each
  frame carries a right-aligned **Line** column (the 1-based statement line of that activation, counted from the
  top of the file: D25). Current frame is marked with an arrow; there are no Show/Close buttons (the dock owns
  close; selection or double-click navigates to the calling statement).
- **Why:** a non-modal, always-available pane fits the dock-based IDE better than a modal dialog, and the interpreter
  *does* track a per-frame current line, so surfacing it is a cheap, useful modern-additive (muscle-memory:
  recognisable list, plus more info). Project name is omitted (single-project interpreter).
- **Status:** **by design** (additive evolution). User-recognisable; strictly more information than VB6's list.

Caller navigation (#1866): selecting or double-clicking a built-in caller now opens its calling statement and
marks it with a green gutter triangle, leaving the current statement unchanged. This matches the behaviour
recorded in #1866; the dockable pane uses selection/double-click rather than VB6's modal **Show** button.

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

### D19. Step Into from design mode runs the program without breaking
- **VB6:** F8 in design mode starts the program and breaks on the first executable line, so the developer watches
  it begin one statement at a time.
- **HexIDE:** from idle, Step Into starts the program and it runs straight through. Measured 2026-09-22 on a
  `--newproject` form whose `Form_Load` called a function: through the Debug toolbar's Step Into button and through
  `step_into` alike, the state stayed `Running` and the Immediate window showed the load had completed. Stepping
  from a pause works, and so do breakpoints.
- **Why:** not known yet. The start path is meant to arm "the starting run's first statement"; the defect is there
  or in how the first statement of a form's load is reached.
- **Status:** **bug**, [#656](https://github.com/hexide-io/HexIDE/issues/656). What VB6 does on exactly this form is
  still owed to the oracle.
- **Resolved for a startup form by #1010 (#822's first slice):** the cause, read from the code rather than traced at
  the time, was that the start was more than one dispatch. Resize was raised first, and the end of its dispatch
  disarmed the armed step before Load ran. The start
  is now one dispatch, declarations, Initialize and Load, and the gate is skipped on declarations, as VB6 never stops
  on one. So Step Into from idle breaks on the first statement of `Form_Initialize`, or of `Form_Load` when there is
  none, and a step off the end of Initialize goes on into Load (pinned in `FormInstanceTests`). A `Sub Main` start
  is not re-measured.

### D20. A step from a function's last statement skips `End Function` — UNMEASURED
- **VB6:** not measured. Whether F8 stops on `End Function` / `End Sub` before returning to the caller needs the
  oracle.
- **HexIDE:** paused on `Twice = n * 2`, the only statement of a `Private Function`, a Step Into went straight to
  the caller's next line without stopping on `End Function` (2026-09-22).
- **Why:** unknown until VB6's behaviour is measured; if VB6 stops there, the walk has no statement to stop on
  for the `End` line.
- **Status:** **unmeasured**, recorded on [#656](https://github.com/hexide-io/HexIDE/issues/656). Not a
  divergence until the oracle says VB6 stops there.

### D21. The time slice was an implicit `DoEvents` every 15 ms — FIXED
- **VB6:** a running program gives up the thread only at `DoEvents`, a modal dialog or the end of an event
  procedure. An event that a statement raises (`Text1.Text = "x"` raising `Text1_Change`) runs to completion
  before that statement finishes, and no other event procedure runs inside one that has not finished. Measured
  since, native and p-code (vb6-fidelity-oracle/one-handler-at-a-time.md, *One handler at a time*).
- **HexIDE:** `DebugController` yields the UI thread whenever 15 ms have passed since the last yield, so that
  Ctrl+Break can land. That yield is effectively a `DoEvents`. If it lands at the first statement of a raised
  event's handler, the setter returns and the raiser runs on before the handler has finished. While any walk is
  yielded, a Timer tick or a click can start another event procedure inside it.
- **Why:** the program and the IDE share one UI thread, and Break has to be able to reach a tight loop.
- **Status:** **fixed** by [#766](https://github.com/hexide-io/HexIDE/issues/766). Found in the #762 review, where a
  test of a fault in a raised event passed on an idle machine and failed on a loaded one. An `EventPump` now runs one
  handler at a time: an event that comes while a handler runs waits for it, the raiser of a nested event waits for it,
  and `DoEvents` runs what is waiting. The time slice still yields, for Ctrl+Break, but lets nothing in.
  `EventOrderTests` and `RunFaultTests` run with `TimeSliceMs` = -1, which yields at every statement. What remains is
  the granularity: a nested handler finishes before the raiser's next statement, the end of its block, or the return
  of a call, rather than inside the very expression that raised it.

### D22. A compile error met while a program runs is a run-time error dialog, and the run carries on
- **VB6:** a compile error found while the program runs, such as a call to a Sub that does not exist, is shown in
  the compile-error box (OK and Help), and the IDE stops at that line. That is how it is commonly described; it is
  **not yet measured** on the oracle.
- **HexIDE:** the error is recorded, so `get_last_runtime_error` returns it, and shown in the run-time error
  dialog. End is enabled; Continue, Debug and Help are not. End ends the run. Closed any other way, the dialog
  leaves the run going, with the event procedure that met the error abandoned, as after a run-time error in an
  event procedure. A syntax error found before the run begins is a start that never became a run: it is reported in
  the same dialog, with the module it is in named, and nothing runs.
- **Why:** one route for every error a run meets. Sub Main's compile errors already went through that dialog
  (#754), and a separate compile-error box would answer the same error two ways. A tree-walker meets a compile error
  only when it reaches the statement, so stopping before the run began is not possible for it.
- **Status:** **open**. The one-dialog route was decided in [#759](https://github.com/hexide-io/HexIDE/issues/759);
  the measurement, and whether to break at the line instead, is
  [#833](https://github.com/hexide-io/HexIDE/issues/833).

### D23. An unhandled run-time error in an event procedure left the run going — FIXED
- **VB6:** in the IDE, an unhandled run-time error stops the program at the statement and enters break mode, offering
  End, Debug and Help, and nothing else runs until the developer chooses. A compiled exe shows the message and ends.
  Measured on the oracle (2026-10-14): `docs/vb6-fidelity-oracle/an-unhandled-run-time-error.md`, "An unhandled run-time error".
- **HexIDE, until #1011:** the error was recorded, so `get_last_runtime_error` returned it, and shown in the run-time
  error dialog, where End ended the run. The event procedure that met it was abandoned, and the run carried on: a Timer
  tick or a click still raised its handler, on whatever state the abandoned procedure left. That included a form's start (#1010): a
  run-time error in `Form_Initialize` is reported, and `Form_Load` is still raised and the form shown. Another form's
  Load is the same (#822): an error there is reported, never trapped by the `On Error` of the statement that loaded the
  form (measured, compiled), and that statement goes on, where a compiled exe would end. A fault, a
  construct HexIDE does not implement or an engine fault, is different: it ends the run, and during the start it stops
  before the window is shown.
- **Why:** the error reaches the dialog only after the event-dispatch boundary in `VBWindowContext` has caught it, once
  the procedure has unwound, so there is no live frame to break into. Breaking at the statement needs the walk to
  pause where the error is raised.
- **Status:** **fixed** by [#1011](https://github.com/hexide-io/HexIDE/issues/1011). An unhandled run-time error now halts
  the program at the statement that raised it, before anything unwinds. The dialog offers End and Debug, and nothing runs
  behind it. Debug breaks there, reason `RuntimeError`, with the procedure live, and Continue runs the statement again,
  all of it. End ends the run as End does. An error is unhandled when no activation would trap it, from the one that
  raised it down to where the program was entered: an event's procedure, a `RaiseEvent` sink or `Sub Main`. So a handler
  in the code that raised an event never sees its error, `RaiseEvent` included. `HaltOnErrorTests` pins each case.
  What remains:
  - **A statement runs again whole.** That is measured, for `x = F() + 1 \ 0`, where F is called again.
  - **A condition runs again on its own,** an `ElseIf`'s, or a `Loop Until`'s without another pass through the body,
    and so does a statement of a single-line `If`'s tail. **These are chosen, not measured.** They are the closest
    reading of "the faulting statement", and they never repeat what came before the fault. But nothing has asked vb6.exe
    what its F5 runs in these places ([#1079](https://github.com/hexide-io/HexIDE/issues/1079)).
  - **A `Case` expression runs again with its `Select Case`** and the `Case`s before it. That is a limit of the walk,
    also unmeasured.
  - **A dialog closed without an answer** takes Debug. What VB6 does then is unmeasured.
  - **Whether `Err` holds the error** at the break is unmeasured, and HexIDE leaves it as it was.
  - **A form's `Terminate`, and a class's `Initialize` and `Terminate`,** are not entries. An error a form's Terminate
    leaves unhandled is reported as before, and the run carries on.
  - **No host to halt:** the Standalone runner has none, and its run-time errors are
    [#757](https://github.com/hexide-io/HexIDE/issues/757). Without one, an error unwinds to the event's dispatch as
    before, and a `RaiseEvent` sink's error still reaches a handler in the raiser.

  D22 is the same divergence for a compile error, and D27 is the one case where HexIDE follows the compiled program
  instead.

### D27. A class's error that its caller handles goes to the caller, where VB6's IDE stops in the class (deliberate)
- **VB6:** under the IDE's default Error Trapping, *Break in Class Module*, a class method with no handler of its own
  that raises an error stops in the class, even when its caller has an active `On Error`. The caller's handler never
  runs. Under *Break on Unhandled Errors*, and in every compiled program, native and p-code, the caller's handler
  catches it (measured, `docs/vb6-fidelity-oracle/an-unhandled-run-time-error.md`, c1). A form counts as a class module here.
- **HexIDE:** the caller's handler catches it, as a compiled program does. An error that nothing handles still breaks
  at the innermost faulting line, a class's included, as the default does (#1011).
- **Why:** a deliberate exception to the rule that HexIDE matches the IDE where the two differ (decided 2026-10-15,
  recorded on #1011). The default stops code that handles its own errors, and following it would mean building
  "Continue from a break hands the error to the caller's handler".
- **Status:** **by design.** An Error Trapping option, which would let a developer choose VB6's default, is
  [#1070](https://github.com/hexide-io/HexIDE/issues/1070).
- **Numbered D24 when the decision was recorded** on #1011's branch, and renumbered when merging main brought in main's
  D24, the header-line breakpoint, which came first.

---

## Line numbering

### D24. No breakpoint or bookmark on a header line or a member's `Attribute` line
- **VB6:** the case could not arise for a line VB6 loaded: it hid the header and every `Attribute` line at load,
  an orphan's included, where HexIDE shows an orphan as editable text that takes a mark, which is never hit.
  One exception is measured: an `Attribute` line pasted into the code window stays code, shown in red, until
  the module is reloaded (V4 in [the measurement](vb6-fidelity-oracle/member-attributes-where-vb6-attaches-them-and-what-its-ide-writes.md)), where HexIDE reads one pasted
  directly under a declaration as a run at once and refuses a mark on it. Whether VB6 lets a breakpoint be set
  on such a red line is **unmeasured**.
- **HexIDE:** F9, Debug ▸ Toggle Breakpoint, Ctrl+F2 and a click in either gutter set no mark on such a line,
  and the status bar says why. A click on a folded header lands on the fold's first line, which is the
  header's, and is refused the same way. `set_breakpoints`, `set_bookmarks`, `run_to_cursor` and
  `set_next_statement` refuse those lines and reply with where the code starts. A form held read-only as a
  whole is not a read-only region, and takes breakpoints in its code as before. Run To Cursor from the Debug
  menu is **not** refused there: it behaves as from any other line that never executes, running until
  something else stops it. Set Next Statement from the menu already refuses any line that is not a statement.
- **A mark can still be on such a line, and can still be removed.** This row used to say that only an older
  sidecar could have left one there. Task 3.17 made that untrue: the sidecar's load drops a mark on a
  read-only line, and logs it (`UserSidecarService.OffReadOnlyLines`). Since #884, file-relative header
  marks from versions 2 and 3 are retained at their file lines; member Attribute marks are still dropped. What can still put
  one there is an edit or a reload, because a breakpoint or a bookmark is a bare line number and does not
  follow its line. Once a project is open, only `HeaderRefresher.ShiftMarks` moves marks, by the header's
  change in length, at a designer change, a save or a reload. A designer change and a save add or remove no
  line below the header, so for them that moves each mark with its line, and neither puts one on a read-only
  line. Three routes remain:
  - an edit that adds or removes lines at or above a mark, by any route: Enter, a paste, a cut, Undo, an
    add-in's edit, `set_file_content`. A member's `Attribute` line can move under it: in the probe, one line
    inserted above `Public Function Total()` moved `Attribute Total.VB_Description` under a breakpoint set on
    `Total = 0`;
  - an edit that makes the marked line itself read-only. An orphaned `Attribute` line is not a region, so it
    takes a mark, and it locks the moment it comes to describe a member (`ReadOnlyRegions.Verdict`). A
    procedure attribute describes any procedure declared on the line above, whatever its qualifier names. A
    `VB_Var` attribute describes the variable it names, declared on the line above in the declarations section,
    or the one variable that line declares once the name it carries is declared nowhere in the module, so
    deleting a declaration elsewhere can lock it. An `Attribute` line typed over a marked line directly under
    `Public Sub Halt()` is read-only from its first `.`, and one pasted there is read-only at once. An
    `Attribute` line typed or pasted directly below the header, dotted or not, joins the header, with any blank
    lines between them (`ReadOnlyRegions.HeaderLineCount`), and so does a mark on it; typed, it joins at the
    first character after `Attribute `;
  - a reload after an external change, which moves marks by the change in the header's length alone (for a
    form, its designer block's), so lines the change added or removed below it move under them. VB6 adding
    an `Attribute` line directly under a procedure's declaration puts it on the number of a breakpoint on
    the procedure's first statement.

  The first two routes were probed headlessly through `CodeEditorViewModel` on 2026-10-14, by writing to the
  document rather than typing; the third, and the rest of the second, are read from `FileReloader` and
  `ReadOnlyRegions`. Such a mark is never hit: the debugger passes over an `Attribute` statement, and an
  `Attribute` line in a procedure without its `= value` is a syntax error, so the run never starts (both
  probed through the interpreter's `DebugController` that day). The gutter does not draw it while its run is
  folded, nor while the header is folded unless it is on the header's first line: both margins draw a mark
  only on the first line of each row the window shows, a folded run is hidden in its declaration's row, and a
  folded header is one row that starts at the top of the file. Unfolded, F9, Ctrl+F2 or a click in the gutter
  beside the line removes it, as do Clear All Breakpoints, Clear All Bookmarks, and `set_breakpoints` or
  `set_bookmarks` with it left out; a set that keeps it is accepted, so a read-then-write round trip is never
  refused. Left alone, a member Attribute mark lasts until the project is next opened, when the sidecar's load
  drops it; a header mark saved in version 3 remains counted from the file's top (#884).
- **Why:** those lines never execute, so a breakpoint there is never hit and Run To Cursor would run to the end;
  and folded, the header is one visible line standing for many, so a mark set there would land on a line the
  developer cannot see.
- **Status:** **by design** (#273 tasks 3.12 and 3.17). That an edit or a reload can still leave a mark on a
  read-only line follows from marks not following their lines: nothing in the IDE anchors a mark to its text.

### D25. Line numbers count from the top of the file, header included
- **VB6:** the code window never showed a file's header — a form's designer block, a class's `VERSION`/`BEGIN`
  block, and every `Attribute` line it loaded — so nothing the developer saw was numbered from it. VB6's code
  window had no line-number margin, and its status bar counts the code view as `/make`'s `Line N` does, 1-based
  where that is 0-based, so neither the header nor any `Attribute` line VB6 hid is counted: C1's caret on
  `Total = mSum`, below a hidden run, read `Ln 7`, and V1's read `Ln 4` on the blank line under a described
  variable. A pasted `Attribute` line, which VB6 holds as code until a reload, is counted (V4). All three are
  in [the measurement](vb6-fidelity-oracle/member-attributes-where-vb6-attaches-them-and-what-its-ide-writes.md), *What `Line N` counts, refined*. When task 3.16 recorded this
  row it called the status bar **unmeasured**, because `/make` cannot answer it; phase 4's IDE sessions on the
  oracle have measured it since.
- **HexIDE:** the code window holds the whole file: the header read-only, greyed and, when longer than one
  line, folded, and each member's `Attribute` lines read-only, greyed and folded under the line they describe.
  Every line number it shows or reports counts from the file's first line, every one of those lines included:
  the line-number margin, the status bar, breakpoints, bookmarks, the current-statement bar, the Call Stack,
  Run To Cursor, Set Next Statement, and the automation and add-in surfaces. In a class with the standard
  header the first line of code is line 14.
- **Why:** one numbering is the point of holding the whole file. A second, code-relative numbering anywhere
  would put back exactly the translation the change removed, and two places disagreeing by the header's length
  is the bug class that motivated it (hexide-io/HexIDE#273). VB6's compiler is the one exception: `/make`
  reports a line in its own code view, every `Attribute` line excluded, and that number is converted where the
  log is parsed and nowhere else ([the measurement](vb6-fidelity-oracle/what-make-writes-to-out-and-what-line-n-counts.md)). That conversion is one line off
  below a continued `Attribute` line ([#968](https://github.com/hexide-io/HexIDE/issues/968)).
- **Status:** **by design** (#273 tasks 3.6 and 3.16). A sidecar written before the change counts from the
  first line of code, and is migrated as its project opens, each mark moved down onto the statement it was set
  on (task 3.17). Sidecar version 3 anchors code marks after the full header, so a conflicted external header
  change that never reaches the stores no longer moves those marks on reopen (#884). Its stored offsets are
  converted at the persistence boundary; the stores and every public surface still use file lines. A newly
  created form's header still first appears at its first designer change, save or reload
  ([#885](https://github.com/hexide-io/HexIDE/issues/885)); version 3 can now distinguish its code marks from
  marks already in a header.
- **Numbered D23 when task 3.16 recorded it**, and renumbered in #273's documentation pass: merging main brought
  in main's own D23 (#1011, cited from `VBWindowContext`), which came first.

### D26. A designer change while a run is paused asks nothing, and moves breakpoints the run numbers the old way
- **VB6:** **unmeasured**: whether VB6 lets a form's design be changed in break mode at all, and whether doing so
  asks to reset the project.
- **HexIDE:** nothing stops a designer change during a run: neither the designer nor `add_control` checks for
  one. Since #273 task 3.13 it raises no reset prompt (D2), because a designer change is not a code edit
  (`ResetPromptTests.ChangingAFormsLayoutWhilePaused`). The change reaches the model, the header and, at a
  save, the file; the running form keeps the layout it was loaded with, since `VBLoader` builds it from the
  definition once. Since lines count from the top of the file (D25), a change that adds or removes lines of the
  designer block moves every line of code below it, and `HeaderRefresher.ShiftMarks` moves the document's
  breakpoints with them and `ProjectRunnerService` pushes the moved set to the run. The interpreter still
  numbers its statements from the text it parsed when the run started, so a moved breakpoint is missed or
  stops another statement. The current-statement bar is a bare line number as well (`CurrentLineRenderer`), so
  it is off by the change from that moment, and at every later pause, which the run reports in its own
  numbering. A save whose rendered header has a different number of lines takes the same path
  (`HeaderRefresher.Saved`).
- **Why:** the prompt was exempted because a designer change is the IDE re-rendering the header, not the
  developer editing code; the run's line numbers are the text it parsed, and nothing re-numbers it.
- **Status:** **open**, [#870](https://github.com/hexide-io/HexIDE/issues/870), read from the code and not yet
  reproduced live. It weighs holding the run's numbering until the run ends, re-numbering the run, which is
  hard, and treating a designer change during a run as the code window treats an edit, the likeliest, which
  waits on measuring what VB6 allows in break mode.

---

## Deferred debugger surfaces (not divergences yet — just not built)

Immediate USER-CALL execution (calling a user Sub/Function from the Immediate window — the deadlock-prone part, D15;
assignment/Set landed in P7c, D14); modern per-line conditional breakpoints / hit counts (deferred additive Evolution
item — VB6's watch-based Break-When-True/Changed shipped in P6b). Tracked in
[`MISSING_FEATURES.md`](MISSING_FEATURES.md) and the spec's v2 list (P8). Serious/compiled debugging belongs to a
compiled backend over DAP. *(Landed since this list was written: Watches + Break-When-True/Changed + Data Tips (P6), Run To Cursor
+ Set Next Statement + Immediate assignment/Set (P7), the Locals property/control surface incl. the form-`Me` +
child-control tree (P8 + D7 residual-1/3, D7 now RESOLVED).)*
