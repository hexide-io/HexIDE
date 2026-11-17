### Fixed

- **VBA7 64-bit syntax no longer gets false syntax errors in the bundled language server** (#4365).
  It accepts `DefLngLng`, `DefLngPtr` and the `^` type character, alongside `LongLong`, `LongPtr` and
  `Declare PtrSafe`, while preserving exponentiation. Acceptance is syntactic and independent of project bitness.
