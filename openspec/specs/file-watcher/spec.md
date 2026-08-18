# file-watcher Specification

## Purpose
Define what happens when a project's files change on disk while the IDE has them loaded.

VB6 predates the workflow that makes this necessary. Today a developer switches branches, pulls, or edits a
file in another editor while the IDE is open, and an IDE holding a stale copy does not merely show the wrong
text — it writes that stale copy back over the change on the next save. That is silent data loss with no
error and no prompt, and it is the reason this exists.
## Requirements
### Requirement: Externally changed files SHALL be detected while loaded
The IDE SHALL detect when a loaded project's source files or their binary companions are modified on disk by
anything other than the IDE itself, whether or not the file is open in a tab.

Restricting detection to open tabs would leave the dangerous case uncovered. A branch switch rewrites files
the developer is not currently looking at, and those are precisely the ones whose staleness goes unnoticed
until a save destroys the change. A companion binary is re-read with the file that owns it, since the two
are one unit and reconciling either alone leaves them disagreeing.

#### Scenario: A file changed by another tool
- **WHEN** a loaded file is modified on disk by another process
- **THEN** the IDE detects it

#### Scenario: A file that is not open
- **WHEN** the changed file is loaded but has no open tab
- **THEN** it is detected in the same way as an open one

### Requirement: The IDE's own writes SHALL NOT be treated as external changes
A change the IDE itself made SHALL NOT be reported as an external change.

Every save would otherwise raise the very prompt this feature exists to raise on real conflicts. Beyond the
annoyance, a prompt that appears when nothing happened teaches the developer to dismiss it without reading —
which is exactly the habit that makes the real one useless.

#### Scenario: Saving from the IDE
- **WHEN** the IDE writes a file it is watching
- **THEN** no external change is reported

### Requirement: A change with no unsaved edits SHALL be adopted silently
Where the IDE has no unsaved edits for a changed file, it SHALL reload from disk without prompting, updating
any open editor and designer.

There is nothing to decide. The developer has no work to lose, so a prompt asks them to confirm the only
sensible outcome — and a branch switch touching thirty files would ask thirty times. Silent adoption is what
makes the feature invisible in the common case, which is what a good version of this feels like.

#### Scenario: Pulling changes with no local edits
- **WHEN** files change on disk and the IDE holds no unsaved edits for them
- **THEN** they are reloaded silently and any open views show the new content

### Requirement: A change conflicting with unsaved edits SHALL be resolved by the developer
Where the IDE holds unsaved edits for a changed file, it SHALL NOT discard either version on its own; it
SHALL present the conflict and let the developer choose to take the disk version or keep their edits.
Multiple conflicts arising together SHALL be presented once rather than one at a time.

Both versions are somebody's work and the IDE has no basis for choosing. Consolidating matters as much as
asking: a burst of changes producing one dialog per file is a dialog the developer clicks through without
reading, which converts a careful design back into data loss.

#### Scenario: A conflict
- **WHEN** a file with unsaved edits changes on disk
- **THEN** the developer is asked whether to take the disk version or keep their edits, and neither is discarded until they choose

#### Scenario: Several conflicts at once
- **WHEN** several files with unsaved edits change together
- **THEN** they are presented as one decision rather than one prompt per file

#### Scenario: Keeping local edits
- **WHEN** the developer chooses to keep their edits
- **THEN** the disk content is not applied, and the same change does not prompt again

### Requirement: Whether a file has unsaved edits SHALL be decided by comparing renders, not raw bytes
For a file whose format the IDE does not reproduce byte-for-byte, the decision SHALL compare what the IDE
would write now against what it would have written when the file was last loaded or saved, rather than
comparing against the bytes on disk.

This is what keeps the feature honest about a format the IDE cannot round-trip perfectly. Comparing a fresh
render against the disk bytes conflates two different questions — "did the developer change something?" and
"does our serializer reproduce this file exactly?" — and since the answer to the second is often no, every
untouched file would look edited and every external change would raise a false conflict.

#### Scenario: An untouched file the IDE cannot reproduce exactly
- **WHEN** an externally changed file has not been edited in the IDE, but the IDE would not write it
  byte-for-byte as it found it
- **THEN** it is treated as having no unsaved edits, and the change is adopted silently

#### Scenario: A file edited in the IDE
- **WHEN** an externally changed file has been edited in the IDE
- **THEN** it is treated as a conflict

### Requirement: Adopting a change SHALL re-establish the baseline it is compared against
After reloading a file from disk, the IDE SHALL record what it would now write as the new comparison point,
and SHALL re-establish every judgement it had formed about the previous contents — including whether the
file can be reproduced faithfully, and therefore whether it is editable.

Without it the file it just adopted still looks edited: closing the project would prompt to save something
the developer never touched, and the next external change to that file would be reported as a conflict when
there is none. Re-establishing the baseline is what makes adoption a complete operation rather than one that
leaves the file in a permanently suspicious state.

The reproduction verdict is part of that baseline and is the half most easily missed, because adopting the
new contents makes the file look correct while the verdict silently still describes the old ones. It fails
in both directions and both are bad: a file whose problem was fixed elsewhere stays locked with a banner
explaining a reason that no longer applies, and — worse — a file that has just acquired one is presented as
editable and saveable, so the developer is invited to make changes that the save must then refuse.

#### Scenario: After a silent reload
- **WHEN** a file has been reloaded from disk
- **THEN** it is reported as having no unsaved edits

#### Scenario: A reload that removes the reason a file was read-only
- **WHEN** a form held read-only is changed externally so that it can now be reproduced, and the change is
  adopted
- **THEN** the form becomes editable and the explanation is withdrawn, without the developer reopening it

#### Scenario: A reload that introduces a reason
- **WHEN** an editable form is changed externally so that it can no longer be reproduced, and the change is
  adopted
- **THEN** the form becomes read-only and the reason is shown, without the developer reopening it

### Requirement: Watching SHALL be optional and SHALL be quiet when the developer is elsewhere
The developer SHALL be able to turn external-change watching off, and a conflict SHALL NOT be raised while
the IDE is not the focused application.

Some workflows rewrite project files constantly and the feature is noise there, so it has an off switch.
Deferring the prompt until the IDE is focused matters more: a modal that appears while the developer is
typing in another window steals focus and takes the next keystroke as an answer to a question they have not
read.

#### Scenario: Changes arriving while the IDE is in the background
- **WHEN** conflicting changes arrive while the IDE is not focused
- **THEN** the prompt waits until the developer returns to the IDE

#### Scenario: Turning watching off
- **WHEN** the developer disables external-change watching
- **THEN** no reloads or prompts occur

