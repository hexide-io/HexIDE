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
- [ ] 2.1 At start, find runs that ended uncleanly (folder present, process gone by id and start time), leaving the runs of IDEs still open alone.
  Delete a run whose `run.json` names no capture, without asking: a kill before any edit leaves one, and so does the port-collision exit (code 3), which closes without the confirmed close that ends a run.
- [ ] 2.2 Offer a never-saved project's work at start, restoring it as an unsaved project holding its documents. A project in a scratch folder the IDE made counts as never saved.
- [ ] 2.3 Offer a saved project's work when it is opened, matched by path, or by its project file where nothing is at the old path; name where the work came from.
  Offer nothing for a document whose backup equals its file on disk. A tick can land inside a load, after the project is added and before its baselines are taken, when every document reads unsaved. A file brought in with Add File also reads unsaved until #2644 is fixed.
- [ ] 2.4 Warn where a file on disk changed after the backup, using the existing baseline.
- [ ] 2.5 List every pending recovery under File, and delete a run's folder once it is restored or discarded.

## 3. Document
- [ ] 3.1 Describe recovery in the user-facing documentation, and update its rows in `docs/MISSING_FEATURES.md`.
