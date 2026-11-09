# Modern profile design for review

## Authority and boundaries

The 2026-11-07 decision in #3301 approves a thin opt-in profile, not a redesign of Classic. The latest
supervisor comment adds settings search to the palette scope. This note proposes mechanisms for
review; questions below are not approved decisions. No VB6 measurement or interpreter change is needed.
Patterns and wording are authored here; no other product's appearance or strings are imported.

Read alongside [profiles](../../specs/profiles/spec.md),
[profile catalog](../../specs/profile-catalog/spec.md),
[theme packs](../../specs/theme-packs/spec.md),
[Options](../../specs/tools-options-dialog/spec.md), and the
[profile bundle survey](../../../docs/profile-bundles.md). The older profile migration change owns its
remaining surface renderers; this note does not tick their tasks or silently extend its schema.

## Bundle and shared services

Propose `--profile modern`, indexed and reserved like the existing built-ins; omission still selects
`vb6`. All eight baseline project offerings and References placement remain as in `vb6` unless Jen
asks otherwise. Modern does not introduce a different interpreter, document model or LSP client.
Saved explicit choices keep precedence over bundle defaults. Recovery selection remains `none`.

Today's version-1 manifest has a closed vocabulary: theme/keymap/language payloads, allowed settings
defaults, project offerings and limited visibility ids. It cannot declare palette availability,
settings presentation or a light/dark theme pair. Its keymap payload can bind only registered commands;
it cannot create a palette command. Its single theme payload cannot encode an OS-following policy.
Before coding, file these as profile-model findings and agree data declarations and validation with
Jen. Host-owned capability ids are a recommendation, not a new manifest format specified here.
Do not add `if (profile.Id == "modern")` consumers or executable bundle callbacks.

## Row 1: command palette (independent implementation issue)

Modern switches on a keyboard-accessible palette entry point. Its sources are menu commands, searchable
settings, open files and, when supported, backend workspace symbols. File results use the existing
open-document list, including unsaved documents; this scope does not imply filesystem indexing.
Settings results navigate to the setting on the shared settings surface; they do not execute text.

Proposed interaction: one search box, visibly grouped results, arrow-key selection, Enter to activate,
Escape to dismiss and return focus. Show each command's current keymap gesture, not a copied shortcut.
Propose a primary-modifier+Shift+P binding for discussion, using the platform keymap convention.
**The default key is unresolved:** audit existing gestures and focused-control conflicts before approval.
Whether Classic also exposes the palette on a chord VB6 did not use remains Jen's question from #3298.

`CommandKeyMapping.Table` already joins routed names to command instances. It is not a complete menu
catalog: ViewModel commands and localized menu metadata also need coverage. Recommend host metadata
that references existing commands, labels and invocation targets; no parallel command handlers.
Capture the originating document/focus target before opening the palette. Recheck the same CanExecute
and route used by the menu on activation, so the palette's search input does not become the command
recipient. Direct ViewModel actions retain their existing predicates and execution path. Any missing
routing/metadata seam is a separate finding, not permission to duplicate the action.

Reuse the existing LSP workspace-symbol client and capability gate. Render returned names and navigate
only to backend-provided navigable locations; no local binding, ranking by semantic inference or symbol
resolution. Missing capability leaves local sources usable. Empty, failed and pending requests need
distinguishable feedback, and stale replies must not replace a newer query. URI-only results must not
be given a fabricated range. Today `VBLspClient.ReadWorkspaceSymbols` substitutes position zero for
a missing range and returns `SymbolInformation`, losing that distinction. File a client-adapter finding
to preserve URI-only provenance before the palette consumes it; opening the supplied file without
claiming a symbol position remains possible. Whether to extend backend resolve support is a separate
question. This proposal does not change the existing consumer or imply resolve already exists.

Strings needed (proposed English, not keys added this run): “Command Palette”, “Search commands,
settings, and open files”, “Commands”, “Settings”, “Open Files”, “Symbols”, “No results”, “Searching
symbols…”, “Symbol search unavailable”, and an accessible search/selection announcement. Reuse existing
command and setting labels; obtain gesture text from the live keymap. Final wording and translations
belong to implementation.

Live review in Modern: open code and designer documents, including one unsaved file; invoke with the
approved chord from each, search and execute a context-sensitive menu command, and compare its enabled
state and target with the menu. Rebind its gesture and inspect the displayed result. Search a setting
and an open file; dismiss and inspect restored focus. Exercise a backend with workspace symbols and
one without, an empty reply and a failed request. Inspect result names and keyboard selection, then
capture layout. In Classic, repeat menu/shortcut actions and confirm no new entry point unless approved.

## Row 2: searchable settings (independent implementation issue)

Modern switches the settings entry point to a searchable page. Classic retains its existing Options
entry point. A shared setting descriptor should supply identity, localized label/category, type,
validation, effective default and reset operation to both presentations and to palette settings search.
Use `ISettingsService`/`SettingsService`, its existing persistence and presence-aware profile layering;
there is no Modern settings file, copied defaults or parallel value cache acting as a source of truth.
Shared editing transactions may stage values without becoming a second persistent store.

