### Fixed

- **The bundled VB server returns no completions after a dot** (#3613), so its keyword and declared-name
  list cannot replace a typed member name such as `Debug.Pr` with `Debug.Preserve`. Member completion
  belongs to a binding server such as RDCore.
