### Added

- **Language servers can read the editor’s Tab Width** (#2669). The client advertises workspace
  configuration support, answers `editor.tabSize`, returns null for unknown settings, and notifies
  running servers when Tab Width changes.
