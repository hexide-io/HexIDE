## ADDED Requirements

### Requirement: External Immediate evaluation
While an external session owns the run, the IDE SHALL evaluate Immediate lines through that adapter,
using the selected stopped frame and DAP REPL context. It SHALL preserve adapter-specific syntax,
display successful result text and adapter errors, and never fall back to the interpreter.
The IDE SHALL permit only one pending Immediate evaluation and SHALL refresh selected-frame locals
after a completed evaluation while the original stop remains current. It SHALL NOT automatically retry
evaluation, which can have side effects.

#### Scenario: Evaluate in the caller
- **WHEN** a stopped external caller is selected and the user submits an Immediate line
- **THEN** the adapter receives the line with that caller's frame ID and REPL context
- **AND** the response is displayed and locals are refreshed without moving execution

#### Scenario: Inspection changes during evaluation
- **WHEN** the selection, stop or external session changes before an evaluation completes
- **THEN** the old response is replaced by an expired-context diagnostic
- **AND** adapter output events remain visible independently

#### Scenario: Output arrives during evaluation
- **WHEN** the adapter emits output while an Immediate request is pending
- **THEN** output and the eventual response are appended without using a stale editor line

#### Scenario: Adapter refuses a line
- **WHEN** the adapter rejects a current-frame evaluation
- **THEN** Immediate displays its error and permits another submission
