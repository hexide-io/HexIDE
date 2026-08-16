# code-editor-minimap Specification

## Purpose
Define the minimap: a zoomed-out view of the whole document beside the editor, used to see where you are in
a file and to jump somewhere else.

VB6 had nothing like it, so this is purely additive. It earns its place because of what VB6 files look like:
modules run long, procedures are separated by little more than blank lines, and there is no structural
navigation to fall back on. A shape-level view of the document is a cheap way to make a two-thousand-line
module navigable.

## Requirements
### Requirement: The minimap SHALL represent the whole document, whatever its length
The minimap SHALL depict the entire document within its available height, condensing the representation for
documents too long to show line-for-line rather than showing only part of the file.

A minimap that shows the first screenful and stops is worse than none: it looks like a map of the document
and is a map of its beginning, so it misleads exactly where a long file most needs help. Condensing loses
per-line detail, which is an acceptable trade — the minimap is read as shape, not as text.

#### Scenario: A document longer than the minimap can show line-for-line
- **WHEN** a document has more lines than the minimap has room for
- **THEN** the whole document is still represented across the full height

#### Scenario: A short document
- **WHEN** a document is short enough to show line-for-line
- **THEN** each line is represented individually

### Requirement: The minimap SHALL be syntax-coloured and follow the active theme
The minimap SHALL colour its content using the same syntax highlighting as the editor, and its colours SHALL
come from the active theme.

The colouring is what makes it readable at that size — the developer recognises a region by the pattern of
comments, strings and keywords rather than by reading it. Taking colours from the theme is what stops it
being a bright panel welded to the side of a dark editor, which is how a minimap ends up switched off.

#### Scenario: Viewing a document with the minimap
- **WHEN** a document is shown in the minimap
- **THEN** its content is coloured consistently with the editor's highlighting

#### Scenario: Changing theme
- **WHEN** the active theme changes
- **THEN** the minimap's colours change with it

### Requirement: The minimap SHALL show which part of the document is on screen
The minimap SHALL mark the region currently visible in the editor, and that marker SHALL correspond to the
region the minimap is depicting even where the document is condensed.

The marker is what turns a picture of the file into a position indicator. It has to be derived from the same
mapping the minimap draws with — if the drawing condenses and the marker assumes one line per row, the two
disagree and the indicator drifts further from the truth the longer the file, which is when it matters most.

#### Scenario: Scrolling the editor
- **WHEN** the developer scrolls the editor
- **THEN** the marked region moves correspondingly

#### Scenario: A condensed document
- **WHEN** the document is condensed to fit
- **THEN** the marker still aligns with the region actually shown

### Requirement: The minimap SHALL scroll the editor when clicked or dragged
Clicking the minimap SHALL move the editor to the corresponding part of the document, and dragging SHALL
scroll continuously.

Without it the minimap is decoration. Click-to-jump is the whole interaction: the developer sees the shape
of the region they want and goes there, which is faster than scrolling and does not require knowing a line
number.

#### Scenario: Clicking
- **WHEN** the developer clicks a position on the minimap
- **THEN** the editor scrolls to the corresponding part of the document

#### Scenario: Dragging
- **WHEN** the developer drags on the minimap
- **THEN** the editor scrolls continuously to follow

### Requirement: The minimap SHALL be optional
The developer SHALL be able to turn the minimap off, and the choice SHALL persist.

It costs horizontal space and not everyone wants it — on a narrow window that space is worth more as code.
Making it switchable is also what allows it to be on by default without imposing it.

#### Scenario: Turning it off
- **WHEN** the developer turns the minimap off
- **THEN** it is hidden and the editor uses the reclaimed width
