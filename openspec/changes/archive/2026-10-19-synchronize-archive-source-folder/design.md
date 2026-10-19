## Context
The HexIDE.TwinBasic reader exposes immutable archive snapshots. A folder projection supports Git without requiring a writer or a compiler connection. The `.twinproj` stays authoritative in this phase; externally edited files stop synchronization and are never silently imported or replaced.

## Layout
All paths are relative to the saved archive's containing directory, not the filesystem root:

```text
MyApp.twinproj
.hexide/
  .twinproj/
    .gitattributes
    MyApp.twinproj/
      Sources/
      Resources/
      Settings
      .meta
      ...exact stored hierarchy...
    .manifests/
      MyApp.twinproj.json
  .twinproj-sync/
    MyApp.twinproj/
      .gitignore
      owner
      lock
      baseline.json
      pending.json
      <transaction-id>/
        next/
        previous/
        new.json
        old.json
```

The archive filename distinguishes sibling projects. The projection and deterministic manifest are **tracked content**, not a disposable cache. Do not ignore all of `.hexide`. `.meta` and hidden entries remain in the exact hierarchy, including any volatile metadata; filtering those would be a separate policy with explicit loss-of-fidelity consequences. Root entry metadata is recorded in the manifest rather than an extra physical root folder.

The manifest has a schema version, container version/hash and ordered entry paths, names, directory status, categories, flags, unsigned revisions, byte lengths/hashes and revision trailers. It has no timestamps or absolute host paths. `.gitattributes` sets `* -text` to preserve bytes through Git even with automatic line-ending conversion enabled. Existing conflicting projection attributes are refused. User-level Git ignore rules or higher-precedence attributes can still affect tracking; synchronization does not rewrite the surrounding repository configuration.

Local state ignores all its contents, including its own ignore file. Staging, atomic metadata-write temporary files and retained backups stay there. No operation deletes old transaction directories. The owner marker prevents reuse of an unrelated state directory. Git clones omit this state: adoption requires the tracked manifest and entire tree to match the currently saved archive exactly. A clone whose archive and projection differ must first restore their matching versions; this phase does not infer which side is newer.

## Transaction and conflicts
One exclusive file lease serializes operations per project. Parse the bounded saved archive and validate every destination before writing content. Stage the complete replacement, verify its bytes, then recheck the source hash, previous tree and tracked manifest. Record a journal before moving the previous tree into retained recovery storage and installing the replacement. Publish tracked metadata and local baseline only after tree verification. A no-op preserves member and manifest timestamps.

Cancellation is honored before the journal. Once the journal is written, finish the commit or retain recovery evidence. On the next invocation with a readable, valid saved archive, recover under the lease: restore a parked previous tree when installation has not happened, or finalize an installed replacement after verifying it. Never overwrite metadata or trees changed since interruption. An edit discovered in the parked previous tree stops the operation, retaining that edit; the caller receives an error with the affected path. Local additions, deletions, renames and empty directories count as divergence, as do file and manifest edits.

## Path and resource limits
Accept saved `.twinproj` projects only. Reject path separators, traversal, control characters, trailing dots/spaces, Windows device names, oversized segments and case/Unicode-normalization collisions on every platform. Reject `.git`, `.gitattributes` and `.gitignore` archive entries because they would become active repository controls. Fail explicitly instead of renaming or omitting any entry. Resolve aliases in the existing project-containing directory first (including the standard macOS `/var` alias), then derive the physical output location. Reject symbolic links and reparse points in the archive file and managed paths, including lock and state files. Parsing and reads are bounded to 64 MiB; inventory is bounded to 100,000 entries and depth 128. Existing unrelated siblings and repository metadata are untouched.

## Limits and future work
This is an explicit one-way operation over saved bytes. No watcher, archive writer, folder import, compiler session, UI command or change to document mutability is included. The next UI slice will report the physical directory and explain edit conflicts. Folder authority remains a later workflow once write-back is proven.

Recovery handles process interruption at tested commit boundaries; it is not a cross-file atomic transaction or a guarantee against power loss. A crash during initial state-directory creation may leave an incomplete ownership marker and require manual inspection. Path checks and repeated hashing detect ordinary external changes but do not lock unrelated editors, prevent every filesystem race or hard-link alias, or defend against a hostile process replacing paths between checks. Retained backups preserve the former tree; automatic pruning is deliberately deferred. If both installed and recovery trees are edited, recovery refuses and leaves both for inspection.

The official impexp helper skips `.meta` and has different overwrite/import behavior. This projection preserves the complete stored hierarchy and does not claim drop-in import interoperability with that helper.
