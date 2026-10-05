# Tasks

## 1. The sequence

- [x] 1.1 Name the opening version once, in `HexIDE.Core`, and use it for the client's `didOpen` and its tracked
  version.
- [x] 1.2 Start a document session's count at the opening version, so its first change carries the next one.

## 2. Tests

- [x] 2.1 On the wire, with a real client and a real session: the open carries the opening version; a single edit
  and a save before any edit each carry a later one.
- [x] 2.2 Against a server that discards a change no later than what it holds, a single edit is
  the text the server ends up holding.
- [x] 2.3 In the session's own tests, the first change carries the version after the open's.

## 3. Record

- [x] 3.1 The lsp-client requirement gains a scenario for the first change after an open.
- [x] 3.2 `docs/lsp-client.md` says how a document's versions run.
- [x] 3.3 CHANGELOG.
- [x] 3.4 The paths review found where a version still does not rise, all predating this change, are filed
  (#719, #720, #721) and named under the lsp-client spec's Purpose as known divergences.
