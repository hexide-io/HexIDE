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

The text is content in the same sense. VB6 source declares no encoding: it is in the code page of the
machine that wrote it. So the IDE keeps each source file of a project in the encoding it was read in, and
refuses an edit that encoding cannot hold rather than converting the file. A file in a code page this machine
cannot read is still loaded, byte for byte, rather than dropped from the project. A file the project only
carries is not held to this yet ([#745](https://github.com/hexide-io/HexIDE/issues/745)).

> **Known divergence — narrower than this note once said.** The requirements below state the intended
> contract. It once read that no file of VB6's own shipped samples survived a save byte-identically, and that
> 6 of 22 were held read-only. Against the corpus gate today, every designer file VB6 wrote (28, from its
> Template tree and from `corpus/`) survives a save byte-identically, its companion included, and none is held
> read-only. The last one held, `Web Browser.frm`, was freed when the companion records cited by a control
> the IDE does not model began to be carried with it ([#60](https://github.com/hexide-io/HexIDE/issues/60)).
> The epic, [#21](https://github.com/hexide-io/HexIDE/issues/21), stays open, and the requirements are still
> stated as intent rather than rewritten to describe the code. The refusal and read-only requirements are met,
> and are what makes any remaining gap safe to ship with.
>
> That read-only count was 12 until menu hierarchies began surviving a round-trip
> ([#83](https://github.com/hexide-io/HexIDE/issues/83)), and container nesting has since been fixed as
> well ([#84](https://github.com/hexide-io/HexIDE/issues/84)) — a control inside a `Frame` or a
> `PictureBox` is recorded, written back nested, hosted by its container at run time and drawn inside it in
> the designer.
>
> The count did not fall, and that is the honest part. Two forms left the read-only set and two entered it:
> closing the container gap freed `Options Dialog.frm` and `Tip of the Day.frm`, while teaching the gate to
> recognise **blob loss** caught `Button ListBox.frm` and `Mover ListBox.frm`, which until then saved
> lossily — dropping a control's picture reference while the `.frx` bytes survived on disk. All six that
> remain are held for unreproducible companion binary content, and none for nesting. That is why the gate is
> tracked as a SET rather than a count: a count would have reported no progress on a change that removed two
> whole classes of corruption.
>
> The set is gated by a corpus test, so membership cannot change quietly in either direction. It has since
> fallen to none, each form leaving only by round-tripping, the last of them when content cited by a control
> the IDE does not model began to be carried ([#60](https://github.com/hexide-io/HexIDE/issues/60)).

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

#### Scenario: VB6's saved Toolbar form (#1088)
- **WHEN** the form in `corpus/inbox-controls/saved/toolbar` is saved, including after a form-caption edit
- **THEN** its carried Toolbar blocks retain `ToolTips`, BorderStyle after `_Version`, HIMETRIC ButtonWidth/ButtonHeight,
  twips placeholder widths and nested ButtonMenus verbatim, and its ImageList companion retains every byte
- **AND** the separate runtime loader reads designed top-level Toolbar button/menu bags under their measured names,
  retaining numeric image indexes and binding the saved ImageList name after all controls are created, including a list
  appearing later in the file; designer editing/rewriting of these bags remains unsupported
- **AND** carried Toolbars SHALL remain view-only, excluded from the Properties component list, including their own
  scalar properties and Buttons, until those edits can be saved faithfully; editing a surrounding form property
  SHALL survive save and reload without changing the carried Toolbar blocks or companion bytes

#### Scenario: A section left by another tool
- **WHEN** a project file ends in a section belonging to some other tool
- **THEN** that section is written back

#### Scenario: Companion content cited by content the IDE does not model
- **WHEN** a property, or a component type, the IDE has no model for cites a record in the binary companion
- **THEN** the record is written back with it, and the citation is rewritten, companion name included, to
  wherever the record lands

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
complete. Where a designer file has a binary companion, the two SHALL be treated as one artifact: a save
SHALL write both halves or neither, and SHALL decide which before either is written.

Interrupted writes happen — a crash, a full disk, a killed process — and the file being written at that
moment is the one the developer cares about most. Writing in place means the interruption lands in the
middle of their source file; completing the write elsewhere first means the worst outcome is a leftover
temporary file next to an intact original.

A companion is not a separate file that happens to sit alongside. Each record's offset is simply where it
landed, and the designer file cites those offsets, so the citations are meaningful only against the exact
companion produced with them. Writing one half without the other leaves citations addressing a partition
that no longer exists — and because a file's faithfulness is re-derived from its own citations, the result
reopens as reproducible. The markers that would have flagged it are the ones that were overwritten.

Two halves cannot be committed to disk in one indivisible step, so the order SHALL be chosen for what an
interruption leaves behind. Writing the companion first leaves the previous designer file, whose citations
may overrun the new companion and be refused on the next load. Writing the text first leaves citations that
resolve inside the larger stale companion — to the wrong records, with nothing to indicate it. An
interrupted save SHALL fail towards the outcome the developer is told about.

#### Scenario: An interrupted save
- **WHEN** a save is interrupted before it completes
- **THEN** the file on disk is still the previous version, unmodified

#### Scenario: A file the IDE will not write
- **WHEN** a designer file cannot be written faithfully
- **THEN** neither it nor its companion is modified, and the developer is told which file and why

#### Scenario: An interrupted save of a designer file and its companion
- **WHEN** a save is interrupted between the two halves
- **THEN** what remains on disk is a pair whose mismatch is detected on the next load, rather than one that
  reads as correct

#### Scenario: A companion the designer file does not reference
- **WHEN** a save produces no binary content for a designer file that cited none
- **THEN** any companion beside it is left alone, because it holds content this file never referenced

#### Scenario: One record referenced by two properties
- **WHEN** more than one property cites the same companion content
- **THEN** it is written once, and both citations address the copy that was written

### Requirement: The IDE SHALL refuse to save a file it cannot reproduce
Where the IDE knows it would not reproduce a file faithfully, it SHALL leave the file on disk untouched and
SHALL tell the developer why, rather than writing a version it knows to be wrong. This SHALL hold wherever
that file would be written, **including a new location**.

Refusing is the honest response to an incomplete implementation, and it is what makes an incomplete one safe
to put in front of people. The alternative is a save that appears to succeed and produces a file the
original product cannot open — silent, discovered later, unrecoverable if it has been committed over the
original. A refusal is none of those things: nothing is lost, and the developer learns immediately what the
IDE cannot yet handle.

Saving to a new location is not a safe middle ground, and the IDE SHALL NOT treat it as one. The copy
carries the loss — content the IDE could not reproduce is absent from it — and because a file's
faithfulness is re-derived from the file itself, the copy then reads as reproducible. The very markers that
caused the refusal are what went missing, so nothing remains to raise it again: the copy opens without a
warning, edits freely, and saves over itself. A refusal the developer could have recovered from becomes a
file that looks correct and is not.

#### Scenario: Saving a file the IDE would not reproduce
- **WHEN** a save is requested for a file the IDE knows it cannot reproduce
- **THEN** the file on disk is unchanged and the developer is told which file and why

#### Scenario: Several such files in one save
- **WHEN** a save covers several such files
- **THEN** they are reported together rather than one prompt at a time

#### Scenario: A single file saved on its own
- **WHEN** one such file is saved outside a batch
- **THEN** the developer is told at that moment, and that refusal is not reported again during a later save

#### Scenario: Saving elsewhere
- **WHEN** the developer saves such a file to a new location
- **THEN** it is refused there too, and no destination is asked for, because the copy would carry the loss

### Requirement: A file the IDE will not save SHALL be presented as read-only
Where the IDE would refuse to save a file, its editing surfaces SHALL be read-only and SHALL state the
reason, while remaining open for viewing. It SHALL remain runnable unless it holds a control the engine
cannot create or a data-bound control, which is refused when it is run (see `interpreter-core`).

Discovering the refusal at save time means discovering it after the work is done. A developer who has spent
an hour on a form and is then told it cannot be written has lost the hour — and the refusal, which exists to
protect them, is what took it. Saying so up front turns the same limitation into something they can plan
around, and keeping the file viewable and runnable preserves the reason they opened the IDE at all.

The exception is a control the engine cannot create or a data-bound control (#859). Leaving out a control or
its binding runs a different program: an absent control is not found by code, and an unbound control shows
its design-time value instead of the current record. That is a
wrong result rather than a late error, so such a form is refused when it is run, and stays viewable
(hexide-io/HexIDE#857).

#### Scenario: Opening such a file
- **WHEN** a file the IDE would refuse to save is opened
- **THEN** its editors are read-only and state why

#### Scenario: An invisible UserControl has no complete recorded extents
- **WHEN** a form holds an InvisibleAtRuntime UserControl missing `_ExtentX` or `_ExtentY`
- **THEN** the designer opens read-only and the form records the missing-extents save-fidelity reason
- **AND** dirty checks and other read-only serialization do not throw
- **AND** saving is refused without changing the file or inventing extents

#### Scenario: Running a project containing one
- **WHEN** a project containing such a file is run, and the file holds neither a control the engine cannot create
  nor a data-bound control
- **THEN** it runs

#### Scenario: Running a form holding a control the engine cannot create
- **WHEN** a project is run whose form holds a control the engine cannot create or a data-bound control
- **THEN** that form is refused when it is built to run, naming the control, and it can still be opened and viewed

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

### Requirement: A container's children SHALL be written back inside their container
Where a file expresses a control as a child of a container, the IDE SHALL write it back inside that
container rather than as a sibling of it.

The existing structure requirement was written for menus and is satisfied by them; this states the harder
half. It matters more than menu nesting because the original product does not defend against getting it
wrong: a file that nests a control under a class which cannot contain one is accepted without complaint and
the control is silently re-parented somewhere else. A flattened file therefore does not fail loudly in VB6 —
it opens, and the form is quietly a different form.

#### Scenario: A control inside a container
- **WHEN** a form with a control inside a container is loaded and saved
- **THEN** the saved file expresses that control inside the same container

#### Scenario: Containers within containers
- **WHEN** a container is itself a child of another container
- **THEN** every level is written back at its original depth

### Requirement: A child's position SHALL be interpreted relative to its container
Where a control is a child of a container, the IDE SHALL treat its stored position as measured from that
container's own origin, in every layer that reads or writes it.

The file measures a child from its container, and a container is free to move — the tab-switching idiom in
the original product's own templates parks a page off-screen and brings it back by assigning the container's
position. A layer that reads a child's stored position as a distance from the form therefore draws it in the
wrong place, and a layer that writes one back as such corrupts it. The two failures are the same mistake in
opposite directions, which is why no layer may hold the assumption privately.

#### Scenario: A child of a container parked off-screen
- **WHEN** a container is positioned outside the visible form and holds children
- **THEN** its children are drawn with it rather than at the equivalent distance from the form

#### Scenario: Moving a child in the designer
- **WHEN** the developer moves a control that is inside a container
- **THEN** what is written back is its position relative to that container

### Requirement: Unreproducible binary content SHALL hold a file read-only
Where a file cites binary content the IDE cannot reproduce, because the cited content did not reach the model,
the IDE SHALL treat that file as one it cannot reproduce. Content cited by a property or component the IDE
does not model is carried with it, so that alone is no longer such a case
([#60](https://github.com/hexide-io/HexIDE/issues/60)).

The reference is what is lost, not the bytes: the companion file is left alone by a separate guard, so the
image survives on disk while the property pointing at it does not. That is a save which looks successful and
silently strips a control's picture, which is the same class of failure as flattening. Recognising it widens
the refusal before the container work narrows it — the count of refused files gets worse before it gets
better, and that is the correct direction, because a refusal is recoverable and a silent strip is not.

Whether the citation was honoured SHALL be judged against **what the file itself cites**, not against what
the companion yielded. A count taken from the companion cannot see a companion that is absent — there is
nothing to count — nor one that is truncated, where the unreachable offset is dropped from both sides at
once and they agree at a number lower than the file actually cites. The citations are in the file being
opened, so they can be counted whatever became of the file beside it.

#### Scenario: A property the IDE does not model referencing a companion blob
- **WHEN** such a file is loaded and the cited content is in the companion
- **THEN** the property is carried with that content, and the file is not held read-only on that account

#### Scenario: A modelled property referencing a companion blob
- **WHEN** the referencing property is one the IDE does model and the cited content reached the model
- **THEN** the file is not held read-only on that account

#### Scenario: A citation that cannot be honoured
- **WHEN** a file cites companion content that did not reach the model, because the companion is absent or
  too short to hold the cited offset
- **THEN** the file is presented read-only, however ordinary the property carrying the citation

#### Scenario: Two properties citing one offset
- **WHEN** more than one property cites the same companion offset
- **THEN** it counts once, so sharing content is not mistaken for a shortfall

### Requirement: A carried file SHALL round-trip without becoming source
The deserializer SHALL recognise the project-file key for a carried file, and SHALL place such an entry in a
collection separate from the compiled modules. A carried file SHALL NOT appear among the modules under any
circumstance.

This is a structural guarantee rather than a defensive one. Every operation that could damage a non-code
file — execution, extension-based rename on save, the module header writer — iterates the module collection.
Absence from that collection is what makes the damage unreachable; a guard at each site is something a later
change forgets to add.

Because the key is now recognised rather than falling through as an unknown line, it SHALL be counted on read
and re-emitted through the same counter on write. A key promoted from unknown to known that is not counted on
both sides shifts the position of every unknown line after it.

#### Scenario: A project carrying non-compiled files is re-saved untouched
- **WHEN** a project holding carried files is read and written back with no change
- **THEN** the output is byte-identical to the input, including any unknown lines that followed a carried entry

#### Scenario: A carried entry names no member name
- **WHEN** an entry uses the key that carries a path but no name field
- **THEN** the member's name is derived from the filename

### Requirement: A non-source file on a code line SHALL be reclassified, and its line preserved
Where an entry on a module or class line names a file that is not VB6 source, the deserializer SHALL treat it
as a carried file, and SHALL preserve that entry's original line to be re-emitted verbatim.

Both halves are required and they answer different risks. Reclassifying prevents the file being treated as
source and rewritten with a module header on the next save. Preserving the line prevents the opposite fault:
reclassification is an inference about intent, and silently editing a developer's project file on the
strength of an inference is a larger liberty than declining to. The line SHALL change only when the project's
membership actually changes, or when the project file is written in another folder, where only its path changes
(see "A kept member's line SHALL name the same file from wherever the project file is written").

A file with no extension SHALL NOT be reclassified on read. The project already claims it is source, and
reclassifying something that cannot be classified is a guess stacked on a guess.

#### Scenario: A document on a module line
- **WHEN** a module or class entry names a file that is not VB6 source
- **THEN** it becomes a carried file, and re-saving emits its original line unchanged

#### Scenario: Source on a module line
- **WHEN** a module or class entry names VB6 source, or a file with no extension
- **THEN** it remains a module

#### Scenario: A file the developer adds
- **WHEN** a carried file joins the project through the IDE rather than by being read
- **THEN** it is written using the project file's own key for a carried file

### Requirement: A path inside a project file SHALL be a Windows path on every host
Paths held in a project file SHALL be read and written as backslash-separated regardless of the host the IDE
is running on. The serializer SHALL emit backslashes for every member path; the deserializer SHALL preserve
the value verbatim, and SHALL derive a member's name from it using backslash and forward slash as separators.
Conversion to the host's separator SHALL happen only where a path is resolved against the filesystem, and
SHALL NOT affect what is written back.

A project file is a Windows-native format. The host path API answers about the *host* filesystem, so it is
the wrong tool for these strings: where a backslash is an ordinary filename character, deriving a name yields
the whole path and a carried file is named after its own directory, while relativising on write yields
forward slashes that go into a file which must contain backslashes. Both faults are silent — each produces a
value that still looks like a path — and neither is reachable on a Windows development machine.

Tests covering this SHALL express their expectations about project-file content as literal backslashed
strings, never by composing them with a host path API. An expectation built from the host follows whichever
machine runs it, and so certifies the defect on the platform where it matters rather than catching it.

#### Scenario: A member in a subdirectory is written
- **WHEN** a project holding a member in a subdirectory is saved
- **THEN** the emitted path uses backslashes, whatever host performed the save

#### Scenario: A member in a subdirectory is read back
- **WHEN** a project file naming a member in a subdirectory is read
- **THEN** the stored relative path is the backslashed value exactly as it appeared in the file

### Requirement: A source file SHALL be written back in the encoding it was read in

The IDE SHALL record, for each VB6 source file it reads (`.vbp`, `.vbg`, `.frm`, `.bas`, `.cls`, `.ctl`,
`.pag`), the encoding it was read in, and SHALL write the file back in that encoding, including a UTF-8
byte-order mark when the file had one. It SHALL NOT change a file's encoding to accommodate an edit, and
SHALL NOT re-encode text that was not edited.

#### Scenario: Accented bytes that also form valid UTF-8 survive an unrelated edit

- **GIVEN** a module without a byte-order mark whose only non-ASCII bytes are `C3 A9`, read in Windows-1252
- **WHEN** an ASCII line is edited and the module is saved
- **THEN** the bytes `C3 A9` are written back unchanged

#### Scenario: A byte-order mark survives a save

- **GIVEN** a module that begins with a UTF-8 byte-order mark
- **WHEN** it is edited and saved
- **THEN** the saved file still begins with the byte-order mark

#### Scenario: A UserControl keeps its encoding whichever half is saved

- **GIVEN** a `.ctl` that begins with a UTF-8 byte-order mark
- **WHEN** it is saved from its designer, or with its project
- **THEN** the saved file still begins with the byte-order mark

### Requirement: A file the host's code page cannot read SHALL still load and save back unchanged

When a source file's bytes cannot be decoded in the host's ANSI code page, or would not encode back to the
same bytes, the IDE SHALL still load the file, reading each byte as the Latin-1 character with that value. It
SHALL warn, naming the file, and SHALL write the file back byte-for-byte when it is saved unedited. This SHALL
apply when a project is opened, when an existing file is added, and when a file changed outside the IDE is
reloaded.

#### Scenario: A Western file on a double-byte host

- **GIVEN** a host whose ANSI code page is 932, and a module containing the byte `E9` followed by a line break
- **WHEN** its project is opened
- **THEN** the module is part of the project, a warning names it, and saving it unedited writes the same bytes

#### Scenario: A byte-order mark over text that is not UTF-8

- **GIVEN** a module that begins with a UTF-8 byte-order mark followed by Windows-1252 text
- **WHEN** its project is opened and saved without edits
- **THEN** the module's header is recognised, and the saved file is byte-identical to the original

#### Scenario: A file recorded as UTF-8 is replaced by an ANSI revision

- **GIVEN** a module read as UTF-8, which an external tool replaces with bytes that are not valid UTF-8
- **WHEN** the IDE reloads it
- **THEN** the reload succeeds and shows the new content, and the next save does not write the old content

### Requirement: A project member that fails to load SHALL keep its line in the project file

When a form or module named in a project file cannot be loaded, for any reason, the IDE SHALL keep that
member's line and SHALL write it back on save, as it does for a member whose file is missing. Saving the
project SHALL NOT remove a member the IDE failed to load.

#### Scenario: A member that throws during load

- **GIVEN** a project whose module fails to load
- **WHEN** the project is opened and saved
- **THEN** the saved project file still names that module

### Requirement: A designer whose two names disagree SHALL open under its Begin line's name, with a warning

Where the designer block of a form, UserControl or PropertyPage names it differently from its `Attribute VB_Name`,
even in case alone, the IDE SHALL name it by the designer block, SHALL say so as the project opens, and SHALL make
`VB_Name` agree, leaving it edited so that its next save writes the agreement. This is what VB6 does.

#### Scenario: VB_Name names another form

- **GIVEN** a form whose designer block says `frmRenamed` and whose `VB_Name` says `Form1`
- **WHEN** its project is opened
- **THEN** the form is called `frmRenamed`, a warning names its file and that name, and its `VB_Name` says `frmRenamed`
- **AND** the form has unsaved changes until it is saved

#### Scenario: The names agree

- **WHEN** a form whose two names agree is opened
- **THEN** nothing is said and the form has no unsaved changes

### Requirement: A UserControl or PropertyPage SHALL be named by its designer block

The IDE SHALL name a UserControl or PropertyPage by its designer block's `Begin` line, whatever its file or its
`Attribute VB_Name` says, as VB6 does. A form that names the control as VB6 does SHALL find it, and one that names it
by its file SHALL NOT, as in VB6.

#### Scenario: A control whose file is named otherwise

- **GIVEN** `Gauge.ctl`, whose designer block names it `ctlDial`, in a project called `probe`
- **WHEN** the project is opened
- **THEN** the control is called `ctlDial`, and a form's `Begin probe.ctlDial` is that control
- **AND** a form's `Begin probe.Gauge` names no control, and is kept as the file had it

### Requirement: A kept member's line SHALL name the same file from wherever the project file is written

When a project file is written in a folder other than the one it was read from, each line the IDE keeps for a
member it did not load, each carried file's preserved original line, and the paths of a designer, the resource
file, the build folder and a reference's library SHALL name the same file from the new folder, in the line's own
shape. The help file's path SHALL be written as it was read, as SHALL every path in the folder it was read from,
and one that names a drive or starts at a root.

#### Scenario: Save Project As to another folder

- **GIVEN** a project, read from one folder, whose form's file is missing
- **WHEN** the project file is saved to another folder
- **THEN** the form's line names its file by a path from the new folder
- **AND** a later save from the new folder names it the same way

#### Scenario: A resource file and a help file

- **GIVEN** a project file holding `ResFile32="res\probe.res"` and `HelpFile="help\probe.hlp"`
- **WHEN** it is saved one folder deeper
- **THEN** it holds `ResFile32="..\res\probe.res"` and `HelpFile="help\probe.hlp"`

#### Scenario: Saved where it was read

- **GIVEN** a project holding kept member lines
- **WHEN** the project file is saved in the folder it was read from
- **THEN** each kept line is written exactly as it was read

### Requirement: A project's description SHALL be read from and written to its project file as VB6 writes it

The IDE SHALL read a project file's `Description` line into the project's description. It SHALL write a description
that is not empty as `Description="..."`, with a quote inside doubled, after `HelpContextID`, or after `Name` when
there is none, and SHALL write no line for an empty one. That is what VB6's own Save Project As writes (#42). A
file's own `Description` line SHALL stay where it was read.

#### Scenario: A description typed into a project read without one

- **GIVEN** a project file holding `Name="probe"`, `HelpContextID="0"` and `CompatibleMode="0"`, and no description
- **WHEN** a description `Orders` is set and the project is saved
- **THEN** the file holds `Description="Orders"` between `HelpContextID="0"` and `CompatibleMode="0"`

#### Scenario: A description with a quote in it

- **GIVEN** a project file holding `Description="He said ""hi"""`
- **WHEN** it is read and saved
- **THEN** the project's description is `He said "hi"`, and the line is written as it was read

#### Scenario: A description cleared

- **GIVEN** a project file holding a `Description` line
- **WHEN** the description is cleared and the project is saved
- **THEN** the file holds no `Description` line

### Requirement: A line of VB6 source SHALL end where VB6 ends it

Every reader of a module's or a form's lines SHALL end a line at `\r\n`, at a lone `\r` and at a lone `\n`, as VB6's
compiler and the code window do (#1020). That includes the header split, the `VB_Name` retarget, the read-only regions
and the guard. A save SHALL write every line of code it read.

#### Scenario: A module whose lines end with a lone CR

- **GIVEN** a `.bas` whose every line, its `Attribute VB_Name` line included, ends with a lone CR
- **WHEN** its project is opened and saved
- **THEN** the file holds every line it held, each ending CRLF

#### Scenario: A stray CR between two lines

- **GIVEN** a CRLF `.bas` whose `Attribute VB_Name` line ends with a lone CR, followed by `Option Explicit`
- **WHEN** it is saved
- **THEN** `Option Explicit` is still in the file, on a line of its own

#### Scenario: An attribute under a blank line made by a lone CR

- **GIVEN** `Public Sub Foo()` ending with a lone CR and then a CRLF, followed by `Attribute Foo.VB_Description`
- **WHEN** the code window reads the attribute line
- **THEN** it describes no member, as VB6 binds it to none


### Requirement: A project file SHALL NOT be written without a member that has no file

When an operation would write a project file while one of the project's forms or modules has never been saved
and is not saved by the same operation, the IDE SHALL refuse the whole operation before writing anything, and
SHALL name those members. A close that asked for the save SHALL leave the project open.

Such a member would be left out of the file. Were it the startup form, VB6 would build the file in silence
into a program that starts at `Sub Main` (#497).

#### Scenario: Closing with the project chosen and its new form not

- **GIVEN** a new project whose startup form `Form1` has never been saved
- **WHEN** it is closed, and only the project is chosen to be saved
- **THEN** nothing is written, the warning names `Form1`, and the project stays open

#### Scenario: A saved project that gains a form with no file

- **GIVEN** a saved project to which the Project Explorer has added `Form1`, which has no file yet
- **WHEN** it is closed, and everything the prompt lists is saved
- **THEN** the prompt lists the project as well as `Form1`, and the saved project file lists `Form1`

### Requirement: Files earlier versions of the IDE wrote as UTF-8 SHALL be read as UTF-8

Earlier versions of the IDE wrote a source file as UTF-8 without a byte-order mark exactly when its text held
a character above U+00FF. A file without a byte-order mark whose bytes are valid UTF-8 and decode to at least
one character above U+00FF SHALL therefore be read as UTF-8, and SHALL be written back as UTF-8 without a
byte-order mark. A file without such a character SHALL be read in the host's ANSI code page even when its
bytes are valid UTF-8.

#### Scenario: A module an earlier version saved with a curly quote

- **GIVEN** a module without a byte-order mark holding `“café”` encoded as UTF-8
- **WHEN** its project is opened
- **THEN** the module reads `“café”`, and saving it unedited writes the same bytes

#### Scenario: Accented text alone is not taken as UTF-8

- **GIVEN** a module without a byte-order mark whose only non-ASCII bytes are `C3 A9`
- **WHEN** its project is opened on a Windows-1252 host
- **THEN** the module reads `Ã©`, not `é`

### Requirement: A project SHALL record UTF-8 that detection cannot infer

When saving a project, a source member retained as UTF-8 without a byte-order mark whose text has no
character above U+00FF SHALL have a `path=utf-8` entry in a trailing `[HexIDE]` section of the `.vbp`.
The path SHALL be the path used by the member's project item line, including after Save As. No entry
SHALL be written for a member with a BOM, a character above U+00FF, or another encoding. Other tools'
trailing sections SHALL be preserved, and the section SHALL follow every standard project key.

A fresh open SHALL try that project's record after the encoding last read in and before the host's code
page. The record SHALL apply only if strict decoding and encoding reproduce the bytes. A shared member
SHALL follow the record of the project being opened. Source bytes SHALL NOT gain a BOM for this purpose.
Whether VB6's File → Save Project retains the section is unmeasured; `/make` accepting trailing sections
does not establish save behaviour.

#### Scenario: An edit removes the UTF-8 signature
- **GIVEN** a BOM-less UTF-8 module holding `' café 日本`
- **WHEN** its code becomes `' café` and the project is saved and freshly opened
- **THEN** its `.vbp` records its member path as `utf-8` and the module reads as `' café`
- **AND** its file remains UTF-8 without a BOM

#### Scenario: Detection becomes sufficient again
- **GIVEN** a member with an encoding record
- **WHEN** a character above U+00FF is added and the project is saved
- **THEN** the redundant record is removed

#### Scenario: An external editor replaces the recorded file with ANSI
- **GIVEN** a member recorded as UTF-8 whose bytes no longer decode strictly as UTF-8
- **WHEN** the project is freshly opened
- **THEN** decoding continues to the host's code page and the byte-for-byte fallback as before

### Requirement: An edit the file's encoding cannot hold SHALL be refused the same way on every save path

When a document holds a character its encoding cannot represent, the IDE SHALL refuse to save it rather than
convert the file or substitute the character, and SHALL name the document and its code page. It SHALL check
every file an operation would write before writing any of them. This SHALL hold for Save, Save As, Save
Project, Make, adding a new form or module, and saves requested through the automation server. An automation
caller SHALL receive the refusal in its reply, naming the document, the code page and the first character
that cannot be held, and SHALL NOT be left waiting on a dialog.

#### Scenario: Make with a form the code page cannot hold

- **GIVEN** a form whose caption holds a character outside the project's code page
- **WHEN** the project is made
- **THEN** no file is written, and the refusal names the form and the code page

#### Scenario: An automation save

- **GIVEN** a module read in Windows-1252
- **WHEN** an automation caller sets its content to text containing `→` and saves
- **THEN** the reply says the save was refused, naming the module, code page 1252 and `→`, and no dialog is shown

### Requirement: Recognised project lines SHALL retain their read order

The writer SHALL preserve read line order on an unedited save. Once the References dialog
has been accepted for the project in the session, even unchanged, it SHALL save `Type=`,
then every `Reference=`, then read `Object=` lines, then members (#2274, SP6 IDE). Components added in the session SHALL
follow the last `Form=` line (#1103). Within the remaining lines, the writer SHALL retain the read order of recognised `.vbp` keys. A kept or reclassified item line SHALL
retain its source slot even when its path is rebased. Other lines SHALL take the next unclaimed slot of
their key, so a renamed form stays in its slot. A line with no remaining slot SHALL follow its predecessor
in the default writer sequence; unused slots SHALL be omitted. A project with no recorded slots SHALL use
the default writer sequence. Unknown lines SHALL retain their recognised-line count hints, rather than
being anchored to slots; adding or removing an item may therefore shift them.

#### Scenario: A form before a reference is renamed

- **GIVEN** a project whose `Form=` line precedes `Reference=`, with an unknown line between them
- **WHEN** the form is renamed and the project is saved
- **THEN** the renamed form retains its slot before the reference and the unknown line retains its hint

#### Scenario: References OK without a change

- **GIVEN** a project with references or read components after member lines
- **WHEN** References is accepted without changing a selection and the project is saved
- **THEN** references precede read components, followed by members
- **AND** cancelling References on a freshly loaded project preserves its read order

#### Scenario: A missing form precedes a loaded form

- **GIVEN** two `Form=` lines, the first naming a missing file and the second a loaded form
- **WHEN** the project is saved, including to another folder
- **THEN** the missing form's line remains first and both paths name the same files from the destination

#### Scenario: A middle kept member joins the live project

- **GIVEN** three kept member lines with recorded source slots
- **WHEN** the middle member's file is restored, added to the project and the project is saved
- **THEN** the other kept lines retain their own source slots and the restored member is named once

### Requirement: Missing control Font blocks inherit a load-time copy of the form font

When loading a `VB.Form` with a declared Font, the deserializer SHALL give each font-bearing
control without its own Font block a value copy of that form font, regardless of container depth.
An explicit control Font block SHALL remain authoritative. Subsequent changes to the form font
SHALL leave those control copies unchanged (measured for #2062; see
[the oracle](../../../docs/vb6-fidelity-oracle/form-font-inheritance.md)).

#### Scenario: A nested control omits its Font block

- **GIVEN** a form declaring Arial 12, a PictureBox declaring Courier New 9.75, and a Frame inside it without a Font block
- **WHEN** the form is loaded
- **THEN** the Frame and its CommandButton without a Font block have Arial 12
- **AND** the PictureBox retains Courier New 9.75

#### Scenario: The form font changes after loading

- **GIVEN** Command1 and Command2 inherited Arial from Form1 on load
- **WHEN** Form1's font name becomes Tahoma
- **THEN** both controls still read Arial

### Requirement: Made programs SHALL carry source text independently of the host code page

Make SHALL encode the package's project, form and module source copies as UTF-8 with a BOM, without
changing the source documents' encodings. The source-encoding refusal before Make SHALL still apply.
Standalone `--check` SHALL read project and form files with the IDE's source decoder.

#### Scenario: A package made from ANSI sources runs on another code page
- **WHEN** Make packages source text held in an ANSI document
- **THEN** each packaged source file declares UTF-8 with a BOM and preserves that text
- **AND** the original document retains its encoding

#### Scenario: Check reads an ANSI project from disk
- **WHEN** Standalone checks a project whose project or form files contain ANSI text
- **THEN** it decodes those files as the IDE does, including UTF-8 BOM declarations

### Requirement: Form save commands follow the active document
The shell SHALL enable Save Form and Save Form As when the active document supplies a focused form,
independent of keyboard focus. It SHALL delegate to that active designer's or form code editor's own save
method, including format-on-save for code. A focused-form change SHALL requery the routed commands without
requiring a keyboard-focus change. Documents without a focused form SHALL retain their own command routing.

#### Scenario: Saving while a tool window has focus
- **GIVEN** a form designer is the active document and a tool window has keyboard focus
- **WHEN** Save Form or Save Form As is invoked
- **THEN** the active designer saves through its own save method

#### Scenario: Saving from form code
- **GIVEN** a form code editor is active and format-on-save is enabled
- **WHEN** the Save keyboard gesture is pressed in the editor
- **THEN** the editor's formatting path runs before saving the form

#### Scenario: Saving a carried file
- **GIVEN** a carried-file editor is active and has keyboard focus
- **WHEN** the Save keyboard gesture is pressed
- **THEN** its own save binding handles the key

### Requirement: Made programs SHALL omit carried files

Make SHALL omit carried files and their item lines from the packaged project, including carried files
reclassified from another item key. The other project lines SHALL retain their relative order. Ordinary
project saves SHALL continue to include carried files; Make SHALL leave source membership unchanged.

#### Scenario: A project carries private notes
- **WHEN** Make packages a project with a carried file, whether added or read as RelatedDoc, Module or Class
- **THEN** the archive contains neither the carried file nor its project item line
- **AND** the other project lines retain their relative order and the source project retains the carried file

### Requirement: PropertyPage saves distinguish HelpContextID from StandardSize
A PropertyPage designer SHALL expose HelpContextID as an editable integer property and save its value
in the `.pag`. A save SHALL omit StandardSize, including a carried line from a loaded file, because
VB6 rejects that property name in a `.pag` (#1631, issuecomment-6426450412). The unmeasured Small and
Large preset dimensions SHALL NOT be inferred from the StandardSize line.

#### Scenario: Edit a PropertyPage help context
- **WHEN** a PropertyPage loaded with HelpContextID 7 is edited and saved
- **THEN** its saved HelpContextID reflects the edit

#### Scenario: Save an invalid StandardSize file line
- **WHEN** a loaded PropertyPage contains `StandardSize = 1 'Small` and is saved
- **THEN** the `.pag` omits StandardSize and keeps its dimensions

### Requirement: Reference paths SHALL follow the measured save rules

The writer SHALL name a library on the project's drive relative to the saved project's folder,
including system libraries. Other-drive and UNC paths SHALL remain absolute. Without registry
resolution, HexIDE SHALL retain empty paths, diverging from VB6 for registered libraries.

#### Scenario: A system library on the project drive

- **GIVEN** an absolute reference path on the project's drive
- **WHEN** the project is saved
- **THEN** the reference names that library relative to the destination folder

### Requirement: CommonDialog scalar saves follow the measured VB6 layout
The serializer SHALL write MSComDlg.CommonDialog own scalar properties under their individual names in the
measured order, omitting defaults, and SHALL preserve the saved controls' order. Font properties SHALL remain
individual lines rather than a Font block. Missing lines SHALL retain the measured runtime defaults.

#### Scenario: VB6's four CommonDialogs are saved again
- **WHEN** the form in `corpus/inbox-controls/saved/commondialog/` is loaded and saved
- **THEN** its bytes are unchanged, including CommonDialog4 preceding the three existing controls

#### Scenario: Explicit defaults are read
- **WHEN** a CommonDialog own scalar property is read at its measured default
- **THEN** the writer omits that line

### Requirement: MSComctlLib ImageList picture persistence

The serializer SHALL preserve the measured MSComctlLib.ImageList format: extender properties, extents, BackColor,
ImageWidth, ImageHeight, MaskColor, UseMaskColor, _Version, then Images. BackColor and MaskColor SHALL be written even
at their defaults; the other own properties SHALL be omitted at their defaults. Each ListImage SHALL write Picture,
then Key (including an empty Key), then a nonempty Object.Tag padded as Tag. Picture records SHALL retain the
StdPicture CLSID, `lt\0\0`, the size and the inserted BMP/ICO file bytes.

#### Scenario: VB6's ImageList saves round-trip

- **WHEN** a form and its companion from `corpus/inbox-controls/saved/imagelist` are loaded and saved
- **THEN** the form text and companion bytes match VB6's saved files
- **AND** each saved image is available to the running program in index order with its Key, Tag and Picture

### Requirement: In-box corpus state consistency

The corpus gate SHALL reject a control folder present in both `corpus/inbox-controls/pending/` and `saved/`,
reporting both paths. It SHALL also reject byte-identical files at the same relative path in those states.
It SHALL NOT compare files across different control folders or outside this corpus. README folder rows SHALL
list each pending and saved control folder in its actual state.

#### Scenario: A merge leaves a control in both states

- **WHEN** both `pending/treeview/` and `saved/treeview/` exist
- **THEN** the guard fails and names both paths

#### Scenario: Different controls share a fixture

- **WHEN** a pending Toolbar and a saved ImageList have identical companion bytes
- **THEN** those shared bytes do not cause a duplicate-file failure

### Requirement: Reference locales SHALL use hexadecimal

The project reader SHALL interpret a reference's LCID as hexadecimal, accepting either case. The writer
SHALL emit lowercase hexadecimal without padding, as measured in VB6 SP6 (#2273).

#### Scenario: Reading and saving reference locales

- **WHEN** references with LCIDs `0`, `9`, `409`, `c0a` and `C09` are read
- **THEN** their model values are 0, 9, 1033, 3082 and 3081
- **AND** saving writes `0`, `9`, `409`, `c0a` and `c09`, respectively

### Requirement: Flushed replacement of saved files

Project source, companion, project and group file saves, carried-document saves,
Make archives and runners, resource and protocol exports (including automation), and project sidecars,
SHALL write a unique sibling temporary file and flush its bytes to disk before closing it
and replacing the target. The writer SHALL attempt temporary-file cleanup on failure
and propagate the original write error. This is a per-file operation, not an atomic
transaction across related files.

#### Scenario: A staged write fails
- **GIVEN** an existing saved file
- **WHEN** writing its replacement fails before the rename
- **THEN** the existing target bytes remain unchanged
- **AND** cleanup of the temporary file is attempted

#### Scenario: Publishing a standalone runner on Unix

- **WHEN** Make replaces its runner executable on Unix
- **THEN** the staged runner receives the source runner's Unix permissions before replacement
- **AND** a copy failure leaves the old executable unchanged

### Requirement: ActiveX EXE saves add Retained at the measured location

When saving `Type=OleExe` without a `Retained=` line, the writer SHALL insert `Retained=0`
between adjacent `Unattended=0` and `ThreadPerObject=0` lines (#2459). It SHALL preserve an
existing Retained line and SHALL leave other project types, missing neighbours and other orders
unchanged because their insertion behaviour has not been measured.

#### Scenario: The ActiveX Document Exe template lacks Retained

- **WHEN** an ActiveX EXE with no Retained line and adjacent Unattended=0 and ThreadPerObject=0 lines is saved
- **THEN** Retained=0 is inserted between those lines
- **AND** the remaining lines retain their order

## Out of Scope
- **Interpreting preserved binary content.** Binary resources the IDE has no model for stay opaque; they are
  carried, never read.
- **Reproducing the original product's omissions.** The original product omits properties left at their
  default; the IDE writes what it holds. The result is semantically equal and slightly larger, but only where
  the IDE's default for a property is the original product's. Where it is not, a line the IDE leaves out
  reads back as something else: `StartUpPosition` was the measured case, a missing line meaning 0 to the
  original product and 3 to the IDE (#932), which is why the IDE's default is now 0.
- **Preserving the original layout of modelled form/control properties.** These properties are written in the
  IDE's own order and spacing. This does not include recognised `.vbp` lines, whose read order is retained.
