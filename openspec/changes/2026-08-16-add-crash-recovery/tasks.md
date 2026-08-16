# Tasks

## 1. Capture
- [ ] 1.1 Back up unsaved editor content to a per-session store, on a timer and after edits settle.
- [ ] 1.2 Include content whose editor has been closed but whose edits are still unsaved.
- [ ] 1.3 Include new files that have no path yet but belong to a saved project.

## 2. Detect and restore
- [ ] 2.1 Distinguish a clean exit from an unclean one, so a normal close leaves nothing to offer.
- [ ] 2.2 Offer restoration when the affected project is next opened, not at startup.
- [ ] 2.3 Match the recovered session to the project by a stable id rather than by path.
- [ ] 2.4 Warn where the file on disk changed after the crash, using the existing baseline.

## 3. Extend and surface
- [ ] 3.1 Cover unsaved designer layout as well as code.
- [ ] 3.2 Show the project id read-only in the properties panel so it is discoverable.
