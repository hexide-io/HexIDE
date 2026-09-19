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

- [ ] 2.1 A per-server consent decision — allow once, allow always, deny — keyed on the server and the
  resolved path, following the existing add-in consent store rather than a second model.
- [ ] 2.2 Resettable in Options, beside the add-in consents.
- [ ] 2.3 The write: inside the workspace only, atomic, and skipped when the content is byte-identical so a
  server's own watcher is not woken for nothing.
- [ ] 2.4 The gate in `EnsureStartedAsync`, before the client is created. A provider that throws, or a write
  that fails, disables that server rather than the IDE, and records the reason on the connection so the
  protocol inspector shows it.
- [ ] 2.5 Tests, including denial, a provider that throws, and a write refused for escaping the workspace.

## 3. The first provider

- [ ] 3.1 Decide the first real consumer, given the caveat in the proposal.
- [ ] 3.2 The provider.
- [ ] 3.3 End to end against a third-party server: the artefact is written before the server starts, and
  the server demonstrably read it.

## 4. Invalidation

- [ ] 4.1 Depends on hexide-io/HexIDE#456 — regenerate on a material project change and notify.

## 5. Documentation

- [ ] 5.1 `docs/language-servers.md`: the new field, what a provider is, and that writing is consented.
- [ ] 5.2 Validate and archive; rewrite the merged spec's Purpose if the CLI replaces it.
