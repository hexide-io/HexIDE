# Add a native step-through debugger for the in-box interpreter

> Reconstructed at conversion time (2026-08-11) from the original design document and the
> commit history, when HexIDE's specs were migrated to the OpenSpec format. The work itself
> was designed on 2026-08-08 and delivered across four phases.

## Why

HexIDE's interpreter already executes VB6 line by line and is `async`, but there is no way to stop it and look. A VB6 professional's first reflex is F5, F8, F9 and a breakpoint; without those the interpreter is a black box that either works or doesn't.

The debug UI shell already exists — Debug menu, Run/Break/End toolbar, the commands, and the Locals/Watches/Immediate tool windows — all wired to a stub that says debugging is not implemented. The gap is real behaviour behind it.

A previous decision gated stepping and inspection on an external compiled-execution backend, on the grounds that the interpreter was a throwaway stub. That premise no longer holds: the interpreter has a full object model with classes, properties, lifetime and events, and runs a multi-class program byte-identically to the reference compiler. A debugger over a *running* walk is execution machinery, not static analysis, so it also sits inside the project's CST-not-AST boundary. This proposal reverses that gate for the in-box interpreter; serious compiled debugging remains the external backend's lane.

## What Changes

- A debug controller in the runtime, adjacent to the interpreter, modelled on established debug-protocol *concepts* — breakpoints, a stopped event carrying reason and location, continue/step, scopes and evaluation — but not the wire protocol. One interface, three consumers: the IDE, the automation surface, and any future external backend.
- A cooperative pause-gate at the interpreter's two statement-execution sites, so every executed statement passes through exactly one gate regardless of what triggered it.
- Breakpoints: a per-document line set, a clickable gutter, and cross-session persistence beside the project.
- Break, Continue, End, Step Into, and the `Stop` statement, with the whole program frozen while paused.
- A current-statement bar that reveals and highlights the paused line.
- A Locals window over the paused frame, and an Immediate window that evaluates expressions against it.
- An automation surface mirroring the IDE commands, because a stepping session cannot be verified by screenshot.

## Impact

- New capability: `interpreter-debugger`.
- Closes a catalogued interpreter gap: the `Stop` statement, previously unimplemented.
- Supersedes the recorded decision that debugging is gated on an external backend.
