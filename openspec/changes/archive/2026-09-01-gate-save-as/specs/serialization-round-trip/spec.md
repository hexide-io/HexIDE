## MODIFIED Requirements

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
