### Added

- **Restart a language server when its workspace artefact changes, opt-in** (#2072). Set
  `workspaceArtifact.restartOnChange` for a server that reads its file only at startup; debounced
  regeneration restarts that server and reopens its documents only when the file changes. Requests
  wait for those documents to reopen, including when a workspace-root restart overlaps it. The
  spring-tide RDCore profile enables it until RDCore can reread its own project file.
