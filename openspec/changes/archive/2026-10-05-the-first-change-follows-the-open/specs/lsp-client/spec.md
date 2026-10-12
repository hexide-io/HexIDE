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

#### Scenario: Editing a document
- **WHEN** the developer edits an open document
- **THEN** the complete new text is sent with a version that increases

#### Scenario: The first change after an open
- **WHEN** a document has been opened and is then changed for the first time, by an edit or by a save that
  sends its text
- **THEN** the change carries a later version than the open did

#### Scenario: Closing a document
- **WHEN** a document is closed
- **THEN** the server is notified, so it can release any state held for that document
