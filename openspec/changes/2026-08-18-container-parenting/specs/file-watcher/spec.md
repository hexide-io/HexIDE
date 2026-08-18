## MODIFIED Requirements

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
