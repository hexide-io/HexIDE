# modern-document-tabs Specification

## Purpose
Define how open documents are presented: as tabs in a docking host rather than as free-floating child
windows inside the IDE.

This is the one place the project deliberately departs from VB6's shell. VB6 put each editor and designer in
a floating child window inside the main frame, which was the convention of its era and is the convention of
almost nothing since — every developer arriving from any tool of the last twenty years expects tabs. Keeping
child windows would be reproducing an inconvenience rather than a behaviour, which is the line the project
draws generally between VB6's intent and its accidents.

## Requirements
### Requirement: Documents SHALL open as tabs in the document area
Opening a code editor or a designer SHALL present it as a tab in the IDE's document area.
A new document SHALL open in the document area that last held focus, including after focus moves to a
tool pane, falling back to the first area. An already open document SHALL be activated in its own area.

The Object Browser, protocol inspector, Language & Debug Servers and translation editor SHALL follow
this same area selection and activation rule, retaining one tab and one owner after a move.

#### Scenario: Reopening a moved tool document
- **WHEN** a tool document has been moved into a second area and is opened again from View
- **THEN** its existing tab is activated there without adding a tab to the first area
- **AND** closing its tab removes it from the area that shows it

#### Scenario: Opening while using a split area
- **WHEN** the second document area last held focus and a new document is opened
- **THEN** the new tab opens in that second area


One presentation for every document is what makes the shell predictable: the developer learns where things
open once. It also puts documents under the same docking model as everything else, so window management
behaves consistently instead of having a separate set of rules inside one region of the frame.

#### Scenario: Opening a document
- **WHEN** the developer opens a code editor or a designer
- **THEN** it appears as a tab in the document area

#### Scenario: Several documents open
- **WHEN** several documents are open
- **THEN** each has its own tab and the developer switches between them by selecting one

### Requirement: Code documents and designers SHALL remain docked
Code documents and designers SHALL NOT offer Float or Float all in their tab menu. A float attempt through
another route SHALL leave the document docked and listed among the open document tabs. Tool-window
floating SHALL remain available.

#### Scenario: Code tab menu
- **WHEN** the developer opens a code document's or designer's tab menu
- **THEN** neither Float nor Float all is offered

#### Scenario: Programmatic float
- **WHEN** automation attempts to float a code document or designer
- **THEN** the document remains docked and listed among the open document tabs

### Requirement: Docked content SHALL remain fully opaque
The IDE SHALL disable Dock's content-reveal animation before creating any dock controls, so presenting
documents and tools again does not leave their contents semi-transparent.

#### Scenario: Consecutive pane switches
- **WHEN** the developer switches document tabs or tool panes twice consecutively
- **THEN** every deferred content control and presenter in the active document and tool pane has opacity 1 after each switch

### Requirement: Opening an already-open document SHALL activate it
Where a document is already open, opening it again SHALL bring its existing tab to the front rather than
creating a second one.

Two tabs onto one file is a way to lose work: the developer edits one, saves the other, and the change
disappears with no error. Activation is also what the developer meant — asking for a file that is already
open is a request to look at it, not to open it twice.

#### Scenario: Opening a file that is already open
- **WHEN** the developer opens a document that already has a tab
- **THEN** that tab is activated and no second tab is created

### Requirement: A document SHALL be closable from its tab
Each tab SHALL offer closing the document it hosts.

Closing from the tab is where every developer reaches for it. It also matters that the affordance belongs to
the tab rather than to a menu: with several documents open, a close command that acts on "the current one"
requires knowing which that is, whereas a control on the tab is unambiguous.

#### Scenario: Closing a document
- **WHEN** the developer closes a document from its tab
- **THEN** the tab is removed and the remaining documents are unaffected

### Requirement: Tool windows SHALL remain distinct from documents
Tool windows SHALL continue to dock, float and resize independently, and SHALL NOT be presented as document
tabs. Floating a tool SHALL show its view in a visible window owned by the main window, with the tool's
caption and a maximize/restore button. Floating tools SHALL remain visible in the tool-window inventory.
The host caption SHALL follow the active tool and language changes. The initial client size SHALL be
at least the docked pane's size and 320 by 200 DIPs, limited to the main window's screen working area;
the host SHALL open within that working area. Closing the floating window SHALL return its tools to
their original dock slot, proportions and tab group, preserving the saved tab order even when
tools are registered in a different order or the window was restored from the layout manifest.
The pane's own close box SHALL hide the tool; View SHALL reopen it docked.

#### Scenario: Float and return a tool
- **WHEN** the developer chooses Float on a docked tool pane
- **THEN** an owned window shows the tool's view, title and maximize/restore button
- **AND** the tool-window inventory reports the tool as visible
- **WHEN** the developer closes the floating window
- **THEN** the tool reappears docked in the main window at its previous proportion

