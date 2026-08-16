# Design: a debugger over a tree-walking interpreter

## The seam

`IDebugController` lives in the runtime, next to the interpreter. Its surface is modelled on the *concepts*
every modern debug protocol shares — breakpoints, a `Stopped` event with reason and location, continue/step,
scopes and variables, evaluate — deliberately **not** on any wire protocol. That gives three consumers one
interface: the IDE debug UI, the automation surface, and, later, an adapter for an external compiled-execution
backend. Shaping the seam this way costs nothing now and avoids a rewrite if that backend ever arrives.

## Why a cooperative pause-gate rather than a cancellation token

The interpreter runs on the UI thread, fire-and-forget async. That rules out suspending a thread, and it makes
a cancellation token the wrong instrument — cancellation unwinds, and a debugger needs to *hold* execution in
place with its state intact so it can be inspected.

A gate that `await`s a `TaskCompletionSource` fits the executor exactly: awaiting yields the UI thread, so the
IDE stays responsive while the program is stopped, and completing the TCS resumes the walk precisely where it
was, with the environment still live. Continue completes it; a step completes it with step mode armed; End
raises an internal control-flow signal that unwinds the walk the same way the existing `GoTo` signal does.

## Two hook points cover everything

The gate is injected at the interpreter's two statement-execution sites — nested statements inside a block, and
top-level statements of a procedure body. Every executed statement passes through exactly one of them, whatever
triggered it: form load, a timer, a control event, a method call, a class initialiser or terminator, a raised
event, or a call embedded in an expression. There is no third path to miss.

Breakpoints match on `(module, line)`. That mapping is exact rather than approximate because the interpreter
parses the same per-module source the editor displays, so a statement's start line *is* the editor line.

## Break mode freezes the whole program

VB6 suspends the entire program at a break, not merely the frame that stopped. Reproducing that matters: without
it, timers and control events would keep firing while the developer reads Locals, and the state under inspection
would shift as they looked at it.

A newcomer activation reaching its first gate while paused is therefore held rather than executed. The frame
that decided to break resumes first; frozen activations wait on a separate resume signal, created once per
pause so that a step cannot orphan or steal them.

## Persistence beside the project, not in the user profile

The original draft put breakpoints in a per-user application-data file. During implementation it turned out the
codebase already persists bookmarks in a per-user sidecar beside the project, so breakpoints joined that store
rather than inventing a second one.

Beside-the-project also gives the developer a choice the profile location does not: they can commit their
breakpoints with the project or ignore them, exactly as modern IDEs allow. VB6 discarded breakpoints on exit,
which is a limitation rather than intended design, so keeping them is a deliberate improvement.

## The `Stop` statement

`Stop` was previously unimplemented and catalogued as a gap. It becomes a source-level breakpoint: under the
debugger it enters break mode; with no controller attached it terminates the program, matching a compiled
executable. Closing the gap falls out of the design rather than needing separate work.

## Edit-and-Continue is a wall, and the honest response is VB6's own prompt

True hot-patching — edit the paused line, continue, keep state — is not feasible here. The execution position
*is* the live host async call stack, parked mid-walk; it is not a re-pointable program counter, so there is
nothing to re-point at freshly parsed code. Reaching it would mean re-architecting to an explicit-stack or
continuation-based virtual machine. That is the external backend's lane.

The response is not to fail silently. VB6 itself showed a prompt — "This action will reset your project. Do you
want to continue?" — whenever it could not apply an edit live. Since the interpreter can apply none live,
surfacing that same prompt for every edit is both truthful and instantly recognisable. It reframes the wall as
familiar behaviour rather than a dead end.

## Rendering Locals

Locals is a tree — arrays by subscript, user-defined types by field, object instances by field — built lazily
with a cycle guard and a depth cap, since a debugger must never hang on a self-referencing structure.

It is rendered with the framework's native tree control rather than the dedicated data-grid-tree control,
because the latter ships under a commercial licence in the version this project targets and the project is
MIT throughout.
