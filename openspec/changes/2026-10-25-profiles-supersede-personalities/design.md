# Profile bundle design

## Boundary and evidence

This is a proposal, not a description of shipped support. #504 settles supersession and removal of the
old flag; approval of this document settles the remaining choices. The dated survey is architectural
input, not authority for VB6 behaviour. No new oracle expectation is introduced.

A profile contains declarative data only. It can select existing commands, template offerings and
packs; it cannot load assemblies, AXAML, scripts, new icons or new localization keys. Profiles do not
change runtime semantics, project file compatibility or command enablement. The add-in loader retains
its own lifecycle and trust rules.

## Directory and manifest

A directory contains `profile.json` and its referenced JSON files. Built-ins use the same shape as
installed bundles and are shipped in a read-only, explicitly indexed embedded resource set; no
`avares://` directory enumeration is assumed. Installed bundles are copied directories, not archives
that the IDE extracts. Version 1 has no inheritance, composition, remote URLs or executable hooks.

Example (the names of surface entries below are proposed public ids):

```json
{
  "schemaVersion": 1,
  "id": "example",
  "keymap": "keymap.json",
  "theme": "theme.json",
  "languageOverrides": { "en": "translations/en.json" },
  "projectTypes": ["standard-exe"],
  "menuVisibility": { "project.references": true, "tools.references": false },
  "toolbarVisibility": { "standard": true },
  "settingsDefaults": "settings.json"
}
```

`schemaVersion` and `id` are required. Ids use lower-case ASCII letters, digits and hyphens, starting
with a letter. `none` is reserved. Folder basenames must equal installed ids; manifest ids do not
redirect lookup. All other members are optional. Omission contributes no override; an empty
`projectTypes` explicitly offers none. The array replaces the baseline list, preserving order.
Visibility maps override only named entries; omitted entries retain baseline visibility. Hiding a
menu parent hides its descendants. Toolbar visibility names bands; per-button visibility uses
`band.command-id` placement ids, allowing the same command on multiple surfaces.

| Member | Existing format or proposed minimal extension |
|---|---|
| `keymap` | Relative file containing `KeymapPackRecord`: `name`, `description`, `bindings`; command names and gesture parsing unchanged. |
| `theme` | Relative file containing `ThemePackRecord`: `name`, `description`, `themeVariant`, `colors`; existing colour-key mapping unchanged. |
| `languageOverrides` | Locale-to-file map; each file is `LanguagePackRecord`, as used by `UserTranslationsService`. Existing orphan-key, empty-value and placeholder validation remains. It overrides shipped locales, not the shipped locale inventory. |
| `projectTypes` | Ordered stable template ids. Initially `standard-exe`, `activex-exe`, `activex-dll`, `activex-control`, `application-wizard`, `wizard-manager`, `data-project`, `enterprise-controls`, in `IProjectTemplate.Templates` order. These are new ids, not captions or the non-unique icon keys. |
| `menuVisibility` | Placement-id-to-boolean map, initially `project.references` and `tools.references`; later catalogued menu placements use the same map. |
| `toolbarVisibility` | Band/placement-id-to-boolean map. Band ids: `standard`, `edit`, `debug`, `form-editor`. The toolbar work publishes its placement catalogue before accepting button entries. |
| `settingsDefaults` | Relative file containing a partial existing `SettingsData` JSON object. Only explicitly present allowed properties contribute; deserializing absent properties into CLR defaults must not overwrite the baseline. |

Settings defaults allow only `requireVariableDeclaration`, `autoListMembers`, `autoQuickInfo`,
`autoIndent`, `tabWidth`, `formatOnSave`, `showGrid`, `gridWidth`, `gridHeight`, `alignToGrid`,
`promptForProjectOnStartup`, `activeTheme`, `activeKeymap`, `activeLanguage`, the four
`is*ToolbarVisible` settings, and `isMinimapVisible`. Existing value validation applies. Window state,
paths, server configuration, unsigned-add-in policy and revocation URLs cannot be supplied. When the
manifest supplies a theme or keymap file, it takes precedence over the corresponding profile settings
default; an explicit saved user choice still wins.

All file references are relative to the bundle root. Rooted paths, traversal and symlink escapes are
rejected, including cross-platform path spellings. Reading a profile never writes back into it.
Unknown manifest fields, schema versions, ids, surface ids, disallowed settings and missing or invalid
referenced files reject the selected bundle before application. Existing pack-level warn-and-skip
rules for unknown colour/command/translation keys remain unchanged; warnings identify the bundle and
file. Validate into a snapshot first, then apply once: a failed profile must not leave half its surfaces
active. Error messages are localized and identify the source and remedy. No silent fallback from an
explicit selection. Built-in validity is a build-time test obligation.

## Selection, resolution and precedence

`--profile <value>` (also `/profile`) is available in Debug and Release. Accept exactly one selector;
missing values or repeated selectors are startup errors. The value is classified in this order:

1. Exact `none`: null profile, safe mode.
2. A rooted path, or a value containing `/` or backslash, or starting with `.`: directory path.
   Resolve relative paths against the invocation directory, before startup changes directory.
   Use `./example` to choose a relative directory whose basename is also a valid name.
3. A valid bundle id: resolve by name. Other input is an error.

