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
