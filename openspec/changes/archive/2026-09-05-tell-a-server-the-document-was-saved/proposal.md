# Tell a server the document was saved

## Why

HexIDE never sends `textDocument/didSave`, to any server. The client has open, change and close, and
nothing else.

A server that defers its analysis to save therefore looks, from inside HexIDE, exactly like a server that
is broken: it connects, it initializes, and it answers nothing. There is no error, no log line, and
nothing on screen — the same silent shape as the URI-matching and capability-shape defects the
foreign-server work already found twice.

**Deferring to save is the normal choice for anything expensive** — a type checker, a whole-project
linter, anything that shells out. So the class of server most worth attaching is the class most likely to
hit this, and it lands precisely where a user has the least evidence about what went wrong.

It is also a conformance gap rather than a missing convenience. A server declares what it wants through
`textDocumentSync.save`; a client that never sends it is not honouring the synchronization contract it
agreed to at `initialize`. HexIDE already reads `textDocumentSync` carefully — it is the one capability
field whose two shapes mean different things — so the reader is most of the way there.

Both editors are affected equally, which is why this is a client change rather than an editor one.

## What Changes

- **`ILspClient` gains `SaveDocumentAsync`**, implemented by the connection and fanned out by the router
  to the servers claiming that document.

- **The client declares `textDocument.synchronization.didSave`**, and gates sending on what the server
  declares in return. Both halves must land together: gating without declaring is a state in which both
  parties are correct and nothing happens.

- **`includeText` is honoured.** The text is sent only when the server asked for it, and is omitted
  entirely — not sent as `null` — when it did not.

- **The signal comes from where bytes reach disk**, not from an editor. A `DocumentSavedEvent` published
  by the project service's save paths, so that File > Save Project, the toolbar button and the
  close-prompt batch all count, none of which involve an editor at all.

- **The document is flushed before the save is announced**, so a server that re-analyses on save is
  re-analysing what was written.

## What This Does Not Do

- **The bundled VB6 server does not gain `save`.** It defers nothing: it reparses on every open and
  change, and its own spec pins that. Advertising a capability it does not implement is the defect that
  `VbServerCapabilities` exists to prevent, pointed the other way. Leaving it save-less also makes it a
  **control** — proof that the gate refuses correctly.
- **No `willSave` or `willSaveWaitUntil`.** Same rule: nothing implements them.

## Design Notes

**Why a bare `textDocumentSync: 1` counts as asking for saves.** Read strictly, a number carries no
options object, so no `save`, so nothing should be sent. Read as the reference implementation does — and
as server authors therefore test against — a non-zero number resolves to `{openClose, change,
save: {includeText: false}}`. HexIDE follows the second reading, because the first leaves this issue's
exact symptom in place for every number-form server, and because `AcceptsOpenClose` already takes the
ecosystem reading of that same number form. Taking the strict reading here and the loose one there would
be the genuinely indefensible combination.

**The object form stays default-deny, which is the opposite polarity to its neighbours.** An object with
no `save` means no `didSave`, where an object with no `change` means changes are still sent. That is not
an inconsistency to be tidied away: `save` is an opt-in a server states, while the others describe a
default a server may narrow. Worth a comment at the reader, or the three get harmonised later by someone
reading them as a mistake.

**Why the save signal is not raised in the editors.** Two reasons, and the second is worse than the
first. The editors miss the primary gesture — Save Project writes every form and module with no editor
involved. And the code editor's Ctrl+S is bound to the form path for *every* document it hosts, so for a
`.bas` or `.cls` it throws into a swallowing handler and does nothing at all; a save notification raised
there would never fire for the two commonest module kinds. Publishing where the file is actually written
sidesteps that rather than inheriting it.

**A saved VB6 document tells its server less than a saved file does.** The IDE's own documents are named
by a scheme URI with no file behind it, so a server that asked for `save` without text learns only that
something happened. That is honoured rather than worked around — overriding a negotiated answer makes the
client unpredictable to a server author, and skipping the notification for such documents would
discriminate between two documents for a reason the caller cannot see. The real fix is that a saved
project's documents *do* have files, and naming them otherwise is a separate question.
