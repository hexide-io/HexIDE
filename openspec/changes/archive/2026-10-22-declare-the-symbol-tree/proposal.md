# Declare the symbol tree

## Why

`textDocument/documentSymbol` has two legal answers: the `DocumentSymbol[]` tree and the flat
`SymbolInformation[]`. Which one a client gets is negotiated. A server that honours the client's
`hierarchicalDocumentSymbolSupport` sends the tree only to a client that declared it, and the flat form
otherwise.

HexIDE reads both answers, but never declared the first (hexide-io/HexIDE#463). Nothing broke visibly. Every
outline from such a server came back flat instead. Measured against clangd, a namespace holding a struct and a
function came back as five symbols at the top level. Once the client declares the tree, the same file comes
back as the namespace holding the other two.

It went unnoticed because the servers HexIDE meets most often send the tree whether or not they were told. A
server that sends the tree unconditionally and a client that never declares it cancel out.

## What changes

- **The client declares `textDocument.documentSymbol.hierarchicalDocumentSymbolSupport: true`.** Nothing else
  in `documentSymbol` is declared. Without `symbolKind`, a server keeps to the kinds from `File` to `Array`, as
  it did when the client declared nothing there. So the declaration changes the shape of the answer and nothing
  else.
- **A requirement for what the client declares.** Where a declaration decides a reply's shape, the client
  declares a shape it reads whenever the undeclared shape carries less of what it keeps, and declares no shape
  it cannot read.

## Not changed

- **The reader.** It already normalises both answers.
- **`definition.linkSupport`.** The client reads a `LocationLink`, but keeps only its target and target
  selection range, which is the `Location` a server sends without the declaration. Declaring it would change
  nothing the client keeps, so the requirement does not ask for it.
- **The neighbours the issue names.** `completion`, `signatureHelp`, `definition` and `foldingRange` are still
  undeclared. For the first three, the defaults are the plain forms the client reads. For `foldingRange`,
  `lineFoldingOnly` would change what a server sends: clangd ends a function's fold a line earlier when it is
  declared, which keeps the closing brace visible in an editor that folds whole lines, as this one does. That
  is a separate change with its own measurement.
