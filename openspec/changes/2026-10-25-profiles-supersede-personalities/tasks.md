# Tasks

Jen approved this change as written on 2026-10-25 in #504 (after #1953). The latest supervisor
comment (2026-10-29) authorises the next unchecked task per run. Sizes use `docs/issue-labels.md`; these are separate
deliveries, not one large implementation issue. No follow-up issue is created by this document.

## 1. Review gate

- [x] Supervisor reviews proposal, design and deltas; Jen approves the detailed shape. Satisfied by
  Jen's 2026-10-25 approval in #504, as confirmed by the latest supervisor comment.

## 2. Prerequisite and independent corrections

- [x] **#415 — small:** complete and guard `CommandKeyMapping.Table` against routed commands. The
  existing table is the seam; prove the guard fails with an omitted entry. Broader command metadata and
  ViewModel/provider joins belong to the surface work, not a new prerequisite hidden in #415. Added
  the three missing entries; the exact-name/instance guard and all three binding cases failed before
  the fix (4 cases), then passed. Full HexIDE.Tests: 3,892 passed, 20 skipped; Desktop Debug: zero
  warnings/errors. Strict OpenSpec validation and tree hygiene pass.
- [x] **#419 — trivial:** verify the live personality spec's toolbar claim is removed (it already is
  in this checkout); when profiles land, reconcile terminology without implementing toolbar gating
  against the retiring personality enum. Verified the live requirement says only menu entries and
  project types; no toolbar gating or terminology migration is introduced in phase 2.

## 3. Loader and migration split-outs

### Shared-reader rework (#504, supervisor review 2026-11-02)

- [x] Merge #2708 from main and route selector resolution through its bundle loader; remove the
  duplicate reader and embedded manifests. Keep the adapter snapshots, CLI, recovery mode and #418.
  `ProfileResolver` now only selects, translates attributed errors and adapts detached JSON, retaining
  adapter validation before application. Built-ins live solely in `IDE/HexIDE/IDE/Profiles/Builtins/`.
  Contained-link acceptance and the old physical-path algorithm tests are removed; the shared reader
  suites own containment/replacement behaviour. Four new resolver integration cases (manifest/payload
  links and size limits) failed before the rework. Symlinked-ancestor policy remains #2829's scope.
  Adapter/migration fixtures use the same physical macOS temporary root as the reader fixtures;
  Linux paths are unchanged and macOS execution remains for CI. The live profile-catalog spec now
  records the consumer seam rather than the old foundation-only boundary.
  No VB6 measurement, new localized string, visual change, MISSING_FEATURES status change or
  MISSING_LANGUAGE count change. OpenSpec validation/archive are deliberately not run.
  Verification: all 248 profile cases completed (245 passed, three Windows-only skips; all four
  new cases passed on Linux). Full HexIDE.Tests with `HEXIDE_FOREIGN_LSP_DOWNLOAD=0`: 5,208 passed,
  35 skipped, zero failures/errors/not-run (5,243 total). The 35 skips are 25 Windows-only cases and
  ten cases lacking VB6 templates; they provide no coverage. Cached foreign servers ran. Full
  Integration: 4,534 passed, two Windows-only font-caption skips, zero failures/errors/not-run
  (4,536 total). Both xUnit XML report checks pass. CI floors rise to 4,980 and 4,309 respectively.
  Desktop Debug builds with zero warnings/errors; tree hygiene passes with ten existing non-blocking
  warnings. An initial full IDE run was interrupted after a reference-free fixture rebuild removed
  the guard DLL from test output; restoring the freshly built helper allowed the complete retry
  above to pass. Only the upstream merge is committed; this run's changes remain uncommitted for review.


