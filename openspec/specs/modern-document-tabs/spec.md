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

One presentation for every document is what makes the shell predictable: the developer learns where things
open once. It also puts documents under the same docking model as everything else, so window management
behaves consistently instead of having a separate set of rules inside one region of the frame.

#### Scenario: Opening a document
- **WHEN** the developer opens a code editor or a designer
- **THEN** it appears as a tab in the document area

#### Scenario: Several documents open
- **WHEN** several documents are open
- **THEN** each has its own tab and the developer switches between them by selecting one

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
tabs.

The distinction is the point of the layout: documents are what is being worked on and tool windows are what
is being worked with. Collapsing them would mean the project tree competing for space with the file it
opens, and would lose the arrangement — panels around a central editing area — the developer set up
deliberately.

#### Scenario: Arranging the workspace
- **WHEN** the developer docks or resizes a tool window
- **THEN** it behaves as a tool window and does not join the document tabs