#### Scenario: Close a floating tool from its pane
- **WHEN** the developer clicks the tool pane's own close box inside a floating host
- **THEN** the tool is hidden without returning to the docked workspace
- **WHEN** the developer opens that tool from View
- **THEN** it reappears docked

#### Scenario: Float and return tabbed tools
- **WHEN** the developer floats a tool dock containing two tabs and closes the host
- **THEN** both tools return together as tabs in the dock's old slot

The distinction is the point of the layout: documents are what is being worked on and tool windows are what
is being worked with. Collapsing them would mean the project tree competing for space with the file it
opens, and would lose the arrangement — panels around a central editing area — the developer set up
deliberately.

#### Scenario: Arranging the workspace
- **WHEN** the developer docks or resizes a tool window
- **THEN** it behaves as a tool window and does not join the document tabs

### Requirement: Tool pane titles preserve their buttons
A docked tool pane SHALL ellipsize a title wider than the available header space, reserving the
width of its visible close and pin buttons. The full title SHALL remain available through the title's
tooltip and the pane's accessible name. This layout rule SHALL NOT change document tab templates.

#### Scenario: A long title in a narrow tool pane
- **WHEN** a tool pane has a title longer than its header at 100% or 150% scaling
- **THEN** the title shows a character ellipsis and the visible header buttons remain inside the pane
- **AND** the tooltip and accessible name retain the full title

### Requirement: Editor tabs indicate unsaved changes
Designer, code and carried-file editors SHALL forward their existing dirty-state signals to Dock's
modified marker. Designer checks SHALL include pending component membership and the owning module's
serialized state for a UserControl or PropertyPage. Code typing SHALL compare only the buffer body with
the model; the serialized model check SHALL run on flush and matching save events, not per keystroke.

#### Scenario: Editing and saving
- **WHEN** a developer deletes a control, commits a designer property edit or edits an editor's text
- **THEN** the affected tab shows the modified marker
- **WHEN** the document is saved and its dirty-state checks are clear
- **THEN** its marker clears without clearing another document's marker

#### Scenario: Deleting after pointer selection
- **WHEN** a developer selects a control by clicking the designer canvas, releases the pointer and presses Delete from a focused child of the designer
- **THEN** the deletion is committed, can be undone and shows the designer tab's modified marker

### Requirement: Recently closed tabs can be reopened during the session
The IDE SHALL retain the last 20 closed code, designer and tool document tabs in newest-first order.
The Window menu SHALL offer Reopen Closed Window, disabled when the history is empty, and a list of
recently closed windows. The rebindable `ReopenClosedDocumentCommand` SHALL default to the platform
command modifier plus Shift+T. Reopening SHALL remove the selected entry, restore its view and, for a
code window, restore its caret position. History SHALL NOT persist across sessions.

#### Scenario: Reopen the last closed code window
- **WHEN** the developer closes a code window and invokes Reopen Closed Window
- **THEN** its code view opens with its previous caret position and its history entry is removed

#### Scenario: A document or project leaves the session
- **WHEN** a document is removed from its project or its project closes
- **THEN** its history entries are removed immediately and tabs closed by that removal are not recorded

#### Scenario: More than twenty windows close
- **WHEN** a twenty-first window closes
- **THEN** the oldest history entry is discarded

### Requirement: Dock tab strips have localized automation names
Document and tool tab strips SHALL expose the localized document-tabs and tool-tabs labels as their
accessible names, overriding the dock title and the library's English fallback.

#### Scenario: Splitting the document area in another language
- **WHEN** the user creates a horizontal or vertical document dock with a non-English language selected
- **THEN** both document strips expose the localized document-tabs name, even if the new dock title is `IDocumentDock`
- **AND** tool strips expose the localized tool-tabs name

### Requirement: Dock structural parts have localized automation names
The dock host, root dock, pinned dock host, pinned tool tabs, dock command bars and documents selector
SHALL expose explicit localized automation names in both the main window and floating tool windows,
overriding the dock title and the library's English fallback.

#### Scenario: Floating a tool in another language
- **WHEN** a tool is floated with German selected
- **THEN** all six kinds of dock parts in both windows expose their German names
- **AND** none of those parts in the floating window announces `IRootDock`

#### Scenario: Changing the language while a tool floats
- **WHEN** the active language changes with a tool window floating
- **THEN** the structural dock names update in both windows
- **AND** automation paths retain their English segments: `Pane[Root dock]` and `Pane[Pinned dock host]`
  in both windows, `Pane[DockControl]` in the main window and `Pane[Dock host]` in floating windows
- **AND** command bars and the selector retain their template-part segments, while pinned strips retain
  their sibling indices

#### Scenario: Resolving a dock path in German
- **WHEN** an automation caller resolves a main-window path through `Pane[DockControl]/Pane[Root dock]`
  or a floating-window path through `Pane[Dock host]/Pane[Root dock]` with German selected
- **THEN** the path resolves to the root dock even though its accessible name is `Stammdock`
