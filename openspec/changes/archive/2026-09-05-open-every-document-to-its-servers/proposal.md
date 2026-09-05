# Open every document to its servers, not only the ones HexIDE compiles

## Why

The previous change made the server list configuration and made routing key on the file extension, so a
user can attach a server for a file type HexIDE has no support for. Every part of that is exercised and
proven — against a server this project did not write, named only in a file on disk.

**And a configured server still has nothing to serve.** A project's carried files — a `RelatedDoc=` in the
`.vbp`: a README, a changelog, a `.sql` — open in `RelatedDocumentEditorViewModel`, which contains no LSP
code at all. Not a partial implementation: zero mentions of the client, of diagnostics, or of markers,
against forty-one in the VB6 code editor. It never opens the document to the language layer, never sends a
change, never subscribes to diagnostics.

So the one editor that opens the file types a foreign server exists for is the one editor that does not
speak to it. A server can be configured, registered, routed and started, and no document will ever arrive.

The gap survived because it is invisible from both sides. Nothing in the language layer is wrong, so its
tests pass. Nothing in the carried-file editor claims to do this, so its tests pass. It was found by
opening a Markdown file in the running IDE and watching no server start.

## What Changes

- **The document lifecycle becomes a shared thing both editors own.** `LspDocumentSession` holds the
  document's URI, opens and closes it, sends debounced changes, and converts published diagnostics into
  editor markers. The VB6 code editor moves onto it; the carried-file editor gains it.

- **A carried file is identified by a `file:` URI.** The IDE's own documents use a `vb6://` scheme that
  names their language, because they have no extension. A carried file is a real file on disk and its
  extension is the whole basis on which a server claims it.

- **Diagnostics render in the carried-file editor**, through the same marker service the code editor uses —
  both views host the same editor control, so it attaches unchanged.

- **A carried file no server claims costs nothing.** No server starts, no error appears. That is already
  what the routing requires; this change must not make an unclaimed file pay for the claimed ones.

## What This Does Not Do

- **No hover, completion, signature help, formatting or rename** (#266). Those are view work — in the VB6
  editor they are completion windows and hover popups in the code-behind, and the carried-file view has
  none of it. Folding them in would double this change and mix *the document reaches the server* with *the
  editor renders the answers*, which fail differently and are reviewed differently.
- **No `didSave`** (#267). HexIDE sends none today, to any server, which makes a server that defers its
  analysis to save look broken. That is a client-and-protocol gap affecting both editors equally, not
  editor wiring.

## Design Notes

**Why shared rather than copied.** The extractable core is about ninety lines, which is small enough to
argue for copying — and copying is wrong here, because the subtle parts are exactly the parts that must not
drift. Diagnostics are matched with `LspDocumentUri.AreSame` rather than `!=`, because a server that
normalises the URI it echoes back silently drops every diagnostic it publishes; that cost a real
investigation. The range-to-offset conversion clamps in three places against a document the server has a
stale copy of. A second implementation would not reproduce those, and its failure would look like
"diagnostics are slightly wrong in the other editor", which is the kind of defect nobody files.

**Why the code editor moves onto it too, rather than only the new editor gaining it.** Extracting a shared
type and then using it in one place pays the design cost and keeps the duplication. The migration is
narrower than the class's size suggests: the per-request methods keep calling the client directly with the
session's URI, and only the lifecycle moves.

**Why the carried-file editor is still not a branch of the code editor.** It was deliberately kept separate
— that class carries a form and a module as sibling nullable fields with consumers re-deriving which kind
they hold, and its save path is already wrong for the second kind. Sharing one narrow collaborator is the
opposite of merging them: it is what makes keeping them separate affordable.