- [x] **Manifest/resolver — large:** implement versioned immutable snapshots, embedded index, installed
  discovery, path containment, validation and attributed errors. This is a new component with multiple
  behaviours and its own tests. Cover missing files, traversal/symlink escape, collisions and atomicity.
  Implemented the internal resolver and immutable, file-attributed JSON snapshots, with an explicit
  embedded index and three built-in manifests. Installed lookup checks actual directory names ordinally
  even on case-insensitive filesystems; references resolve every symlink ancestor before containment.
  All 55 loader cases ran and passed on Linux. Teeth: 54 failed with the resolver reduced to an
  unimplemented API scaffold; the additional indirect-symlink regression failed against the first draft
  that resolved only the link's final target. Full HexIDE.Tests with foreign downloads disabled:
  4,957 passed, 32 skipped, zero failed/errors (4,989 total); the xUnit report guard passes. Raised the
  CI floor to 4,739 (95% of this total). Desktop Debug: zero warnings/errors. Loader docs, the live
  profiles seam spec, all full language packs and `changelog.d/504.md` updated. No CLI/consumer migration,
  visual change, VB6 measurement, MISSING_FEATURES status change or MISSING_LANGUAGE count change.
  No OpenSpec validation or archive was run, following the 2026-10-29 supervisor instruction.

  Skips are not coverage: seven `CompanionBinaryPreservationTests` and three
  `ClassModuleSaveFidelityTests` lack the VB6 template tree. The 22 Windows-only skips are in
  `ChildProcessJobTests` (3), `WorkspaceArtifactProjectTests` (2), `UserDataPathTests` (1),
  `PackageVerificationTests` (1), `UserSidecarSaveTests` (1), `EditorServiceNavigationTests` (1),
  `MadeProgramTests` (1), `ProjectTreeBuilderTests` (3), `WindowStateUnconfirmedTests` (1),
  `VbSettingsStoreTests` (2), `TypeLibraryNamesTests` (1), `LspDocumentSessionTests` (1) and
  `ProcessWindowsToolTests` (4). None of the new loader tests skipped.

  #2621 first slice adds immutable declaration snapshots, the embedded built-in index and installed
  folder discovery only. Discovery returns untrusted candidates (including reserved built-in names);
  it never opens manifests or payloads. Validation, selected-bundle loading, containment, name
  collision resolution and atomic payload capture remain incomplete. No consumer is wired.
  Verification: 12 catalog cases pass without skips in an isolated harness using the same source
  and tests; disabling the implemented behaviours makes all 12 fail (mutation proof, not a
  pre-change baseline). Full HexIDE.Tests and Desktop Debug build were attempted but blocked by
  Antlr4BuildTasks Locker initialisation before the tests or IDE build could complete. Strict
  validation of the new live profile-catalog spec passes; no language-coverage counts change.
  #2621 second slice adds validation of parsed manifest declarations: required version/id, installed
  folder-id matching, exact field/type checks, approved surface/template/locale ids and duplicate
  rejection, with internal file/field/code diagnostics. Built-ins use the same validator. This parser
  opens no files and makes no containment guarantee. Reading installed JSON, syntax diagnostics,
  referenced pack/settings validation, containment, collisions, atomic loading and localized error
  presentation remain incomplete. No consumer is wired. See the live profile-catalog spec for the
  implemented boundary.
  Verification: all 31 new cases failed against the unchanged step-1 parser extracted into a testable
  method, before validation was added; all 43 profile cases then passed without skips. Full
  HexIDE.Tests ran through the direct xUnit executable after `dotnet test` stalled at runner launch:
  4,627 passed, 28 skipped, zero failures/errors (4,655 total, above the CI floor of 4,249).
  `HEXIDE_FOREIGN_LSP_DOWNLOAD=0` was set; skips were unavailable VB6 templates and Windows-only
  behaviour, not profile coverage. The xUnit XML report checker passed. Desktop Debug built with zero
  warnings/errors using serial MSBuild with shared compilation disabled. Strict profile-catalog spec
  validation and tree hygiene passed (nine existing non-blocking hygiene warnings).
  MISSING_FEATURES status and MISSING_LANGUAGE counts remain unchanged; no VB6 semantics or UI changed.
  #2621 third slice adds portable reference spelling validation in the declaration parser and a separate
  directory-manifest preflight for symlinks/reparse points, including Windows junctions. It refuses all
  links, including internal links, rather than resolving them. Missing references remain declarations;
  this is not an atomic loader or permission to open a path later. Installed manifest reading, payload
  capture/validation, collision resolution and protection against concurrent replacement remain pending.
  No consumer is wired; MISSING_FEATURES status and MISSING_LANGUAGE counts remain unchanged.
  Verification: 25 containment cases failed against the declaration-only parser before checks were
  added (the directory entry point initially delegated to that unchanged parser). All 69 profile cases
  then completed: 68 passed, one Windows-only junction test skipped. The skipped test is not Linux
  coverage or a measured teeth case. Full test-project and Desktop Debug builds passed with zero
  warnings/errors. Full-suite attempts via `dotnet test` and the direct xUnit executable stalled
  without a final report; no full-suite pass is claimed. `HEXIDE_FOREIGN_LSP_DOWNLOAD=0` was set.
  Strict live-spec validation and tree hygiene pass (nine existing non-blocking hygiene warnings).
  #2621 fourth slice adds a standalone contained byte reader, without wiring any consumer. Linux
  opens each component relative to a held directory descriptor with `O_NOFOLLOW`; Windows holds
  ancestor handles without write/delete sharing and inspects opened handles for reparse points.
  This deliberately refuses links in ancestors as well as within a bundle. Other hosts refuse reads
  until an equivalent implementation exists. This primitive does not capture an atomic multi-file
  snapshot. Bundle assembly, collision resolution, atomic loading and payload validation remain pending.
  Verification: the file-link, directory-link and root-link swap cases all failed before the handle
  reader was implemented, against a pathname reader exposed through the new seam (three teeth).
  The ordinary-file success test also passes, without claiming teeth. All 74 profile cases completed:
  72 passed, two Windows-only junction tests skipped, including the new junction-swap case. These skips
  prove no Windows behaviour. MISSING_FEATURES status and MISSING_LANGUAGE counts remain unchanged;
  there are no new VB6 semantics or UI changes.
  Full HexIDE.Tests completed through the direct xUnit executable with verbose/long-running diagnostics
  and `HEXIDE_FOREIGN_LSP_DOWNLOAD=0`: 4,657 passed, 30 skipped, zero failures/errors (4,687 total,
  above the 4,249 floor). The XML report checker passed. Skips were 20 Windows-only cases and ten
  unavailable VB6 template cases; cached foreign servers ran. Full test-project and Desktop Debug builds
  passed with zero warnings/errors. Tree hygiene passed with nine existing non-blocking warnings.
  Strict OpenSpec validation was attempted but the installed CLI stalled without a result; no pass is
  claimed for that command. Changes remain uncommitted for supervisor review.
  #2621 cleanup correction removes file/directory symlinks and Windows junctions as entries before
  recursively deleting fixtures. Escape targets live outside the deleted fixture tree; every fixture
  asserts its target directory and sentinel bytes survive cleanup. Two new Linux regression cases
  (file and directory links) failed against the unchanged cleanup before the unlink pass was added.
  This corrects test infrastructure only; bundle assembly, payload validation, collisions and atomic
  multi-file snapshots remain pending. Windows junction cleanup still requires the supervisor's run.
  No IDE consumer, UI, feature status or MISSING_LANGUAGE count changes.
  Verification: the direct xUnit executable completed all 76 profile cases: 74 passed, two Windows-only
  junction cases skipped (no Windows coverage claimed). The XML error checker passed. Both new cleanup
  cases failed before implementation (two teeth). The Desktop Debug build passed serially with zero
  warnings/errors; tree hygiene passed with existing non-blocking warnings. Full HexIDE.Tests
  was attempted with `HEXIDE_FOREIGN_LSP_DOWNLOAD=0`: an initial run began before helper output copying
  completed and was stopped after failures and a pipe-test stall; a fresh run with helpers present
  reached its ten-minute limit after 3,137 completed cases without a reported failure, while
  `NamedPipeWorkspacePlaceholderTests.ThePidPlaceholderGivesTheServerItsOwner(withWorkspace: True)`
  was active. No complete full-suite result or XML report is claimed. Windows-only and unavailable
  VB6-template cases skipped; cached foreign servers ran. Changes remain uncommitted for review.
  #2621 bundle assembly slice adds a standalone assembler over the contained reader, returning detached
  JSON and immutable declarations only after every reference is captured. It validates JSON syntax,
  object/record shapes and the settings allowlist, types and duplicate fields, with file/field/code
  diagnostics. Existing pack entry filtering and effective settings validation remain adapter work.
  This assembler explicitly makes no multi-file atomicity guarantee and is not connected to consumers.
  Name resolution now reserves built-in ids through the embedded index, while explicit directories
  can select colliding ids. Invalid names are refused; no CLI selector is wired. Atomic loading remains
  incomplete; #2621 is not complete.
  No VB6 semantics, UI, MISSING_FEATURES status or MISSING_LANGUAGE counts change.
  Verification: nine assembly cases failed against the declaration-only assembler stub before assembly
  was implemented; six name/collision cases failed against installed-only name forwarding before
  reserved-name resolution was added (15 distinct pre-change failures, no mutation-only claim).
  Full HexIDE.Tests completed through the direct xUnit executable with foreign-server downloads disabled:
  4,674 passed, 30 skipped, zero failures/errors (4,704 total, above the 4,249 floor). The XML error
  checker passed. Twenty Windows-only cases and ten unavailable VB6 template cases skipped; these
  prove no coverage on those platforms/fixtures. Cached foreign servers ran. An initial ten-minute
  run timed out and had two process-logging failures because the fast test rebuild omitted the LSP
  guard DLL from its output. Copying the already-built guard DLL restored the helper; all four logging
  cases then passed, and the full fifteen-minute retry above passed. No product workaround was added.
  All 91 profile cases completed: 89 passed, two existing Windows-only junction cases skipped, providing
  no Windows coverage. The xUnit XML error check passed. Desktop Debug built with zero warnings/errors.
  Tree hygiene passed with nine existing non-blocking warnings. Strict live-spec validation timed out
  after 45 seconds without a validation result, including a retry with telemetry disabled; no pass is
  claimed. Changes remain uncommitted for review.
  #2621 security review slice caps each manifest and payload at 1 MiB, preserving attributed
  `TooLarge` diagnostics and reading at most the limit plus one byte even during growth. Linux uses
  fstat on the opened handle to refuse non-regular files; Windows refuses non-disk handles. Bundle ids
  now exclude portable Windows device names. Native Linux stat layouts support x64 and arm64; other
  architectures fail closed. The live catalog spec now has short, single-behaviour requirements and
  scenarios. Atomic multi-file snapshot loading remains incomplete; #2621 is not complete and the
  assembler must not be connected to consumers. No VB6 semantics, UI, MISSING_FEATURES status or
  MISSING_LANGUAGE counts change.
  Verification: 29 new cases failed before implementation (22 device ids, three attributed size-limit
  cases, a file growing during a stream read, and Linux FIFO/device/socket refusal). The exact-limit
  success case already passed; no teeth claimed for it. All 122 profile cases completed: 119 passed,
  three Windows-only cases skipped (the two junction cases and the new pipe-handle case). Skips prove
  no Windows coverage. The test-project and Desktop Debug builds passed with zero warnings/errors,
  using a fresh in-clone artifacts path because existing intermediate files were read-only. The ANTLR
  task's Locker failed; the cached Java and ANTLR jar generated the current grammars directly before
  building. Profile tests ran through the direct xUnit executable. Strict live-spec validation and
  tree hygiene pass (nine existing non-blocking hygiene warnings). Full HexIDE.Tests completed through
  the direct xUnit executable with `HEXIDE_FOREIGN_LSP_DOWNLOAD=0`: 4,797 passed, 43 skipped, zero
  failures/errors (4,840 total). The CI floor rises to 4,598 (95% of the measured total). Skips provide
  no coverage: 21 Windows-only cases, 12 cases finding a stale bundled-server binary at their default
  path, and ten unavailable VB6 template cases. Cached foreign servers ran. Changes remain uncommitted
  for review.
  #2621 older-glibc compatibility slice keeps fstat where its entry point and stat layout are
  available. An injectable mode reader lets Linux tests simulate its absence. Missing fstat and
  unsupported architectures fall back to FileStream.CanSeek, refusing pipes without consuming them
  and preserving the caller's handle. The fallback intentionally cannot distinguish a seekable device
  from a regular file, as accepted by the latest issue review. Native errors other than a missing
  entry point retain their existing refusal. No new VB6 measurement, user-facing string or UI change
  is involved; MISSING_FEATURES status and MISSING_LANGUAGE counts are unchanged.
  Four new Linux cases failed against the previous behaviour before implementation (regular file
  acceptance and pipe refusal, each for missing fstat and unsupported architecture), then passed.
  The full profile slice has 123 passes, three Windows-only skips and no failures or xUnit errors.
  Skips provide no Windows coverage. Strict live-spec validation passes. Atomic multi-file snapshot
  loading remains the final #2621 task: this assembler still must not be connected to consumers.
  Final verification: full HexIDE.Tests ran through the direct xUnit executable with
  `HEXIDE_FOREIGN_LSP_DOWNLOAD=0`: 4,812 passed, 32 skipped, zero failures/errors (4,844 total).
  The xUnit XML error checker passes; the CI floor rises to 4,602 (95%). Skips provide no coverage:
  21 Windows-only cases, ten unavailable VB6 template cases, and one bundled-server rename case
  whose freshness guard found changed source during the run. Cached foreign servers ran. An initial
  full run was interrupted after two process-lifecycle failures exposed a missing guard DLL in the
  test output; restoring the freshly built DLL allowed the complete retry above to pass. MSBuild's
  initial test invocation failed in a child node; dependency and test builds then completed separately.
  Desktop Debug builds with zero warnings/errors. Strict live-spec validation and tree hygiene pass
  (nine existing non-blocking hygiene warnings). Upstream was merged, resolving only CI-floor
  conflicts by retaining the higher values. This slice remains uncommitted for review.
  #2621 macOS compatibility slice shares the Unix openat walk with Linux, selecting Darwin's
  no-follow, directory-only, close-on-exec and nonblocking flags from XNU's `bsd/sys/fcntl.h`.
  Unsupported platforms refuse with an internal InvalidDataException naming the platform. macOS uses
  the existing seekability fallback, without interpreting Linux stat offsets; the documented seekable
  device limitation remains. Profile fixtures use /private/tmp on macOS because /tmp is a symlink;
  ancestor links remain refused. The contained-read and link-swap cases now also run on macOS.
  Two new platform-selection cases failed before the fix (two teeth). macOS CI must confirm ordinary
  bundle assembly, attributed oversized-file errors, and file/directory/root link swaps; Linux cannot
  prove Darwin's native opens. Atomic snapshots remain pending for the next scoped delivery (#2714).
  No UI, VB6 semantics, feature status or MISSING_LANGUAGE count changes.
  Verification: all 128 profile cases completed (125 passed, three Windows-only skips); both new
  platform-selection cases failed before the fix. The full HexIDE.Tests executable completed with
  HEXIDE_FOREIGN_LSP_DOWNLOAD=0 and fixed seed 1: 4,933 passed, 35 skipped, zero failures or runner
  errors (4,968 total). The XML report checker passes; the CI floor rises to 4,720 (95%). Skips are
  25 Windows-only cases and ten unavailable VB6-template cases, and provide no coverage. Cached
  foreign servers ran. The corpus smoke case passed after 398 seconds; its long duration was not a
  confirmed stall. Earlier incomplete attempts are not counted as passes.
  Antlr4BuildTasks failed during lock initialization. Regenerating runtime and LSP grammar output
  directly with the installed Java/ANTLR tools allowed complete test-project and Desktop Debug builds
  with zero warnings/errors; the exact Desktop command then also passed. The fresh guard DLL had to
  be copied into test output before the full retry. Strict profile-catalog validation, tree hygiene
  (nine existing non-blocking warnings) and diff whitespace checks pass. Changes remain uncommitted
  for supervisor review; only the upstream merge was committed.
  #2714 retains the bundle directory for the entire assembly. Linux/macOS payload opens use the
  retained descriptor; Windows ancestor locks deny write/delete sharing until assembly completes.
  The manifest is re-read through that directory before returning; changed bytes refuse with internal
  BundleChanged/file/field diagnostics. This implements the issue's manifest re-read option, not a
  transaction for arbitrary in-place payload writes with unchanged manifest bytes. No consumer is
  connected. The live profile-catalog spec records the guarantee and its boundary.
  Both new common-platform regression cases failed against the original reader with only the callback
  seam added (two Linux teeth): a swapped bundle mixed old theme/new settings, and a changed manifest
  was accepted. Windows and macOS native execution remain for their runners; Linux proves neither.
  No UI, VB6 semantics, feature status or MISSING_LANGUAGE count changes.
  Verification: all 130 profile cases completed: 127 passed, three Windows-only skips
  (WindowsPipeHandleIsRefused, JunctionEscapeIsRefused and
  JunctionSwappedInAfterPreflightIsRefusedDuringRead). The skips prove no Windows behaviour.
  Desktop Debug builds with zero warnings/errors; strict profile-catalog validation and tree hygiene
  pass (nine existing non-blocking warnings).
  The complete direct xUnit run with HEXIDE_FOREIGN_LSP_DOWNLOAD=0 and seed 1 passes: 4,935 passed,
  35 skipped, zero failures/errors/not-run (4,970 total, above the 4,720 floor). The XML error checker
  passes. Skips are 25 Windows-only cases and ten unavailable VB6-template cases; cached foreign
  servers ran. The prior dotnet-test attempt reached its 15-minute timeout while still progressing
  through corpus smoke tests, so it is not counted as a pass; the complete retry took 930 seconds.
  Changes remain uncommitted for supervisor review.

