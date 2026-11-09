# HexIDE Runtime — the built-in VB6 interpreter

This project is HexIDE's **built-in interpreter**: the thing that runs when you press **F5**. It executes your
VB6/VBA code directly — no compile step, no external toolchain, nothing to install — so you can draw a form, write
a `Sub`, and watch it run, then set a breakpoint and step through it. It is what makes HexIDE usable *out of the
box*.

It is a **demonstrator, not a production runtime.** The goal is faithful *tyre-kicking* — try the language, try a
form, prototype a bit of logic — not to be a drop-in replacement for the real Visual Basic 6 runtime. Understanding
where that line falls is the point of this document.

## How it works

- **It runs your code by walking the syntax tree.** The interpreter parses VB6/VBA to a concrete syntax tree and
  *executes the tree directly*. There is no separate compilation, no generated binary.
- **It re-implements VB6's behaviour; it does not call VB6.** HexIDE does not load `MSVBVM60` or shell out to the
  real runtime — it reproduces the observed behaviour itself. That is why it runs identically on Windows, macOS, and
  Linux.
- **Behaviour is checked against the real compiler.** Where VB6's behaviour is subtle — a numeric result type, an
  overflow code, a rounding rule, a `Format` mask, a coercion — the expected result is pinned against real
  `vb6.exe` (the "fidelity oracle") rather than guessed. The aim is *intended* VB6 behaviour, not VB6's bugs.

**What works today** is a real, usable subset: procedures and the full type system, most of VB6's intrinsic functions
including a complete `Format` engine, the statement set, files, structured error handling (`On Error`/`Err`/`Resume`),
the object-model core (classes, `Implements`, `UDT`s, `Property` procedures, custom `Event`s, `Collection`), and control
arrays (indexing, shared-event `Index`, `Load`/`Unload`). It is enough to run real forms and non-trivial programs.
[`docs/MISSING_LANGUAGE.md`](../../docs/MISSING_LANGUAGE.md) gives the status of every name.

## The three hard caps (permanent, by design)

These are not "not done yet" — they are deliberate boundaries that define what this interpreter *is*.

1. **It runs code; it does not *analyse* it.** The interpreter works at the *syntactic* level plus a runtime
   execution engine. It never builds a **bound/semantic model** of your program. So it does **no** cross-file name
   binding, type inference, semantic error-checking beyond syntax, rename, find-all-references, workspace symbol
   search, or automated refactoring. Producing and reasoning over a proper semantic model is the job of a real
   language engine, delivered over HexIDE's replaceable backend seam — not of this interpreter. (This is also why
   the optional `Option Explicit` undeclared-variable check is **off by default**: a trustworthy version of it needs
   exactly that semantic model.)

2. **It is an approximation, not exhaustive fidelity.** It targets the *common, real* surface of the language and
   will diverge on some edges. When it hits one of its walls it **fails cleanly** (a clear, trappable error) rather
   than silently doing the wrong thing. Hitting a wall is expected — each one marks the boundary where a real engine
   takes over. For production-grade behaviour on every corner of the language, use a real VB6/BASIC compiler.

3. **Classic Microsoft dialects only.** The interpreter targets **VB6, VBA, and VBScript**. It will **never** grow
   the *extended* language surface of a modern BASIC compiler (new keywords, types, operators, or semantics that
   classic VB never had). Feed it such a feature and it fails cleanly and points you at a real compiler — this is a
   permanent wall, not a gap.

It **does not** reimpose VB6's *artificial* limits (module line counts, nesting caps, and the like). It **does**
respect physically-real ones: runaway recursion runs out of stack, and pathologically deep nesting is rejected with
a clean compile error instead of crashing.

## What is permanently out of reach

No language construct. The permanent limit is **binding**: a check that relates two declarations without running
either, such as whether a `Property Let` agrees with its `Property Get`, or whether a `Public` signature exposes a
`Private Type`. VB6 reports those when it compiles. Here the same error is raised when the code runs, where that is
possible, and a path never taken is never checked. The *Walled off* section of
[`docs/interpreter-gaps.md`](../../docs/interpreter-gaps.md) has the list.

## What is still being filled in (deferred, *not* walls)

For contrast, this is ordinary language surface that isn't implemented yet, or not fully:

- **Legacy branching:** `GoSub`/`Return` and computed `On … GoTo` or `On … GoSub` stop the run with a clear error.
  They need a position in a linear statement sequence, which a tree-walk doesn't have.
- **Default members:** #174 for a class, #790 for a control. A class's default member used as a value is refused,
  untrappably, naming #174, and a class with none raises 438, as in VB6. A control is refused in a Let and in a numeric
  conversion, and still gives its .NET type name in a concatenation.
- **Forms that run only in part:** fixed-length strings, `LSet` and `RSet` beyond named Strings and Variants, bang
  access on anything but a `Collection`, indexed `Property Let` and `Set` on a class instance, and type-declaration
  characters (#742).
- **Missing objects and statements:** the `Printer` object.
- **What needs the platform:** COM activation (`New Excel.Application`, `CreateObject` of anything but a known,
  referenced library), `GetObject`, `AppActivate`, `SendKeys` and actually *calling* a `Declare … Lib` function. COM is
  in scope but Windows-gated.

[`docs/MISSING_LANGUAGE.md`](../../docs/MISSING_LANGUAGE.md) says what runs, name by name.
[`docs/interpreter-gaps.md`](../../docs/interpreter-gaps.md) says why something is missing, and
[`docs/vb6-fidelity-oracle/README.md`](../../docs/vb6-fidelity-oracle/README.md) records what was verified against
`vb6.exe`.

## Debugging features permanently out of scope

HexIDE ships a genuine step-through debugger **for this interpreter** — breakpoints, Step In/Over/Out, the Call
Stack, Locals, Watches, data tips, Run To Cursor, and an Immediate window. A few debugging capabilities are
permanently out of scope here by design, because they belong to a real compile/debug backend:

- **True Edit-and-Continue.** You cannot hot-patch a running program's code. Editing while stopped offers to
  **reset and re-run** instead — an honest stand-in for the real thing.
- **Set Next Statement into a nested line.** You can move the next statement among the *top-level* statements of the
  paused procedure, but not to a line nested inside an `If`/`For`/`Do` block. (This is a tree-walker limit;
  arbitrary jump targets need a linearized control-flow model — compiler territory.)
- **Calling your own `Sub`s/`Function`s from the Immediate window.** Evaluating expressions and running assignments
  or `Set` works; invoking user procedures is refused, because it would deadlock the debugger's cooperative
  single-threaded pause. (A trappable, explained refusal — not a crash.)
- **Compiled / production / attach debugging.** Debugging an actual compiled executable is a real debug backend's
  job, not this in-process interpreter's.

Divergences from VB6's *exact* debugger behaviour are catalogued in `docs/debugger-vb6-divergences.md`.

## Why the limits are drawn here

Keeping the interpreter an honest *demonstrator* — rather than a half-built compiler front-end — is a deliberate
design decision. It keeps HexIDE a **shell plus a runnable demonstration** of the language, with the heavy lifting
(a bound semantic model, whole-program analysis, a real compiler, serious debugging) living behind a **replaceable
backend** that a dedicated language engine can provide. The boundaries above are the map of where that handover
happens. When you reach one, that is the interpreter working as intended: it tells you cleanly, and points you at
the right tool for the job.
