## What the IDE asks before replacing or dropping a file (2026-11-17, #2504)

**The question.** #2504's file-safety audit (`docs/file-safety-audit.md`) left a VB6 column unmeasured for each IDE
command that can replace a file or drop unsaved work. When the destination already exists, or the item has unsaved
changes, what does VB6's IDE ask, with which buttons, and what does each answer do to the files?

**The probe.**
- **The IDE:** VB6 SP6 on the Win10 guest. It was driven with real keystrokes and clicks on the console, and each
  dialog was read through its window texts and button styles. Every file's SHA-256 was taken before and after each step.
- **The folder** held:
  - `P.vbp`, whose one form was VB98's `Options Dialog` template, copied in as `Form1.frm` and `Form1.frx`;
  - the template's 12-byte `.frx`, the form's Icon. Its TabStrip block was removed, so the form loads without
    mscomctl.ocx;
  - `Other.frm` with an unrelated 15-byte `Other.frx`;
  - a lone 16-byte `Lonely.frx`, with no `Lonely.frm`;
  - a `Form2.frm` from another project;
  - a 15-byte `P.exe` that was not an executable.

| Command, onto what | What VB6 asks | Buttons, default first | Each answer |
|---|---|---|---|
| Save Form1.frm As, onto the existing `Other.frm` | the common dialog's own **Confirm Save As**: "Other.frm already exists. Do you want to replace it?" | **No**, Yes | **No:** back in Save File As, the typed name kept, nothing written. **Yes:** `Other.frm` replaced, and **`Other.frx` replaced too, with no question of its own**. `Form1.frm` untouched |
| Save Form1.frm As `Lonely.frm`, where only `Lonely.frx` exists | **nothing** | | `Lonely.frm` written, and **`Lonely.frx`'s 16 bytes replaced silently** |
| A new form's first Save, onto the existing `Form1.frm` | Save File As, proposing the form's name, then Confirm Save As | **No**, Yes | No, then Cancel: nothing written |
| Save Project As, onto an existing `.vbp` | Confirm Save As | **No**, Yes | No, then Cancel: nothing written |
| File > Make P.exe, onto the existing `P.exe` | Make Project, opened in the project's folder with `P` proposed, then Confirm Save As: "P.exe already exists. Do you want to replace it?" | **No**, Yes | **No:** back in Make Project, `P.exe` unchanged. **Yes:** compiled, and `P.exe` replaced (15 bytes became 16,384) |
| `VB6.EXE /make`, onto an existing exe | **nothing** | | replaced. Exit code 0, and the `/out` file reads "Build of 'Q.exe' succeeded." |
| Project > Remove, a new form never saved and never changed | **nothing** | | removed |
| Project > Remove, a new form never saved, with a CommandButton added | VB6's own "Save changes to the following files?", listing **`Form1`**, the object's name | **Yes**, No, Cancel | **Cancel:** kept. **Yes:** Save File As; cancelling that keeps the form too, so the removal is abandoned. **No:** removed, nothing written |
| Project > Remove, a saved form with a CommandButton added | the same question, listing **`Lonely.frm`**, the file's name | **Yes**, No, Cancel | **No:** removed, and **`Lonely.frm` and `Lonely.frx` unchanged on disk**. Removal never deletes a file |
| A module's `(Name)` changed from Module1, then Save | **nothing** | | written to the **same `Module1.bas`**, with `Attribute VB_Name = "Module1Renamed"`. Save Project wrote `Module=Module1Renamed; Module1.bas`. No file is renamed or created |

**What it shows:**
- **Every replace question comes from the Windows common dialog** (its `OFN_OVERWRITEPROMPT` box), and asks only
  about the name typed. **VB6 never asks about the companion:** a `.frx` beside the destination is replaced with the
  `.frm`, or replaced alone when no `.frm` was there.
- **The default is No** in every replace question, so Enter keeps the file.
- **The unsaved-changes question is VB6's own,** with Yes as the default. It names a saved item by its file and an
  unsaved one by its object name. Cancelling, at either step, leaves the item in the project.
- **`/make` asks nothing,** as a command-line build can't.

**Seen along the way:**
- **VB6 loads a form's designer only when something needs it.** With a malformed first `.frx`, "Errors during load"
  and the form's `.log` appeared at Save As, not at project open.
- **Save File As opened in VB6's current folder,** the one the driver started VB6 in, not the project's folder. Make
  Project opened in the project's folder.

**Unmeasured:**
- a companion other than `.frx`, such as `.ctx` or `.dsx`;
- Save Project, as opposed to Save Project As, onto a member file name another project owns;
- the Remove Project question;
- VB6's behaviour on a read-only destination.

Screenshots of each question are kept off-tree.
