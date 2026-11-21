## A module shared by two projects in a group (2026-11-21, #2756, design-time, IDE, SP6)

**The question.** Two projects in one group can list the same file. Does VB6 show it under both? Does it hold one copy
or two? Which edit does a save keep, and does a run see the other project's unsaved edit?

**The probe.** VB6.EXE 6.00.9782 (SP6) in a clone of the oracle VM, driven on its console. One folder held:
- `g.vbg`, listing `P2.vbp` as the startup project and `P1.vbp`;
- `P1.vbp` with `Own1.bas` and `Shared.bas`, and `P2.vbp` with `Own2.bas` and `Shared.bas`, each starting from
  `Sub Main`;
- `Shared.bas` holding `Public Function SharedValue() As Long` / `SharedValue = 1`;
- each `Sub Main` showing `MsgBox "P1 " & SharedValue()` or `"P2 "`.

Edits were Replace Alls in the module's code window; each changed `SharedValue = n` to another number. Every file's
length and time were read in the guest after each step.

**Display.** The Project Explorer listed `Shared (Shared.bas)` under both projects.

**Two copies, and one code window at a time.**
- **The edit:** P1's `Shared` was opened and changed to 2.
- **Opening P2's `Shared` while P1's code window was open** said `The copy of this file which might have changes is
  already opened.`, with OK and Help. P2's code window didn't open.
- **Closing P1's code window** asked `Shared (P1) is shared by other project(s). Would you like to save it?`, with Yes,
  No and Help.
  - **Yes** wrote `Shared.bas` at once, holding 2.
  - It then asked `Would you like to update Shared in project(s) which share it with P1?`, with Yes, No and Help.
- **With No to the update,** P2's `Shared` opened showing `SharedValue = 1`. VB6 holds a copy per project, and P2's
  hadn't followed the file.

**A run uses its own project's copy.** With P2's copy still at 1 and the file at 2, F5 ran P2 and showed `P2 1`.

**Two modified copies.**
- **P2's copy changed to 3,** and its code window closed. The same save question came up; with No, nothing was
  written and no update question followed.
- **P1's copy changed to 4,** closed the same way, again with No.
- **File > Save Project Group** then said: `Multiple copies of the shared file '<path>\Shared.bas' have been
  modified.  In the following dialog, select the copy that should be saved.`, with OK and Help.
- **After OK,** the usual `Save changes to the following files?` list came up with `g.vbg`, `P1.vbp`, `Shared.bas`,
  `P2.vbp` and `Shared.bas`, every row selected.
  - **Yes with both copies selected** brought the same warning back. Nothing was written.
  - **With P2's row deselected,** Yes wrote `Shared.bas` holding P1's 4.
- **The update question came again.** With Yes, P2's copy was replaced by the file's: its code window showed
  `SharedValue = 4`, and F5 showed `P2 4`. P2's unsaved 3 was gone, but only after that question was answered.
- **What else was written:** `g.vbg` was rewritten after the update answer. `P1.vbp` and `P2.vbp` weren't written at
  any point.
- **The exit that followed** raised no save question.

So VB6 never lost an edit without asking, but it asks after the fact. A run of the other project sees only its own
copy, and Save Project Group refuses to choose between two edited copies on its own.

**What resisted explanation.** Straight after the update, P2's Project Explorer branch read `Module1 (Shared.bas)`,
listed above `Own2`. Opened, the module was `P2 - Shared`, and the branch read `Shared (Shared.bas)` below `Own2` again.
It was seen once and not repeated.

### Not measured

- **A shared class or form.** Only a `.bas` was shared.
- **Save Project of one project** while the other's copy is modified.
- **No to the update question** after Save Project Group, with the other copy still modified.
- **A run of P1,** and an ActiveX project that the other one references.
- **The release build.**
