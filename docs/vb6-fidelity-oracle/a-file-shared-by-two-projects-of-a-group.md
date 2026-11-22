## A file shared by two projects of a group (2026-11-21, #2756, IDE, SP6)

**The question.** Two projects of a group can list the same `.bas`, `.cls` or `.frm` file. #2756 asked:
- what VB6's IDE shows;
- whether it holds one copy of the file or two;
- which copy a save writes.

HexIDE needs those answers before it can decide how to handle the case without losing an edit.

**The probe.** A group, `gen2756b.vbg`, of two Standard EXE projects, PX and PY. Each `.vbp` lists its own module and
the shared file:

```
Type=Exe
Module=ModPX; ModPX.bas
Module=Shared; ..\Shared.bas
Startup="Sub Main"
ExeName32="PX.exe"
Name="PX"
```

`Shared.bas`, beside the `.vbg`:

```vb
Attribute VB_Name = "Shared"
Public Function Hello() As String
    Hello = "orig"
End Function
```

The group was opened in VB6's IDE (VB6.EXE 6.0.9782, SP6) through the console agent. Project Explorer nodes were
picked with the keys, and every dialog was recorded and screenshot before it was answered. The steps:
1. View Code on PX's `Shared`, and add a comment line, `'from PX`, at its end.
2. View Code on PY's `Shared`, with PX's window still open.
3. Close the active code window with Ctrl+F4.
4. View Code on PY's `Shared` again, and add `'from PY`.
5. File > Save Project Group.

There were four runs. The first answered every question Yes (Enter), and the second answered No to each "update"
question. Then the No run was repeated with the shared file as a class (`Class=Shared; ..\Shared.cls`) and as a form
(`Form=..\Shared.frm`). The form's nodes were picked by their place in the tree, since a form's node leaves the
Properties window empty until its designer is open.

| Step | What VB6 did, in all four runs |
|---|---|
| Opening the group | `Shared` listed under both projects, and no message |
| 2. PY's copy, with PX's edited window open | "The copy of this file which might have changes is already opened." (OK, Help). After OK, PX's window stays in front showing `'from PX`, and PY's copy isn't opened |
| 3. Closing PX's window | "Shared (PX) is shared by other project(s). Would you like to save it?" (Yes, No, Help). After Yes, "Would you like to update Shared in project(s) which share it with PX?" (Yes, No, Help) |
| 4. PY's copy, after Yes to that update | shows `'from PX` |
| 4. PY's copy, after No to that update | shows the original text without `'from PX`, though the file on disk now has it |
| 5. Save Project Group | one question: "Would you like to update Shared in project(s) which share it with PY?" (Yes, No, Help) |
| Viewing PX's copy after the save | "The copy of this file which might have changes is already opened." again |

| Run | The shared file afterwards |
|---|---|
| `.bas`, Yes throughout | the original, then `'from PX`, then `'from PY` |
| `.bas`, No to each update | the original, then `'from PY`. `'from PX`, written by the save at step 3, is gone |
| `.cls`, No to each update | the same as the `.bas`: `'from PY` only |
| `.frm`, No to each update | the same: `'from PY` only |

On each Save Project Group, the `.vbg` was rewritten. Neither `.vbp` changed.

VB6 also rewrote the other two files' headers:
- **The class** gained `Persistable`, `DataBindingBehavior`, `DataSourceBehavior` and `MTSTransactionMode` lines in its
  `BEGIN` block.
- **The form's** `ClientHeight` went from 1000 to 1005, and its `ClientWidth` from 2000 to 1995. That's consistent with
  rounding to whole pixels at 15 twips a pixel. The rounding is inferred, not measured.

**What it shows:**
- **Each project holds its own copy of a shared file.** After No to the update question, PY's copy kept the original
  text, although PX's had been saved to disk.
- **VB6 won't open a second copy's code window while another copy's code window is open.** It warns, and leaves the
  open window in front, whether or not that copy has unsaved changes. The warning's own wording, "might have changes",
  doesn't decide it.
  - **Corrected 2026-11-22.** This line first said "while one has unsaved changes". The table's last row contradicts
    that: straight after Save Project Group, with PY's window open and nothing unsaved, viewing PX's copy gave the same
    warning.
  - **The other page agrees:** `a-module-shared-by-two-projects-in-a-group.md` (#5268) found the warning while P1's
    code window was open.
  - **Not measured:** a copy with unsaved changes whose window has been closed.
- **Saving a shared file asks whether to update the other projects' copies.** Yes copies the saved text into them, and
  No leaves them as they were.
- **Nothing checks the file on disk before a save.** After No, PY's later save overwrote the file PX had saved, without
  a word. So VB6 can lose a saved edit here. That is the case #2756 asked to know about before matching it.
- **A module, a class and a form behave alike** in every message and in the result.

**Incidental, not probed further.** A first run named its projects VA and VB. The IDE showed the project named VB as
"vb4projectVB", in the Project Explorer and the title bar, while `VB.vbp` still said `Name="VB"`. The run was set aside
because of it.

**Not measured:**
- Running one project while the other's copy has an unsaved edit, the fourth question in #2756.
- Saving one project on its own rather than the group.
- A change to a shared form's design, as opposed to its code.
- Closing the group, or VB6, with copies unsaved.
