# 4. Can't snapshot or drive the IDE while a form is running — **CLOSED** (#340, 2026-09-08)

> **Fixed.** Every window-addressing tool — `take_snapshot`, `dump_visual_tree`, `inspect_element`,
> `interact`, `type_text`, `press_key`, `hover` — takes an optional `window`: `"auto"` (the default,
> unchanged) or `"ide"`. The policy lives in `ForegroundWindow.Pick(scope, …)` beside the rule it
> qualifies, and `take_snapshot` now shares the resolver instead of repeating the selection inline.
>
> The diagnosis below was right, and its most useful part is the *negative* result: activation is not the
> lever. `set_window_state`, breaking before the form is shown, and `activate_document_tab` all succeed and
> change nothing, because the preference is in target *selection*. Three failed workarounds are why the fix
> is a parameter and not a focus call.
>
> **Measured while paused at a breakpoint**, which is the state the whole entry is about:
>
> ```
> dump_visual_tree()              → window: "Form1"   (VBFormRuntime — the old behaviour)
> dump_visual_tree(window:"ide")  → window: "MainWindow", "Project1 - HexIDE [run]"
> hover(<code editor>, window:"ide")
>                                 → tip: total = 42
> ```
>
> That last line is the **debugger's Auto Data Tip**, read as text — the thing gap 7 was filed for in P6c
> and could not verify. Confirmed on screen by the maintainer at the same moment.
>
> A `"form"` scope was considered and left out: `"auto"` already resolves to the running form, and an enum
> cannot say *which* form once a project shows more than one. An unrecognised value is named rather than
> quietly treated as `"auto"` — a caller passing this at all wants a window the default would not give them.


**Symptom.** While a VB6 program is *running* — including when the **interpreter is paused at a breakpoint** —
`take_snapshot` captures the `VBFormRuntime` window, not the IDE main window (`activeDialog` reports the form).
So the IDE's **paused-state editor** — the amber current-statement bar and the red breakpoint gutter as they
appear *during* a break — cannot be captured. (This is the inverse of gap #1: there the modal *over* the form is
invisible; here the *IDE behind* the form is.)

**How it bit.** Verifying the interpreter debugger (Phase 1): the pause was fully confirmable via
`get_debug_state` (Paused / module / 1-based line / reason) and the reveal was confirmable *after* `stop_project`
(the caret lands on the break line — `Ln 6, Col 1` in the status bar), and the red dot is snapshottable while
*not* running — but the **amber bar while paused** could only be confirmed by the **user watching the live IDE**.
The functional path is the same `Stopped`-event handler that `get_debug_state` reflects, so it's provable; the
one *pixel* needs a human.

**Also blocks DRIVING the IDE while a form runs (not just snapshotting).** `dump_visual_tree` walks the *active
window* — which is the running `VBFormRuntime` — so it never returns the IDE's code-editor control, and
`press_key` / `type_text` (which need a path from `dump_visual_tree`) therefore can't target the editor while a
form is up. **How it bit (Phase-3 E&C affordance):** the "edit code while running → VB6 reset-project prompt" can't
be triggered over MCP — sending an editor keystroke needs the IDE editor addressable, which it isn't while the form
is foreground. The prompt logic is VM-tested (`ConfirmResetWhileRunningAsync`, `IsProjectRunning`) and the run-state
(`IsSessionActive`) is runtime-tested, but the live keystroke→dialog step needs the **user** (or a fix below).

**How it bit (Phase-5 Call Stack window).** The populated Call Stack pane (and by extension the populated Locals
pane) while paused can't be pixel-snapshotted for the same reason. Two escape hatches were tried and **both fail**:
(1) breaking early in `Form_Load` **before** the form is shown does **not** hand the IDE the foreground — the
`VBFormRuntime` window already exists and is preferred the moment the run starts, even pre-`Show`; (2)
`set_window_state("Maximized")` on the IDE main window does **not** override `take_snapshot`'s form preference
(`activeDialog` still reports the form). So the earlier "break in `Form_Load`-before-show → IDE foreground" note is
**wrong** — the only reliable IDE-foreground state is `stop_project`, which clears the paused panes. The pane's data
was instead verified via `get_call_stack` (the exact model the pane binds), its behaviour via `step_over`/`step_out`
+ `get_debug_state`, its binding via VM tests, and its **chrome** (title + "Procedure"/"Line" headers) via a
post-`stop_project` IDE snapshot — only the *populated rows* pixel needs a human.

**Root cause.** Same window-selection logic as gap #1: the running form is a separate top-level window that
`take_snapshot` **and** `dump_visual_tree` prefer; there's no way to ask for the IDE main window specifically while
a form is up. Confirmed (P5) that `set_window_state` doesn't change which window is captured — the preference is in
the capture-target selection, not window Z-order/activation.

**And now `hover`, which is what makes this the blocker it is (2026-09-08).** The `hover` action closed the
editor half of gap 7, so **Auto Data Tips are the one paused-state feature that a tool could otherwise verify
outright** — not a pixel, but the tip's actual text. It cannot: `hover` resolves a path against the active
window like everything else, so with a form up there is no path to the code editor to aim at.
`activate_document_tab` was tried as an escape hatch and **also fails** — it succeeds, and the tools still
resolve against `VBFormRuntime` — which is a third confirmation, after `set_window_state` and the
break-before-`Show` attempt, that the preference is in target selection and not in activation.

So the "highest-value dev-server fix" note below is now understating it: this no longer blocks only *pixel*
verification of paused-state panes. It blocks a **textual, assertable** one.

**Workarounds used.** (a) `get_debug_state` for the pause fact; (b) a post-`stop_project` IDE snapshot to confirm
the caret-reveal + Immediate output + tool-pane chrome; (c) the user confirming live paused-state pixels
(amber bar, populated Locals/Call Stack rows).

**Fix consideration.** A `take_snapshot` **and** `dump_visual_tree` target/scope parameter
(e.g. `window: "ide" | "form" | "auto"`), so debugger/IDE-chrome verification can force the main window even while a
form runs. This is now the single highest-value dev-server fix — it blocks pixel-verifying *every* paused-state tool
pane (Locals, Call Stack, and future Watches/data-tips).
