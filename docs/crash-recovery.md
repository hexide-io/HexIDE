# Recovering unsaved work after a crash

While HexIDE runs, it keeps a private copy of everything you haven't saved. If the IDE ends without you deciding
what happens to that work, the next start offers it back. That covers a crash, the process being killed, the machine
losing power, and a terminate signal. VB6 had nothing like this; it is a HexIDE addition.

## What is kept

Every document with unsaved changes, whether or not it has a file yet:

- forms, modules, classes, UserControls and property pages, as the save would write them;
- a form's binary companion (`.frx`), so its pictures and icons come back too;
- the project file's own text, so a document you added and never saved is still part of the project;
- typing in a code window you closed without saving, and controls the designer holds but hasn't committed.

The copy is taken every five seconds, and written only when it has changed, so an idle IDE doesn't touch the disk.
It is kept as UTF-8 whatever the document's code page, since a backup must not refuse a character. The code page is
recorded, and a restored document is saved in it as usual.

## Where it lives

```text
%LocalAppData%\HexIDE\Recovery                Windows
$XDG_STATE_HOME/HexIDE/Recovery               Linux and macOS, when XDG_STATE_HOME is set
~/.local/state/HexIDE/Recovery                Linux and macOS otherwise
```

A session started with [`--user-data-dir`](command-line.md) keeps it in `Recovery` under that directory instead.

- **Never beside your project, and never in its user sidecar.** Both can end up in source control, and a copy of
  unsaved work has no business there.
- **Local, not roaming.** Unsaved work belongs to the machine it was typed on.
- **One folder per run of the IDE**, named by when the process started and its id. A run's folder is deleted when it
  closes cleanly.

## When it is kept

A close counts as **clean** when somebody decided about the unsaved work: you answered the save prompt, there was
nothing unsaved, or an automation client asked the IDE to close. The folder is deleted, and nothing is offered next
time.

Anything else is **unclean**, and the folder stays for the next start: a crash, a kill, a power cut, or a terminate
signal. HexIDE catches a terminate signal only on Linux and macOS, in a Debug build serving automation, and takes a
final copy before it closes. Everywhere else the signal ends the process outright, and the copy is the last one taken,
at most five seconds old.

A run whose IDE is still open is never offered by another, so starting a second HexIDE beside the first offers nothing
from it.

## When it is offered

- **A project that was never saved** is offered as HexIDE starts, since there is nothing to open. So is a project in
  the scratch folder `--newproject` makes. Restoring it stands in for the New Project dialog; a project named on the
  command line still opens afterwards.
- **A saved project** is offered when you open it. It is matched by its path. Where nothing is at that path any more,
  it is matched by the project file's content. A project whose folder was moved or renamed is still found, and the
  dialog says where its work came from. A copy opened while the original is still in place is not offered the
  original's work.
- **Nothing is offered when your files already hold the work.** That happens when the crash came just as a project
  was loading, or when the files were saved some other way afterwards. The copy is deleted.
- **File → Recover Unsaved Work** lists everything still waiting, so work for a project you don't open again is not
  out of sight for good.

## The dialog

Each row is a project: its name, where its work came from, and when the copy was taken.

| Button | What it does |
|---|---|
| **Restore** | Puts the work back in the IDE, unsaved. See below. |
| **Discard** | Deletes the copy, after asking. It can't be undone. |
| **Decide Later** | Closes the dialog and keeps the copy, under File → Recover Unsaved Work. |

**"Changed on disk since then"** names files that have changed after the copy was taken, perhaps in another editor.
Restoring puts the copy in the IDE in place of the newer file's content, and your next save would write it over the
file. Restore only when the copy is the version you want.

## What a restore does

**The work comes back unsaved.** The documents show as edited, and their files on disk are not touched until you save.

- **A saved project** is opened from its files as usual. Each copied document then replaces what was opened, and a
  document the project no longer lists is added back. A document whose file already holds the copy is left alone.
- **A never-saved project** comes back as it was: a project with no file, holding its documents.
- **The project's other settings are not restored.** A changed startup object or reference is not part of the copy,
  only the documents and which documents the project holds.

## Clearing it by hand

Each run's copy is an ordinary folder in the directory above. To throw all of it away without opening HexIDE, close
every HexIDE and delete the folders inside `Recovery`. Don't delete the folder of an IDE that is still running: that
IDE writes there.
