# Declare whole-line folding

## Why

The editor folds a server's range by whole lines. `Vb6FoldingAdapter.FromServer` reads a fold's `startLine` and
`endLine` and ignores its `startCharacter` and `endCharacter`. But the client never declared
`textDocument.foldingRange.lineFoldingOnly`, so a server was told a fold may end part-way along a line
(hexide-io/HexIDE#1363).

clangd does that: it ends each fold just before its closing brace. Read by whole lines, the fold runs to the end
of the brace's line, so collapsing it hid the brace. Measured against clangd 22.1.6, a four-line function's fold
ends on line 3, the brace's line, for a client that declares nothing or declares `foldingRange` empty. It ends on
line 2 for a client that declares `lineFoldingOnly`.

## What changes

- **The client declares `textDocument.foldingRange.lineFoldingOnly: true`.** The spec defines it as the client
  only supporting folding complete lines, which is what the reader does today.
- **A requirement ties the declaration to the reader.** The change that makes the reader honour a fold's
  characters removes the declaration with it. `FromServer`'s remarks say the same, so that change finds it.

## Not changed

- **The reader.** #1022 is the other path: honour `startCharacter` and `endCharacter`, with the `Merge` trap it
  describes. It stays open. On its own, honouring offsets would still hide a closing brace at column 0 from
  clangd, which leaves `endCharacter` out when it is 0, and a missing `endCharacter` means the end of the line.
- **The rest of `foldingRange`.** `rangeLimit`, `foldingRangeKind` and `foldingRange.collapsedText` stay
  undeclared: nothing limits the number of folds, reads a fold's kind, or shows a server's label.