- [x] **Pack/default adapters — medium per adapter:** separately connect existing theme, keymap,
  language and presence-aware settings readers. Each is a producer/consumer change following existing
  pack precedents. Test precedence, invalid data and preservation of saved user values.
  - [x] **Presence-aware settings adapter:** accept validated snapshot defaults in `SettingsService`,
    merge saved properties by presence before the existing reader validates values, and preserve
    save provenance. Five changed-behaviour cases failed against an API scaffold without layering;
    two invalid-default cases already passed through the resolver (no new teeth claimed for them).
    All seven cases now pass on Linux, with no skips. Full HexIDE.Tests (foreign downloads disabled):
    4,964 passed, 32 skipped, zero failures/errors, 4,996 total; the xUnit report guard passes.
    Raised the CI floor to 4,746 (95% of the measured total). Desktop Debug builds with zero
    warnings/errors; tree hygiene passes. Validation used isolated build output because existing
    cache files are read-only in the sandbox, reusing unchanged generated parsers. For observable
    full-suite progress, use `-p:TestingPlatformCaptureOutput=false` with `--long-running 30`.
    The same 32 skips listed under Manifest/resolver above remain: ten need VB6 templates and 22
    are Windows-only. None is claimed as coverage. No visual change, new localization key, VB6
    measurement, MISSING_FEATURES status change or MISSING_LANGUAGE count change. OpenSpec validation
    and archive were not run, following the latest supervisor instruction.
    This is a coherent first adapter slice; the parent task remains open for the other three adapters.
  - [x] Theme adapter, including profile payload versus saved theme choice and atomic invalid-colour handling.
    Reuse `ThemePackRecord` and the existing colour mapping against snapshot data. Payloads beat profile
    settings defaults; explicit saved theme choices (including Classic) and current edits win. Preserve
    explicit-choice provenance after saving. Validate known colours in the resolver before any surface
    application, and prepare replacement dictionaries before removing the active override. Unknown keys
    retain warn-and-skip with bundle/file attribution. Seven cases run on Linux without skips; five
    failed against the pass-through API scaffold, while the two saved-choice cases already passed and
    are not counted as teeth. Startup/CLI wiring remains with the migration task. No new localized
    string, VB6 measurement, MISSING_FEATURES status change or MISSING_LANGUAGE count change.
    Full HexIDE.Tests (foreign downloads disabled): 4,971 passed, 32 skipped, zero failures/errors,
    5,003 total; xUnit report guard passes. Raised the CI floor to 4,752 (95% of the measured total).
    The same 32 skips enumerated under Manifest/resolver remain (ten need VB6 templates, 22 are
    Windows-only); none is claimed as coverage. The first full attempt was cancelled when diagnostics
    identified a missing LSP guard helper after a reference-free incremental build. Rebuilding the full
    test project copied the helper; the retry passed, including all seven named-pipe notice cases.
    Desktop Debug: zero warnings/errors. Tree hygiene passes with existing non-blocking warnings.
    OpenSpec validation/archive were not run, following the latest supervisor instruction.
  - [x] Keymap adapter, including profile payload versus saved keymap choice and invalid gestures.
    Consume snapshot bindings above profile settings defaults and below explicit saved or edited
    choices, including Default; retain that provenance after saving. Validate known gestures during
    resolution before any command mutation; unknown commands retain warn-and-skip with source/file
    attribution. Seven cases pass on Linux without skips. Four failed against a pass-through API
    scaffold; the two saved-choice cases and missing-payload fallback already passed (no teeth claimed).
    Startup/CLI wiring remains with migration. No new localization key, VB6 measurement,
    MISSING_FEATURES status change or MISSING_LANGUAGE count change. No OpenSpec validation/archive,
    following the latest supervisor instruction.
    Full HexIDE.Tests with foreign downloads disabled: 4,978 passed, 32 skipped, zero failures/errors,
    5,010 total; xUnit report guard passes. Raised the CI floor to 4,759 (95% of that total).
    Skips are the same ten missing-template and 22 Windows-only cases enumerated under
    Manifest/resolver; none is coverage. Desktop Debug builds with zero warnings/errors; tree hygiene
    passes with existing non-blocking warnings. Changes are left uncommitted for supervisor tooling,
    as the run's direct instructions require.
  - [x] Language adapter, including per-locale fallback, user override precedence and placeholder validation.
    Consume snapshot strings above shipped packs and below user neutral/region overrides, preserving
    Chinese script fallback and the shipped locale inventory. Reuse the existing placeholder validator;
    skip orphan, blank and invalid-format entries. Clearing a user override live-restores the profile.
    Seven cases pass on Linux without skips; six failed against an adapter API scaffold that ignored
    the snapshot. Missing-payload fallback already passed and is not counted as new teeth. Startup/CLI
    wiring remains with migration. No new localization key, VB6 measurement, visual surface change,
    MISSING_FEATURES status change or MISSING_LANGUAGE count change. Changes remain uncommitted for
    supervisor tooling, as the run's direct instructions require. OpenSpec validation/archive were
    not run, following the latest supervisor instruction.
    Full HexIDE.Tests with foreign downloads disabled: 4,985 passed, 32 skipped, zero failures/errors,
    5,017 total; the xUnit report guard passes. Raised the CI floor to 4,766 (95% of the measured total).
    Skips are the same ten missing-template and 22 Windows-only cases enumerated under Manifest/resolver;
    none is coverage. Desktop Debug: zero warnings/errors. Tree hygiene passes with existing
    non-blocking warnings.
