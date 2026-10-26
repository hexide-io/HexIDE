## ADDED Requirements

### Requirement: An entry's arguments SHALL be adjustable without replacing it
The configuration SHALL let a user add arguments to an entry by its identifier, apart from the entry, so that an entry whose command the user cannot write can still be adjusted. The arguments SHALL be added to the entry's own, never replace them, and SHALL apply to whichever entry holds the identifier after layering. They SHALL be added in the entry's own form, and a different form SHALL be reported and left out.

#### Scenario: An argument for the bundled server
- **WHEN** the configuration adds arguments for the bundled server's identifier
- **THEN** the bundled server is started with its found command and those arguments

#### Scenario: Arguments in a different form
- **WHEN** an entry's arguments are a command line and the added ones are a list, or the reverse
- **THEN** the entry is left as it was and the problem is reported

#### Scenario: Arguments for no server
- **WHEN** added arguments name an identifier no entry has, or an entry the IDE starts no process for
- **THEN** they are reported and change nothing
