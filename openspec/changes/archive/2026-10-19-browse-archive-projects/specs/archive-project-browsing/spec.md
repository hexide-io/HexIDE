## Purpose
Allow offline inspection of archive projects without modifying project content or invoking a compiler.

## ADDED Requirements

### Requirement: Open archive projects independently of VB6 models
The IDE MUST route supported archive project paths to the archive reader from normal project opening and recent-project reopening. It MUST show the stored hierarchy and a persistent read-only indication without registering VB6 modules or attaching a language server.

#### Scenario: Open an archive project
- **WHEN** a user opens a supported archive project
- **THEN** Project Explorer shows its ordered hierarchy separately from VB6 projects
- **AND** the project path is available for recent-project reopening

### Requirement: Inspect content without mutations
Known text documents MUST support Find and copy. Editing and replacement MUST NOT change their buffers or source bytes. Unsupported text previews MUST show an explanatory status. Entry metadata and hidden entries MUST be available through explicit inspection controls.

#### Scenario: Find and replace in an archive tab
- **WHEN** the user finds text and invokes replacement
- **THEN** Find selects the text and replacement leaves the document unchanged

#### Scenario: Inspect a designer resource
- **WHEN** the user opens an opaque designer entry
- **THEN** the tab shows metadata and explains that text preview is unavailable

### Requirement: Reload and close archive documents consistently
Explicit reload MUST publish only a validated replacement and update retained open entries by identity. Removed entries MUST close. Failed reload MUST preserve the last good tree and tabs. Closing an archive MUST close its tabs without saving content.

#### Scenario: Reload fails validation
- **WHEN** an external replacement is invalid and the user reloads
- **THEN** the existing snapshot and previews remain available

### Requirement: Prevent implicit startup fallback from archive focus
Selecting an archive entry or activating an archive tab MUST NOT implicitly target a loaded VB6 startup project for build, run or project mutations. Explicitly targeted VB6 operations and controls for an existing debug session MAY remain available.

#### Scenario: Archive selected in a mixed workspace
- **WHEN** an archive is selected while a VB6 startup project is loaded
- **THEN** implicit startup build, run and add-item commands are unavailable
