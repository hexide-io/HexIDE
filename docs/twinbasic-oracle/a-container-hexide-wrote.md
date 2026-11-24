# A container HexIDE wrote, opened in the twinBASIC IDE (#5143)

**The question.** HexIDE saves a `.twinproj` the twinBASIC IDE wrote, with one entry changed. Does the IDE read that
file, and what does it do when the file changes on disk while the IDE has it open? Measured on 2026-11-23 with
BETA 1044, from the harness VM's clean checkpoint. The compiler was launch-and-watch only: the harness drove the
IDE's front end and read the files it saved.

## The probe

HexIDE's own reader and writer, `TwinBasicContainerReader` and `TwinBasicContainerWriter` at main `ef65c5d96`,
read `tb1044-02-reopened` and wrote it back with one change:
- **The edit:** `Form1.twin`'s `[Description("")]` became `[Description("x")]`, which is what the harness's
  `char` step types.
- **The variants:** three were written, with `Form1.twin`'s revision left at 144, raised by 1 to 145, and raised
  by 2 to 146.
- **Everything else** was written as read: the other entries, their order and their revisions.

Two harness steps were added for this (`tools/twinbasic-oracle/run.mjs`):
- **`show`** changes nothing. It reports `Form1.twin`'s `[Description(...)]` line as the editor holds it, then
  saves.
- **`external`** replaces the project file on disk while the IDE has it open, as an atomic save does. It copies a
  file beside the project and renames it over it. It waits 10 s, keeps a screenshot, reports the editor's line, and
  saves nothing.

## What came back

**HexIDE's write of the +2 variant is the IDE's own save, byte for byte.**
- It's identical to `tb1044-04-one-character`, all 9,464 bytes. That's the IDE's save of the same edit, typed
  through its editor after a reopen.
- The other two variants differ from `04` only in the revision's byte.

**The IDE reads each variant, and keeps the edit.**
- **The runs:** the IDE opened each variant as a fresh project, at a path where a run had already opened
  `tb1044-02`, holding the old text and the same revisions. It then saved with `show`.
- **The editor held** `[Description("x")]` every time, and so did each save.
- **What else changed** is what a reload of the IDE's own file changes (see
  [Saving with no change](twinproj-container-v1.md#saving-with-no-change)). The entry order was reversed, and every
  revision rose by 2 from what the file held. `Form1.twin` went 144 → 146, 145 → 147 and 146 → 148.

So the revision isn't a cache key at load. An edit whose revision wasn't raised was read all the same.

**A file replaced while the IDE has it open is lost, silently.**
- **The replace:** with the project open, `external` renamed the +2 variant over it (`MoveFileEx` with replace,
  which is what .NET's `File.Move(…, overwrite: true)` calls). The rename was allowed: the IDE doesn't hold the
  file open.
- **After 10 s** no notice had appeared, the Debug Console was empty, and the editor still showed
  `[Description("")]`.
- **The IDE's next Ctrl+S** wrote its own model: the same bytes as the run that opened `tb1044-02`. The edit on
  disk was gone, and nothing said so.

## What the obvious guesses were

- **"The IDE caches by revision, so an edit must raise it."** It doesn't: an edit at the old revision was read.
- **"An IDE that holds a project notices it change on disk."** It doesn't: the replace went through, and was
  overwritten at the next save.

## Not measured

- **What a revision counts.** +2 reproduces this save, but `Settings` rose by 1 on Apply in an earlier run (see
  [Not explained yet](twinproj-container-v1.md#not-explained-yet)).
- **An external save while the project is closed in the IDE, then reopened.** It's what the variants' fresh opens
  amount to, but no run closed and reopened one session's project around an external write.
- **Writing the file in place** rather than renaming over it. The rename was allowed, so `external` never tried.
- **A longer wait than 10 s,** or a change of focus, before the IDE might notice.
- **Any beta but 1044.**
