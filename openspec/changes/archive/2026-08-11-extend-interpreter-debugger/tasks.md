# Tasks

Delivered as four phases, each verified over the automation surface before the next began.

## 1. Step Over / Step Out and the Call Stack
- [x] 1.1 Add an explicit activation stack to the interpreter, pushed and popped at the single procedure-invocation sink, removing by identity for async re-entrancy safety.
- [x] 1.2 Capture each activation's depth at push time rather than reading the live stack count.
- [x] 1.3 Generalise the one-shot armed step into a mode (none / into / over / out) with a target depth.
- [x] 1.4 Disarm an unconsumed step at the event-dispatch boundary so it cannot leak into the next event.
- [x] 1.5 Add a call-stack query anchored at the paused frame, walking toward the root.
- [x] 1.6 Add the Call Stack tool window, rebuilt on break and cleared on continue; bind Ctrl+L.
- [x] 1.7 Expose step-over, step-out and the call stack over the automation surface.
- [x] 1.8 Fix the review findings before commit: the depth clamp, the step leak, and a phantom frozen frame in the stack.

## 2. Watches and data tips
- [x] 2.1 Add the Watches window and the Add Watch dialog, with a typed evaluation seam.
- [x] 2.2 Bind Shift+F9 to Quick Watch, pre-filled from the identifier under the caret, and Ctrl+W to edit a watch.
- [x] 2.3 Evaluate break-when-true and break-when-changed watches at the pause-gate.
- [x] 2.4 Apply watch additions and edits to a run already in progress.
- [x] 2.5 Add auto data tips — hover an identifier in break mode to read its value.
- [x] 2.6 Expose adding and reading watches over the automation surface.

## 3. Execution-point control and Immediate mutation
- [x] 3.1 Add Run To Cursor as a one-shot temporary breakpoint at the caret, starting or continuing as needed.
- [x] 3.2 Add Show Next Statement.
- [x] 3.3 Add Set Next Statement for top-level statements of the paused procedure; refuse nested-block targets with an explanation.
- [x] 3.4 Execute assignment and `Set` typed into the Immediate window against the paused frame.
- [x] 3.5 Keep rejecting user-procedure calls, including on an assignment's right-hand side.

## 4. Locals depth
- [x] 4.1 Add a property surface to Locals so a loaded form's `Me` root shows the form window's own properties.
- [x] 4.2 Preserve expansion state across successive breaks.
