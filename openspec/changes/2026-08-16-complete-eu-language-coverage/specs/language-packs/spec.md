## ADDED Requirements

### Requirement: The shipped set SHALL cover every official language of the European Union
A pack SHALL ship for each of the European Union's official languages, and removing one SHALL be treated as
a regression rather than a routine change.

Complete coverage of a defined set is a claim that can be stated in one sentence and verified by anyone;
"most of them" is not. Writing it down as a requirement is what stops the set eroding — a pack dropped
during some future reorganisation would otherwise cost the claim without anyone noticing it had been made.

#### Scenario: Checking European coverage
- **WHEN** the shipped languages are compared against the European Union's official languages
- **THEN** every one of them has a pack

#### Scenario: Removing a pack
- **WHEN** a pack for an official EU language is proposed for removal
- **THEN** it is treated as a regression against this requirement
