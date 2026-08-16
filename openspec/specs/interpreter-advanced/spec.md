# interpreter-advanced Specification

## Purpose
Define the interpreter's object model — multiple modules, user-defined types, classes, properties and events —
and, more importantly, define where it stops and why stopping there is deliberate.

These constructs are what separates a program from a script. A large share of real VB6 is built on a
collection of objects with properties and events, so an engine without them can run examples but not
anything a developer recognises as their own code.

This capability carries the scope statement for the whole interpreter, because this is where the pressure to
keep going is strongest. Everything below the object model looks like a missing feature; almost none of it
is. The rules that govern *what does not get built* are requirements here in the same way the behaviour is,
and they are expected to hold after the project is public and taking contributions — which is precisely when
they will be tested. Behavioural fidelity within the implemented surface, and the rule that uncertain
semantics are verified against the original product rather than guessed, are specified in `interpreter-core`
and apply here unchanged.

## Requirements
### Requirement: The interpreter SHALL remain an approximation by design
The interpreter SHALL be maintained as an out-of-the-box demonstrator rather than developed toward parity
with a production VB6 runtime, and changes that increase completeness at the cost of that character SHALL be
declined on principle rather than on quality.

The reason to write this down is that every individual step toward completeness looks reasonable. Exhaustive
quirk coverage, deterministic reference counting, a bytecode virtual machine, static checking — each is
defensible alone, and together they are a second-rate reimplementation of something another engine already
does properly behind the replaceable backend seam. The triage question is therefore whether a change keeps
this a simple demonstrator, not whether it makes it more correct; a change can be entirely correct and still
be out of scope.

#### Scenario: A contribution that improves fidelity but grows the engine
- **WHEN** a change would move the engine toward being a production runtime
- **THEN** it is declined on scope, independently of its quality

#### Scenario: A capability that belongs to a real engine
- **WHEN** a capability requires whole-program analysis, a compilation stage, or an execution model this
  engine does not have
- **THEN** it belongs behind the replaceable backend seam rather than here

### Requirement: The interpreter SHALL target the classic Microsoft dialect family only
The interpreter SHALL implement the language as shipped by Microsoft — VB6, VBA and VBScript — and SHALL NOT
implement language extensions introduced by other implementations of the language beyond that family.

This is a wall of principle, not of difficulty. Another implementation's extensions are that project's
reason to exist and the thing people pay for; implementing them would take something that is not ours to
take, from a project this one would rather see succeed and expects to depend on. The test is simply whether
a construct shipped in a Microsoft-era release. Presentation is part of the requirement: the extended
surface is not advertised as compatible, and no part of it appears as planned work.

#### Scenario: A construct from another implementation's extended surface
- **WHEN** a program uses a language construct that no Microsoft-era release shipped
- **THEN** the engine stops with an error identifying it as belonging to another implementation
- **AND** it names that implementation as the place to run the program

#### Scenario: Describing what the interpreter supports
- **WHEN** the interpreter's language support is described
- **THEN** it claims only the classic dialect family, and lists no extended-surface feature as planned

### Requirement: Unimplemented constructs SHALL be documented as boundaries
Each construct the engine does not implement SHALL be recorded as a documented boundary stating where the
demonstrator ends, rather than as outstanding work.

The distinction is not presentational. A backlog implies the list should shrink and that its length is a
measure of failure; a boundary map is a deliverable that tells a developer exactly when to reach for a real
engine, which is more useful than a partial implementation that fails somewhere they cannot predict. It also
makes the deferrals coherent — someone can read the whole edge at once instead of discovering it one crash
at a time.

#### Scenario: Meeting a documented boundary
- **WHEN** a program uses a construct on the boundary map
- **THEN** it fails clearly, and the boundary map already says so

#### Scenario: Recording a new boundary
- **WHEN** a construct is found to be out of reach
- **THEN** it is added to the boundary map with what it is, rather than minimised

