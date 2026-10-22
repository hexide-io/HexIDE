# One document for every window holding its name

## Why

Two code windows over one name shared a single LSP document, but each kept its own version counter and closed the
document for both (hexide-io/HexIDE#721). Since #273 a saved document is named by its file, so the case that remains
is one file shared by two projects of a group, which VB6 allows and which happens. On the one connection:

- **A second open with no close between.** The second window opened a document the server already held.
- **Two counters interleaved.** One window sent 7, 8, … while the other sent 2, 3, …. A server that orders by
  version discarded the second window's edits until its count passed the first's.
- **Closing either window closed the document for both.** The survivor's next edit went out as a change to a
  closed document.

## What changes

- **The windows share the document, per client.** Every session holding a name on a client shares one record of it:
  how many hold it, the version its last message carried, and what was last published for it. The first holder opens
  it. Every holder draws its versions from the one sequence, still at the moment of the edit and together with its
  text. The last holder to let go closes it.
- **A rename follows the same rule.** The old name is closed for the rename only by its last holder, because that
  close also withdraws the name's diagnostics on every connection and in the ledger. The new name is opened only by
  its first holder.
- **A window that joins sends nothing of its own.** The server holds the document as the windows last edited it, and
  sending the joining window's copy as it opened would undo another window's unsaved edits on the server. The joining
  window shows what was last published for the document instead, so it is not left waiting on an answer to an open it
  did not send. Its own edits reach the server as the next versions in the shared sequence.
- **A scenario names the shared document.** The lsp-client requirement for full synchronization gains one, and the
  known-divergences note under the spec's Purpose drops #721, leaving #720.

## Not changed

- **The client and the registry.** The shared record lives in the document session, beside the version a change is
  drawn with. Numbering where messages are sent would let two crossing sends give older text the higher number, the
  reason #470 gave for capturing a version with its text. Since #719 the client also declines an open that its
  connection already had. That stays as a backstop, but with this change the client never sees the second window's
  open at all.
- **What each window shows.** Both windows still accept any publication for the name, because it is one document.
  When their buffers differ, the server holds whichever text was sent last, as it did before.
- **#720**, a debounced change crossing the flush that superseded it, stays filed.
