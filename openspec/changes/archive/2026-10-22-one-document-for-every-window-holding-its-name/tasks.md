# Tasks

## 1. The fix

- [x] 1.1 Sessions holding one name on one client share a record of the document: holders, the last version and the
  last publication.
- [x] 1.2 The first holder opens it, and the last holder closes it.
- [x] 1.3 Every holder draws its versions from the shared sequence, at the moment of the edit.
- [x] 1.4 A rename closes the old name only for its last holder, and opens the new one only for its first.
- [x] 1.5 A window that joins an open document shows its last publication and sends nothing of its own.

## 2. Tests

- [x] 2.1 In the session's tests: a second window sends no second open; two windows draw one sequence of versions;
  closing one of two windows leaves the document open; renaming one of two leaves the old name open for the other; a
  joining window shows what was already published.
- [x] 2.2 On the wire, two windows over one name on one real client: one open, changes from either window at rising
  versions, the remaining window's edit after the other closed, and one close at the end.
- [x] 2.3 Each gate reverted on its own fails at least one of these: the open, the close, the rename's close, the
  shared sequence and the joiner's diagnostics.

## 3. Record

- [x] 3.1 The lsp-client requirement gains a scenario for two windows holding one name, and the known divergences
  under its Purpose drop #721.
- [x] 3.2 `docs/lsp-client.md` says how windows holding one name share its document.
- [x] 3.3 CHANGELOG.
