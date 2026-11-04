# Issue #3074 validation

## Scope

Go to Implementation and Go to Type Definition use the server's answer exclusively. No interpreter
semantics or VB6 measurement is added, and MISSING_LANGUAGE counts do not move.

## Live check for the supervisor

With RDCore advertising implementationProvider and typeDefinitionProvider, open a code window and
check both entries beside Definition in View and the code context menu. With one provider absent,
only its command must be disabled; with no server, both must be disabled. Check enablement again
after connecting and disconnecting the server.

Ask for an implementation and a variable's type. A single result must open at the returned selection
range (including the column, across files); several must open the existing Find References pane,
show every returned location, and navigate on Enter and double-click. A null/empty answer must leave
navigation unchanged. Check Pseudo (LTR) for both labels. Both names must be available to keymap packs,
with no default gestures, and VB6's existing bindings must still work.

## Coverage regeneration

`python3 scripts/probes/issue-3074-coverage.py
artifacts/lsp-metamodel/f20ba0702f2bbfe538226a5255739fc118744407/metaModel.json`
regenerates the coverage rows and counts from the cached protocol model and retained assessments.

## Continuation verification (2026-11-04)

Reviewed interrupted commit `8ba2ec5f`; no further product change was needed. The branch contains
`upstream/main` (`17d96a10`). Coverage regeneration produced no diff.

- Feature tests: 12 unit and 6 headless UI cases pass, with no skips.
- Teeth: `python3 scripts/probes/issue-3074-teeth.py` removes the new capability declarations,
  registry forwarding, View menu entries and command enablement while retaining API declarations so
  tests compile. All 12 unit and 6 UI cases fail by assertion, with zero errors/skips; sources are
  restored in `finally`. This is a behavior-removal check, not a checkout of the pre-feature commit.
- Full integration suite via the built xUnit executable: 4,905 total, 4,903 passed, 2 skipped,
  zero errors/failures. `check-xunit-report.sh` passes. The skips are ToolbarCaptionTests' Windows
  Segoe UI and GDI font measurements; neither is coverage for this issue.
- Debug Desktop build: zero warnings/errors. Tree hygiene: passes with nine existing warnings.

Sandbox runner limitation: `dotnet test` builds successfully but stalls at MTP runner startup. The
hang-dump attempt additionally failed before tests because its mutex directory under `/run/user`
is read-only. Retried with `XDG_RUNTIME_DIR` inside this clone, then stopped the stalled MTP runs.
The built xUnit executables run successfully using their native runner arguments (`-class` for a
focused run, `-xml` for the full report). The first direct full unit attempt hit a 600-second wall
clock limit while still progressing; it supplies no passing-suite claim.

The final direct full unit run completed in 992 seconds: **5,720 total, 5,685 passed, 35 skipped,
zero failures/errors**. Its XML passes `check-xunit-report.sh`; both full-suite totals exceed the CI
floors (5,378 unit / 4,599 integration). Unit skips: 25 Windows-only cases (job objects, window/process
inspection, filesystem/registry/drive paths, type libraries and made-program launch), plus 10 cases
requiring the absent VB6 Template tree in CompanionBinaryPreservationTests/ClassModuleSaveFidelityTests.
These skips prove nothing here. No #3074 test skips. After source restoration, the rebuild has zero
warnings/errors and all six focused UI cases pass again. The full unit result includes all 12 focused
unit cases passing again. No language implementation status or MISSING_LANGUAGE count changed.


## Batch 175 merge verification (2026-11-04)

Merged `upstream/main` (`3f5001d2`) in merge commit `458c0735`. Resolved six files:
`LspMessages.cs`, `CodeEditorView.axaml.cs`, `MainViewViewModel.cs`, `MISSING_FEATURES.md`,
`lsp-client.md` and the live lsp-client spec. Kept both navigation capabilities plus range formatting,
both supplied-location events feeding Find References, and command requery plus code lens refresh.
No new behavior or tests were added. No test class lives in a conflicted file.

Regenerated coverage using `scripts/regenerate-lsp-coverage.py` and the pinned cached model:
44/93 overall, 23/41 textDocument messages. The existing changelog fragment stays unchanged.
`CLAUDE.md` only receives upstream's changes; no local guidance edit was made.
No runtime fidelity status or MISSING_LANGUAGE count changes in this conflict resolution.

Debug Desktop build passed with zero warnings/errors (4m08s), using
`--no-restore -p:UseSharedCompilation=false -m:1` and `DOTNET_PROCESSOR_COUNT=4` after shorter
parallel/single-worker attempts were cancelled or timed out during C# compilation. Windows PATH entries
were removed before every test/build attempt. Focused #3074 tests pass: 12 unit and 6 headless UI,
zero skips. Historical teeth evidence remains the 18 assertion failures recorded above; this merge
adds no behavior needing a new teeth claim.


Full-suite `dotnet test --no-build --no-restore` attempts produced only startup lines for several
minutes and were cancelled; retried both through the built xUnit executables with XML reporting.
Full integration: 4,948 total, 4,946 passed, 2 skipped, zero errors/failures, 396.417 seconds.
`check-xunit-report.sh` passes. Skips: ToolbarCaptionTests' Windows Segoe UI and GDI measurements;
neither is coverage here. Final tree hygiene passes (existing warnings; identity scan unavailable
without HEXIDE_IDENTITY_PATTERN).


Full unit: 5,805 total, 5,770 passed, 35 skipped, zero errors/failures, 1,193.319 seconds.
`check-xunit-report.sh` passes. Skips: 25 Windows-only cases (filesystem/drive paths, type libraries,
registry, job objects, process/window inspection and made-program launch), plus 10 requiring the absent
VB6 Template tree in CompanionBinaryPreservationTests/ClassModuleSaveFidelityTests. These skips prove
nothing in this sandbox. Both full suites include the #3074 cases; no #3074 case skips.
Both totals exceed main's CI floors. No bundled-server source was changed by the resolution, so no
separate server-suite run was needed. The supervisor reports PR #3092's live check already passed;
this merge adds no UI behavior requiring a new live check.