- [x] **CLI and built-in migration — medium:** wire the resolved profile to consumers, convert the
  three built-ins, remove personality APIs, add `--profile` and the removed-flag error. Producer and
  consumers must land together. Pin the old table before removal, test name/path/none and argument
  ordering, old-flag refusal, help width, default selection and both flag prefixes. Translate diagnostics
  into all shipped packs; update command-line docs and live specs only as behaviour ships.
  Implemented `--profile` name/path/none selection after parsing user-data redirection, before UI
  construction, with invocation-relative paths and embedded `vb6` by default. Replaced personality APIs
  and their consumers with the snapshot-backed profile service; startup now connects settings defaults,
  themes, keymaps and translations. Removed flags refuse launch with exit code 2, even beside a valid
  selector; help still succeeds. All three new strings, including the canonical-English help summary,
  have keys and translations in every full shipped pack. The width guard measures resolved summaries.
  The three original personality records were temporarily reloaded and their complete ordered template
  sequences and References flags matched the migrated service (3 passing parity cases, no new teeth).
  The temporary legacy APIs were then removed again.

  All 25 migration cases pass on Linux with no skips. Teeth: eight CLI cases failed against the original
  parser; 21 of the initial 24 cases failed with migration disabled through API scaffolds retaining the
  old parser/entry point and baseline offerings. The added automation case failed against the old
  `personality` source tag, for 22 distinct failing cases in total. Preserved baseline cases are not
  claimed as pre-existing bugs. Process checks run without display access and assert actual exit codes,
  readable diagnostics and absence of configuration writes; automation retains add-in offerings.

  Full HexIDE.Tests with foreign downloads disabled: 5,010 passed, 32 skipped, zero failures/errors,
  5,042 total. Full Integration: 4,296 passed, 2 skipped, zero failures/errors, 4,298 total. Both xUnit
  report guards pass. The unit skips are the same ten missing-template and 22 Windows-only cases
  enumerated under Manifest/resolver. Integration skips are `ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent`
  (Windows GDI) and `ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements` (the measured
  Windows Segoe UI font build). None is claimed as coverage. Raised the measured CI floors to 4,790
  and 4,083 respectively. Final suites use prebuilt binaries and separate clone-local XDG configuration
  roots; Integration's HangDump mutex also needs a private writable `XDG_RUNTIME_DIR` in this sandbox.
  Its first host attempt failed before tests because the inherited runtime directory was read-only;
  the retry with that directory redirected passed in full.

  Desktop Debug builds with zero warnings/errors; tree hygiene passes with existing non-blocking
  warnings. Command-line docs, profile survey, live specs, changelog and CLI instructions are updated.
  No VB6 measurement, MISSING_FEATURES status change or MISSING_LANGUAGE count change. No OpenSpec
  validation/archive was run, following the latest supervisor instruction. Changes remain uncommitted
  for supervisor tooling, as the run's direct instructions require. Recovery protections, #418 and
  menu/toolbar renderers remain subsequent tasks; `none` currently selects only the null bundle.
  Supervisor live check: verify Project/Tools References and New Project counts 8/1/0 for vb6/vbaode/vba,
  the unchanged four-template Add Project flyout, and custom theme/keymap/translations with fresh
  configuration versus explicit saved choices, including the resulting rendering and shortcuts.
