# Replace the copyleft language server with a permissively-licensed one

> Reconstructed at conversion time (2026-08-16) from the shipped code, when HexIDE's specs were migrated
> to the OpenSpec format. The work landed on 2026-07-26. The document it replaces
> (`lsp-server-swap`) was written as a phased implementation plan; this change records what the work
> was for, and the accompanying spec states the contract the server now has to honour.

## Why

HexIDE's pitch is a clean, permissively-licensed, familiar-first IDE. The language server undermined it:
the server itself was fine, but it embedded a VBA grammar from another project under a copyleft licence,
and that single dependency made the whole server copyleft. The consequences were not theoretical — a
mixed-licence table in the documentation, a source-bundling obligation, two grammars producing two
different parse trees for the same language, and a split in the contributor guidance depending on which
half of the tree you were touching.

It also made the licence story conditional. "Permissively licensed" with an asterisk is a claim a careful
reader has to check, and a project whose whole argument is about being clean cannot afford that.

The one risk that had previously argued for deferring the swap was whether the replacement grammar would
hold up on the input a language server actually sees — partial, mid-keystroke, syntactically broken text.
That was measured rather than assumed before committing: a soak of roughly nine and a half thousand
truncated and malformed inputs produced no crashes and no timeouts, with worst-case latency comfortably
inside the editor's debounce window.

## What Changes

- The language server is rebuilt on a permissively-licensed grammar — the one the interpreter already
  used — and a permissively-licensed server framework, making the source tree uniformly permissive with
  no exceptions to explain.
- The copyleft grammar, its licence file, and every test input derived from the replaced project are
  removed. Nothing is carried across without checking it was ours to relicense.
- The process boundary is deliberately kept. It was never a licensing device, so removing the licensing
  problem is not a reason to collapse it: it buys crash isolation and a replaceable backend.
- The server parses each change once and shares the resulting tree across diagnostics, symbols and
  completion, rather than parsing separately for each.
- The wire contract the IDE depends on is pinned as a requirement rather than left as an emergent
  property of one implementation, so a replacement backend has something to conform to.

## Impact

- New capability: `language-server`.
- Retires the `lsp-server-swap` document, which described this work as a plan.
- The source tree becomes uniformly permissive, removing a release gate.
- No change to `lsp-client`: the contract it consumes is unchanged, which is what made the swap
  survivable in the first place.
