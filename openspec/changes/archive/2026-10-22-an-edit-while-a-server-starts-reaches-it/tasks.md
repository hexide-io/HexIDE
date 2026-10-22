# Tasks

## 1. The fix

- [x] 1.1 The document session hands its debounced change and its flush on whether or not a server is running.
- [x] 1.2 The client records, per document, the connection whose server has been opened on it. It does not send an
  open that connection has had, and it does not send a change before that connection's open.
- [x] 1.3 The client's open keeps a recorded change that is newer than the open's text, and sends it.
- [x] 1.4 The registry records an open before restarting for a moved workspace, and opens its claimants with the
  text as recorded when it sends the open.

## 2. Tests

- [x] 2.1 On the wire, with a real session, the real registry and real clients against a server whose start is held:
  an edit, and a save, made while the only server starts each reach it. The server is opened once, before any
  change, and its versions only rise.
- [x] 2.2 The same with two servers, the second still starting: both end up holding the edited text, each opened
  once and before any change.
- [x] 2.3 In the session's own tests, a change made while no server is running is handed on.
- [x] 2.4 In the registry's tests, a change made while a moved workspace stops its old servers is what the open
  sends.
- [x] 2.5 Directly on a client: an open that arrives after a change the client has recorded carries the change, at
  its version, and the change is not sent ahead of it.
- [x] 2.6 Each part of the fix reverted on its own fails at least one of these.

## 3. Record

- [x] 3.1 The lsp-client requirement gains a scenario for an edit made while a server starts, and the known
  divergences under its Purpose drop #719.
- [x] 3.2 `docs/lsp-client.md` says what happens to an edit made while a server starts.
- [x] 3.3 CHANGELOG.