- [x] **Recovery mode — medium:** gate customization loaders and settings/layout writes with the null
  profile. This introduces session state handled by multiple consumers. Test no bundle/user pack/add-in
  loads and no settings writes, while explicit project opening still works.
  Set recovery session state before constructing services. Bypass saved settings/layout, user keymap
  discovery, user translations and add-in loading; use Classic, Default and neutral English. Settings,
  layout/reset and translation edits remain in memory, and add-in enablement cannot persist. A real
  project file still opens through the CLI startup hook and production project service on Linux.
  All ten new cases failed before the guards (an inert session-state API scaffold), then passed without
  skips. Full HexIDE.Tests with foreign downloads disabled: 5,020 passed, 32 skipped, zero failures/errors,
  5,052 total; the xUnit report guard passes. Raised the CI floor to 4,799 (95% of that measured total).
  Skips remain the ten missing-template and 22 Windows-only cases enumerated under Manifest/resolver;
  none is claimed as coverage. Desktop Debug: zero warnings/errors. Tree hygiene passes with existing
  non-blocking warnings. Updated command-line/profile docs, the profiles and state-persistence live specs,
  MISSING_FEATURES and changelog. No new localization key, VB6 measurement or MISSING_LANGUAGE count change.
  No OpenSpec validation/archive, following the latest supervisor instruction. Changes remain uncommitted
  for supervisor tooling, as the run's direct instructions require. #418 remains the next task.
  Supervisor live check: seed saved custom settings/layout and user packs, launch `--profile none` with
  an explicit project, and confirm Classic/Default/English, baseline layout/References/project offerings
  and no add-in contributions. Change settings, translation overrides and layout (including reset), then
  close and relaunch normally; confirm the saved customization is intact. Language-server configuration
  and process launch retain their existing behaviour; this recovery mode is not a sandbox.
