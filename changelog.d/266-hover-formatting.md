### Added

- **Hover and formatting in carried files** (#266, stage 2). Hover shares the code window's delay,
  cancellation and Markdown tooltip. Format Document and Format Selection use advertised providers
  and the editor's Tab Width, apply each reply as one undo step, and refuse read-only edits. Carried
  files do not format on save; the code window keeps its existing hover and guarded formatting.
