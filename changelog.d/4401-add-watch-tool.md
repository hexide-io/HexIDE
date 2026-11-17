### Fixed

- **The `add_watch` tool, paused inside a Property accessor, scopes the watch to that accessor** (#4401). It stored the
  frame as `Class1.X`, which no longer matched once each accessor became its own watch context, so the watch read
  `<Out of context>` in its own frame. It now stores `Class1.X(Get)`, as the Add Watch dialog does.
