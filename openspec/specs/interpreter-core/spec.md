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

### Requirement: ObjPtr SHALL preserve object identity
`ObjPtr` SHALL return Long identities satisfying the relations measured in
[#2622](https://github.com/hexide-io/HexIDE/issues/2622) (measuring PRs #2624 and #2628).
These identities need not be native addresses. Unmeasured non-object operand types SHALL be refused
as not implemented.

#### Scenario: Nothing and live object aliases
- **WHEN** `ObjPtr` reads Nothing, an unset Object or an Object set to Nothing
- **THEN** it SHALL return zero as a Long
- **WHEN** two names reference the same live object
- **THEN** their identities SHALL be equal and non-zero
- **AND** a different live object SHALL have a different identity

#### Scenario: Measured invalid operands
- **WHEN** `ObjPtr(5)` or `ObjPtr("a")` executes
- **THEN** it SHALL raise run-time error 13 for VB6's compile-time Type mismatch

### Requirement: ErrObject assignments and creation SHALL match measured VB6 errors

A variable declared `As ErrObject` SHALL accept Err and Nothing. Assigning a Collection SHALL
raise 13 and preserve the previous reference. `New ErrObject` SHALL raise a trappable run-time
-2147221164, "Class not registered" (VB6 SP6 native/p-code measurements, #2627).

#### Scenario: Refused Collection assignment

- **GIVEN** `e As ErrObject` holds Err
- **WHEN** `Set e = New Collection` executes under `On Error Resume Next`
- **THEN** Err.Number is 13 and `e Is Err` remains True

#### Scenario: ErrObject cannot be created

- **GIVEN** `e As ErrObject` holds Nothing
- **WHEN** `Set e = New ErrObject` executes under `On Error Resume Next`
- **THEN** Err.Number is -2147221164, Err.Description is "Class not registered", and e remains Nothing

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

#### Scenario: Converting a hex or octal String
- **WHEN** CInt, CLng, CByte, CDbl, CCur, CSng, CDec or Val reads a hex or octal String
- **THEN** width follows the unsigned value, independent of leading zeros
- **AND** CInt and Val read signed 16 bits through &HFFFF; the other conversions retain that unsigned value
- **AND** values from &H10000 through &HFFFFFFFF are read as signed 32 bits before range checking
- **AND** above &HFFFFFFFF CInt, CByte and Val retain low 32 bits, CLng raises 6, and the other conversions read the whole value
- **AND** numeric conversions refuse a trailing `&` with error 13 (the literal form remains valid)

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

### Requirement: CallByName SHALL dispatch class and implemented in-box members
`CallByName` SHALL support Collection `Add` with item/key and `Item` with one index through VbMethod,
and `Err.Number` through VbGet, as expressions or statements, bare or VBA-qualified (#2519).
Class instances SHALL support public scalar field Get/Let, Property Get/Let/Set and method calls through the
same procedure binder as direct calls, preserving conversions, Optional, ParamArray and ByRef writeback.
Class object results SHALL be returned for Set; a Collection result used as a value raises 450.
A Sub used as a value SHALL return Empty. Missing or empty class member names and call types 0/16 raise 438;
Get on a method raises 450, too few arguments 449, and too many 450.
Forms/controls, Collection object results, private members, non-scalar fields, field Set/indexing and
unavailable accessors SHALL refuse as not implemented. So SHALL other unmeasured member/call-type/arity
combinations, named/omitted arguments and name/call-type conversions.

#### Scenario: Collection lookup by name
- **WHEN** a Collection receives `CallByName k, "Add", VbMethod, "a", "ka"`
- **THEN** `CallByName(k, "Item", VbMethod, "ka")` returns String `a`
- **AND** Get on Count or Item raises 438

#### Scenario: Class ByRef call
- **WHEN** `n As Integer` holds 4 and `CallByName c, "Twice", VbMethod, n` calls a method that doubles its ByRef argument
- **THEN** the caller's n holds 8

#### Scenario: Object property identity
- **WHEN** `CallByName c, "O", VbSet, k` assigns a Collection to a class object property
- **THEN** `Set got = CallByName(c, "O", VbGet)` returns that same Collection

#### Scenario: Receiver and name errors
- **WHEN** CallByName receives Nothing, a scalar receiver, or a Variant Null name respectively
- **THEN** it raises 5, 424, or 94 respectively

### Requirement: Empty parentheses SHALL preserve a declared whole array
A declared array named with empty parentheses SHALL evaluate as the whole array. Array parameters and
ByRef Variant parameters SHALL share it; ByVal Variant parameters and a Let into a Variant SHALL copy it.
A Variant holding an array, named with empty parentheses, SHALL raise "Expected Sub, Function, or Property"
when the statement executes (measured, #2474). Passing a ParamArray as `T a()` to another ParamArray
SHALL pass one element holding a copy of the array (measured native and p-code, #2474; #2532).

#### Scenario: Passing the whole array
- **WHEN** `S arr()` or `Call S(arr())` passes `Dim arr(2) As Long` to `S(x() As Long)`
- **THEN** `UBound(x)` is 2 and adding 1 to `x(0)` changes the caller's element to 1

#### Scenario: Reading and copying the whole array
- **WHEN** `w = arr()` assigns that array into a Variant
- **THEN** w is a Long() with upper bound 2 and changing its elements leaves arr unchanged

#### Scenario: Forwarding a ParamArray with empty parentheses
- **WHEN** a procedure passes its ParamArray as `T a()` to another ParamArray
- **THEN** the callee receives one element holding the array
- **AND** writing through that element leaves the caller's ParamArray unchanged

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

#### Scenario: The line an error was raised on
- **WHEN** an error is raised on line 20 of a numbered procedure or below it with no number between, or in a procedure
  it called there, and that procedure's handler reads `Erl`
- **THEN** `Erl` SHALL read 20, a Long, until an `On Error` statement, a `Resume` or leaving the handler sets it to 0;
  `Err.Clear` SHALL leave it (measured, #2528)

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

### Requirement: Timer runtime member table

The interpreter SHALL resolve Timer members through the measured table rather than shared visual-control
bindings. Enabled, Index, Interval, Name, Parent and Tag SHALL resolve; Interval SHALL read as Long and
Parent SHALL return the owning form. Left and Top SHALL raise 393 on reads and 382 on writes. Measured
absent members SHALL raise trappable 438 when executed, including early-bound accesses translated to runtime,
except early-bound TabIndex and ToolTipText reads SHALL retain the missing-member compile error (#801 review).

Implementation gap (#801): TabIndex and ToolTipText reads currently use that compile error through Object references too,
where the measured late-bound result is trappable 438.
Unmeasured members and scroll-bar BackColor/ForeColor SHALL refuse as not implemented (#801).

#### Scenario: Timer size is absent
- **WHEN** code reads Timer1.Width or assigns o.Width with o holding a Timer
- **THEN** error 438 is raised and can be trapped
- **AND** the shared property bindings do not expose Width in the Timer's property listing

#### Scenario: Locals omits unavailable Timer and scroll-bar properties
- **WHEN** the debugger expands a Timer or scroll bar
- **THEN** it SHALL omit the Timer's designer-only Left and Top and members outside its measured runtime table
- **AND** it SHALL omit scroll-bar BackColor and ForeColor rather than displaying their refusal as error rows

#### Scenario: Timer extender members remain absent
- **WHEN** code reads Timer1.TabIndex or Timer1.ToolTipText under On Error Resume Next
- **THEN** the missing-member compile error is raised rather than swallowed by the handler

#### Scenario: Timer Interval arithmetic
- **WHEN** a Timer with Interval 1000 is read and multiplied by 40
- **THEN** Interval has type Long and the product is 40000

### Requirement: Code-driven RichTextBox document members

A minimal `RichTextLib.RichTextBox` form SHALL run with Text, TextRTF, SelStart, SelLength, SelText,
SelRTF, the measured Sel character formatting properties and SelAlignment backed by the in-tree RTF
model. Its default member SHALL be TextRTF. Unsupported members and RTF conversions SHALL refuse with
HexIDE's not-implemented error rather than return a guessed value. Runtime access to remaining
RichTextBox properties remains outside this slice (#1100); the first input/drawing slice is specified below.
Non-Null SelColor reads SHALL refuse until
the measured Variant subtype 19 is represented; mixed colours SHALL still read Null.

#### Scenario: Format a selection and read RTF
- **WHEN** a program loads the oracle's Arial RTF, replaces `quick` with `slow`, selects `slow`, and applies
  bold, italic, underline, red, Courier New and 14-point formatting
- **THEN** TextRTF and SelRTF reproduce the recorded whole-document and selection RTF byte for byte
- **AND** a mixed character selection reads Null for properties whose values differ

#### Scenario: Invalid paragraph alignment preserves the prior value
- **WHEN** a program assigns SelAlignment 3, 4, 100, -1 or Null
- **THEN** error 380 is raised and the existing alignment is kept

### Requirement: RichTextBox saved text

RichTextBox SHALL load saved TextRTF through its RTF model and round-trip the measured form and companion
byte for byte. Unsupported inline RTF normalization SHALL hold the designer read-only. Single-line
loading and Text assignment SHALL keep only the first paragraph. Unmeasured empty first-paragraph
RTF output, single-line TextRTF and selection paragraph insertion SHALL refuse.

#### Scenario: Save a RichTextBox form
- **WHEN** the recorded RichTextBox form is saved
- **THEN** extents are recomputed and own properties follow the measured order, with Enabled and TextRTF always written
- **AND** handwritten Locked is ignored; ReadOnly is the persisted spelling
- **AND** TextRTF is a dollar-prefixed companion citation of a four-byte little-endian length and RTF bytes without a terminator

#### Scenario: Load single-line text
- **WHEN** a saved RichTextBox has MultiLine False and TextRTF containing several paragraphs
- **THEN** only the first paragraph survives loading and saving
- **AND** assigning Text containing CR LF at runtime also keeps only its first paragraph

### Requirement: RichTextBox designer placement and editing limits

RichTextLib SHALL be offered in Components and its control in the referenced project's toolbox.
Saved own properties SHALL remain unavailable for designer editing until faithfully modelled.
The designer SHALL draw a labelled, view-only placeholder, including for RTF unsupported at runtime.
RTF edits with a nonzero MaxLength SHALL refuse until their truncation semantics are measured.

#### Scenario: Place a RichTextBox
- **WHEN** a developer places a RichTextBox from the toolbox
- **THEN** its form declares RICHTX32.OCX, its size is 1500 by 750 twips, and its RTF contains its own name as text

### Requirement: RichTextBox typing and character drawing

A running RichTextBox SHALL accept typed text over its selection through the RTF model, preserving
character formatting. Enter SHALL insert CR LF and Backspace immediately after CR LF SHALL remove
both characters. Typing and Enter SHALL raise KeyDown, KeyPress, SelChange, Change, KeyUp; Backspace
SHALL raise its KeyPress after SelChange and Change. Saved ReadOnly/MaxLength and protected runs
SHALL constrain real input. Character runs SHALL draw their face, size, colour, bold, italic, underline
and strikethrough. Selection SHALL draw black with white text and hide on focus loss by default.
Paragraph drawing, formatted clipboard transfer and unmeasured keyboard navigation/drag-selection edges
remain outside this input/drawing slice (#1100). Non-ASCII KeyPress conversion SHALL refuse until measured;
code-driven ANSI text remains supported.

#### Scenario: Type over formatted text
- **WHEN** a user types a character over a bold selection
- **THEN** that character replaces the selection, retains bold, and moves the caret after it
- **AND** SelChange precedes Change

#### Scenario: Backspace after a paragraph break
- **WHEN** a user presses Backspace immediately after CR LF
- **THEN** both characters are deleted and the edit events precede KeyPress


### Requirement: RichTextBox measured selection and editing shortcuts

RichTextBox SHALL select the clicked caret position after MouseDown, MouseUp and Click. A double-click
SHALL select the measured ASCII word with its trailing space after DblClick. Modified clicks, drag selection
and other word boundaries SHALL refuse until their endpoints are measured. Right-click SHALL raise Click.
The platform command shortcuts SHALL select all, insert a tab, copy/paste plain text, align the paragraph
and undo the previous input edit through the RTF model. Bold and underline shortcuts SHALL raise KeyPress
without changing formatting. The control SHALL scroll to keep the caret visible and use the same offset
when hit-testing the mouse. Copying formatted selections and pasting sources carrying non-text formats SHALL refuse rather than flatten text. Undo after a code edit SHALL refuse
until that interaction is measured. A refusal met while handling the keyboard or mouse SHALL reach the running program as
its run-time error, and SHALL NOT leave the input handler, where it would end the IDE.

#### Scenario: A refused key
- **WHEN** the user presses plain Home over a selection set by code
- **THEN** the selection is unchanged and the program stops on its run-time error
- **AND** the IDE keeps running

#### Scenario: Double-click a word
- **WHEN** the user double-clicks beta in `alpha beta gamma`
- **THEN** DblClick precedes SelChange selecting `beta `

#### Scenario: Undo paragraph alignment
- **WHEN** the user right-aligns, then left-aligns a paragraph, then invokes Undo
- **THEN** the paragraph is right-aligned again and SelChange precedes Change


### Requirement: RichTextBox box drawing

RichTextBox SHALL read saved BackColor in hexadecimal or decimal OLE_COLOR form and use it while
enabled. Disabled text SHALL draw in grey on button face.
Re-enabling SHALL restore the saved background and character colours. BorderStyle 0 SHALL omit the
border; a bordered control SHALL use its saved Appearance for flat or inset edges. Enabled SHALL
support Boolean runtime reads and writes. Appearance and BorderStyle SHALL read their saved/default
values and reject runtime assignments with error 383. Runtime BackColor access, paragraph drawing
and character offsets remain outside this slice (#1100).

#### Scenario: Disable and re-enable coloured text
- **WHEN** a RichTextBox with coloured text and background is disabled
- **THEN** the text is grey and the background is button face
- **AND** re-enabling restores the saved background and the document's character colours

#### Scenario: Runtime border assignment
- **WHEN** a program assigns Appearance or BorderStyle
- **THEN** error 383 is raised and the saved value remains unchanged

### Requirement: RichTextBox measured keyboard selection

RichTextBox SHALL extend selection with Shift+arrows and Shift+Home/End using the measured
unwrapped CRLF endpoints. Line-end extension SHALL include the break, including the hidden
final break at Len(Text)+2. A code selection SHALL initially move its upper end; a keyboard
selection SHALL retain its anchor. Plain Left/Right SHALL collapse an existing selection to
the pressed side. Command+Right SHALL only collapse an existing selection. Command+arrows
SHALL use the recorded word stops, with punctuation runs separate and the apostrophe in
`it's` within the word. Wrapped vertical endpoints, lone-break navigation and non-ASCII word
endpoints SHALL refuse until measured. Collapsing a selection containing the hidden final break
and command-left collapse SHALL also refuse until measured.
Without a selection, plain Left/Right/Home/End/Up/Down SHALL move to the corresponding recorded
Shift endpoint. Plain Home/End/Up/Down with a selection SHALL refuse their unmeasured endpoints.

#### Scenario: Move without extending selection
- **GIVEN** the caret is at position 9 in the unwrapped keyboard probe
- **WHEN** plain End is pressed
- **THEN** SelStart is 42 and SelLength is 0

#### Scenario: Extend across an empty paragraph
- **GIVEN** the unwrapped 67-character keyboard probe recorded in the RichTextBox oracle
- **WHEN** Shift+Down is pressed three times from position 3
- **THEN** selection is successively 3+41, 3+61 and 3+66

#### Scenario: Select the hidden final break
- **GIVEN** the caret is at position 67 in that probe
- **WHEN** Shift+Right is pressed
- **THEN** SelStart is 67 and SelLength is 2

### Requirement: RichTextBox measured runtime editing limits

RichTextBox SHALL expose Boolean Locked and SelProtected access through its input guards while allowing
code to edit locked or protected text. Mixed protection SHALL read Null. MultiLine and ScrollBars SHALL
read saved/default values and reject runtime assignments with 383. MaxLength SHALL accept the recorded
0, 5 and 20 values, truncate Text to the limit, and drop SelText appended at a full limit. Unmeasured
limit assignments, reduction below existing text, other code selection replacements with a limit,
and RTF assignments with a limit SHALL refuse with the not-implemented error.

#### Scenario: Assign text at a limit of five
- **WHEN** a program sets MaxLength to 5 and assigns Text `1234567890`
- **THEN** Text reads `12345`
- **AND** appending SelText `abc` at its end leaves the text unchanged

#### Scenario: Protect text from real input
- **WHEN** a program locks the box or protects a selected run and the user types over it
- **THEN** key events are raised without changing the text
- **AND** code can still replace the text

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

### Requirement: A literal's sign SHALL be an operator
The interpreter SHALL type numeric literals from their digits before applying a leading sign, and SHALL
apply negation after exponentiation. It SHALL keep exponent signs within the literal token and parse
subtraction without requiring spaces. Negation of `&H8000` or a typed Integer holding -32768 SHALL raise
Overflow (6), following p-code.

#### Scenario: Negation after power
- **WHEN** `-2 ^ 2` or `Const C = -2 ^ 2` is evaluated
- **THEN** the result is Double -4
- **AND** `(-2) ^ 2` is Double 4

#### Scenario: Digits determine the type before negation
- **WHEN** `-32768` or `-2147483648` is evaluated
- **THEN** the result is respectively Long -32768 or Double -2147483648
- **AND** an explicitly typed Integer constant or assignment can hold -32768

#### Scenario: Subtraction without spaces
- **WHEN** `x-1` is evaluated with Integer x holding 5
- **THEN** the result is Integer 4

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

### Requirement: Err help properties

Err SHALL expose writable HelpFile and HelpContext and read-only LastDllError. Fresh Err and Clear
SHALL provide an empty HelpFile and a zero Long HelpContext. Raise SHALL accept HelpFile and
HelpContext as its fourth and fifth arguments, preserving them through trapping. An omitted
HelpContext SHALL use 1000000 plus a built-in error number, or 1000095 for an application-defined
error. LastDllError SHALL be a zero Long while Declare calls cannot execute. Non-string HelpFile
and unmeasured HelpContext conversions SHALL be refused as not implemented.

#### Scenario: Explicit and omitted help arguments

- **WHEN** Err.Raise 5, , , "h.hlp" executes under On Error Resume Next
- **THEN** HelpFile is h.hlp and HelpContext is 1000005
- **WHEN** Err.Clear executes and then Err.Raise 5, , , , 9 executes
- **THEN** HelpFile is empty and HelpContext is 9

#### Scenario: Writable help fields and a read-only native error

- **WHEN** HelpFile is assigned "mine.chm" and HelpContext is assigned "13"
- **THEN** they read back as mine.chm and Long 13
- **AND** LastDllError reads as Long 0
- **WHEN** LastDllError is assigned 3
- **THEN** execution refuses with Can't assign to read-only property

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

### Requirement: Shell SHALL launch programs with the measured argument and result contract
`Shell` SHALL work as a function and a statement, launch asynchronously and return the process ID as
Double. Explicit styles 0–4, 6 and 7 SHALL select the Windows startup show state; Unix hosts SHALL
accept these styles without changing window state. WindowStyle SHALL convert through CInt.
Empty paths and directories SHALL raise 5, missing programs 53 and Null arguments 94.
Unmeasured non-string PathName conversions, successful launches with omitted WindowStyle, and other
OS launch failures and unmeasured style values SHALL refuse explicitly as not implemented pending measurement (#2522).

#### Scenario: A program launches a helper
- **WHEN** code calls `Shell` with a quoted executable path, arguments and an accepted explicit style
- **THEN** the helper starts and the function returns its positive process ID as Double

#### Scenario: The helper cannot be found
- **WHEN** code calls `Shell("nosuchprogram_x.exe")`
- **THEN** the runtime raises error 53

### Requirement: Reset and FileAttr
The interpreter SHALL close all open files and flush buffered writes on `Reset`, unless a module-local
Sub named Reset handles the call. `FileAttr` SHALL return Long mode values 2 for Output, 8 for Append,
4 for Random and 32 for Binary, defaulting its second argument to 1. An open Input file,
second-argument conversions from types other than Byte/Integer/Long, and named arguments SHALL be
refused as not implemented until measured (#2521).

#### Scenario: Reset releases buffered files
- **WHEN** Reset runs with open files
- **THEN** their writes are flushed, FreeFile returns 1, and their former numbers raise error 52 in EOF and FileAttr
- **AND** closing those numbers again succeeds

#### Scenario: FileAttr arguments
- **WHEN** FileAttr receives a file number
- **THEN** it converts as CInt, including numeric strings, rounding 1.6 to 2 and raising 94 for Null
- **AND** attribute 0, 2 or 3 raises 5, and an unopened number with attribute 1 raises 52

### Requirement: CVErr SHALL construct an Error Variant

The runtime SHALL reproduce Error Variant construction, inspection, conversions, comparisons,
display and refusals measured in [#2520](https://github.com/hexide-io/HexIDE/issues/2520)
and [#2613](https://github.com/hexide-io/HexIDE/pull/2613). It SHALL report VarType 10
and TypeName "Error". Missing SHALL remain a separate omitted-argument sentinel with its
existing measured behaviour. Unmeasured Error conversions and declared stores SHALL refuse
as not implemented.

#### Scenario: Explicit conversion differs from an implicit store
- **WHEN** a Variant holds CVErr(5)
- **THEN** CLng returns Long 5 and CStr returns "Error 5"
- **AND** assigning it to a Long or String raises error 13 without changing the destination

#### Scenario: Constructing an Error value
- **WHEN** CVErr receives 1.5 or 2.5
- **THEN** it returns Error 2
- **AND** CVErr(65536) raises error 5

#### Scenario: The follow-up explicit conversions
- **WHEN** a Variant holds CVErr(5)
- **THEN** CBool, CByte, CSng and CCur return True, Byte 5, Single 5 and Currency 5
- **AND** CDec and CDate raise error 13

#### Scenario: An Error selector compared with a number
- **WHEN** a Variant holding CVErr(5) reaches Case 5 under On Error Resume Next
- **THEN** the comparison raises 13 and runs that Case's body, retaining Err.Number after End Select

#### Scenario: Displaying an Error value
- **WHEN** Print # or Write # receives CVErr(5)
- **THEN** Print # writes "Error 5" and Write # writes "#ERROR 5#"
- **AND** implicit string coercion still raises 13

### Requirement: Forms SHALL hold the program's loaded forms as VB6's does
`Forms` SHALL be a program global, reached where nothing the program declares claims the name. It SHALL hold the loaded
forms in the order they loaded, a form from the start of its own Load to the end of its own Unload. Its index SHALL be a
position from 0, converted as CInt converts, with no keys: any other String and Null SHALL raise 13, and a position out
of range 9.

Measured on vb6.exe, native and p-code (`docs/vb6-fidelity-oracle/the-forms-collection.md`, #2496).

#### Scenario: Closing every form from the main form's Unload
- **WHEN** Form1's `Form_Unload` runs `For Each f In Forms: Unload f: Next` with Form2 and Form3 loaded
- **THEN** Form2 and Form3 SHALL each raise QueryUnload and Unload, the `Unload f` that reaches Form1 SHALL do nothing,
  and the run SHALL end once Form1's Unload has returned

#### Scenario: Unloading by index from 0
- **WHEN** a program with Form1, Form2 and Form3 loaded runs `For i = 0 To Forms.Count - 1: Unload Forms(i): Next`
- **THEN** Form1 and Form3 SHALL unload, `Forms(2)` SHALL raise 9, and Form2 SHALL stay loaded

#### Scenario: A form reloaded goes at the end
- **WHEN** Form2, Form1 and Form3 have loaded in that order, and Form2 is unloaded and loaded again
- **THEN** `Forms` SHALL hold Form1, Form3, Form2

#### Scenario: For Each walks the forms held when it began
- **WHEN** a For Each over `Forms` unloads the last form on its first pass, and loads Form4
- **THEN** it SHALL still hand out the unloaded form when it reaches it, and SHALL NOT reach Form4

### Requirement: App logging SHALL implement the measured file modes
App.StartLogging and App.LogEvent SHALL implement the file output and mode/path values measured in
[#2552](https://github.com/hexide-io/HexIDE/issues/2552). App.LogMode SHALL be a Long and App.LogPath a String;
LogPath assignment SHALL reject the write as read-only. Default and NT logging SHALL deliberately write
no operating-system event, while retaining the measured values. Unmeasured modes, conversions, encoding
and file-failure semantics SHALL fail explicitly as not implemented.

#### Scenario: File logging appends
- **WHEN** a program selects mode 2 and logs a measured event type
- **THEN** the file receives the measured padded severity, application path, integer thread id, message and CRLF
- **AND** an existing file is appended to

#### Scenario: Overwrite flag disables logging
- **WHEN** a program selects mode 18 and logs an event
- **THEN** LogMode reads Long 1 and the existing file remains unchanged

#### Scenario: Default logging selects NT without a system side effect
- **WHEN** a program logs an event without StartLogging
- **THEN** LogMode changes from Long 1 to Long 3 and LogPath remains empty
- **AND** no operating-system event is written

### Requirement: FormatDateTime SHALL use the program's locale
`FormatDateTime(Date, [NamedFormat])` SHALL return a String using `VbLocale`: omitted or 0
uses CStr's general date rendering, 1 the long date, 2 the short date, 3 the long time, and
4 24-hour hours and minutes. It SHALL accept the date serials, date text, Empty, Null and
True measured in [#2516](https://github.com/hexide-io/HexIDE/issues/2516).
NamedFormat SHALL convert as CInt does and reject formats outside 0–4 with error 5.

#### Scenario: Measured en-GB date and time
- **WHEN** `FormatDateTime(#2026-10-28 14:05:09#)` runs in the measured en-GB regional settings
- **THEN** it returns the String `28/10/2026 14:05:09`
- **AND** format 4 returns `14:05`

#### Scenario: Null date and invalid format
- **WHEN** the date argument is Null and the format is omitted
- **THEN** the result is an empty String
- **WHEN** a date is supplied with NamedFormat Null or `"x"`
- **THEN** the error is 94 or 13 respectively

### Requirement: CommonDialog uses managed colour and print dialogs
The runtime SHALL route ShowColor and Action 3 to the host's managed colour chooser for measured Flags 0
and 1, and ShowPrinter and Action 5 to the host's managed print dialog for measured Flags 0 with default
page range and portrait orientation, accepting Copies 1 or 3. Unmeasured configurations and native printer
DC requests SHALL be refused as not implemented. Actual printing remains not implemented.

#### Scenario: Accepting the print dialog
- **WHEN** the user accepts the supported print configuration
- **THEN** Copies is 1, Orientation is 1 and hDC reads Long 0, with Flags unchanged
- **AND** no print job is submitted

#### Scenario: Cancelling either dialog
- **WHEN** the user cancels the colour or print dialog
- **THEN** its result properties are unchanged
- **AND** CancelError True raises 32755

### Requirement: Measured application status members

The interpreter SHALL expose App.HelpFile, App.StartMode, App.TaskVisible and IMEStatus with the
values and types measured in [issue #2551](https://github.com/hexide-io/HexIDE/issues/2551).
It SHALL refuse unmeasured IMEStatus arguments, nonempty project HelpFile values and
HelpFile/TaskVisible conversions as not implemented.

#### Scenario: Application status is read and changed
- **GIVEN** no project help file is named, App.HelpFile starts as String "" and App.TaskVisible as Boolean True
- **WHEN** a program reads App.StartMode, assigns "probe.hlp" to App.HelpFile and False to App.TaskVisible
- **THEN** the values are Integer 0, String "probe.hlp" and Boolean False respectively
- **AND** IMEStatus returns Long 0 in the no-IME host
- **AND** assigning App.StartMode raises the read-only compile diagnostic as the statement executes

### Requirement: Width controls sequential Print item wrapping

The interpreter SHALL implement the `Width #` cases measured in
[#2554](https://github.com/hexide-io/HexIDE/issues/2554): CInt conversion, widths 0–255,
error 5 for 256 and -1, and error 52 for an unopened number. Zero SHALL remove the limit.

#### Scenario: Items exceed a width of five
- **WHEN** `Width #1, 5` precedes `Print #1, "ab"; "cd"; "ef"` on an Output file
- **THEN** the file SHALL contain `abcd`, CRLF, `ef`, CRLF
- **AND** an oversized item SHALL stay whole, starting a new line if the column is nonzero
- **AND** numeric padding SHALL count toward the width

#### Scenario: Position and statement boundaries
- **WHEN** a comma moves beyond the width
- **THEN** Print SHALL start a new line
- **AND** a trailing semicolon SHALL preserve the column for the next Print
- **AND** Write SHALL retain its existing unwrapped output

#### Scenario: Other measured modes
- **WHEN** Width is used on an open Binary or Input file
- **THEN** it SHALL accept the measured width of five without error
- **AND** no effect in those modes is specified by this measurement

### Requirement: File locks within a program

The interpreter SHALL implement Lock and Unlock for a whole file, a single record, an inclusive
`start To end` range, and `To end` with start 1, in accordance with the native and p-code measurements
in [#2553](https://github.com/hexide-io/HexIDE/issues/2553). Binary ranges SHALL count bytes and Random
ranges SHALL count records of the file's Len; sequential ranges SHALL cover the whole file. The
runtime SHALL refuse a duplicate lock or non-exact unlock with error 70, an invalid range with 63,
and an unopened number with 52. Unmeasured range conversions from types other than Byte, Integer or
Long SHALL be refused as not implemented. Locks are scoped to this program; cross-process locking is not provided.

#### Scenario: Another file number reads a locked range
- **GIVEN** two Binary handles on one file and `Lock #1, 2 To 4`
- **WHEN** `Get #2, 4, b` executes
- **THEN** it raises error 70, while `Get #1, 4, b` may read its own lock
- **AND** `Unlock #1, 2 To 3` raises 70 while `Unlock #1, 2 To 4` succeeds

#### Scenario: Closing the owner releases locks
- **GIVEN** two handles on a file and a range locked by number 1
- **WHEN** number 1 closes
- **THEN** number 2 can read the released range

### Requirement: Form termination order at the run's end

At a normal run's end, the program SHALL terminate remaining form instances in the order the project lists
its forms, and newest instance first within each form, as [#2496 measured](../../../docs/vb6-fidelity-oracle/the-forms-collection.md#the-terminates-at-the-runs-end).

#### Scenario: Creation order differs from project order

- **GIVEN** the project lists Form3, Form1, Form4, Form2
- **WHEN** instances are made in the order Form4, Form2, Form1, Form3, then a second Form2
- **THEN** termination runs Form3, Form1, Form4, the second Form2, then the first Form2

### Requirement: Mid statement spellings SHALL overwrite the existing string
`Mid` and `Mid$` statements SHALL replace at most the supplied length, replacement length and remaining
target length, without growing the target. A Variant target holding an Integer SHALL become a String. A Null target
or replacement SHALL raise 94 without changing the target. `MidB` and `MidB$` statements SHALL overwrite
bytes of the target's low-byte-first UTF-16 representation, preserving all untouched bytes and the total
character length, including when the write crosses character boundaries or has an odd byte count.
These outcomes are measured on vb6.exe SP6, native and p-code ([#2623](https://github.com/hexide-io/HexIDE/issues/2623)).

#### Scenario: A character write is clamped
- **WHEN** `s` is `"abcdef"` and `Mid$(s, 5) = "WXYZ"` runs
- **THEN** `s` is `"abcdWX"`

#### Scenario: A byte write crosses characters
- **WHEN** `s` is `"abcdef"` and `MidB(s, 2, 2) = "X"` runs
- **THEN** its character units are 22625, 0, 99, 100, 101, 102

#### Scenario: A Null target is preserved
- **WHEN** `v` holds Null and `Mid$(v, 1, 1) = "9"` runs
- **THEN** error 94 is raised and `v` remains Null
