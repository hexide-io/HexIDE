# Gate Save As with the same faithfulness refusal as Save

## Why

A form HexIDE cannot reproduce opens read-only and Save refuses it. **Save As did not.** The exemption was
deliberate and is recorded in two places — `docs/MISSING_FEATURES.md` ("bypasses the faithfulness gate by
design: the original file is not at risk") and this capability's own *Saving elsewhere* scenario ("the save
proceeds, since the original is not at risk").

That reasoning is true about the original and silent about the copy, and it is **stale rather than wrong-
headed**: the exemption, the scenario and the doc row all arrive together on 2026-08-16, when the gate's
only cause was structural flattening. The byte-losing cause landed two days later (#89) and touched none of
the three. The rationale silently inherited a harm class it never reasoned about.

Three consequences, all verified in the code:

1. **The copy loses its blobs.** `WriteCompanionBinary` protects a companion only where one already exists;
   at a new path that predicate is false, so the regenerated companion is written without the content it
   could not reproduce.
2. **And then the copy looks clean.** Faithfulness is re-derived from the file on load. The citations that
   flagged the form are exactly what went missing, so on reopening the copy `CanSaveFaithfully` is **true**:
   no banner, freely editable, the loss permanent and invisible. An outcome-0 refusal becomes an outcome-3
   file — the one `docs/serialization-outcomes.md` calls never acceptable.
3. **"The original is not at risk" was never enforced.** Nothing compares the chosen path with the form's
   own, and the picker pre-fills the same filename, so Save As onto the original is the default choice.

A second defect surfaced alongside it: a refusal outside a batch was recorded and **never reported**. It
persisted in the pending list and surfaced during the next unrelated Save Project, naming a file that was
not part of that batch.

## What Changes

- Save As is refused on the same condition as Save, and refused **above the file picker** — the developer
  is not asked for a destination that will never be written. This keeps the existing message ("Its file on
  disk is unchanged") literally true, so the change needs **no new localization keys**.
- A lone refusal is reported when it happens. The project-wide batch keeps reporting once for all members.

## Impact

- Modifies `serialization-round-trip`: the *Saving elsewhere* scenario is inverted, and a scenario is added
  for reporting a single refusal at the time it occurs.
- `docs/MISSING_FEATURES.md`'s Save As row is rewritten; its "by design" note is now the opposite of the
  behaviour.
- **Not covered, each now its own issue.** Verifying them changed both the count and the ranking: three are
  **worse than this one**, because they damage the developer's own file rather than a copy, and each
  launders the same way.
  - **#146** — a form opened without its `.frx` is never flagged, so an ordinary Ctrl+S at its *original*
    path strips its citations. The trigger property is `Icon`, which is on essentially every form VB6 wrote.
  - **#147** — Save Project writes a UserControl the gate refused, with no edit and no keystroke in the
    file. (An earlier draft of this proposal said "even on a plain Ctrl+S at its original path". That is
    wrong: `CodeEditorViewModel.SaveModule()` has no caller in the IDE. Save Project is the reachable path,
    and needs no interaction with the file at all.)
  - **#148** — a `.frm` can take freshly renumbered citations into a companion the save refused to write.
    Already failing on the current tree for `ODBC Log In.frm`.
  - **#149** — Make EXE packages a refused form silently. The source is untouched, so this one is moderate.
- **Not a fix for the underlying loss.** The blobs are still unreproducible; this stops HexIDE writing a
  file that hides it. #60 is what makes such a form savable at all.
