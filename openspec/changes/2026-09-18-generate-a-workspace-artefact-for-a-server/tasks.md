# Tasks

## 1. The seam

- [x] 1.1 `WorkspaceProjectSnapshot` in Core: the project's name, the path of the file that defines it, its
  files and its references. Read-only, and a projection rather than the live model, so a provider cannot
  reach back into the IDE.
- [x] 1.2 `WorkspaceProjectFile` carries **both** the path as the project spells it (backslashed, as the
  native format requires on every host) and the resolved host path. A provider writing a descriptor needs
  the first; one reading a file from disk needs the second. Conflating them is the `System.IO.Path` trap
  that only fails on Linux.
- [x] 1.3 `WorkspaceProjectReference`: name, GUID, major, minor, LCID, path. Every field optional but the
  name, because a native project file records what it was given and a descriptor consumer needs whatever
  survived.
- [x] 1.4 `IWorkspaceArtifactProvider` — a name, and one method returning content or null. Null means "no
  artefact for this project", which is not a failure.
- [x] 1.5 `IWorkspaceArtifactProviderRegistry` — lookup by name, so configuration names a provider and the
  set of providers is open.
- [x] 1.6 `workspaceArtifact` on a server entry: a relative `path` and a `provider` name.
- [x] 1.7 Loader validation, reported rather than thrown, consistent with every other configuration fault:
  a spec naming no provider, an absolute path, an empty path, and a path escaping the workspace are each
  refused with the entry named. A refused artefact spec must not prevent the server starting — it has to
  degrade to the server's own behaviour, not to no server.
- [x] 1.8 Carry the validated spec onto the registration, so the launch path has it without re-reading
  configuration.
- [x] 1.9 Tests: registry lookup, miss, case-insensitivity and last-wins; configuration round-trip; and
  each validation refusal, asserting in every case that the entry and its registration survive it.
  **Nothing populates a snapshot yet** — it is a value type until the shell projects one in phase 3, so
  there is no projection to test here and none is claimed.
- [x] 1.10 Prove the two separator guards by mutation, since neither is provable on one platform alone.
  Splitting on `/` only kills two cases on Windows. Reducing the absolute check to `Path.IsPathRooted`
  kills three **on Linux and none on Windows** — the same broken code passing 21/21 here and failing there,
  which is the bug class this guard exists for, demonstrated rather than asserted.

## 2. Consent and the write

- [x] 2.1 A per-server consent decision — allow once, allow always, deny — keyed on the server and the
  resolved path, following the existing add-in consent store rather than a second model.
- [x] 2.2 Resettable in Options, beside the add-in consents.
- [x] 2.3 The write: inside the workspace only, atomic, and skipped when the content is byte-identical so a
  server's own watcher is not woken for nothing.
- [x] 2.4 The gate in `EnsureStartedAsync`, before the client is created. A provider that throws, or a write
  that fails, leaves the server launching without the artefact, and records the reason on the connection so the
  protocol inspector shows it.
- [x] 2.5 Tests, including denial, a provider that throws, and a write refused for escaping the workspace.

## 3. The first provider

- [x] 3.1 The first consumer is clangd for a project carrying C/C++ sources, resolving the proposal’s caveat.
- [x] 3.2 The provider.
- [x] 3.3 End to end against a third-party server: the artefact is written before the server starts, and
  the server demonstrably read it.

## 4. Invalidation

- [ ] 4.1 Depends on hexide-io/HexIDE#456 — regenerate on a material project change and notify.
  Debounced regeneration is implemented; the watched-file notification remains with #456.

## 6. The RDCore provider (#455, phase 2)

- [x] 6.1 `WorkspaceProjectSnapshot` carries the project's Conditional Compilation Arguments, empty for a line
  vb6.exe refuses, read with the parser the interpreter uses.
- [x] 6.2 The `rdcore` provider: every form, module, class and user control as a module, by its host path relative
  to the root; the references; the arguments as precompiler constants; PascalCase keys; `Version` 0.1.0 unless
  settings give one; no `OtherFiles`, which RDCore parses as code; nothing for a project with no code files.
- [x] 6.3 Measured against RDCore `653244e2` over the wire, with the provider's own output: every listed module gets
  RDCore's semantic diagnostics, in a subfolder or beside the project folder, the constants are honoured, and VBA
  resolves with no reference. RDCore reads the file only when it starts.
- [x] 6.4 The spring-tide demo generates its `.rdproj` instead of committing one.
- [ ] 6.5 Live check in the IDE: the consent prompt names `.rdproj`, and RDCore's diagnostics arrive for the demo.

## 7. Restart on change (#2072)

- [x] 7.1 `restartOnChange` on `workspaceArtifact`, off unless set: a regeneration that changes the file's bytes,
  or takes it back, asks the registry to restart that server. Not the write before a launch, an identical
  regeneration, or a refused or denied write. The debounce batches restarts with regeneration.
- [x] 7.2 The registry restarts only that server, through the workspace-move path's stop, and opens its documents on
  it again; the other servers keep theirs. A server not running is left alone, and a failed one stays failed.
- [x] 7.3 The spring-tide profile and the RDCore entry in `docs/language-servers.md` set it. A stopgap until RDCore
  re-reads its `.rdproj`.

## 5. Documentation

- [x] 5.1 `docs/language-servers.md`: the new field, what a provider is, and that writing is consented.
- [ ] 5.2 Validate and archive; rewrite the merged spec's Purpose if the CLI replaces it.
