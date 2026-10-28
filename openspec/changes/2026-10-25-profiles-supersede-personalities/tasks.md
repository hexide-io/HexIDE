# Tasks

Jen approved this change as written on 2026-10-25 in #504 (after #1953). The latest supervisor
comment authorises phase 2 only for this delivery. Sizes use `docs/issue-labels.md`; these are separate
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

- [ ] **Manifest/resolver — large:** implement versioned immutable snapshots, embedded index, installed
  discovery, path containment, validation and attributed errors. This is a new component with multiple
  behaviours and its own tests. Cover missing files, traversal/symlink escape, collisions and atomicity.
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
- [ ] **Pack/default adapters — medium per adapter:** separately connect existing theme, keymap,
  language and presence-aware settings readers. Each is a producer/consumer change following existing
  pack precedents. Test precedence, invalid data and preservation of saved user values.
- [ ] **CLI and built-in migration — medium:** wire the resolved profile to consumers, convert the
  three built-ins, remove personality APIs, add `--profile` and the removed-flag error. Producer and
  consumers must land together. Pin the old table before removal, test name/path/none and argument
  ordering, old-flag refusal, help width, default selection and both flag prefixes. Translate diagnostics
  into all shipped packs; update command-line docs and live specs only as behaviour ships.
- [ ] **Recovery mode — medium:** gate customization loaders and settings/layout writes with the null
  profile. This introduces session state handled by multiple consumers. Test no bundle/user pack/add-in
  loads and no settings writes, while explicit project opening still works.
- [ ] **#418 — small after migration:** make New Project and Add Project consume the same profile
  offerings and provide an intentional no-offerings state. The common source is settled here; keep it
  separate from migration parity. Test `vba`, `vbaode`, default and disabled template entries, and verify
  the dialog/flyout against the running IDE.

## 4. Surface split-outs

- [ ] **Data-driven menus — large:** publish host-owned placement ids and a recursive model with
  command references/typed arguments, localized labels, live shortcut derivation and named providers
  for dynamic content. Guard command/provider joins, preserve focus routing and test visibility and
  invalid declarations. This is a new component with multiple behaviours and a suite of its own.
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
