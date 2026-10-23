# Tasks

## 1. The fix

- [x] 1.1 `TextDocumentClientCapabilities` gains a `foldingRange` member, appended, carrying `lineFoldingOnly`.
- [x] 1.2 The client declares it as true at initialization.
- [x] 1.3 `FromServer`'s remarks say the declaration goes if the reader starts reading a fold's characters.

## 2. Tests

- [x] 2.1 On the wire: the `initialize` the client sends carries the declaration.
- [x] 2.2 Against clangd: a four-line function's fold ends on line 2, the line before its closing brace.
- [x] 2.3 The declaration removed, or declared false, fails both.

## 3. Record

- [x] 3.1 The lsp-client spec gains a requirement tying the declaration to the reader.
- [x] 3.2 `docs/lsp-client.md`'s protocol table notes the declaration.
- [x] 3.3 CHANGELOG.
