## ADDED Requirements

### Requirement: A change the IDE refuses SHALL be reported as refused, with its reason
Where the IDE refuses a change to a file that the assistant asked for, or that the developer applied from a
reply, nothing SHALL be written, the assistant SHALL be told that the change was refused and why, and the
developer SHALL see the refusal and its reason in the transcript.

The assistant is shown each file whole, header included, and the IDE keeps the header for itself, so a model that
re-types a form's designer block with one space out of place has its edit refused. That makes a refusal routine
rather than rare. Reporting it as success is the failure the approval requirement exists to prevent, arriving from
the other side: the assistant carries on as though the edit landed, and the developer believes it did.

#### Scenario: The assistant's edit is refused
- **WHEN** the assistant asks to replace a file's content with text whose header differs from the file's
- **THEN** nothing is written, the assistant is told the edit was refused and why, and the transcript shows the
  refusal and its reason

#### Scenario: Applying a block the IDE refuses
- **WHEN** the developer applies a code block whose header differs from the active file's
- **THEN** nothing is written, the transcript says the block was not applied and why, and the assistant is told
  the same
