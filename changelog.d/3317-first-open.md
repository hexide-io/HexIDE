### Fixed

- **RDCore can read a VB6 project's language on first open** (part of #3317). Entries declaring both
  `workspaceArtifact` and `projectImport` seed a missing descriptor before launch, with consent, then
  import as usual. Existing files are never overwritten by the seed, and later changes only import.
