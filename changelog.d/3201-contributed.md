### Fixed

- **Opening a project an add-in reads again with different path casing is refused on Windows too** (#3201).
  It was loaded a second time; it is now refused as already open, as a `.vbp` already is.
