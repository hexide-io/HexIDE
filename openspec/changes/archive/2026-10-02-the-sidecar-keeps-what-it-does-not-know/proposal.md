# The sidecar keeps what it does not know, and every change reaches it

## Why

The per-user sidecar lost state in two separate ways (hexide-io/HexIDE#466).

**A save rebuilt the file from nothing.** It wrote the bookmarks and breakpoints and nothing else, so content a
newer build had recorded was deleted by an older one, which is the case the user-sidecar spec forbids. It
stamped every rewrite with this build's format, so a newer build's file came back labelled as an older one's.
And Save Project wrote a sidecar for every project, `{"version": 1, "bookmarks": null, "breakpoints": null}`
included, where the spec says one is created only when there is state to record.

**The debounced save was shared by every project.** A change in one project cancelled another's waiting save,
and so did closing one project. Closing a project within the debounce cancelled its own last change. None of
these losses was visible: personal state is not part of what makes a project need saving, so nothing prompted
for it.

This lands ahead of the sidecar migration in #273 task 3.17. A version-2 sidecar puts its marks under new keys,
and an older build without this fix deletes them on its next save. A build with it keeps them.

## What changes

- **A save rewrites rather than rebuilds.** What it keeps is what the file holds when it is rewritten, read again
  at that moment, so content another build wrote while this one had the project open survives as well as content
  that was there when it opened, and content deleted with the file stays deleted. For this build's own keys,
  `bookmarks` and `breakpoints`, this session's state wins.
- **The recorded format is never lowered.** A newer build's sidecar keeps its version through a rewrite.
- **Nothing to record, no file.** A save with nothing to record creates nothing, where content this build does
  not understand counts as something. A sidecar whose last mark is removed is rewritten without it rather than
  deleted, and an empty kind is left out rather than written as null.
- **A sidecar that cannot be read is not rewritten**, and neither is one whose marks cannot be applied, nor one
  that can no longer be read when it is saved. The project's marks are not saved meanwhile, and the log says so,
  once for each.
- **Saved elsewhere, a project's sidecar content goes with it**, even with no marks of its own, and a sidecar
  already at the new place is replaced, as the project file beside it is. A new project's first save replaces a
  stale sidecar lying where it is saved rather than taking its content, which would otherwise show marks nobody
  set in it. A refusal to rewrite the old file does not stop a write at the new place. A place becomes the
  project's own once its sidecar is written there, or found empty with nothing to write, and a sidecar that
  appears there afterwards is kept as the project's own. Paths compare as the file system does, so on Windows a
  spelling in another case is the same place.
- **Each project has its own waiting save**, keyed by the project itself, since a group can hold two projects of
  one name. A change in one, or the closing of one, leaves the others alone.
- **A change stays owed until it is written.** Closing a project writes what it owes first, then clears it from
  memory, so its last change is kept and clearing it never erases the file. A write that fails, because another
  program holds the file, is made again when the project closes, including a write to a new place that no change
  asked for.
- **Closing the IDE writes what every project owes**, from the main window's close. An ordinary close has already
  written it by closing each project; a forced one does not close them.
- **Nothing is written while a sidecar is read.** The stores do not hold its marks yet, so a write then would drop
  them for good. A change made meanwhile waits, and is written once they are in.
- **A project closed while its sidecar is read gets none of it.** The stores outlive the project, so marks put
  there for it would stay, and the next change to them would write its sidecar without them.
- **Loading one project no longer silences the others.** Suppressing the change events while marks are applied
  is per project; it was one flag for all of them, held across the file read.
- **Writes are synchronous.** A sidecar is a few hundred bytes, and a write nothing can interleave with is what
  makes the save on closing, an explicit save during a waiting one, and the single temporary file safe.
- **A write that would change nothing is skipped**, so saving a project does not touch a sidecar it did not change.

**One addition beyond #466's text**, found in review of this change's first version, because the refusal above
made an old failure worse:

- **A form and a module of one name no longer stop the sidecar loading.** The load built a name dictionary that
  threw on the duplicate, and with the refusal above that would have meant no mark saved for the project at all.
  An entry now goes to every document of its name, and every entry naming a document is united with the others
  naming it, rather than each replacing the last; a save already unions their marks under that name, so this
  reads back what it writes.

## Deliberately not in this change

**Entries for a document that is absent for now.** A form whose file was not found is not in the project, and a
module whose file is missing has no lines, so the next save drops their marks, as it did before. Carrying them
forward is the sidecar migration's to do (#273 task 3.17), whose design already says an entry whose shift cannot
be measured is kept unchanged. A first attempt here kept every entry naming no document of the project, and review
showed why that belongs with the migration: it cannot tell a document absent for now from one renamed or removed
this session. A renamed document's old-name entry then stayed in the file beside the new one, a mark removed while
renamed came back when the name did, and a later document of the old name would inherit marks nobody set in it.
The migration has to keep the names a project had when it opened, and carry only entries naming none of them.

**Deleting a sidecar when it becomes empty.** It may hold content this build does not understand, and it may be
committed.

**Merging a sidecar already at a Save As destination.** It is replaced, as the project file beside it is.

**Repairing or moving aside a sidecar that cannot be read.** The developer is told, and decides.

## For the migration that follows

A version-2 sidecar this build has rewritten holds this build's `bookmarks` and `breakpoints` beside the version-2
keys, under the version-2 label. Only builds that count lines from the first line the code window showed write
those two keys, so a reader must treat them that way whatever `version` says.
