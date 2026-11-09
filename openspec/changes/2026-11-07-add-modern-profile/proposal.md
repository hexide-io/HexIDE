# Thin opt-in Modern profile for v1 (#3301)

## Why

Jen decided on 2026-11-07 that v1 includes a thin Modern profile, with Classic remaining the default.
This records the design for review requested by the latest comment on
[#3301](https://github.com/hexide-io/HexIDE/issues/3301), following #3298. It ships no implementation.

## What changes

An opt-in `modern` bundle sits beside `vb6`, `vbaode` and `vba` in the existing profile model.
Three independently deliverable features provide its headline patterns:

- A keyboard command palette searches menu commands, settings and open files, and displays symbols
  supplied by an external backend through `workspace/symbol`.
- A searchable settings page marks changed values and resets individual settings, using the same
  settings store and validation as Options.
- OS light/dark following chooses between two theme packs and always applies a concrete variant.

The deltas describe intended contracts, not current support. The [design](design.md) separates the
recorded scope from recommendations and questions for Jen. Other Modern patterns wait until after v1.

## Impact

Classic remains unchanged, including default selection, command routing, settings values and explicit
user choices. Profiles remain data, with no executable hooks or runtime-language changes. Each feature
needs a host capability shared with Classic; data selects its presentation and defaults. Missing data
vocabulary needs a separate finding before implementation, rather than a branch on the profile name.

Only this OpenSpec change is authored in this run, as requested by the supervisor. Live specs, code,
tests, localization packs, changelog fragments and coverage inventories are unchanged. The three
implementation issues are to be filed when staffed; this run does not file them or claim completion.
