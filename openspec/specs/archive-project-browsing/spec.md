# archive-project-browsing Specification

## Purpose
Allow offline inspection of archive projects without modifying project content or invoking a compiler.

## Requirements

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

### Requirement: Grey a source entry's inactive conditional branches as the archive's compiler compiles them
A `.twin` entry's preview MUST grey the lines its `#If` directives leave out under the archive compiler's measured
built-ins at 32-bit (#1691), in the theme's `InactiveCodeTextBrush`. Under that compiler a call to one of VB's
functions in a directive MUST read 0. Other entries MUST NOT be greyed.

#### Scenario: A branch the compiler's 32-bit build leaves out
- **WHEN** the user opens a `.twin` entry whose `#If Win64 Then` branch has an `#Else`
- **THEN** the `Win64` branch is grey and the `#Else` branch is drawn as code

### Requirement: Leave branches ungreyed that the archive cannot decide
A built-in the archive does not record MUST leave the branches it decides ungreyed: the compiler's build number
always, and its build type when the project's settings name none. A directive that cannot be evaluated MUST leave
the whole entry ungreyed.

#### Scenario: A condition on the build number
- **WHEN** a `.twin` entry tests the compiler's build number, which the archive does not record
- **THEN** neither branch of that test is grey

### Requirement: Report refused archive opens without unloading projects
The IDE MUST report a refused archive open with the selected file name and the reader's reason. Opening an archive from the project picker MUST preserve already open projects and their documents on failure.

#### Scenario: The reader refuses the selected archive
- **WHEN** the user selects a malformed archive in Open Project
- **THEN** an error message names the file and includes the reader's reason
- **AND** the existing projects and documents remain open
- **AND** the refused archive is not added to recent projects
