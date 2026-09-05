## ADDED Requirements

### Requirement: A server that asked to hear about saves SHALL be told about them
The client SHALL declare that it can send save notifications, and SHALL send one when a document it has
opened is written to disk — to each server claiming that document that asked for them, and to no other.
Whether a server asked SHALL be read from what it declared at initialization.

A server that defers its analysis to save is otherwise indistinguishable, from inside the IDE, from one
that is broken: it connects, it initializes, and it answers nothing. Deferring is the ordinary choice for
anything expensive, so the servers most worth attaching are the ones most likely to be silenced by this.

Declaring and gating SHALL land together. A client that gates on what the server declared without first
declaring its own half creates a deadlock in which both parties are correct and nothing happens: the
server withholds the capability because the client never claimed it, and the client declines to send
because the server did not ask.

#### Scenario: A server that asked for save notifications
- **WHEN** a document it has open is saved
- **THEN** it is told, after the current text has been sent

#### Scenario: A server that did not ask
- **WHEN** a document it has open is saved
- **THEN** it is not told, and nothing is reported as an error

#### Scenario: Two servers claiming one document, only one of which asked
- **WHEN** the document is saved
- **THEN** only the server that asked is told

### Requirement: A save notification SHALL carry the text only when the server asked for it
Where a server declared that save notifications should include the document's text, the client SHALL send
the text it last gave that server. Where it did not, the client SHALL omit the text entirely rather than
sending an empty or null value.

The distinction is not cosmetic. A server tests whether the field is present to decide between reading
the document from disk and using what it was handed, so a null sent in place of an absent field selects
the wrong branch — and it does so silently, which is the failure mode this capability's whole area keeps
producing.

The client SHALL send the document's current text, not a version the server has yet to receive. A save
announced against text the server was never given describes a file it cannot see, which is worse than not
announcing it at all.

#### Scenario: The server asked for text
- **WHEN** a saved document is announced to it
- **THEN** the text accompanies the notification, and matches what the editor wrote

#### Scenario: The server did not ask for text
- **WHEN** a saved document is announced to it
- **THEN** the notification carries no text field at all

#### Scenario: A document saved while an edit is still settling
- **WHEN** the document is saved before pending changes have been sent
- **THEN** those changes are sent first, so the save describes what the server holds

### Requirement: Every write of a document to disk SHALL count as a save
A save notification SHALL follow any path that writes an open document to its own file, including paths
that involve no editor — saving a project, saving every project, and saving from the prompt shown when
something closes with unsaved work.

Raising this in an editor would miss the IDE's primary save gesture, which writes every form and module
without an editor being involved at all. Writing a copy elsewhere is not a save of the document and SHALL
NOT be announced: building an executable writes every document to a temporary location and then restores
the model, and announcing those would report saves the developer never made.

#### Scenario: Saving the project from the File menu
- **WHEN** the developer saves a project whose documents are open
- **THEN** each open document's servers are told it was saved

#### Scenario: Building an executable
- **WHEN** the documents are written to a temporary location as part of producing a binary
- **THEN** no save is announced, because the developer's files were not written
