# Work in progress: ideas not yet designed

The documents here record ideas worth keeping before anyone has decided to build them. Each one gathers what
was discussed, what was measured, what other projects learned, and the open questions, so the thinking is not
lost when the conversation that produced it ends.

They are **not** design records. Nothing in this folder is a contract, and nothing here describes how HexIDE
behaves today except where a section says "today" and cites the code. How the system behaves is in
[`openspec/specs/`](../../openspec/specs); work being built is in [`openspec/changes/`](../../openspec/changes);
the backlog is in [GitHub Issues](https://github.com/hexide-io/HexIDE/issues).

## How a document leaves this folder

- **Picked up:** it becomes an OpenSpec change (`openspec/changes/<id>/proposal.md`) or a set of issues, and the
  WIP document is deleted in the same change. Do not keep both: two accounts of one plan drift apart.
- **Overtaken:** delete it, and say in the commit what replaced it.
- **Still an idea:** leave it here, and update it when something is measured or decided.

Every document opens with the date it was captured and its status, and closes with the issues it relates to.
Measurements of real `vb6.exe` made while writing one belong in
[`docs/vb6-fidelity-oracle/README.md`](../vb6-fidelity-oracle/README.md) as well, which is where a later reader will look.

## Contents

| Document | Captured | What it is about |
|---|---|---|
| [Building with the real VB6](vb6-native-build.md) | 2026-10-13 | The native build's hidden compiler and linker switches, capturing what C2 and LINK print, and a private copy of the user's VB6 so HexIDE can do both without touching the install |
| [One Build command, many compilers](choosing-the-compiler.md) | 2026-10-13 | Retiring "Make with VB6" in favour of a per-project toolchain setting, and what VS Code and others teach about building a polyglot repository |
| [The next forms demos](forms-demos.md) | 2026-10-13 | A forms-based Battleship, then Minesweeper, as showcases of what the interpreter runs |
