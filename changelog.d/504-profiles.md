### Added

- **Profile bundle loader** (#504): validate versioned manifests and snapshot their JSON payloads through
  the installed-profile reader, which refuses links inside a bundle and files over 1 MiB. Resolve embedded
  or installed names and explicit directories, and refuse escaping file references with localized,
  attributed errors naming the refused file.

- **Profile settings defaults** (#504): layer validated profile defaults below explicitly saved user
  values, using existing value validation and preserving settings-file presence when saving edits.

- **Profile theme adapter** (#504): apply snapshot theme payloads above profile defaults and below
  explicit saved theme choices. Reject invalid known colours before applying a bundle, retaining the
  active theme; keep attributed warnings for unknown colour keys.

- **Profile keymap adapter** (#504): apply snapshot bindings above profile defaults and below explicit
  saved or edited keymap choices. Validate known gestures before applying a bundle and attribute
  unknown-command warnings to its source file.

- **Profile language adapter** (#504): layer snapshot translations over shipped locale fallback and
  below user overrides, reusing placeholder validation and preserving the shipped locale inventory.
  Clearing a user override reveals its profile fallback.

### Changed

- **Profiles replace personalities** (#504): select embedded or installed bundles and explicit directories
  with `--profile`. Keep the three built-ins' References placement and New Project offerings, and apply
  profile defaults, themes, keymaps and translations below explicit user choices. The removed personality
  flag now explains its replacement and exits before opening a window. `--profile none` provides a
  recovery launch with Classic, Default and neutral English, bypassing saved settings/layout, user
  keymaps/translations and add-ins. Settings, layout and translation edits stay in memory; explicit
  project paths and new-project flags still work. Language confirmation and automation replies explain
  that recovery language changes last only for the session. Automation replies stay in English
  after switching the chrome language. Profile messages are translated in every shipped language pack.

- **New Project and Add Project agree on profile templates** (#504, #418). The toolbar flyout uses
  the profile's ordered offerings and disables unsupported entries. Empty profiles explain how to
  continue by opening an existing project.
