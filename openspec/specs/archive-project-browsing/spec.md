# archive-project-browsing Specification

## Purpose
Allow offline inspection of archive projects and safe source editing without invoking a compiler.

## Requirements

### Requirement: Open archive projects independently of VB6 models
The IDE MUST route supported archive project paths to the archive reader from normal project opening and recent-project reopening. It MUST show the stored hierarchy and a read-only indication for entries without an edit path without registering VB6 modules. twinBASIC text previews MAY attach language servers by extension under opaque archive-entry URIs.

#### Scenario: Open an archive project
- **WHEN** a user opens a supported archive project
- **THEN** Project Explorer shows its ordered hierarchy separately from VB6 projects
- **AND** the project path is available for recent-project reopening

### Requirement: Name archive text to language servers
An available text preview MUST use
`archive-entry:/<percent-encoded segments>?container=<percent-encoded file URI>`.
Entry segments MUST compare ordinally; the container MUST compare as a host file path, ignoring case on Windows.
A twinBASIC source entry (`.twin` or a VB6 source extension, ignoring case) MUST route only to servers
that claim its extension and declare language `twinbasic`, ignoring case. Other entries MUST route by extension.

#### Scenario: A source name contains a space and a hash
- **WHEN** a text entry named `Module # 1.twin` opens with a server claiming `.twin` and declaring `twinbasic`
- **THEN** didOpen names `Module%20%23%201.twin` under the archive-entry scheme and carries its text

### Requirement: Maintain read-only archive document sessions
Matching servers MUST receive open text and close notifications. Reload, reopen and workspace replay MUST
preserve the language label. An unmatched preview MUST remain available without diagnostics. Read-only
entries MUST NOT send changes. Diagnostics MUST reach the matching tab even when a server changes the
container drive letter's case on Windows. Reload MUST recreate sessions and markers.

#### Scenario: Reload an open text entry
- **WHEN** a validated replacement updates an open entry
- **THEN** its old session closes and a new session opens with the replacement text
- **AND** diagnostics reach the new buffer while read-only entries continue to refuse edits

### Requirement: Record archive document conversations
Protocol recording and inspection MUST retain the archive-entry URI as sent.
Pseudonymised exports MUST redact entry and container names, and their disclosure preview MUST name the entry without its container query.

#### Scenario: Inspect and export an entry's open notification
- **WHEN** the user inspects and exports the recorded didOpen
- **THEN** inspection shows its wire URI and export replaces its entry and container names with pseudonyms
- **AND** the disclosure preview shows the entry name

### Requirement: Inspect content without mutations
Known text documents MUST support Find and copy. Except for editable project-backed `.twin` sources, editing and replacement MUST NOT change their buffers or source bytes. Unsupported text previews MUST show an explanatory status. Entry metadata and hidden entries MUST be available through explicit inspection controls. The Project Explorer's Show hidden archive entries toggle MUST be visible only when a twinBASIC archive root or node is selected.

#### Scenario: Find and replace in a read-only archive tab
- **WHEN** the user finds text and invokes replacement
- **THEN** Find selects the text and replacement leaves the document unchanged

#### Scenario: Inspect a designer resource
- **WHEN** the user opens an opaque designer entry
- **THEN** the tab shows metadata and explains that text preview is unavailable

### Requirement: Reload and close archive documents consistently
Explicit reload MUST publish only a validated replacement and update retained open entries by identity. Removed entries MUST close. Failed reload MUST preserve the last good tree and tabs. Closing an archive MUST resolve modified source tabs through the ordinary save-changes prompt before closing its tabs. Reload MUST also resolve unsaved edits before replacing the snapshot.

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
functions in a directive MUST read 0. Other entries MUST NOT be greyed. Reopening a recently closed tab MUST retain
the latest project build type supplied on open or reload and preserve its inactive lines.

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

### Requirement: Browse Office documents through the container seam
The IDE MUST use a shared container abstraction for twinBASIC and Office documents, without
adding a writable Office project type. Opening an Office document from the picker, recent list or path
MUST preserve the loaded workspace and group. A missing VBA project MUST name the file; an unreadable
project MUST show the reader's reason. Failed opens MUST NOT enter recent projects.