Built-in ids `vb6`, `vbaode`, `vba` always resolve through the embedded index. Their names are reserved
and cannot be shadowed. Other names resolve only at `UserDataPath.For("profiles")/<id>/profile.json`.
`--user-data-dir` therefore also selects the installed-bundle root; parse it before resolving profiles,
regardless of argument order. There is no current-directory search, machine-wide install directory,
network discovery or automatic selection of newly installed bundles. A path selects that exact bundle,
even if its id collides with a built-in; its provenance remains the absolute path. Name matching is
ordinal and lower-case on every platform. A broken installed bundle errors instead of falling back.

Omitting the flag selects embedded `vb6`; the selection is not persisted. Settings resolution is:
host defaults, profile defaults and payloads, explicit saved user values, then existing session flags.
This needs presence-aware reading of settings, not comparison against default values. Existing settings
files count every present property as explicit; there is no speculative migration that guesses intent.
Language strings merge using the existing locale fallback chain, with profile overrides above embedded
strings and user translation overrides last at each corresponding locale layer. User choices of theme
and keymap supersede profile choices. Profile visibility is the maximum offered surface; a saved user
visibility choice may hide it further, never resurrect an excluded placement.

`none` loads no bundle, user pack discovery, user translations, saved settings/layout or add-ins. It
uses the host baseline with Project References shown, Tools References hidden and all baseline project
types offered, Default keymap, Classic theme and neutral English. This is a recovery launch, not a
request to hide every command. It does not delete or rewrite saved configuration; settings/layout
changes in this mode stay in memory. Explicit project paths and `--newproject` retain their existing
meaning. Safe mode is limited to customization loading; it does not redefine language-server launch or
promise a network/process sandbox. Safe mode is selected only by `--profile none`, not a second flag.

## CLI removal and the help budget

Proposed option row: `--profile <value>` with summary
`Base profile: name, directory path, or none (safe mode).`
The longest current value column remains `--user-data-dir <path>` (22 cells); the new spelling is 17.
The proposed row is 82 cells (`2 + 22 + 2 + 56`), below 94, without widening the other rows.
Keep `ServerOptions.Options` as the parser/help source and extend its existing documentation/width tests.

`--personality` and `/personality` are removed from accepted options and help. Recognize those removed
spellings solely to refuse launch before a window opens, with a localized diagnostic meaning
“--personality has been removed. Use --profile vb6, --profile vbaode or --profile vba.” and exit code 2.
This applies even when a valid `--profile` is also present; no old value silently chooses the default.
`--help` remains a successful help-only request. This targeted migration error does not change the
parser's treatment of unrelated unknown flags. Concrete localized keys and every shipped translation
belong to implementation, not this proposal.

## Built-in migration: preserve the current three effects

| Bundle | Project References | Tools References | New Project offerings |
|---|---|---|---|
| `vb6` | visible | hidden | All eight entries, in `IProjectTemplate.Templates` order, including disabled placeholders |
| `vbaode` | hidden | visible | Standard EXE only |
| `vba` | hidden | visible | Empty |

Built-ins declare only those visibility entries and project types. They provide no theme, keymap,
translation, settings or toolbar overrides. Existing user configuration thus keeps its effect.
The common host baseline preserves today's remaining chrome. In particular, the toolbar Add Project
flyout still offers its current four templates and `vba`'s New Project dialog remains empty at the
migration boundary. #418 owns reconciling those surfaces; reproducing the current state is not a claim
that it is correct VB6 behaviour. #419 has already removed the unsupported toolbar claim from the live
personality spec; replacing the concept must not restore that claim as current implementation.

Remove `PersonalityName`, `PersonalityProfiles`, `IPersonalityService` and name-switch consumers only
when the profile seam is working and covered. Assert the table against the old implementation before
removal, and assert every unmodified surface stays unchanged through the migration.

## Alternatives considered

- Keep `--personality` as an alias: contradicts the recorded decision; it would preserve a second public
  concept indefinitely. A targeted error makes stale launch scripts diagnosable.
- Separate `--safe-mode`, `--profile-dir` and named personalities: duplicates precedence and lifecycle
  rules. One value expresses all three with an explicit path escape for ambiguous names.
- Let installed names shadow built-ins: convenient for replacement, but makes default launches depend
  on disk contents. Reserved names keep recovery and compatibility predictable; explicit paths allow
  deliberate replacements.
- Use the add-in package loader/signature flow: profiles contain no code and must shape the first
  window, while add-in consent arrives later. Validate data and constrain paths; named callbacks in
  future surface models resolve only host-registered providers and cannot execute bundle-supplied code.
- Introduce new theme/keymap/translation schemas: duplicates shipped parsers and migration obligations.
  Only the manifest and visibility/template ids are new. Standalone pack discovery remains useful.
- Build a full placement DSL now: exceeds #504. Visibility targets host-owned placement ids. The menu
  and toolbar split-outs define declarative command placement and host-named dynamic providers, with
  arguments where needed, before shipping those expanded surfaces. No widget escape hatch is allowed.
- Copy defaults into settings on first launch: erases provenance and makes switching bundles sticky.
  Presence-aware layering keeps profiles read-only and saved user choices explicit.

## Review and implementation risks

Approve safe-mode scope, reserved-name precedence, settings allowlist and atomic rejection together with
the manifest. These are proposed decisions, not missing VB6 measurements. Surface catalogues are
versioned public API; their ids need guarded joins to commands and localized labels. Applying the
existing persisted settings as explicit choices intentionally means an established user may see less of
a profile's theme/default changes than a fresh user. Each split-out must report this clearly in tests.
