# Tasks

## 1. Capture
- [x] 1.1 Create a per-run recovery folder at start, in the local store or under `--user-data-dir`, recording the process's id and start time; delete it at a clean exit.
  `RecoveryPath`, `RecoveryRun` and `RecoveryService`. The folder is named by the process's start time and id, and its `run.json` is written last, naming the capture that is whole.
- [x] 1.2 Back up what a save would write for every unsaved document through the atomic writer, on a timer and after edits settle, reading editor buffers on the UI thread and writing off it.
  One five-second tick does both: it captures every time, and writes only when the capture differs from the last one written. Only an editor or designer ahead of its model is flushed (`ApplyAllUnsavedChangesEvent.OnlyWhereAhead`).
- [x] 1.3 Include documents with no file, in saved and never-saved projects alike, the project file's own text, and content whose editor was closed with its edits unsaved.
- [x] 1.4 Include a form's layout, with the controls the designer holds but hasn't committed to the model, and its binary companion.
- [x] 1.5 Keep the folder when an exit discards unsaved work without anyone deciding (a terminate signal); delete it when automation asks for the close.
  The terminate signal is caught only where the IDE already catches it, a Debug build serving automation. Elsewhere on Linux and macOS it ends the process outright, and the folder stays with the last tick's capture.

## 2. Detect and restore
- [x] 2.1 At start, find runs that ended uncleanly (folder present, process gone by id and start time), leaving the runs of IDEs still open alone.
  Delete a run whose `run.json` names no capture, without asking: a kill before any edit leaves one, and so does the port-collision exit (code 3), which closes without the confirmed close that ends a run.
  `PendingRun`. A run is claimed with an exclusive handle on a file in its folder, held while the IDE runs, so two IDEs never offer one run. A process whose start time can't be read counts as still running. A folder with no `run.json` is left alone for a minute after its last write, and a run written by a newer layout is left for the build that wrote it.
- [x] 2.2 Offer a never-saved project's work at start, restoring it as an unsaved project holding its documents. A project in a scratch folder the IDE made counts as never saved.
  The project is rebuilt by the usual loader from its backed-up project file, put back in a new folder under the temporary directory, which is deleted afterwards. A scratch project whose folder is still there is reopened from it, with its work laid over it. A scratch project whose files already hold its backup, encoded as its save would write it, is dropped without asking. #2678's fix (#2691) counts every scratch document as unsaved, and without this every killed `--newproject` run would raise the dialog at the next start. The offer at start stands in for the new-project prompt. A project named on the command line, or `--newproject`, still opens after it, and `--newproject` now saves the project it made rather than the startup project, which after a restore is the restored one.
- [x] 2.3 Offer a saved project's work when it is opened, matched by path, or by its project file where nothing is at the old path; name where the work came from.
  Offer nothing for a document whose backup equals its file on disk. A tick can land inside a load, after the project is added and before its baselines are taken, when every document reads unsaved. A file brought in with Add File also reads unsaved until #2644 is fixed.
  A backup the opened project's files already hold is dropped without asking. Restoring lays each backed-up document over the one opened, through the reload path the file watcher uses, so open windows follow, and adds back a document the project no longer lists. **The project's own settings are not restored**, such as a changed startup object or reference: only which documents it holds.
- [x] 2.4 Warn where a file on disk changed after the backup, using the existing baseline.
  The row names the changed files. Each document's backup records the hash of its file as last read or written, and the file is hashed again when the dialog opens.
- [x] 2.5 List every pending recovery under File, and delete a run's folder once it is restored or discarded.
  File → Recover Unsaved Work. Restore, Discard (confirmed first) and Decide Later; a run's record loses each project as it is dealt with, and its folder goes with the last.

## 3. Document
- [x] 3.1 Describe recovery in the user-facing documentation, and update its rows in `docs/MISSING_FEATURES.md`.
  `docs/crash-recovery.md`, linked from `--user-data-dir` in `docs/command-line.md`, and a File menu row for Recover Unsaved Work, which had none. The changelog entry is `changelog.d/499.md`.