#### Scenario: Open an Office document beside a project group
- **WHEN** an Office document with a VBA project opens while a two-project group is loaded
- **THEN** it appears separately and the group's name, order, startup project and saved state remain unchanged

### Requirement: Present Office project structure
An Office root MUST name its project and file, and offer nonempty folders in host objects, Forms,
Modules, Class Modules and References order. Reference captions MUST fall back to Libid when unnamed.
Locked projects MUST show their lock and no modules, with an explanation when opened.

#### Scenario: Open a locked document
- **WHEN** the reader reports a viewing lock
- **THEN** the root is marked locked and no module source can be opened

### Requirement: Present Office source honestly
Source tabs MUST be read-only and state, as the root tooltip does, that Office can execute compiled
p-code differing from the stored source. UserForms MUST open code with a designer-pending notice.

#### Scenario: Open a UserForm module
- **WHEN** the user opens a UserForm from the Forms folder
- **THEN** its decoded code is shown read-only with the source/p-code and pending-designer notices

### Requirement: Name Office modules for language servers
Office source entries MUST use `archive-entry:` URIs whose last segment appends `.bas` for a standard
module, `.cls` for a class or document module, or `.frm` for a UserForm. Browser and automation entry
paths MUST remain unchanged, and automation's entry URI MUST equal the tab's wire URI.

#### Scenario: Open a document module
- **WHEN** the user opens `/Document/ThisWorkbook`
- **THEN** its wire URI ends in `/Document/ThisWorkbook.cls?container=…` while its browser path stays unchanged

### Requirement: Route Office modules as project members
Office modules MUST be offered as project members to servers that establish VB6, routed by extension.
Each server MUST receive its configured language identifier (`vb6` for the bundled server).

#### Scenario: Open a class without an export header
- **WHEN** an Office class module's stored source has no `VERSION 1.0 CLASS` header
- **THEN** a VB6 server claiming `.cls` receives the module as a project member

### Requirement: Keep Office language-server sessions read-only
Office source tabs MUST receive the same read-only diagnostic sessions as archive previews.
Sessions MUST send `didOpen` and `didClose`, with no `didChange`, and close on tab close, container close
or reload. Locked projects MUST expose no source sessions.

#### Scenario: Reload or close an Office source tab
- **WHEN** an Office source tab or its container is closed, or the container is reloaded
- **THEN** the old session sends `didClose`; a retained tab on reload opens a new session and clears old markers

#### Scenario: Browse a locked Office project
- **WHEN** an Office project is locked for viewing
- **THEN** no module source or language-server session is exposed

### Requirement: Respect Office browser capabilities
Office entries MUST NOT receive inactive shading, Show Hidden or Synchronize Folder. Office node and tab
focus MUST refuse implicit build and run targeting a loaded startup project. Opening a document MUST NOT
execute its code or events.

#### Scenario: Read Office source beside a loaded startup project
- **WHEN** an Office source tab is active
- **THEN** diagnostics are shown as markers and implicit build and run commands are unavailable

### Requirement: Save editable twinBASIC sources safely
Available UTF-8 `.twin` entries in a `.twinproj` project MUST be editable with an unsaved marker. Ordinary
Save MUST collect that container's modified sources, preserve their UTF-8 BOM choices and increase each
changed file's revision by 2. Other entries and metadata MUST remain unchanged. The proposed container
MUST be written in memory and read back equal before any disk write. Save MUST refuse externally changed
disk bytes, retain edits on refusal, and use a flushed atomic replacement with recovery of the previous bytes.
On success the project MUST publish a snapshot read from the written bytes and clear the saved markers.
Standalone previews, Office entries and non-`.twin` entries MUST remain read-only. Modified tab close MUST
ask whether to save, discard or cancel. Detecting another IDE with the project open is outside this capability.

#### Scenario: Save a source edit
- **WHEN** an editable source changes and the user invokes ordinary Save
- **THEN** the checked container replaces the unchanged disk baseline with a recovery sibling
- **AND** the snapshot refreshes and the unsaved marker clears

#### Scenario: Another program saved the container
- **WHEN** Save finds disk bytes different from the snapshot's baseline
- **THEN** it refuses with a localized reason naming the file, preserving the disk bytes and unsaved edit
