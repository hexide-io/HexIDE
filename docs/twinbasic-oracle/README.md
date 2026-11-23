# twinBASIC oracle: what the twinBASIC IDE actually writes

HexIDE opens, edits and saves a `.twinproj` (#5138). What a `.twinproj` holds is settled the way VB6's behaviour
is settled in [`docs/vb6-fidelity-oracle/`](../vb6-fidelity-oracle/README.md): by making the real tool do it and
reading what came back. Here the real tool is the twinBASIC IDE. The maintainer decided on 2026-11-20 that it
serves as an oracle as `vb6.exe` does, and its author has said that's fine. The Community Edition is enough.

## The compiler is off-limits

**No decompiling, disassembly, reading its binaries, instrumenting, hooking, patching or shimming the twinBASIC
compiler, for any reason.** The oracle is the files the IDE saves, and the messages on the compiler's channel may be
observed. Nothing else. Every measurement here comes from a save, and none from the compiler's internals.

## How measurements are made

`tools/twinbasic-oracle/` (#5139) drives the stock IDE's front end in a VM. It
opens a copy of a baseline project, makes one known change, saves, and keeps the bytes. The saves are committed as
fixtures under `IDE/HexIDE.TwinBasic.Tests/Fixtures/`, whose README says how each was made, and the harness's
`tbdump.py` compares two of them field by field.

**Record what was measured, as the VB6 oracle does.** Name the beta, the fixture and the change. Keep anything
not yet explained marked as unexplained, and never tidy an odd result into a rule.

## Pages

- [The `.twinproj` container, version 1](twinproj-container-v1.md) (#5140). Its layout, what each field showed on
  BETA 1044, what an unchanged save rewrites, and what is still unexplained.
- [A container HexIDE wrote, opened in the twinBASIC IDE](a-container-hexide-wrote.md) (#5143). HexIDE's write
  matches the IDE's own save, the IDE reads it, and a file replaced while the IDE has it open is lost.
