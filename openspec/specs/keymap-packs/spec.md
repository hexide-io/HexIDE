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

## Requirements
### Requirement: Shortcuts SHALL be declared as data
A keymap SHALL be a data file mapping commands to gestures, and adding one SHALL require no code change.

With bindings compiled in, every disagreement about a shortcut is a code change and a release. As data, a
keymap is something a user can hold an opinion about and act on — and the project can ship more than one
opinion rather than having to pick the single correct answer.

#### Scenario: Adding a keymap
- **WHEN** a new keymap is contributed
- **THEN** it is a data file mapping commands to gestures, with no code change

### Requirement: A keymap SHALL take effect without restarting
Applying a keymap SHALL rebind commands in the running IDE.

The only way to evaluate a keymap is to use it, and a restart between each adjustment makes tuning one
impractical. Live rebinding also means a conflict is discovered immediately rather than at the next launch.

#### Scenario: Applying a keymap
- **WHEN** the user selects a different keymap
- **THEN** the shortcuts change immediately

### Requirement: The default keymap SHALL require no pack
The IDE SHALL have a working set of shortcuts with no keymap file loaded.

The bindings declared alongside the commands are the baseline, and treating that baseline as a pack would
mean a missing or malformed file could leave the IDE with no shortcuts at all. Keeping it as the state where
nothing has been applied makes the failure mode benign.

#### Scenario: No keymap selected
- **WHEN** no keymap pack is applied
- **THEN** the IDE's built-in shortcuts are in force

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
