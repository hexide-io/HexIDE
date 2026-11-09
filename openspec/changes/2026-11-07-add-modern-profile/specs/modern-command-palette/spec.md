## Purpose
Provide Modern's keyboard search over existing shell actions and backend-supplied symbols (#3301).

## ADDED Requirements

### Requirement: Palette search reaches shared actions
The Modern profile SHALL expose a keyboard-accessible palette searching menu commands, settings and
open files. Command activation SHALL use the same routing, target and enablement as the corresponding
menu action. Settings results SHALL navigate to their setting; file results SHALL activate the selected
open document. Displayed gestures SHALL follow the active keymap. Classic's entry points SHALL remain
unchanged unless separately approved.

#### Scenario: A context-sensitive command
- **WHEN** the user opens the palette from a document and selects a command
- **THEN** activation checks the menu's current enablement against the originating target
- **AND** the palette's search input does not become that command's target

#### Scenario: Searching local sources
- **WHEN** a query matches a setting or an open unsaved document
- **THEN** the result navigates to that setting or activates that document without creating a saved file

### Requirement: Symbols belong to the backend
The palette SHALL display workspace symbols only through a backend that supports `workspace/symbol`.
The IDE SHALL NOT resolve symbols itself or invent missing navigation locations. Missing capability
or failed symbol search SHALL leave local search usable with localized feedback.

#### Scenario: No symbol capability
- **WHEN** the connected backend does not offer workspace symbols
- **THEN** commands, settings and open files remain searchable without local semantic analysis

#### Scenario: Backend results arrive
- **WHEN** the backend returns symbols for the current query
- **THEN** the palette displays those results and uses only provided navigable locations
- **AND** a stale response cannot replace a newer query's results
