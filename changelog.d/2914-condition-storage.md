### Added

- **Breakpoint conditions** (#2914): choose Condition… from a breakpoint's gutter menu to set or remove
  an expression, with a dialog prompt explaining when the breakpoint stops. Conditional breakpoints have
  a distinct glyph and expression tooltip, persist with IDE
  breakpoints, and send conditions to capable external adapters. Incapable adapters and interpreter runs
  stop unconditionally, with a localized notice once per session. Launch-file refusals direct users to
  the gutter menu; adapter verification and messages remain visible in tooltips.
