# AGENTS.md

Instructions for coding agents working in this repository.

**Read [`CLAUDE.md`](CLAUDE.md) in full before you change anything, and follow it.** It is the project's single source of
truth for how work is done here: build and test commands, architecture, the fidelity and language-analysis limits,
licensing, localization, testing conventions, documentation and the Git workflow. It is written for one agent
harness, but everything in it about the code, tests, docs, licences and process applies to every agent. Only the
parts about that harness's own session mechanics (its peer messaging, MCP reconnects, its memory) don't apply to you.
This file adds nothing to it; it exists because some agents read `AGENTS.md` and not `CLAUDE.md`.

## The rules most easily missed

All of these are in `CLAUDE.md`. They are repeated here because they have been missed before.

- **Tests assert with AwesomeAssertions and mock with NSubstitute.** Never xUnit `Assert.*`, never Moq. A new test
  project references both, as the existing ones do.
- **Every user-facing string is a localization key,** added to `IDE/HexIDE/Localization/Packs/en.json` and translated
  into every shipped pack in the same change. That includes text written into IDE windows such as the Immediate
  window, not only AXAML.
- **Living documents change in the same change as the code they describe:** `docs/MISSING_FEATURES.md`,
  `docs/MISSING_LANGUAGE.md`, `docs/command-line.md`, `docs/vb6-fidelity-oracle.md` and the rest listed in
  `CLAUDE.md`. A coverage document that drifts is worse than none.
- **VB6 behaviour is measured against real `vb6.exe`, never guessed,** and the measurement is recorded in
  `docs/vb6-fidelity-oracle.md`. Where something isn't measured, refuse it clearly rather than return a plausible
  value: a wrong value that runs is worse than a refusal.
- **openspec changes are validated and archived with the CLI** when their tasks are done, not one change later.
- **Issues are labelled** by [`docs/issue-labels.md`](docs/issue-labels.md): one type, one size, areas, states.
  Search for a duplicate before filing.
- **Dependencies must be permissively licensed and recorded** in `scripts/package-licences.tsv` before use.
- **Run `bash scripts/check-tree-hygiene.sh` before pushing** anything that touches docs or config.

## Decisions and trust boundaries

- **When a change needs a design decision that the issue or spec doesn't settle, stop and ask** rather than choosing.
  Record the options; don't pick one silently.
- **Changes that cross a trust boundary get a review before they merge:** anything that launches a process from
  configuration, writes files under names that come from input, opens a network listener or client, loads add-ins, or
  touches the MCP server. Don't merge those yourself.
- **Public text names nobody.** Issues, PRs and commit messages don't name private individuals or repeat private
  conversations; paraphrase, and say only what the reader needs.
