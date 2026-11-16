# Code from outside this computer

HexIDE runs a project's code with your own rights. A program can read, change and delete your files and start other
programs, and HexIDE has none of the macro security Office puts in front of a workbook's code. So HexIDE asks before
it runs code that came from outside your computer (#1121).

## What HexIDE does

- **Opening a project never runs its code.** No `Form_Load`, no `Sub Main`, no `UserControl_Initialize`, and nothing
  like Office's `Workbook_Open` or `AutoOpen`. Showing a form in the designer doesn't run it either.
- **The first run asks.** When any file of a project's code came from outside this computer, HexIDE asks before the
  first run in a session. The question names each such file and where it came from. **No** is the default button.
- **Every way of running the code asks:** F5, F8, Run To Cursor, Start With Full Compile, Restart, an add-in's launch,
  and Run with VB6. Starting an external debug session asks too, when the launch file you pick is itself marked as
  downloaded, because a launch file names the program it starts.
- **A Yes holds for the rest of the session**, for those files, in any project that includes them. A file marked
  later, such as one you add, is asked about on its own.
- **A No starts nothing,** and the next start asks again.
- **Nothing is remembered between sessions.** There's no "don't ask again".

A project you created, or whose files you saved yourself in an earlier session, doesn't ask: a save writes a new file,
which carries no mark. Within one session, though, the mark HexIDE read when it opened a file stands. Saving a
downloaded module, or saving it under a new name, doesn't stop the question.

## What counts as "from outside this computer"

| Where it came from | What HexIDE reads |
|---|---|
| A file **Windows** marks as downloaded | The file's Mark of the Web: its `Zone.Identifier` stream, which browsers, mail clients and Explorer's own unzipping write, and which Office reads. Zone 3 (Internet) or 4 (Restricted sites) marks the file. So does a stream that is there but can't be read, or names no zone. Zones 0 to 2 (this computer, the intranet, trusted sites) don't. |
| A file **macOS** quarantines | The `com.apple.quarantine` extended attribute, which Gatekeeper reads. Any value marks the file. |
| A project read from an **Office document** | Always asked about, whatever marks the file: a project whose own file is a Word, Excel, PowerPoint, Access or Visio document. HexIDE can't open those yet; this is in place for when it can. |

The files that count are the project file and each form, module, class, UserControl and designer. A file the project
only carries, such as a text file, isn't run, so it isn't asked about.

HexIDE never shows what a mark says about where the file came from, such as its `HostUrl`: whoever supplied the file
wrote that. Names it does show have control and formatting characters removed, so a name can't reverse or hide part of
itself.

## What isn't covered

These are gaps, not guarantees. Code that reaches HexIDE without a mark runs without asking.

- **Linux has no download mark.** The system writes none, so nothing is asked about there except an Office document.
- **A mark lost before HexIDE sees the file.** An archive tool that doesn't carry the mark onto what it extracts, a
  copy through a volume that can't hold one (FAT or exFAT on Windows), a `git clone`, or a copy made by a tool that
  drops it: each leaves files HexIDE can't tell from your own.
- **macOS: reasoned, not checked here.** The reading is tested on CI's macOS runner, which sets the attribute and reads
  it back. It hasn't been checked on a real download yet.
- **A file changed on disk after a Yes.** A Yes is for the file by its path, so new content at the same path, read in
  the same session, isn't asked about again.
