# Add crash recovery for unsaved work

> Converted from the `crash-recovery` design record (2026-08-16). **Amended 2026-10-28 for #499**, now Beta gate 3
> (#2057: unsaved work survives a crash). The amendment keys recovery by IDE run rather than by path, keeps the text
> of unsaved documents in a private local store, and recovers a project that was never saved. That last was a
> documented limitation in the original. **The maintainer approved the design on #499 on 2026-10-28.**

## Why

A clean exit already prompts to save. The gap is the unclean one: a crash, a kill or a power loss. There the IDE simply
disappears with whatever was unsaved, and offers nothing on the way back.

The project has no autosave by design. VB6 didn't have one either, and the explicit-save model is deliberate. But "we
do not autosave" and "we lose your work when we crash" are different claims, and only the first is defensible.
Backing up unsaved work without touching the real files keeps the model and removes the loss.

The case most worth recovering is the one the original design excluded: an hour in a project that has never been
saved. #489 made a document with no file a normal state, measured against VB6, which writes nothing for a form or
module until the project is saved. A path can't key it, and a fingerprint can't restore it.

## What Changes

- **Each IDE run keeps a private recovery folder.** It is created at start, written while the run lasts, and deleted
  at a clean exit.
- **It holds what a save would write for every unsaved document,** with or without a file, and the project file's own
  text. That covers forms, modules, classes, UserControls and property pages alike. A form's layout is included,
  with the controls the designer holds but hasn't yet committed to the model, and so is its binary companion. It is
  written within a few seconds of a change.
- **A run that ended uncleanly is found at the next start:** its folder is still there and its process is gone.
- **An exit that discards unsaved work without anyone deciding is not a clean one.** A terminate signal leaves that
  work recoverable. A forced close that automation asks for is the caller's decision, and is clean.
- **A never-saved project is offered at start,** and restores as an unsaved project holding its documents.
- **A saved project is offered when it is next opened.** It is matched by its path, or by its project file where
  nothing is at the old path any more, so a moved project still matches. The offer says where the work came from.
- **Restoring warns** where a file on disk changed after the backup.
- **Work for a project that is never reopened isn't stranded:** a File command lists every pending recovery.
- **Restoring or discarding deletes the run's folder.**

## Decisions

These were proposed on #499 on 2026-10-28, and the maintainer approved them there the same day.

1. **Keyed by run, not by path.** A run's folder records, for each project it held:
   - the project's path, if it has one;
   - a fingerprint of its project file;
   - each document by its name within the project, the key the user sidecar already uses (#273's union rule covers
     duplicate names).

   A document's identity in memory, `DocumentIdentity`, is a reference and can't outlive the process. So the name is
   the key, and the run is what makes it unique.
2. **Content, not a fingerprint.** A hash answers "did this change", not "what was it". The folder carries the text.
3. **A private, local store.** The folders live in:
   - Windows: `%LocalAppData%\HexIDE\Recovery`, the machine's own app data and not the roaming `%AppData%\HexIDE`
     that holds settings. The roaming profile can follow a user to another machine, and unsaved work belongs to the
     machine it was typed on.
   - Linux and macOS: `$XDG_STATE_HOME/HexIDE/Recovery`, by default `~/.local/state/HexIDE/Recovery`, which is what
     the XDG convention reserves for state an application reuses on restart.
   - With `--user-data-dir`: `Recovery` under that directory, so a session started with it stays self-contained.

   **Never the project's folder or its sidecar,** which may be committed to source control. Every write goes through
   the atomic writer (#2503).
4. **No persisted project id.** The original design added one. Two places could hold it, and both were rejected:
   - **A line in the project file:** only an explicit save may write a user's VB6 files, and VB6 must still read the
     file as it wrote it.
   - **The user sidecar:** it would create a file beside every project ever opened, which is new clutter in the user's
     folder. A copied folder also copies the id, so the id would still need the rule below to tell a move from a copy.

   Matching by path, then by the project file's fingerprint only when the old path no longer holds a project, gives
   the moved case and refuses the copied one, without writing anything beside the project. With no id there is
   nothing to show in the properties panel, so the original task for that goes.
5. **A clean exit is one where somebody decided.** That means the save prompt was answered, or nothing was unsaved,
   or a caller asked to discard: MCP's `shutdown_ide` closes without prompting by default, and that is its caller's
   choice. A terminate signal also closes without prompting, but nobody chose to lose the work, and that is the loss
   this change exists to prevent. It is unclean, as are a crash, a kill and a power loss.

   Counting an automation close as unclean was considered and rejected: every session on the development loop calls
   it after editing, so each relaunch would open on a recovery prompt for work the caller had already thrown away.

## Impact

- **New capability:** `crash-recovery`.
- **Reuses** `FileHasher` and the per-file baseline the file watcher keeps, to warn where a file changed on disk.
- **Backup only.** Real project files are still written solely by an explicit save. Nothing here changes what a save
  does or when it happens.
- **A recovery offer is a new modal at start-up.** Automation sees it as it sees any dialog, and answers it.
- **A scratch project counts as never saved.** `--newproject`, the automation flag, saves its project into a folder
  the IDE made under the temporary directory, and nobody reopens a project from there. So after a crash its work is
  offered at start, as a never-saved project's is, rather than waiting on an opening that won't come.
