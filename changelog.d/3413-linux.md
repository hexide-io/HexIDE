### Added

- **The repack tool now handles Linux builds** (#3413). `HexIDE.Repack` renames the executable from a brand's `id` and
  writes a `.desktop` file and icons from the same `brand.json`. A branded build sets its X11 window class to match,
  so a desktop groups its windows with its menu entry.
