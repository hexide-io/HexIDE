# Tasks

## 1. The fix

- [x] 1.1 `TextDocumentClientCapabilities` gains a `documentSymbol` member, appended, carrying
  `hierarchicalDocumentSymbolSupport`.
- [x] 1.2 The client declares it as true at initialization.

## 2. Tests

- [x] 2.1 On the wire: the `initialize` the client sends carries the declaration.
- [x] 2.2 Against clangd: a namespace holding a struct and a function comes back as the namespace holding the
  other two.
- [x] 2.3 The declaration removed, or declared false, fails both.

## 3. Record

- [x] 3.1 The lsp-client spec gains a requirement for the reply shapes the client declares, which
  `definition.linkSupport` already meets by going undeclared.
- [x] 3.2 `docs/lsp-client.md`'s protocol table notes the declaration.
- [x] 3.3 The document-symbol shape tests no longer say the shape is not negotiable.
- [x] 3.4 CHANGELOG.
