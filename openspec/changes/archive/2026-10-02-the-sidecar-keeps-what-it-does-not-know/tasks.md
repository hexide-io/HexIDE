# Tasks

## 1. Save

- [x] 1.1 A save keeps the unrecognised content of the file as it is when rewritten, read again then, and writes
  this build's keys around it. A file deleted at the project's own place restores nothing it held.
- [x] 1.2 The version written is the higher of the one read and this build's.
- [x] 1.3 No file is created when there is nothing to record, where unrecognised content counts. An existing one
  is rewritten without an empty kind, never with a null and never deleted.
- [x] 1.4 A sidecar that could not be read or applied at load, or that cannot be read when saved, is not
  rewritten. Each refusal is logged once per project.
- [x] 1.5 A project saved elsewhere, or saved for the first time, carries what it read and replaces what lies at
  the new place. The new place becomes the project's own once written, or found empty with nothing to write.
  Paths compare as the file system does.
- [x] 1.6 A write that would leave the file as it is, is skipped.

## 2. When

- [x] 2.1 The debounced save is per project, keyed by the project instance.
- [x] 2.2 A change is owed until written, and so is a write that failed. A project's close writes what it owes,
  then clears its marks from memory without writing.
- [x] 2.3 `IUserSidecarService.FlushAll` writes what every project owes, called from the main window's close.
- [x] 2.4 Change events are suppressed per project while this service fills or empties that project's stores.
- [x] 2.5 The load registers its project before reading. Nothing is written while it reads, and what was held back
  is written once the marks are in; nothing is applied if the project closed meanwhile.
- [x] 2.6 Writes are synchronous, under one lock, and a waiting save that wakes after its project closed writes
  nothing.

## 3. Load

- [x] 3.1 Every entry naming a document is united for it, and an entry goes to every document of its name, so a
  form and a module of one name no longer throw and two spellings of one key no longer replace each other.

## 4. Tests

- [x] 4.1 `UserSidecarSaveTests`, one test per case above. The debounce and the file read are both seams the tests
  hold open, so nothing sleeps; the refusals' log is captured through a Serilog sink; the late-waking save is held
  in a queued context, as the UI thread holds it; a failed write is made with the temporary file's path occupied by
  a directory, which fails on every system. Case-insensitive paths are pinned on Windows only, where they hold.
- [x] 4.2 The existing `UserSidecarBreakpointTests` comment that said the debounced save did not fire is
  corrected: it does fire, and the explicit save now supersedes it.
