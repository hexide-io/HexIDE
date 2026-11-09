### Added

- **A `brand.json` beside the executable makes an installation another product** (#2042). It sets the name, logo,
  tagline, links and added credits shown in the title bar, dialogs, menus, About and `--help`, and an id that gives
  the product its own settings, logs and recovery files. HexIDE's own credits and notice stay, and a broken file
  stops the IDE from starting rather than letting it run as HexIDE. See `docs/white-label.md`.
