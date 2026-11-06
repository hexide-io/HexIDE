### Fixed

- **A project an add-in reads is refused on Windows when it's opened again with different path casing** (#3201).
  It is no longer loaded a second time, as a `.vbp` already isn't. The add-in's reports of its members use the
  same shared path rule.
