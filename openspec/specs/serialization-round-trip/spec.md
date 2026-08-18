# serialization-round-trip Specification

## Purpose
Define what the IDE guarantees when it reads a project's files and writes them back.

This is the promise that decides whether the IDE can be pointed at work that matters. A developer opening a
real project is not evaluating a feature list — they are asking whether it is safe to press Ctrl+S. Real
projects are full of content the IDE has no model for: third-party component properties, add-in metadata,
binary resources, whole component types it has never heard of. If any of that is quietly dropped on save,
the damage is invisible at the moment it happens and discovered later, in the original product, on a file
that no longer opens.

The rule that follows from that is not "save everything correctly" — no reimplementation of a
twenty-year-old file format achieves that on the first pass. It is that the IDE must never write a file it
cannot reproduce. Preservation where it understands the content, and refusal where it does not.

> **Known divergence — this capability is not met today.** The requirements below state the intended
> contract. Against VB6's own shipped sample projects, no file currently survives a save byte-identically,
> and **6 of 22 are held read-only** by the refusal requirement rather than saved. Tracked as an epic in
> [#21](https://github.com/hexide-io/HexIDE/issues/21), with narrower cases in
> [#15](https://github.com/hexide-io/HexIDE/issues/15) and
> [#22](https://github.com/hexide-io/HexIDE/issues/22). The requirements are stated as intent rather than
> rewritten to describe the code, because the code is incomplete here, not the contract. The refusal and
> read-only requirements *are* met, and are what makes the gap safe to ship with.
>
> That read-only count was 12 until menu hierarchies began surviving a round-trip
> ([#83](https://github.com/hexide-io/HexIDE/issues/83)). The six that remain are controls nested inside a
> container ([#84](https://github.com/hexide-io/HexIDE/issues/84)); none of them contains a menu. The
> number is gated by a corpus test, so it can fall but not quietly rise.

## Requirements
### Requirement: Content the IDE does not understand SHALL survive a round-trip
Where a file contains content the IDE has no model for — a property, a property block, an entire component
type, a project key, or a trailing extension section — that content SHALL be preserved and written back.

This is the whole point. A file format this old is defined as much by what accumulated in it as by what was
specified: every add-in, every third-party control, every tool that ever touched the project left something
behind. An IDE that keeps only what it recognises is not reading a project, it is rewriting it as its own
subset — and the developer discovers which parts by finding out what stopped working.

#### Scenario: A property on a component the IDE does model
- **WHEN** a component carries a property the IDE does not recognise
- **THEN** the property is written back with the component

#### Scenario: An entire component type the IDE does not model
- **WHEN** a file contains a component of a type the IDE has no model for
- **THEN** the component is written back in place rather than dropped

#### Scenario: A section left by another tool
- **WHEN** a project file ends in a section belonging to some other tool
- **THEN** that section is written back

### Requirement: Preserved content SHALL be written back near where it was found
Content preserved verbatim SHALL be written back in a position corresponding to where it was read, as far as
the IDE's own additions and removals allow.

Position is not cosmetic in a format read by other tools: a key that has moved out of its section, or a
component that has moved out of its parent, is a key or component that has changed meaning. Approximating
the original position keeps the file recognisable to whatever wrote it, and keeps a diff readable — a save
that reshuffles a file is a save nobody can review.

#### Scenario: An unrecognised key among recognised ones
- **WHEN** an unrecognised project key sits between recognised keys
- **THEN** it is written back in approximately that position rather than collected at the end

### Requirement: A binary companion SHALL be named for the file it belongs to
Where a file's binary resources live in a companion file, that companion SHALL be named by the convention
belonging to that file's own type.

The convention differs per designer type, and the original product looks for the file the convention names —
nothing else. Writing every companion under one type's extension produces a file the original product does
not look for and one it cannot read, so the resources are lost even though they were written correctly.

#### Scenario: Saving a designer that is not a form
- **WHEN** a designer type with its own companion convention is saved with binary resources
- **THEN** the companion is written under that type's extension, not a form's

### Requirement: A save SHALL NOT be able to destroy the previous version
Writing a file SHALL be arranged so that the previous content survives intact until the new content is
complete.

Interrupted writes happen — a crash, a full disk, a killed process — and the file being written at that
moment is the one the developer cares about most. Writing in place means the interruption lands in the
middle of their source file; completing the write elsewhere first means the worst outcome is a leftover
temporary file next to an intact original.

#### Scenario: An interrupted save
- **WHEN** a save is interrupted before it completes
- **THEN** the file on disk is still the previous version, unmodified

### Requirement: The IDE SHALL refuse to save a file it cannot reproduce
Where the IDE knows it would not reproduce a file faithfully, it SHALL leave the file on disk untouched and
SHALL tell the developer why, rather than writing a version it knows to be wrong.

Refusing is the honest response to an incomplete implementation, and it is what makes an incomplete one safe
to put in front of people. The alternative is a save that appears to succeed and produces a file the
original product cannot open — silent, discovered later, unrecoverable if it has been committed over the
original. A refusal is none of those things: nothing is lost, and the developer learns immediately what the
IDE cannot yet handle.

#### Scenario: Saving a file the IDE would not reproduce
- **WHEN** a save is requested for a file the IDE knows it cannot reproduce
- **THEN** the file on disk is unchanged and the developer is told which file and why

#### Scenario: Several such files in one save
- **WHEN** a save covers several such files
- **THEN** they are reported together rather than one prompt at a time

#### Scenario: Saving elsewhere
- **WHEN** the developer explicitly saves such a file to a new location
- **THEN** the save proceeds, since the original is not at risk

### Requirement: A file the IDE will not save SHALL be presented as read-only
Where the IDE would refuse to save a file, its editing surfaces SHALL be read-only and SHALL state the
reason, while remaining open for viewing and running.

Discovering the refusal at save time means discovering it after the work is done. A developer who has spent
an hour on a form and is then told it cannot be written has lost the hour — and the refusal, which exists to
protect them, is what took it. Saying so up front turns the same limitation into something they can plan
around, and keeping the file viewable and runnable preserves the reason they opened the IDE at all.

#### Scenario: Opening such a file
- **WHEN** a file the IDE would refuse to save is opened
- **THEN** its editors are read-only and state why

#### Scenario: Running a project containing one
- **WHEN** a project containing such a file is run
- **THEN** it runs

### Requirement: Round-trip fidelity SHALL be measured against real projects
Fidelity SHALL be assessed by reading and rewriting real VB6 projects and comparing the result against the
originals, not by tests over content the IDE authored itself.

A test written against the IDE's own output only proves it is self-consistent. Everything this capability
exists to protect — the accumulated content, the conventions nobody wrote down, the things the original
product emits but never documents — is present only in files the original product actually produced. The
sample projects shipped with VB6 are the accessible corpus of exactly that, which makes them the measure.

#### Scenario: Assessing fidelity
- **WHEN** round-trip fidelity is assessed
- **THEN** it is measured over real VB6 projects, comparing rewritten files against the originals

### Requirement: Structure the IDE does model SHALL survive a round-trip
Where a file expresses a hierarchy among the things the IDE models, the IDE SHALL write that hierarchy
back as a hierarchy. Reading a nested structure and writing a flat one SHALL be treated as data loss, not
as formatting.

The existing requirements cover content the IDE does *not* understand, which it carries verbatim. This is
the opposite case and the more dangerous one: the IDE understood the components perfectly well and lost
the relationship between them. A menu tree written back flat is not a cosmetic difference — every item
becomes a top-level menu, which is a different program.

#### Scenario: A nested menu
- **WHEN** a form whose menus are nested is loaded and saved
- **THEN** the saved file expresses the same nesting

#### Scenario: Depth beyond one level
- **WHEN** a menu contains a submenu which itself contains items
- **THEN** every level is written back at its original depth

### Requirement: The refusal gate SHALL narrow as reproduction improves
Where the IDE becomes able to reproduce a structure it previously could not, the refusal gate SHALL stop
firing for that structure, and SHALL continue to fire for the structures still unreproducible.

A gate that stays shut after the reason for it is fixed is as wrong as one that never closed — it holds
files read-only for a defect that no longer exists, and it teaches developers that the read-only state is
arbitrary. Narrowing it in the same change that fixes the underlying defect is what keeps the gate
meaningful.

#### Scenario: A form whose only nesting is menus
- **WHEN** such a form is loaded, once menu nesting round-trips
- **THEN** it is editable and saveable

#### Scenario: A form with a populated container
- **WHEN** such a form is loaded, while container nesting does not round-trip
- **THEN** it is still presented read-only

## Out of Scope
- **Interpreting preserved binary content.** Binary resources the IDE has no model for stay opaque; they are
  carried, never read.
- **Reproducing the original product's omissions.** The original product omits properties left at their
  default; the IDE writes what it holds. The result is semantically equal and slightly larger.
- **Preserving the original layout of content the IDE does model.** Recognised properties are written in the
  IDE's own order and spacing. Preserving the original arrangement would mean holding every file as raw text
  and giving up the model that makes the IDE an editor rather than a text editor.
