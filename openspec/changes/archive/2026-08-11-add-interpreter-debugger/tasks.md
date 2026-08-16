# Tasks

Delivered as four phases, one at a time, each verified over the automation surface before the next began —
a stepping session cannot be verified by screenshot.

## 1. Core — the gate, breakpoints, and break mode
- [x] 1.1 Add `IDebugController` to the runtime with state, breakpoint set, `Stopped`/`Continued`/`Terminated` events, and continue/step/pause/stop commands.
- [x] 1.2 Inject the cooperative pause-gate at the interpreter's two statement-execution sites.
- [x] 1.3 Add a breakpoint store: a per-document sorted line set, mirroring the existing bookmark model.
- [x] 1.4 Persist breakpoints in the per-user sidecar beside the project, alongside bookmarks.
- [x] 1.5 Add the breakpoint gutter margin — a red dot per breakpoint line, click to toggle.
- [x] 1.6 Wire Break, Continue and End, replacing the "not yet implemented" stub.
- [x] 1.7 Add the current-statement bar: reveal the module, scroll to the line, paint it.
- [x] 1.8 Implement the `Stop` statement as a break under the debugger, terminate when standalone.
- [x] 1.9 Freeze the whole program while paused — hold newcomer activations at their first gate.
- [x] 1.10 Expose breakpoints, break, continue and debug state over the automation surface.
- [x] 1.11 Address adversarial-review findings: a restart race, teardown ordering, session bleed, and handler leaks.

## 2. Step Into
- [x] 2.1 Add one-shot armed step mode to the controller; the next statement breaks, then disarms.
- [x] 2.2 Bind F8, with start-and-step semantics: idle starts and breaks at the first statement; paused steps; running breaks next.
- [x] 2.3 Create the resume gate once per pause so a step cannot orphan or steal a frozen activation.
- [x] 2.4 Expose step-into over the automation surface.
- [x] 2.5 Remove the procedure-end step-clear after review — it orphaned frozen events and mis-fired on empty handlers. Step is a pure one-shot carrying to the next executed statement.

## 3. Locals
- [x] 3.1 Add a runtime inspector that partitions the paused frame into parameters and locals versus a module root.
- [x] 3.2 Expand user-defined types by field, arrays by subscript, and object instances by field, lazily.
- [x] 3.3 Guard against cycles by ancestor path, and cap depth.
- [x] 3.4 Render with the framework's native tree control, not the commercially licensed data-grid tree.
- [x] 3.5 Rebuild on each break; clear on continue.
- [x] 3.6 Expose locals over the automation surface as a depth-capped tree.

## 4. Immediate — evaluation
- [x] 4.1 Make the Immediate buffer editable; evaluate on Enter against the paused frame.
- [x] 4.2 Accept `?expr`, `Print expr` and a bare expression; print the result on the next line.
- [x] 4.3 Reject calls to user procedures, which would deadlock the suspended execution path.
- [x] 4.4 Report clearly when evaluation is attempted outside break mode.
- [x] 4.5 Expose evaluation over the automation surface.

## 5. Close-out
- [x] 5.1 Record the reversal of the "debugging is gated on an external backend" decision.
- [x] 5.2 Start a living catalogue of deliberate divergences from VB6 debugger behaviour.
- [x] 5.3 Add the edit-during-run reset prompt as the honest Edit-and-Continue affordance.