Search matches localized labels/categories and exposes the matching control, including its accessible
name. A changed marker and per-setting reset are required. Recommend “changed” mean an explicit user
override, even if equal to the current default; reset removes that override and reveals the current
profile/host default. This follows existing saved-choice provenance, but **Jen must approve the meaning
of changed and reset**, including edits not yet confirmed. Recommend retaining Options' confirm/cancel
transaction and appearance-preview rollback rather than introducing autosave in this row. Jen must
choose page hosting and commit semantics before implementation.

The issue says Classic keeps VB6's tabbed Options (#47), while the live `tools-options-dialog` contract
requires a category tree and the current design uses that model. This run does not change Classic or
rewrite that live requirement. Ask Jen whether tabs are a separate #47 delivery; the new page's delta
only promises preservation of the existing Classic surface. No new VB6 layout claim is made.
Add-in custom pages do not automatically provide searchable individual settings: ask whether v1 indexes
only their page names or requires descriptor contributions. Do not claim full field search without them.

Strings needed: “Settings”, “Search settings”, “No matching settings”, “Changed”, “Reset”, an accessible
“Reset {0}” action, and a default/override explanation. Reuse setting labels, validation messages and
existing confirm/cancel strings where applicable. Default provenance text needs final wording after
Jen chooses reset semantics; all new strings require canonical keys and shipped translations later.

Live review in Modern: find settings from different categories by keyboard, edit a valid and invalid
value, inspect markers and validation, reset one field without changing its neighbour, and confirm then
reopen/relaunch to check persistence. Exercise saved values equal to defaults and profile defaults.
Cancel an appearance preview and check rollback under the approved transaction design. In Classic,
open Options, inspect the same persisted values and edit them; reopen Modern against the same isolated
user-data directory to prove one store. Inspect accessible labels and capture both layouts.

## Row 3: following OS light/dark (independent implementation issue)

Modern enables an OS-following preference with a chosen light pack and dark pack. Classic retains its
fixed theme selection. Both use the existing theme loader, resource keys, validation and application
service. An explicit user fixed-theme choice wins over the Modern default; selecting a fixed theme
stops OS-following for that choice. Persist the policy and pair separately from its currently resolved
pack so an OS event cannot masquerade as a user choice.

Observe initial platform color values and `ColorValuesChanged` through Avalonia platform settings;
marshal updates to the UI thread and detach subscriptions when no longer following. Select the pair's
matching pack and apply its declared concrete Light or Dark variant. `ThemeService` never sets
`ThemeVariant.Default`. Validate both choices before enabling the pair; do not partially apply an
invalid pair. Keep the form canvas's pinned Light scope. Following changes IDE chrome, not VB6 form
properties or runtime semantics.

Jen must choose the shipped pair, whether Classic counts as its light member, the fallback for absent
or indeterminate OS preference, and the invalid/missing-pair recovery policy. Recommend retaining the
last valid concrete theme and explaining the problem, but do not pin that as settled behaviour.
High-contrast policy is also open; this light/dark row must not silently invent one.

Strings needed: “Follow system appearance”, “Light theme”, “Dark theme”, “Use a fixed theme”, and an
attributed invalid-pair/unavailable-system-preference message with a remedy after the fallback decision.
Reuse pack names and theme validation diagnostics. These are proposed labels for later localization.

Live review in Modern: start under OS Light and Dark, switch each way with menus, Options and tool panes
open, and inspect the concrete requested variant and pack. Capture chrome and the unchanged form canvas.
Choose a fixed theme, change the OS again and verify it stays fixed; relaunch and verify precedence.
Exercise missing/invalid pair and unavailable platform preference once policy is approved. In Classic,
repeat OS switches with Classic and an explicit dark pack and verify each fixed choice stays fixed.

## Questions for Jen before implementation

1. Approve the palette chord per platform and decide Classic availability; confirm open-files-only scope.
2. Approve a data vocabulary for the three profile switches and pair declaration, with versioning and
   allowlist changes. Staff separate findings where current bundles cannot express the design.
3. Choose settings page hosting, confirm/cancel versus immediate saving, changed/reset semantics and
   add-in search depth. Reconcile the tabs direction with the live tree contract through #47.
4. Choose the light/dark packs, unavailable-preference fallback, invalid-pair recovery and high-contrast
   boundary. Decide how an explicit follow-system user choice behaves when returning to Classic.
5. Confirm the baseline project offerings and References placement recommendation for `modern`.

## Alternatives and delivery

A full Modern redesign exceeds v1. A second settings store breaks interchange with Options. Switching
to `ThemeVariant.Default` breaks the concrete-pack/variant invariant. New palette handlers would drift
from menu routing. Hard-coded profile-name branches would bypass the bundle model this delivery tests.

Each row can be staffed and verified independently after its decisions and data seam are approved.
Bundle assembly follows the three capabilities and validates all referenced data. Tests for each later
implementation must fail before the change; shared-store, command-target and fixed-theme regressions
need headless coverage plus live review in both profiles. This docs-only run makes no coverage claim.
