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

### Requirement: Measured compile refusals SHALL stop execution
The interpreter SHALL raise its compile-error exception when execution reaches `Continue Do` or `With Debug`,
with "Syntax error". A scalar assignment through explicit `Me.` to an explicitly Private class field SHALL
raise "Method or data member not found". These refusals are measured on vb6.exe SP6, native and p-code (#2121);
the walk does not check paths it never executes.

#### Scenario: Continue in a post-tested Do loop
- **WHEN** execution reaches `Continue Do` inside `Do ... Loop Until`
- **THEN** execution stops with the compile error "Syntax error"

#### Scenario: Debug used as a With target
- **WHEN** execution reaches `With Debug`
- **THEN** execution stops with the compile error "Syntax error"

#### Scenario: Private field assigned through Me
- **WHEN** a class declares `Private mN As Integer` and its method executes `Me.mN = 5`
- **THEN** execution stops with the compile error "Method or data member not found"
- **AND** `On Error Resume Next` cannot trap that compile error

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
SHALL support by-value, optional, named and ParamArray arguments. Each call SHALL keep only its own bindings
(parameters, locals, return name and `Me`), falling back to the module or instance environment before
project-wide names, without copying the module's bindings.

By-reference-by-default is the single most consequential difference between VB6 and the languages most
implementers reach for. A procedure that modifies its parameter is idiomatic VB6, and an engine that passes
by value will run such a program to completion while producing the wrong answer.

#### Scenario: A procedure without a local binding for a module variable
- **WHEN** a procedure reads or writes a module variable it does not shadow
- **THEN** the lookup reaches the module's existing slot through the enclosing environment
- **AND** that binding is not copied into the procedure's own names

#### Scenario: A procedure modifying its parameter
- **WHEN** a procedure assigns to a parameter that was not declared by value
- **THEN** the caller's variable is modified

#### Scenario: A parameter declared by value
- **WHEN** a parameter is declared by value and the procedure assigns to it
- **THEN** the caller's variable is unchanged

#### Scenario: A ParamArray parameter
- **WHEN** a procedure declared with a ParamArray is called with arguments after its fixed parameters
- **THEN** the ParamArray is a Variant array from 0 holding those arguments, empty when there are none
- **AND** a store through one of its elements modifies the caller's variable, converted to that variable's type

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

### Requirement: Empty parentheses SHALL preserve a declared whole array
A declared array named with empty parentheses SHALL evaluate as the whole array. Array parameters and
ByRef Variant parameters SHALL share it; ByVal Variant parameters and a Let into a Variant SHALL copy it.
A Variant holding an array, named with empty parentheses, SHALL raise "Expected Sub, Function, or Property"
when the statement executes (measured, #2474). The ParamArray form remains deferred until after #2462.

#### Scenario: Passing the whole array
- **WHEN** `S arr()` or `Call S(arr())` passes `Dim arr(2) As Long` to `S(x() As Long)`
- **THEN** `UBound(x)` is 2 and adding 1 to `x(0)` changes the caller's element to 1

#### Scenario: Reading and copying the whole array
- **WHEN** `w = arr()` assigns that array into a Variant
- **THEN** w is a Long() with upper bound 2 and changing its elements leaves arr unchanged

### Requirement: A name read through a type character SHALL be the name's own read
A variable, constant, Enum member, parameter or array read with a type-declaration character (`s$`, `i%`,
`a%(1)`) SHALL be read as the name without it, and SHALL raise VB6's compile error "Type-declaration character
does not match declared data type" when the character is not the name's type. A name nothing declares SHALL be
declared by the read, with the character's type.

Measured on vb6.exe, native and p-code (`docs/vb6-fidelity-oracle/a-name-read-with-a-type-character.md`, #742).

#### Scenario: A String read through its character
- **WHEN** `Dim s As String` holds "x" and the program reads `s$`
- **THEN** the read SHALL give the String "x"

#### Scenario: A character the name's type does not match
- **WHEN** the program reads `v%` for `Dim v As Long`, or `v$` for a Variant
- **THEN** the interpreter SHALL raise the compile error "Type-declaration character does not match declared data type"

#### Scenario: A type that takes no character
- **WHEN** the program reads a Variant, an object, a Byte, a Boolean or a Date through any character
- **THEN** the interpreter SHALL raise the same compile error

#### Scenario: An undeclared name under Option Explicit
- **WHEN** a module with `Option Explicit` reads `u%` and nothing declares `u`
- **THEN** the interpreter SHALL raise "Variable not defined"

#### Scenario: An undeclared name read through a character
- **WHEN** a module without `Option Explicit` reads `z@` before anything declares `z`
- **THEN** the read SHALL give the Currency 0, and a bare `z` after it SHALL read the same Currency

#### Scenario: A ByRef argument written with its character
- **WHEN** `Bump i%` passes an Integer `i` to `Sub Bump(n As Integer)`, which adds 1 to `n`
- **THEN** `i` SHALL be 1 greater afterwards

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

### Requirement: One Next SHALL close every loop it lists
A Next that lists several counters, `Next j, i`, SHALL close one loop per name, innermost first, exactly as
`Next j` followed by `Next i` does, for For and For Each loops alike, as vb6.exe was measured to (#2393). The
interpreter and the LSP server SHALL both accept it. A list with a name missing, `Next j,` or `Next , i`, SHALL
remain a syntax error.

#### Scenario: Two nested loops closed by one Next
- **WHEN** `For i = 1 To 2` encloses `For j = 1 To 2`, and the inner body appends `i & j`, closed by `Next j, i`
- **THEN** the body sees 11, 12, 21 and 22, and both counters are 3 afterwards

#### Scenario: Exit For leaves only the inner loop
- **WHEN** the inner loop of a pair closed by `Next j, i` runs `Exit For`
- **THEN** the outer loop continues with its next pass

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

### Requirement: A declaration's type character SHALL be its As clause
A `Dim`, `Private`, `Public` or `Static` declaration, a parameter, or a Property Get written with `%`, `&`, `!`, `#`,
`@` or `$` after its name SHALL declare Integer, Long, Single, Double, Currency or String, as the As clause the
character stands for would, arrays included. A variable or a parameter with an As clause as well SHALL raise VB6's
compile error. A Property Get's As clause SHALL win over its character. These are measured in the oracle entry "A
declaration written with a type character" (#2409).

#### Scenario: A module-level declaration
- **WHEN** a module declares `Dim n%, s$` and stores 7.5 into `n`
- **THEN** the module loads, `s` is an empty String, and `n` is Integer 8

#### Scenario: A parameter's character
- **WHEN** `Sub TakeI(n%)` is called as `TakeI 2.6`
- **THEN** `n` holds Integer 3

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

### Requirement: Managed referenced libraries

The interpreter SHALL share one library model for controls and managed classes, carrying a GUID, version,
enums and ICSharpProxy factories. Only known project references SHALL activate class libraries, in project
reference order; unknown libraries SHALL retain existing behaviour. New and CreateObject SHALL call the
class factories for separate instances.

#### Scenario: Lazy shared GlobalMultiUse instance
- **GIVEN** a referenced managed library with two GlobalMultiUse classes
- **WHEN** a member of one class is first called
- **THEN** only that class's implicit instance is created
- **AND** subsequent bare and library-qualified calls share its state until Sub Main returns
- **AND** normal return releases implicit instances in reverse creation order within that library
- **AND** End suppresses termination

#### Scenario: Bare member precedence
- **WHEN** a project procedure and referenced GlobalMultiUse classes expose the same bare name
- **THEN** the project procedure wins without creating an implicit library instance
- **AND** otherwise the first reference exposing that member wins ahead of VBA
- **AND** explicit VBA qualification still reaches VBA

#### Scenario: Managed instance lifetime
- **WHEN** an explicit managed instance loses its last runtime reference
- **THEN** its IDisposable host, when supplied, is disposed unless End has ended the run
- **AND** the implicit instance remains held until Sub Main returns

### Requirement: Interpreter parsing uses SLL with an LL fallback
Module, standalone expression and standalone statement parsing SHALL first try SLL prediction with a
bailing error strategy, then retry from the beginning with LL on a cancelled parse or an incomplete standalone entry. Each pass SHALL use
a fresh nesting guard. The LL pass SHALL preserve the entry point's existing error policy: throwing
syntax errors for modules and expressions, and returning null for unparseable standalone statements.

#### Scenario: Long concatenations take the fast path
- **WHEN** a module contains repeated long string concatenations that parse successfully under SLL
- **THEN** the interpreter uses that tree without performing an LL pass

#### Scenario: SLL cannot parse an entry
- **WHEN** SLL cancels parsing a module or Immediate-window entry
- **THEN** LL reparses from the first token with a fresh nesting guard and the original error policy

### Requirement: Procedure activation storage is reclaimed on exit
The interpreter SHALL reclaim storage allocated by a procedure activation after its scope-exit reference
releases, on normal return, early exit, error unwind and parameter-binding failure. Caller storage aliased
ByRef and persistent Static storage SHALL survive the callee's exit. Freed slot numbers SHALL NOT be reused;
exhausting the slot counter SHALL raise error 7 rather than overwrite live storage. Debugger inspection of
freed storage SHALL report it as out of scope without throwing into the UI.

#### Scenario: Repeated calls
- **WHEN** a procedure with parameters and locals is called repeatedly
- **THEN** completed activations leave no allocated slots of their own behind
- **AND** recursive activations retain their slots until each returns

#### Scenario: A retained debugger frame
- **WHEN** the debugger inspects a frame after its procedure returns
- **THEN** freed locals are absent from Locals and a watch on freed storage is out of scope

### Requirement: Err.Source identifies the raising project

A trapped run-time error SHALL report the raising project's Name as Err.Source, without a class
suffix and independently of Title. Err.Raise SHALL retain an explicit source; with no source argument
it SHALL fill an empty Source from the project name and preserve an already set Source. Err.Clear
SHALL empty Source. Error provenance SHALL survive propagation across the managed library boundary.
Native ActiveX DLL activation remains outside the implemented runtime surface.

#### Scenario: A class raises into its caller

- **WHEN** a class in project Probe raises a run-time error trapped by its caller
- **THEN** Err.Source is Probe, even with Title set to Titled App

#### Scenario: A managed library propagates an error

- **WHEN** code in project ProbeDll raises an error into project Probe
- **THEN** the caller's handler sees ProbeDll, or the source explicitly supplied to Err.Raise

#### Scenario: A sourceless Raise follows a manual Source

- **WHEN** Err.Source is Set and Err.Raise 7 supplies no source
- **THEN** Err.Source remains Set

### Requirement: Assignment targets with type characters

The interpreter SHALL resolve an unqualified scalar assignment target with `$`, `%`, `&`, `!`, `#` or `@`
by its bare name. It SHALL check the character against the slot's declared type, raising
"Type-declaration character does not match declared data type" when they disagree, including a Variant.
Without Option Explicit, a first assignment through a character SHALL create a variable of that type.
A For counter, and the name on its Next, written with `%`, `&`, `!`, `#` or `@` SHALL name the variable of the
bare name in the same way, at any scope. These behaviours are measured in the oracle's 2026-10-11 assignment
probes and its 2026-10-27 entry "A For counter written with a type character" (#739).

#### Scenario: A suffixed assignment reaches a declared variable

- **WHEN** a String variable `s` is assigned through `s$ = "x"`
- **THEN** a bare read of `s` returns `"x"`, at module level or locally, with or without Option Explicit

#### Scenario: An implicit suffixed target has the character's type

- **WHEN** an undeclared name is first assigned as `k% = 3` without Option Explicit
- **THEN** a bare read of `k` returns Integer 3

#### Scenario: A counter through its character uses the declared variable

- **WHEN** a variable `c` declared As Long, locally, in the module or as another module's Public, is the counter of
  `For c& = 1 To 2` closed by `Next` or `Next c&`
- **THEN** the body reads 1 and 2 through the bare `c`, and a bare read after the loop returns Long 3

#### Scenario: A counter character the declared type contradicts

- **WHEN** `Dim i As Long` is followed by `For i% = 1 To 2: Next`, or `Dim i As Integer` by `For i = 1 To 2: Next i&`
- **THEN** the statement raises "Type-declaration character does not match declared data type"

### Requirement: The Screen object SHALL answer as VB6's does
`Screen` SHALL be a program global. Its size SHALL be the primary screen in twips, 15 to a device-independent pixel.
`MousePointer` SHALL convert as a control's does, and every form SHALL show it while it is not 0. `ActiveForm` and
`ActiveControl` SHALL be Nothing, with no error, when no form is active. A member a program cannot write SHALL raise
382 or 383, naming it.

Measured on vb6.exe, native and p-code (`docs/vb6-fidelity-oracle/the-screen-object.md`, #2431).

#### Scenario: Centring a form on the screen
- **WHEN** a program reads `Screen.Width` on a screen 1024 device-independent pixels wide
- **THEN** it SHALL read the Single 15360

#### Scenario: The hourglass over a form
- **WHEN** a program sets `Screen.MousePointer = vbHourglass` while a form is shown
- **THEN** the form and every control on it SHALL show the hourglass, until the program sets it to 0

#### Scenario: No form active
- **WHEN** a program with no form shown reads `Screen.ActiveForm` or `Screen.ActiveControl`
- **THEN** it SHALL read Nothing, and raise no error

#### Scenario: A write to a member a program cannot write
- **WHEN** a program writes `s.Width = 100` through `Set s = Screen`
- **THEN** the interpreter SHALL raise 382, "'Width' property cannot be set at run time"

### Requirement: Measured payment, future value and present value calculations

The interpreter SHALL execute the measured positional `Pmt`, `FV` and `PV` calls from
[#2523](https://github.com/hexide-io/HexIDE/issues/2523), preserving their exact Double bits and
measured errors. It SHALL round the base `1 + rate` to Double before retaining higher-precision
intermediates. Unmeasured named calls, argument errors and unsupported calculation domains SHALL
fail clearly as unimplemented, rather than supply an invented VB6 result.

#### Scenario: Ordinary loan payment retains intermediate precision
- **WHEN** a program evaluates `Pmt(0.08 / 12, 120, 10000)`
- **THEN** the result is a Double with bits `C05E54F74E50A768`

#### Scenario: The zero-rate and fractional-period cases execute
- **WHEN** a program evaluates a measured zero-rate or fractional-period call
- **THEN** its exact Double bits agree with the issue's measured row

#### Scenario: Measured invalid payment inputs report their errors
- **WHEN** a program calls `Pmt(0.1, 0, 1000)` or `Pmt(Null, 10, 1000)`
- **THEN** it raises error 5 or 94 respectively

### Requirement: Measured periods, interest, principal and rate calculations

The interpreter SHALL execute every measured positional NPer, IPmt, PPmt and Rate row in
[#2523](https://github.com/hexide-io/HexIDE/issues/2523), preserving exact Double bits and errors.
It SHALL use the fitted per-function spills described in
[Financial precision](../../../docs/financial-precision-2523.md). Unmeasured argument conversions,
named calls and unsupported domains SHALL refuse as unimplemented.

#### Scenario: Beginning-period interest and zero-rate interest
- **WHEN** the program evaluates the measured first-period beginning payment or zero-rate IPmt call
- **THEN** it returns positive zero or negative zero respectively, as recorded in the issue

#### Scenario: Rate depends on its initial guess
- **WHEN** the program evaluates the measured Rate calls with omitted Guess or Guess 0.5
- **THEN** each result preserves the distinct Double bits recorded in the issue

#### Scenario: Invalid periods are measured errors
- **WHEN** the program evaluates a measured invalid-period IPmt or PPmt call, or a measured failing Rate call
- **THEN** it raises error 5