- [x] **#418 — small after migration:** make New Project and Add Project consume the same profile
  offerings and provide an intentional no-offerings state. The common source is settled here; keep it
  separate from migration parity. Test `vba`, `vbaode`, default and disabled template entries, and verify
  the dialog/flyout against the running IDE.


  The flyout now reads the same ordered `IProfileService.AvailableProjectTypes` list as the dialog,
  preserving template command parameters and icons, and disabling unsupported flyout entries. Empty
  profiles show a localized explanation on both surfaces; Existing/Recent opening and add-in dialog
  contributions retain their existing behaviour. The new string is in canonical English and all 29
  full translation packs.
  All five headless regression cases failed against the original implementation, then passed on Linux
  without skips (vb6, vbaode, vba, omitted selection and the empty dialog). The live profile spec,
  feature catalog, profile survey and changelog are updated. No VB6 measurement or MISSING_LANGUAGE
  count change. Supervisor live check: compare dialog/flyout offerings and ordering (8/1/0 for
  vb6/vbaode/vba), disabled flyout placeholders, and empty-state text wrapping; confirm Existing and
  Recent still open projects. Running-IDE verification is delegated to the supervisor by this run's
  direct instructions. No OpenSpec validation/archive or commit, following the run instructions.
  Full Integration: 4,301 passed, 2 skipped, zero failures/errors, 4,303 total; the xUnit report
  guard passes. Raised its CI floor to 4,088 (95% of the measured total). The two skips are
  `ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent` (Windows GDI) and
  `ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements` (Windows Segoe UI font build),
  neither claimed as coverage.
  Desktop Debug builds with zero warnings/errors; tree hygiene passes with ten existing non-blocking
  warnings. The first full unit attempt was cancelled after missing-helper failures: a build with
  project references disabled omitted `HexIDE.LspGuard.dll`. Rebuilding the full test project restored
  that helper before the full-suite retry. No product change was needed for this test setup issue.
  Full HexIDE.Tests retry with foreign downloads disabled: 5,020 passed, 32 skipped, zero failures
  or xUnit errors, 5,052 total; the xUnit report guard passes. Skips are the same ten missing-template
  and 22 Windows-only cases enumerated under Manifest/resolver above, with none claimed as coverage.

