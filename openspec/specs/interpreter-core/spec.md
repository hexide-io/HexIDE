# interpreter-core Specification

## Purpose
Define the VB6 language runtime built into the IDE: what it executes, how faithfully, and where it stops.

The interpreter exists so that pressing Run does something real. A VB6 developer evaluating the IDE will type
a few lines and run them within minutes, and an engine that dies on the second non-trivial line answers the
question badly. It is not a reimplementation of the full VB6 runtime and does not try to be — that is the job
of a full language engine behind the replaceable backend seam. What it is, within the surface it covers, is
genuinely faithful rather than approximate, because a runtime that is *nearly* right about arithmetic is
worse than one that is obviously incomplete.

> **Known divergences.** The requirements below state intended behaviour. "Unimplemented language surface SHALL
> fail clearly" is not fully met. In each of the cases below, something HexIDE lacks is raised as an error a
> handler can trap, so under `On Error Resume Next` the statement is skipped and the program carries on:
>
> - **A member on a value that is not a control.** `Clipboard.SetText` and `Collection.Remove` raise a run-time
>   error with no number, and `Err.Number` is left at 0. Tracked in
>   [#257](https://github.com/hexide-io/HexIDE/issues/257).
> - **A member VB6 has, raised as VB6's own error.** `App.LogEvent` and `App.StartLogging` raise 438, which reads
>   as though VB6 had no such member.
> - **`For Each` over anything but an array,** a control array included, raises Type mismatch (13).
> - **A `Class_Terminate` that raises while the failure unwinds** replaces it with its own error
>   ([#767](https://github.com/hexide-io/HexIDE/issues/767)).
>
> Separately, some of the engine's own failures carry no message. Such a run does end, and its dialog says that
> HexIDE does not implement something, but not what ([#181](https://github.com/hexide-io/HexIDE/issues/181)).
>
> The requirement is stated as intended rather than written down to match the code, because the code is wrong
> here, not the intent.

## Requirements

### Requirement: One engine SHALL serve every execution path
Running from the IDE, running under automation, and running in tests SHALL all execute through the same
interpreter, and there SHALL NOT be a separate simplified engine for any of them.

A demonstration engine that diverges from the tested one is a guarantee that the tests are green about
something nobody ships. Sharing one engine means a passing test is evidence about the thing the developer
actually runs, and a bug found by automation is a bug in the product.

#### Scenario: Running the same program three ways
- **WHEN** the same program is run from the IDE, from automation, and from a test
- **THEN** all three produce the same result, because all three execute the same engine

### Requirement: Execution SHALL walk the parse tree directly
The interpreter SHALL execute by walking the concrete syntax tree and applying runtime semantics, and SHALL
NOT construct a bound semantic model or perform static semantic analysis.

This is a boundary rather than a stage. Producing a bound model — cross-file name resolution, type
inference, semantic diagnostics — is what a real language engine does, and starting one here would grow a
half-built compiler frontend that nobody committed to finishing and that would make the backend seam harder
to hand over rather than easier. Runtime scope and runtime types are execution machinery and are in bounds;
analysing code without running it is not.

#### Scenario: A feature that needs whole-program knowledge
- **WHEN** a proposed feature requires resolving or type-checking code without executing it
- **THEN** it is out of scope for this engine and belongs behind the replaceable backend seam

#### Scenario: An error VB6 reports before running
- **WHEN** a condition VB6 detects when compiling is detected here only as the statement executes
- **THEN** it is reported with the same error number, at run time rather than before it
- **AND** the difference in timing is recorded rather than treated as a defect

### Requirement: Cross-module constants SHALL initialise their module on demand
A module-level Const or array bound reading another standard module's Public Const SHALL run that module's
declarations first, once, regardless of project list order or startup entry point. Re-entering a module still
running its declarations SHALL raise "Circular dependencies between modules" as a compile error during execution.
The dependency check SHALL operate per module, including array bounds, and SHALL NOT treat procedure bodies as
module declaration dependencies. Within one module, constant expressions SHALL retain source order.

#### Scenario: A declaration reads a later module's constant
- **WHEN** Module1 declares `Public Const A = Module2.B + 1` and Module2 declares `Public Const B = 2`
- **THEN** A is 3 in either module list order

#### Scenario: A module cycle without a constant cycle
- **WHEN** Module1 declares `Public Const A = 1` and `Public Const C = B + 1`, and Module2 declares `Public Const B = A + 1`
- **THEN** execution refuses the declarations with "Circular dependencies between modules"

### Requirement: Behaviour SHALL be verified against the real product before it is pinned
Where the correct behaviour of a language rule is uncertain, it SHALL be established by running the original
VB6 and observing what it does, and SHALL NOT be settled from documentation or recollection. The verified
result SHALL be recorded so it is not re-derived.

This is not caution for its own sake — checking has repeatedly overturned the obvious answer. A result type,
an overflow boundary, a rounding rule and a coercion have each turned out differently from what the
documentation implied. Pinning a test to a guess is worse than leaving it unpinned, because it makes the
wrong answer permanent and looks like evidence.

#### Scenario: An uncertain semantic rule
- **WHEN** the exact behaviour of an operator, conversion or intrinsic is not certain
- **THEN** it is checked against the original product before a test asserts it

#### Scenario: A verified result
- **WHEN** behaviour has been confirmed against the original product
- **THEN** it is recorded so the next person does not have to repeat the experiment

### Requirement: The engine SHALL reproduce VB6's intended behaviour, including its deliberate quirks
The runtime SHALL implement the semantics VB6 intended rather than the semantics of the host language, and
SHALL reproduce VB6's deliberate behaviour even where it is surprising.

The quirks are the product. A developer who knows that string positions start at one, that rounding goes to
even, that one truncation function floors and the other truncates toward zero, and that a hexadecimal
literal is a two's-complement bit pattern, will read results as correct or incorrect on that basis. An engine
that quietly uses the host language's rules instead is wrong in exactly the places an experienced user
checks first. This does not extend to reproducing VB6's *defects*, which is a separate judgement.

#### Scenario: Rounding a value ending in a half
- **WHEN** a value exactly between two integers is narrowed
- **THEN** it rounds to even, not away from zero

#### Scenario: Indexing a string
- **WHEN** a string position is given
- **THEN** the first character is at position one

#### Scenario: A hexadecimal literal at the top of the range
- **WHEN** a hexadecimal literal fills the width of an integer
- **THEN** it is interpreted as a two's-complement bit pattern rather than a large positive number

#### Scenario: A For body changes its integral numeric counter
- **WHEN** a Long or Variant counter in `For i = 1 To 10` is incremented by the body
- **THEN** Next reads the variable back and adds the step, giving passes 1, 3, 5, 7, 9 and leaving 11
- **AND** resetting the counter to 0 at 3 in `For i = 1 To 3` repeats 1, 2, 3 until execution exits

### Requirement: The value model SHALL implement VB6's scalar types with their real ranges
The runtime SHALL provide VB6's distinct numeric types with their documented ranges and a working date type,
SHALL type literals as VB6 types them, and SHALL raise a trappable overflow when a result or a store falls
outside the target type's range rather than widening silently.

Collapsing the numeric types onto one wide type makes most programs appear to work and a few produce
silently different answers — the worst possible failure distribution. Overflow in particular is load-bearing:
a VB6 program can depend on it being raised, and an engine that quietly promotes instead will compute a
different result rather than reporting a problem.

#### Scenario: Arithmetic exceeding the result type
- **WHEN** an arithmetic result falls outside the range of its result type
- **THEN** an overflow error is raised and can be trapped, rather than the value widening

#### Scenario: Storing a value too large for its declared type
- **WHEN** a value outside the declared type's range is assigned
- **THEN** an overflow error is raised

#### Scenario: Typing an unsuffixed literal
- **WHEN** a whole-number literal is written without a type suffix
- **THEN** it takes the narrowest VB6 type that holds it
- **AND** an unsuffixed literal with a decimal point is a double-precision value

### Requirement: Procedure calls SHALL bind parameters as VB6 does
User-defined procedures SHALL be callable, SHALL pass parameters by reference unless declared otherwise, and
SHALL support by-value, optional and named arguments.

By-reference-by-default is the single most consequential difference between VB6 and the languages most
implementers reach for. A procedure that modifies its parameter is idiomatic VB6, and an engine that passes
by value will run such a program to completion while producing the wrong answer.

#### Scenario: A procedure modifying its parameter
- **WHEN** a procedure assigns to a parameter that was not declared by value
- **THEN** the caller's variable is modified

#### Scenario: A parameter declared by value
- **WHEN** a parameter is declared by value and the procedure assigns to it
- **THEN** the caller's variable is unchanged

#### Scenario: An unspaced parenthesised member-call argument
- **WHEN** a member call statement puts its parenthesised argument directly after the member name
- **THEN** it runs as the spaced form does, with the parenthesised expression passed by value

### Requirement: Name resolution SHALL let user code shadow built-in functions
A name SHALL be resolved as a local variable or array first, then as a procedure defined in the project, and
only then as a built-in function.

VB6 resolves in that order, so a project defining its own function with the name of an intrinsic gets its
own. Resolving to the intrinsic instead would silently call different code than the developer wrote, in a
program that compiles and runs.

#### Scenario: A user function named like an intrinsic
- **WHEN** a project defines a procedure whose name matches a built-in function
- **THEN** calls resolve to the project's procedure

#### Scenario: A private sibling Sub named Reset
- **WHEN** `Class_Initialize` makes a bare `Reset` call and its class defines a private Sub named `Reset`
- **THEN** the interpreter SHALL call that Sub on the current instance

### Requirement: Runtime errors SHALL be trappable and SHALL carry VB6's error numbers
Runtime failures SHALL be raised as trappable VB6 errors carrying the standard error number and description,
and the engine SHALL support VB6's error-handling statements, including continuing at the next statement,
transferring to a handler, retrying, clearing the handler, and raising an error explicitly.

Error handling is not an edge case in VB6 — it is how ordinary programs are written, and the surrounding
tooling depends on it too. An engine whose failures cannot be trapped forces every demonstration to be a
program that never fails.

#### Scenario: Continuing past a failing statement
- **WHEN** the program has asked to continue at the next statement on error
- **THEN** the failing statement is skipped and the error's number is readable afterwards

#### Scenario: Transferring to a handler
- **WHEN** an error occurs while a handler is active
- **THEN** control transfers to the handler, which can inspect the error, retry, or continue

#### Scenario: An arithmetic failure
- **WHEN** an arithmetic operation overflows or divides by zero
- **THEN** it raises a trappable error with VB6's number for that condition

#### Scenario: Diagnostic type names
- **WHEN** an interpreter diagnostic formats a value's type
- **THEN** it SHALL print the inner type name and array suffixes without an internal `Left` or `Right` wrapper
- **AND** missing-member diagnostics SHALL omit the receiver type, retaining the member and, for assignment, variable name

### Requirement: Unimplemented language surface SHALL fail clearly
Where the engine does not implement part of the language, it SHALL fail in a way that identifies what is
missing, and SHALL NOT silently substitute different behaviour. The failure SHALL reach the developer, and an
error handler in the program SHALL NOT be able to trap it. A form holding a control the engine cannot create
SHALL be refused the same way, before any of its controls is placed, naming each control it cannot create.
A control carrying DataSource SHALL likewise refuse the form before placement, naming the control and data source and stating that data binding is not implemented (#859).

The engine is deliberately incomplete, so meeting its edge is a normal outcome rather than an exceptional
one. A clear stop tells the developer they have found the boundary and lets them decide what to do; a silent
approximation tells them the feature works and lets them build on an answer that is wrong. Where an
approximation genuinely is the best available option, it is documented as one rather than presented as
fidelity.

A handler must not see the failure, because all a handler could do with it is carry on past a statement that
never ran, which is the silent substitution this requirement forbids. The failure is a fact about the engine,
not about the program, so it carries no VB6 error number for the program to branch on. And it has to reach the
person running the program: a failure that only the engine's log records is, at the keyboard, a statement that
silently did nothing (hexide-io/HexIDE#754).

A control the engine cannot create is the same edge reached through a form rather than a statement. Leaving it out
runs a different program, whether or not the code names it, so the form is refused rather than built without it
(hexide-io/HexIDE#857). A control nested inside one is named too, even of a kind the engine creates elsewhere,
because nothing inside an uncreatable control is read.

#### Scenario: Using a feature the engine does not implement
- **WHEN** a program uses a language feature the engine does not implement
- **THEN** it stops with an error identifying the unimplemented feature
- **AND** when reached through a statement, the error text SHALL append `at <statement>`, naming the
  innermost failing statement from its own source rather than its caller (#758)

#### Scenario: Under an error handler
- **WHEN** a program that has asked to continue at the next statement on error, or that has a handler active,
  uses a language feature the engine does not implement
- **THEN** it stops just the same, neither skipping the statement nor entering the handler

#### Scenario: Reference cleanup during an unimplemented fault
- **WHEN** an unimplemented feature fails while statement temporaries or procedure-owned references exist
- **THEN** cleanup skips Class_Terminate, preserving the original failure even if that hook would raise a trappable error

#### Scenario: In a running IDE
- **WHEN** a program running in the IDE reaches an unimplemented feature, in an event procedure or in Sub Main
- **THEN** the run ends and the developer is shown that the engine does not implement what the program reached

#### Scenario: A deliberate approximation
- **WHEN** the engine answers with an approximation because the real behaviour is unavailable to it
- **THEN** that approximation is documented at the point of implementation rather than presented as faithful

#### Scenario: A form holding a control the engine cannot create
- **WHEN** a form holding a control the engine cannot create is built to run, as the startup form or because the
  program loads it, under an error handler or not
- **THEN** it is refused before any of its controls is placed, naming every control it cannot create, nested ones
  included

#### Scenario: A form holding a data-bound control
- **WHEN** a form containing a control carrying DataSource is built to run, including a control nested in a Frame
- **THEN** it is refused before any control is placed, naming the control and source and explaining that data
  binding is not implemented
- **THEN** F5 reports the refusal to the developer, and a made program shows it in its error box

### Requirement: Measured empty statements and labelled End If SHALL run

The interpreter SHALL execute the measured leading-colon and spaced-empty-statement forms from issue #223.
It SHALL treat `If True Then:` with no statement on that line as an empty single-line If, allowing the next
line to execute independently. Falling through a label immediately before `End If` SHALL close the block
without calling the label as a procedure. Jumps to labels sharing a construct terminator remain deferred
by the tree-walking execution limit.

#### Scenario: Leading empty statements
- **WHEN** `:  :  : Debug.Print "A"` executes
- **THEN** it prints `A`

#### Scenario: Empty single-line If
- **WHEN** `If True Then:` is followed by `Debug.Print "A"` on the next line without an `End If`
- **THEN** it prints `A`

#### Scenario: Fall through a labelled End If
- **WHEN** a true If prints `T` and closes with `Fin: End If`, followed by a print of `AFTER`
- **THEN** it prints both `T` and `AFTER` without attempting to call `Fin`

### Requirement: Line continuations preserve name boundaries
A single line continuation between identifier fragments SHALL preserve their name boundary rather than
concatenate them. Multi-word keywords
SHALL continue to accept a continuation between their words.

#### Scenario: A split assignment leaves the declared Long untouched
- **WHEN** `Dim value As Long` is followed by `val _` and `ue = 5` on successive physical lines
- **THEN** `Debug.Print value` prints Long 0, as recorded for `continuation-illegal/split-identifier`

#### Scenario: A multi-word keyword spans physical lines
- **WHEN** `Select _` is followed by `Case n` on the next physical line
- **THEN** the interpreter executes a Select Case statement

### Requirement: Expression execution SHALL guard available stack space
The interpreter SHALL probe available host stack space during expression execution and raise trappable
run-time error 28 (Out of stack space) before a long operator chain overflows the process stack.
Module-level constant-expression validation SHALL also probe available stack space and raise error 28
before its recursive walk overflows.
Declaration collection SHALL skip expression subtrees, which contain no declarations or attributes.
This is a host safety bound, not a claim about VB6's acceptance of long physical or continued lines.

#### Scenario: Resuming after a long operator chain exhausts stack space
- **WHEN** a 10,000-term addition chain executes under `On Error Resume Next`, plain or continued
- **THEN** stack exhaustion raises error 28 and execution continues at the next statement without terminating the host

#### Scenario: A long chain on a conditional-compilation line
- **WHEN** an `#If`, `#ElseIf` or `#Const` expression produces a tree deeper than 300 nodes
- **THEN** an iterative check SHALL refuse it through the existing not-an-expression path before recursive evaluation
- **AND** the IDE-side project runner SHALL report the load failure without terminating the host

#### Scenario: A long chain in a module constant on a small stack
- **WHEN** a module-level `Const` holds a 10,000-term addition chain on a 1 MiB thread stack
- **THEN** its constant-expression validation raises error 28 without terminating the host
- **AND** the IDE-side project runner reports the failure and stops the run
