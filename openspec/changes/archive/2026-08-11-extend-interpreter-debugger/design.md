# Design: extending the debugger to Step Over, watches and mutation

## The interpreter needed a real activation stack

Step Into needs no notion of depth — break at the next statement, wherever it is. Step Over and Step Out do:
"run until we are back at this frame or shallower" is meaningless without knowing which frame we are in.

The interpreter had no activation stack of its own; it reused the host call stack, which is invisible to it.
This change adds an explicit stack of activations, pushed and popped at the single point where a procedure is
invoked, removing by identity rather than by position so that re-entrant async activations cannot pop each
other.

Each activation captures its depth **at push time** rather than reading the live stack count. That distinction
is load-bearing: an overlapping event frame arriving mid-step would otherwise change the count underneath the
stepper and desync it. With a captured depth, Step Over breaks when the frame depth is at or below the target,
and Step Out when it is strictly below.

## One-shot step becomes a small state machine

The first release's armed-step flag generalises to a mode — none, into, over, out — plus a target depth. A step
that is armed but never consumed is disarmed when the event-handler chain returns to idle, so it cannot leak
into the next unrelated event.

## The call stack is anchored, not global

The window walks from the paused frame toward the root rather than listing every live activation. An event
frozen by break mode after the paused frame began is deliberately excluded: it is not part of how execution
reached this point, and showing it would imply a causal chain that does not exist.

## Break-type watches pay for themselves at the gate

An expression watch only needs evaluating when the program stops. A break-when-true or break-when-changed watch
must be evaluated at every statement, which means routing every statement through the slower gate path. That
cost is accepted and confined: a run carrying no break-type watches is unaffected.

## Immediate assignment, and the wall that stays

Assignment and `Set` now execute against the paused frame. Calling a user `Sub` or `Function` from the Immediate
window remains rejected, including on the right-hand side of an assignment: user code would have to run on the
very execution path that is suspended, and the paused gate would deadlock against itself.

## Set Next Statement is bounded by the walker

The top-level body of a procedure is addressable — it is a loop over statements, so the position can be moved.
Statements inside `If`, `For`, `Do` or `Select` blocks are not: they execute by recursive descent in the host
language, and there is no way to re-enter that descent at an arbitrary point without a linearised control-flow
graph. The move is therefore refused with an explanation rather than half-working, and the limit is recorded as
a known divergence from VB6, which allowed the move anywhere in the procedure.