Supervisor live-check rework (2026-11-02, #2873): the flyout's model collection was populated,
but its already-created presenter retained an empty snapshot. An observable ItemsSource now carries
opening-time changes to the presenter. The headless test counts presenter items (8/1/1/8 for
vb6/vbaode/vba/omitted, including the disabled fallback), rather than trusting MenuFlyout.Items.
Refused payload links now attribute the payload file. Recovery language gates and automation replies
use a session-only notice, translated into all shipped full packs. Seven regression cases failed before
the corresponding fixes: four flyout selectors, one linked payload, one recovery reply and one gate prompt.
The reused released fragment name is replaced with `changelog.d/504-profiles.md` for this branch's
unreleased changes. No VB6 measurements or MISSING_LANGUAGE counts change. Live verification remains
with the supervisor: open the Add Project flyout under vb6 and vba, then switch language in recovery
and inspect the bilingual notice and automation reply.
Full Integration passes: 4,539 passed, two Windows font/GDI skips, zero failures (4,541 total).
The three affected unit classes pass all 65 cases without skips. Desktop Debug builds with zero
warnings/errors; tree hygiene passes with ten non-blocking warnings.

Final full unit run (xUnit runner, foreign downloads disabled): 5,229 passed, 35 skipped,
zero failures/errors, 5,264 total (above the 4,980 CI floor). The xUnit report guard passes for both
full suites. The containment helper's four stale manifest-filename expectations now name the refused
payload, matching the supervisor's correction; its class passes 31 cases with two Windows junction skips.
The 35 unit skips comprise 25 Windows/platform cases and ten cases needing the absent VB6 template
tree; none is claimed as coverage. Exact skipped unit cases:

- `HexIDE.Tests.Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text`
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does`
- `HexIDE.Tests.Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main`
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile`
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible`
- `HexIDE.Tests.Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused`
- `HexIDE.Tests.UserDataPathTests.OnWindowsItIsUnderAppData`
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead`
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionEscapeIsRefused`
- `HexIDE.Tests.Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception`
- `HexIDE.Tests.LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step`
- `HexIDE.Tests.Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn`
- `HexIDE.Tests.IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves`
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests`
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot`
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation`
- `HexIDE.Tests.Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess`
- `HexIDE.Tests.LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches`
- `HexIDE.Tests.Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it`
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_closed_job_refuses_a_process`
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it`

Integration skips: `ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent`
and `ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements` (Windows font/GDI).

## 4. Surface split-outs

- [ ] **Data-driven menus — large:** publish host-owned placement ids and a recursive model with
  command references/typed arguments, localized labels, live shortcut derivation and named providers
  for dynamic content. Guard command/provider joins, preserve focus routing and test visibility and
  invalid declarations. This is a new component with multiple behaviours and a suite of its own.
  First slice (#3510): embedded host-owned main-menu declarations and registries, typed command
  arguments, settings toggles and disabled placeholders, guarded recursively against MainView AXAML.
  Dynamic captions retain host label bindings and fallback localization keys. Provider declarations
  cover recent projects, run choices, closed documents, panel layouts and add-ins (including its
  static Manager child). Contract: `openspec/specs/main-menu-model/spec.md`.
  First-slice validation: the declaration has 199 placements: 18 submenus, 127 command references, 40 separators, six
  providers, three placeholders and five toggles. All 26 new cases pass without skips in an isolated
  xUnit host compiling the actual component and test sources; all 26 fail against inert API scaffolds
  (empty loading/parsing/validation and command registry, inert state accessors). The production files
  were never replaced by those scaffolds. Desktop Debug builds with zero warnings/errors; strict
  validation of `main-menu-model` and tree hygiene pass. No new localization key, VB6 measurement,
  rendered surface, MISSING_FEATURES status change or MISSING_LANGUAGE count change.
  Full HexIDE.Tests: 6,884 passed, 40 skipped, zero failures or xUnit errors (6,924 total),
  including all 26 new cases passing. Raised the measured CI floor to 6,577. The skips are 27
  Windows/platform cases, ten cases requiring the absent VB6 template tree, and one each requiring
  the unavailable DAP schema, netcoredbg and debugpy; none is claimed as coverage. Changes remain
  uncommitted for supervisor review at that boundary. Rendering and the other surface split-outs
  were left for later work.
  Second slice (#3510): a separate Avalonia renderer uses these declarations, host bindings, providers,
  toggles and existing live gesture handling. The embedded data now includes visibility and icon keys.
  The IDE continues to display AXAML; switching it over, focus routing and expanded profile visibility
  remain incomplete. Renderer/AXAML parity is exercised in English and Arabic, including populated
  layout providers. All eight renderer cases pass on Linux without skips; all eight fail against an
  inert renderer scaffold. The metadata parity check and unknown-visibility guard also fail when the
  new data/validation are removed (ten cases with demonstrated teeth).
  Full renderer-slice validation (WSL): HexIDE.Tests 7,124 passed, 50 skipped (7,174 total);
  Integration 5,540 passed, two skipped (5,542 total). Both reports have zero failures and zero
  xUnit errors. CI floors rise to 6,815 and 5,264. The unit skips are 37 Windows/platform cases,
  ten absent VB6 templates, one unavailable DAP schema and two uncached debug adapters. The
  integration skips require Windows font/GDI measurements. No skip is claimed as coverage.
  Desktop Debug builds with zero warnings/errors; strict spec validation and tree hygiene pass.
  MISSING_FEATURES status and MISSING_LANGUAGE counts are unchanged. Live rendering verification
  remains with the supervisor; no renderer is installed in the IDE by this slice.
- [ ] **Data-driven toolbars — large:** publish bands/placement ids and render commands and host-owned
  dynamic providers from data, sharing the registry and visibility contract. Test command, toggle,
  flyout, enablement and visibility parity. The shippable model is larger than merely rendering today's
  buttons from an in-tree list; the latter can be a separate medium first slice.
- [ ] **#419 follow-through — small once the renderer exists:** hook profile toolbar visibility into
  that renderer, with migration parity tests. Do not conflate the already-corrected spec claim with
  implementing the future profile capability.

## 5. Completion obligations for each implementation delivery

- [ ] Demonstrate changed-behaviour tests fail before the fix; run their full test projects with
  `HEXIDE_FOREIGN_LSP_DOWNLOAD=0` for HexIDE.Tests; build Desktop Debug with zero warnings.
- [ ] Verify visual surfaces in the running IDE, including References location, project offerings,
  theme/keymap application and visibility; record observable state and rendering.
- [ ] Update relevant living docs, localization packs, live capability specs and changelog fragments;
  run strict OpenSpec validation and tree hygiene. Update MISSING_FEATURES only on status changes;
  MISSING_LANGUAGE counts should remain unchanged unless separate language work actually changes them.
- [ ] Archive through the OpenSpec CLI only after implementation tasks are complete; inspect merged
  Purpose sections. Keep this change unarchived while it describes proposed behaviour.
  #2714 Windows review correction: retained bundle/ancestor handles request FILE_LIST_DIRECTORY
  instead of attribute-only access, with FILE_SHARE_READ only. The replacement regression now
  explicitly requires IOException on Windows, then checks generation 4; a second row renames an
  ancestor. Windows execution is required from the supervisor; Linux cannot prove Windows sharing.
  Sharing rule: https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew
  (omitting FILE_SHARE_DELETE denies delete access, which includes rename; attribute requests are
  exempt from sharing). No feature status or MISSING_LANGUAGE count changes.
  Windows-review validation (2026-11-02): all 18 ProfileBundleTests pass on Linux, no skips.
  The complete direct xUnit run with downloads disabled and seed 1 passes: 5,031 passed, 35 skipped,
  zero failures/errors/not-run (5,066 total, above the 4,720 floor); XML error checker passes.
  The wrapper attempt was cancelled after producing no result and is not counted as a pass.
  Desktop Debug builds with zero warnings/errors; strict profile-catalog validation, tree hygiene
  and diff whitespace checks pass. Windows teeth remain unverified locally: its two theory rows
  must reject rename and return generation 4; Linux passes do not prove that native sharing rule.
