# interpreter-core Specification

## Purpose
Define the VB6 language runtime built into the IDE: what it executes, how faithfully, and where it stops.

The interpreter exists so that pressing Run does something real. A VB6 developer evaluating the IDE will type
a few lines and run them within minutes, and an engine that dies on the second non-trivial line answers the
question badly. It is not a reimplementation of the full VB6 runtime and does not try to be — that is the job
of a full language engine behind the replaceable backend seam. What it is, within the surface it covers, is
genuinely faithful rather than approximate, because a runtime that is *nearly* right about arithmetic is
worse than one that is obviously incomplete.

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

### Requirement: Name resolution SHALL let user code shadow built-in functions
A name SHALL be resolved as a local variable or array first, then as a procedure defined in the project, and
only then as a built-in function.

VB6 resolves in that order, so a project defining its own function with the name of an intrinsic gets its
own. Resolving to the intrinsic instead would silently call different code than the developer wrote, in a
program that compiles and runs.

#### Scenario: A user function named like an intrinsic
- **WHEN** a project defines a procedure whose name matches a built-in function
- **THEN** calls resolve to the project's procedure

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

### Requirement: Unimplemented language surface SHALL fail clearly
Where the engine does not implement part of the language, it SHALL fail in a way that identifies what is
missing, and SHALL NOT silently substitute different behaviour.

The engine is deliberately incomplete, so meeting its edge is a normal outcome rather than an exceptional
one. A clear stop tells the developer they have found the boundary and lets them decide what to do; a silent
approximation tells them the feature works and lets them build on an answer that is wrong. Where an
approximation genuinely is the best available option, it is documented as one rather than presented as
fidelity.

#### Scenario: Using a feature the engine does not implement
- **WHEN** a program uses a language feature the engine does not implement
- **THEN** it stops with an error identifying the unimplemented feature

#### Scenario: A deliberate approximation
- **WHEN** the engine answers with an approximation because the real behaviour is unavailable to it
- **THEN** that approximation is documented at the point of implementation rather than presented as faithful
