# Tasks

The store's layout is written first in group 1, so group 1 waits on the design decision on #499.

## 1. Capture
- [ ] 1.1 Create a per-run recovery folder at start, in the local store or under `--user-data-dir`, recording the process's id and start time; delete it at a clean exit.
- [ ] 1.2 Back up what a save would write for every unsaved document through the atomic writer, on a timer and after edits settle, reading editor buffers on the UI thread and writing off it.
- [ ] 1.3 Include documents with no file, in saved and never-saved projects alike, the project file's own text, and content whose editor was closed with its edits unsaved.
- [ ] 1.4 Include a form's layout, with the controls the designer holds but hasn't committed to the model, and its binary companion.
- [ ] 1.5 Keep the folder when an exit discards unsaved work without anyone deciding (a terminate signal); delete it when automation asks for the close.

## 2. Detect and restore
- [ ] 2.1 At start, find runs that ended uncleanly (folder present, process gone by id and start time), leaving the runs of IDEs still open alone.
- [ ] 2.2 Offer a never-saved project's work at start, restoring it as an unsaved project holding its documents. A project in a scratch folder the IDE made counts as never saved.
- [ ] 2.3 Offer a saved project's work when it is opened, matched by path, or by its project file where nothing is at the old path; name where the work came from.
- [ ] 2.4 Warn where a file on disk changed after the backup, using the existing baseline.
- [ ] 2.5 List every pending recovery under File, and delete a run's folder once it is restored or discarded.

## 3. Document
- [ ] 3.1 Describe recovery in the user-facing documentation, and update its rows in `docs/MISSING_FEATURES.md`.
