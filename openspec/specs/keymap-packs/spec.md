# keymap-packs Specification

## Purpose
Define how keyboard shortcuts are bound: as data rather than compiled constants, so a shortcut can be
changed without changing code.

Shortcuts are the deepest muscle memory a developer has, and they are also where a familiarity-first project
runs into a genuine conflict — VB6's bindings and modern conventions disagree, and some keys are wanted by
two commands at once. Making bindings data means those conflicts are resolved by choosing a keymap rather
than by arguing about a default.

This is one of three independent pack systems following the same shape: this one swaps gestures,
`theme-packs` swaps colours, `language-packs` swaps text.

Disk discovery closes the embedded-only gap tracked in [#414](https://github.com/hexide-io/HexIDE/issues/414).

## Requirements

### Requirement: Window commands transfer keyboard focus
The View Code, Properties, Project Explorer, Immediate and Object Browser commands SHALL transfer keyboard
focus into the requested window after its content is shown, including when that window is already open.
Code and Immediate SHALL focus their text area, Properties its visible property list, Project Explorer its
tree and Object Browser its search input (#3393).

#### Scenario: Invoking a window shortcut from the designer
- **WHEN** a window command is invoked while the form designer has keyboard focus
- **THEN** the requested window is shown and its input control receives keyboard focus
- **AND** invoking the command again after returning to the designer transfers focus again

### Requirement: Shortcuts SHALL be declared as data
A keymap SHALL be a data file mapping commands to gestures, and adding one SHALL require no code change.

With bindings compiled in, every disagreement about a shortcut is a code change and a release. As data, a
keymap is something a user can hold an opinion about and act on — and the project can ship more than one
opinion rather than having to pick the single correct answer.

#### Scenario: Adding a keymap
- **WHEN** a JSON keymap is placed in the active user-data directory's `keymaps` subdirectory before startup
- **THEN** its filename without `.json` appears in Options → Keymap and is the persisted selection name
- **AND** applying it reads that file and rebinds commands without restarting
- **AND** `Default` and `VB6` remain reserved built-in names, compared case-insensitively
- **AND** unreadable or invalid packs revert to Default when applied

Pack files use the existing `name`, optional `description`, and `bindings` JSON fields. `bindings` maps
command names to gesture strings; an empty string clears a binding. Unmentioned commands retain their
factory gestures and unknown command names are logged and skipped. Discovery is top-level only, sorted
by ordinal file path; duplicate names differing only by case use the first file. A missing or unreadable
directory leaves the built-in choices available. Adding files requires another startup for discovery;
edits to an already discovered file are read on its next application.

### Requirement: User overrides survive pack selection
The active user-data directory's `keymaps/overrides/bindings.json` SHALL override factory, pack or
profile bindings before observers are notified. Its `bindings` mapping SHALL replace gestures with
strings, clear them with empty strings, and retain omitted commands' baselines. Unknown commands SHALL
be logged and skipped. Invalid known gestures or unreadable JSON SHALL reject the whole layer while
retaining the baseline. Recovery mode SHALL bypass the file.

#### Scenario: Keeping personal bindings across pack changes
- **WHEN** a user switches packs or reapplies an updated pack with an override file present
- **THEN** the personal bindings take precedence and unrelated commands follow the selected pack
- **AND** removing an override restores that command's selected-pack binding on the next application
- **AND** the file is read again on each application, without watching for external edits

#### Scenario: A profile provides the baseline
- **WHEN** a profile supplies a keymap payload and user overrides exist
- **THEN** the user overrides take precedence without changing the profile's keymap name

### Requirement: Options stages individual shortcut edits
The Keymap page SHALL list every command in `CommandKeyMapping.Table` with its localized name where
available, effective gesture and pack or user source. Ambiguous labels SHALL include their localized
menu path, also used in conflict prompts. Rows and the search box SHALL have accessible names.
Captured punctuation and Enter SHALL display recognizable key labels rather than enum identifiers. Search SHALL match names, command identifiers and
gestures. Set SHALL capture a pressed gesture, Clear SHALL create an empty override, and Reset SHALL
remove one override. Switching packs SHALL preserve staged overrides.

#### Scenario: Editing a shortcut
- **WHEN** a user sets, clears or resets a command on the Keymap page
- **THEN** its displayed gesture and source change without rebinding the running IDE
- **AND** switching packs preserves the edits and previews the new pack's other bindings

### Requirement: Captured conflicts require confirmation
A conflicting captured gesture SHALL name the other commands and require Keep or Cancel before assignment.
Keep SHALL NOT remove the other commands' bindings.

#### Scenario: Keeping a conflicting gesture
- **WHEN** a captured gesture is used by another command
- **THEN** the page names that command and offers Keep and Cancel
- **AND** Keep assigns the gesture to the selected command without silently unbinding the other

### Requirement: Options manages bulk overrides transactionally
Reset All SHALL remove every staged override. Import and Export SHALL use normal file pickers and the
existing overrides JSON format. Import SHALL validate the whole file before replacing staged overrides,
explaining a refusal without changing them. A missing bindings object SHALL have a localized explanation.
Export SHALL preserve literal plus signs for hand editing. OK SHALL write overrides atomically before applying them.
Cancel SHALL leave live and saved bindings unchanged. Recovery mode SHALL NOT write overrides.

#### Scenario: Importing invalid overrides
- **WHEN** JSON or a known command's gesture is invalid
- **THEN** the page explains the refusal and every staged binding remains unchanged

#### Scenario: Applying or discarding staged overrides
- **WHEN** the user edits overrides individually or in bulk
- **THEN** OK saves and applies them while Cancel discards them
- **AND** Export writes the staged overrides without applying them

### Requirement: Every routed application command is bindable
Keymap command names SHALL cover every public static routed command in `ApplicationCommands`, using
its field name and the same command instance. Non-routed helpers SHALL NOT appear in that table.

#### Scenario: Binding a previously omitted command
- **WHEN** a pack assigns or clears a gesture for Call Stack, Reset Window Layout or Go To Declaration
- **THEN** the corresponding routed command receives that binding
- **AND** applying Default restores its factory gestures

#### Scenario: Code-window gestures follow the pack
- **WHEN** a pack clears or moves Find References, Go To Definition, Go To Declaration or Format Document
- **THEN** the code window stops invoking that command through its former gesture
- **AND** a former gesture assigned to another command invokes that command instead

### Requirement: A keymap SHALL take effect without restarting
Applying a keymap SHALL rebind commands in the running IDE. Command-backed menu items SHALL display the
first live command gesture, or no shortcut when unbound, immediately after applying the keymap.
Menu placements SHALL NOT override that gesture with a literal accelerator.

The only way to evaluate a keymap is to use it, and a restart between each adjustment makes tuning one
impractical. Live rebinding also means a conflict is discovered immediately rather than at the next launch.

#### Scenario: Applying a keymap
- **WHEN** the user selects a different keymap
- **THEN** the shortcuts change immediately

### Requirement: The default keymap SHALL require no pack
The IDE SHALL have a working set of shortcuts with no keymap file loaded.
Every binding documented in `Default.json` SHALL match the declared factory gesture, accounting for
the platform command modifier and the existing macOS Replace and Toggle Fold exceptions.

Factory gestures are the baseline, so a missing or malformed file cannot leave the IDE without shortcuts.

#### Scenario: No keymap selected
- **WHEN** no keymap pack is applied and no personal override replaces a binding
- **THEN** the IDE's built-in shortcuts are in force

#### Scenario: Default debug shortcuts
- **WHEN** Default is in force
- **THEN** Step Over uses Shift+F8, Step Out uses the platform command modifier plus Shift+F8,
  Run to Cursor uses the platform command modifier plus F8, Quick Watch uses Shift+F9,
  and Set Next Statement uses the platform command modifier plus F9

#### Scenario: Default Components shortcut
- **WHEN** Default is in force
- **THEN** Components uses the platform command modifier plus T
- **AND** Project → Components displays that gesture

### Requirement: A VB6 keymap SHALL be available
A keymap reproducing VB6's shortcuts SHALL ship.

This is the whole point for the audience: a developer with twenty years of VB6 in their fingers should be
able to have those keys back. Shipping it as a pack rather than as the default is what lets both audiences
be served — the keys that conflict with modern convention are a choice rather than an imposition.

#### Scenario: Choosing VB6 shortcuts
- **WHEN** the user selects the VB6 keymap
- **THEN** VB6's shortcuts are in force, including where they differ from the default

### Requirement: A gesture claimed by two commands SHALL be resolvable without code changes
Where two commands want the same gesture, a keymap SHALL be able to decide which gets it.

Contested keys are not an edge case here — the IDE is reproducing one product's conventions while living
alongside another's, so collisions are structural. If resolving one needed a code change, every keymap would
be constrained by the default's choices; letting a pack reassign both sides makes each keymap internally
coherent.

#### Scenario: Two commands wanting one gesture
- **WHEN** a keymap assigns a contested gesture to one command
- **THEN** that command receives it and the other does not, without either being changed in code
