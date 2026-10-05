# The first change to a document follows its open

## Why

A document's first `textDocument/didChange` carried the same version as its `textDocument/didOpen`, which is 1
(hexide-io/HexIDE#470). The client sent the open at a hardcoded 1, and the document's session counted its changes
from 0, so its first change was 1 again. LSP has a document's version rise with every change, and the lsp-client
spec already says a change is sent "with a version that increases". The code met that from one change to the next
but not from the open to the first change, and no test compared those two.

It was harmless while no server read the version. A foreign server measured against HexIDE now does: it discards
a change whose version is not later than the document's, so a single edit, or a paste, followed by a pause was
lost to it until the next edit or a save. Its diagnostics, folds and symbols described the text before the edit meanwhile.

## What changes

- **One named opening version.** `LspDocumentVersion.Opening` is the version every `didOpen` carries, and a
  document's session counts its changes on from it. The two halves of the sequence used to agree only by
  accident of two literals, and nothing at the session's end said what it had to follow.
- **A scenario names the open.** The lsp-client requirement for full synchronization gains a scenario saying the
  first change after an open carries a later version than the open did.

## Not changed

- **Who owns the count.** Moving the counter into the client would remove the second counter, but the session
  captures a change's version together with its text at the moment of the edit. A counter assigned when a message
  is sent would number sends rather than edits, so if two sends ever crossed, the older text would carry the
  higher number and a server ordering by version would keep it.
- **Paths where the version still does not rise.** Review of this change found three. All predate it, none is
  reached by it, and each is filed: an edit made while a server is starting (#719), a debounced change crossing
  the flush that superseded it (#720), and two editors holding one name (#721).
- **`ILspClient`'s signatures.** The opening version is named rather than passed, so no caller or test double
  changes.
