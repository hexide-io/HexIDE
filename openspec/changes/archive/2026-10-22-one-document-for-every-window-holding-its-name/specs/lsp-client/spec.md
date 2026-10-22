## MODIFIED Requirements

### Requirement: Documents SHALL be synchronized in full
The client SHALL send the entire document text on open and on every change, rather than a delta, and SHALL
notify the server when a document closes. Each change SHALL carry a version later than the last message about
that document, the open included.

Full synchronization removes an entire class of desynchronization bug — a dropped or misapplied delta
leaving the server's copy silently diverged from the editor's — at a cost that is negligible for source
files of the size VB6 projects contain.

The version is the server's only way to order what it receives, and a server may discard a change that is not
later than what it holds. The open counts: a first change that repeated the open's version was lost to such a
server (hexide-io/HexIDE#470).

A change made while a server is still starting SHALL NOT be lost. That server SHALL be opened on the document once,
before any change to it, with the text as it stands when the open is sent. Starting a server takes long enough to
type in, and an edit dropped in that window left the server analysing the text from before it until the next
edit (hexide-io/HexIDE#719).

A document SHALL be one document to a server however many editors hold its name. Its first holder SHALL open
it, every holder SHALL draw its versions from one sequence, and its last holder SHALL close it. Two windows over one
file kept two version counts, so a server discarded whichever was behind, and either window's close closed the
document for both (hexide-io/HexIDE#721).

#### Scenario: Editing a document
- **WHEN** the developer edits an open document
- **THEN** the complete new text is sent with a version that increases

#### Scenario: The first change after an open
- **WHEN** a document has been opened and is then changed for the first time, by an edit or by a save that
  sends its text
- **THEN** the change carries a later version than the open did

#### Scenario: An edit while a server is starting
- **WHEN** a document is edited, or saved, while a server it is opened to is still starting
- **THEN** that server is opened on the document once, before any change to it, with the text as edited
- **AND** every later message about the document carries a later version than the one before

#### Scenario: Two editors holding one name
- **WHEN** a second editor opens a document whose name another editor already holds, and either then edits it
- **THEN** the server has been opened on the document once, and every change carries a later version than the one
  before, whichever editor it came from
- **AND** closing one editor leaves the document open for the other, and closing the last closes it

#### Scenario: Closing a document
- **WHEN** a document is closed
- **THEN** the server is notified, so it can release any state held for that document