### Requirement: Names SHALL resolve across modules at execution time
A program SHALL be able to span multiple standard and class modules, with names resolved against a runtime
registry as execution reaches them, and SHALL NOT depend on any resolution performed before execution.

Resolving at execution is what an interpreter does, and it keeps this side of the boundary: a registry
consulted as a call happens is execution machinery, whereas the same information computed ahead of time in
order to check a program would be the bound model this engine deliberately does not build. The visible
consequence is that a name or type error appears when the statement runs, with VB6's error, rather than
before the program starts.

#### Scenario: Calling across modules
- **WHEN** a procedure in one module calls a procedure declared in another
- **THEN** the call resolves at execution time and runs

#### Scenario: A name that does not resolve
- **WHEN** a name cannot be resolved
- **THEN** the failure is raised as a runtime error when that statement executes, not before the program runs

### Requirement: User-defined types SHALL have value semantics
A user-defined type SHALL be copied on assignment and when passed by value, so that modifying one copy does
not affect the other.

VB6 user-defined types are values, and code relies on it — assigning a record to take a working copy is
ordinary practice. An implementation that shared a reference instead would run such a program and silently
corrupt the original, which is the failure mode hardest to attribute to the engine rather than to one's own
code.

#### Scenario: Assigning a user-defined type
- **WHEN** one user-defined-type value is assigned to another
- **THEN** the target is an independent copy, and later changes to it do not affect the source

### Requirement: Classes SHALL support creation, lifetime callbacks, properties and events
The engine SHALL support declaring class modules, creating instances, running initialization and termination
callbacks, defining properties with distinct read, assign and object-assign forms, and declaring, raising and
handling events.

These are the constructs the object idiom is made of, and they are not separable in practice: a class without
a termination callback cannot express cleanup, and properties without the distinction between assigning a
value and assigning an object reference get the common case wrong in a way that only shows up with objects.

#### Scenario: Creating and releasing an instance
- **WHEN** an object is created and later goes out of reference
- **THEN** its initialization callback runs at creation and its termination callback runs when the last
  reference is released

#### Scenario: Assigning a value versus an object
- **WHEN** a property is assigned
- **THEN** the value form and the object-reference form are distinguished as VB6 distinguishes them

#### Scenario: Raising an event
- **WHEN** a class raises an event and a handler is bound to that source
- **THEN** the handler runs synchronously before the raising procedure continues

### Requirement: Object lifetime SHALL follow reference counting, including its consequences
Termination SHALL be driven by reference counting, and a cycle of references SHALL NOT be collected.

Reference counting is what makes termination deterministic, which is the property VB6 code depends on when
it releases a resource in a termination callback. Its known consequence is that a reference cycle never
reaches zero and its members never terminate. That is left as it is, because it is what VB6 does — adding
cycle collection would be both a large piece of machinery and a divergence from the product being
reproduced.

#### Scenario: Releasing the last reference
- **WHEN** the last reference to an object is released
- **THEN** its termination callback runs at that point rather than at some later time

#### Scenario: A cycle of references
- **WHEN** two objects reference each other and both go out of scope
- **THEN** neither terminates, matching the original product

### Requirement: Limits SHALL be the real ones, reported as VB6 errors
Where a program exhausts a genuine physical resource, the engine SHALL report it as the corresponding VB6
error rather than terminating, and SHALL NOT impose limits that exist only as artefacts of the original
product's era.

The two halves are easy to confuse. A finite call stack and a finite address space are real, and VB6 has
errors that name them, so hitting one should look to the program exactly as it looked in VB6. A cap on
nesting depth or module length is not real — it was an implementation limit of a 1998 product, and
reimposing it would be reproducing an inconvenience rather than a behaviour.

#### Scenario: An era-specific limit of the original product
- **WHEN** a program exceeds a limit that existed only because of how the original product was built
- **THEN** the engine does not impose it

#### Scenario: Exhausting a real resource
- **WHEN** a program exhausts a genuinely finite resource
- **THEN** the condition is reported as the VB6 error for it, so the program can trap it
