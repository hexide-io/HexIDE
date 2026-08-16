# Extend the interpreter debugger to the full VB6 surface

> Reconstructed at conversion time (2026-08-11) from the original design document and the
> commit history, when HexIDE's specs were migrated to the OpenSpec format. The work itself
> ran from 2026-08-09 across four phases.

## Why

The first debugger release made the interpreter inspectable — breakpoints, break, Step Into, Locals and an evaluating Immediate window. It stopped short of the surface a VB6 professional actually uses in a session.

Four gaps dominated. Without Step Over, every call has to be stepped through line by line. Without a call stack there is no answer to "how did execution get here". Without watches the developer re-types the same expression at every break. And an Immediate window that evaluates but cannot assign is half a window — poking a value and continuing is a core VB6 debugging move.

Each was deferred deliberately rather than overlooked: the first release existed to prove the pause-gate and the seam. Extending them is now mechanical rather than exploratory.

## What Changes

- Step Over and Step Out, which require the interpreter to gain an explicit activation stack — it previously reused the host call stack and so had no notion of frame depth.
- A Call Stack window, anchored at the paused frame.
- Watches: expression watches evaluated at each break, plus break-when-true and break-when-changed watches evaluated at the pause-gate.
- Data tips — hover an identifier while paused to read its value.
- Set Next Statement and Run To Cursor.
- Immediate assignment: an assignment or `Set` typed while paused now executes and mutates the paused program.
- Locals gains a property surface and preserves expansion state across breaks.

## Impact

- Modifies `interpreter-debugger`: stepping, the Immediate window, the keymap and the automation surface all widen.
- The Immediate window's read-only constraint is lifted; calls to user procedures remain rejected.
- Break-type watches evaluate at every statement, which slows a run that carries them.
