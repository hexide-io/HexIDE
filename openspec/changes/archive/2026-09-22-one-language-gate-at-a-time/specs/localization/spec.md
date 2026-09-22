## ADDED Requirements

### Requirement: Only one language change SHALL await confirmation at a time
While a language change is awaiting confirmation, another language change SHALL be refused, and nothing SHALL
be applied by the refused change.

A change that is not confirmed reverts to the language active when it was made. A second change made while the
first is unconfirmed would record the first's unconfirmed language as the one to return to, so whichever
resolved last would decide where the IDE ended up. That could leave the IDE in a language nobody kept.

#### Scenario: A second change while the first is unconfirmed
- **WHEN** a language change is awaiting confirmation and another change is requested
- **THEN** the second is refused, the first's confirmation is still the only one open, and not confirming it returns the IDE to the language it had before either
