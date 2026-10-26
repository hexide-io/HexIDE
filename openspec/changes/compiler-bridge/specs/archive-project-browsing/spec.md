## MODIFIED Requirements

### Requirement: Open archive projects independently of VB6 models
The IDE MUST route supported archive project paths to the archive reader from normal project opening and recent-project reopening. It MUST show the stored hierarchy and a persistent read-only indication without registering VB6 modules. An open archive entry MAY be offered to a language server that claims its extension, under an archive entry URI, and its tab MUST stay read-only when one is.

#### Scenario: Open an archive project
- **WHEN** a user opens a supported archive project
- **THEN** Project Explorer shows its ordered hierarchy separately from VB6 projects
- **AND** the project path is available for recent-project reopening

#### Scenario: An entry with a language server
- **WHEN** an archive entry is opened and a configured server claims its extension
- **THEN** the server receives the entry under its archive entry URI, and its diagnostics show on the tab
- **AND** the tab stays read-only
