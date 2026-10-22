# An edit made while a server starts reaches it

## Why

An edit made while a language server was still starting never reached it (hexide-io/HexIDE#719). The server
analysed the text from before the edit until the next edit or a save. With two servers claiming one language, the
second was also opened twice, the second time at a lower version with the text from before the edit. Both broke the
lsp-client requirement that each change carries a version later than the last message about the document.

Three layers each lost the edit in their own way, so no one of them could be fixed alone:

- **The document's session** handed a change on only while the client reported a server running. During a start
  nothing is running, so the change was dropped and recorded nowhere. A save's flush had the same gate, and
  it cancels the pending change first, so a save lost both.
- **The client** let an open replace a change it had already recorded. The registry opens a document only once its
  last claimant has started, with the text it captured when the open began. By then the client had recorded the
  edit, and its reconnect replay had sent it. The registry's open then sent the older text again, at the opening
  version.
- **The client** also sent a change to a server that had not yet been opened on the document, which a foreign server measured here ignores.
  That happens when a second server is still starting: the first is running, so the change goes out.

The window is a server's start-up. That is under a second for the bundled server and longer for a foreign server with more to start. It is easy to
hit from automation, with `open_file` followed at once by an edit.

## What changes

- **The session hands every change on.** The debounced change and the flush no longer check that a server is
  running. The registry and the client already record a change before their own gates, so the open that follows a
  server's start carries it.
- **The client never goes behind what it holds.** An open of a document whose recorded change is newer keeps the
  change and sends it as the open. Each document records which connection's server has been opened on it. An open
  that the replay has already sent on this connection is not sent again, and a change waits for that connection's
  open rather than going ahead of it.
- **The registry opens with the text as recorded when it sends the open**, not as it was when the open began. It
  also records the open before it restarts the servers of a moved workspace. A change made while the old servers
  stop then has somewhere to land.
- **A scenario names the start-up window.** The lsp-client requirement for full synchronization gains one, and the
  known-divergences note under the spec's Purpose drops #719.

## Not changed

- **#720 and #721.** A debounced change crossing the flush that superseded it, and two editors holding one name,
  are separate paths and stay filed. Two editors over one name now share a document's open, rather than each
  sending one, because an open that a connection has already had is not sent again. They still keep two version
  counters and close the document for each other.
- **Who numbers the versions.** The session still captures a change's version together with its text, for the reason
  #470 gave.
