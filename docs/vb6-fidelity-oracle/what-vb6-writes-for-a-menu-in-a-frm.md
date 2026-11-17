## What VB6 writes for a menu in a .frm (2026-11-16, #4141, IDE)

**The question.** For HexIDE's Menu Editor (#4141), the exact `.frm` text vb6.exe writes for a menu item. That covers:
- each Shortcut it offers;
- HelpContextID, NegotiatePosition, Index and the other properties;
- the order of the lines within a `Begin VB.Menu` block.

**The probe.**
- **The IDE:** VB6.EXE 6.00.8176 SP6, in the guest's console session through the agent ("Reaching the VB6 *IDE*, not
  just the compiler" in the README).
- **The forms:** two, hand-written with guessed spellings.
  - **Form A:** a menu `Keys` holding 75 items, one per shortcut: Ctrl+A to Ctrl+Z, F1 to F12 without F10, the same F
    keys with Ctrl, with Shift and with Shift+Ctrl, Ctrl+Ins, Shift+Ins, Del, Shift+Del and Alt+Bksp.
  - **Form B:**
    - the alternative spellings;
    - an item with every property set, in scrambled order;
    - a two-element menu array;
    - items with HelpContextID 0 and 7;
    - top-level menus with NegotiatePosition 0 to 3;
    - a top-level menu with WindowList.
- **The method:** each form's project was opened in the IDE, its load log read, and the form saved with File > Save As.
  A save regenerates the file from what the IDE loaded, so the saved text is VB6's own, not the input echoed. That's
  shown by B's alternative spellings coming back rewritten.

**Shortcut spellings.** Form A loaded with no errors, and its re-save wrote every one as given:

| Keys | VB6 writes |
|---|---|
| Ctrl+A to Ctrl+Z | `^A` … `^Z` |
| F1 to F12, no F10 | `{F1}` … `{F9}`, `{F11}`, `{F12}` |
| Ctrl+F*n* | `^{F1}` … |
| Shift+F*n* | `+{F1}` … |
| Shift+Ctrl+F*n* | **`+^{F1}`** … |
| Ctrl+Ins | **`^{INSERT}`** |
| Shift+Ins | **`+{INSERT}`** |
| Del | **`{DEL}`** |
| Shift+Del | **`+{DEL}`** |
| Alt+Bksp | **`%{BKSP}`** |

**Form B's alternatives:**

| Read | Written back |
|---|---|
| `^+{F1}` | `+^{F1}`: accepted, and canonicalised Shift first |
| `{DELETE}` | `{DEL}` |
| `+{DELETE}` | `+{DEL}` |
| `%{BACKSPACE}` | `%{BKSP}` |
| `^{INS}` | **refused**: the load log reads "Line 20: Property Shortcut in mnuB1 had an invalid value." The item saves with **no Shortcut line**. |
| `+{INS}` | refused the same way |

**The other properties,** from form B's re-save:
- **HelpContextID:**
  - 0 writes **no line**, even when the input had one;
  - 7 writes `HelpContextID   =   7`.
- **NegotiatePosition:**
  - 0 writes no line;
  - 1 to 3 write `NegotiatePosition=   1  'Left`, `2  'Middle` and `3  'Right`. The 17-character name leaves no padding
    before `=`.
- **Index:** each element of a menu array is its own `Begin VB.Menu` block with the shared name, and an
  `Index           =   0` line.
- **Defaults are omitted:** `Visible = -1` and `Enabled = -1` were in the input and are not written. `Visible = 0` and
  `Enabled = 0` are written, as `0   'False`.
- **WindowList on a sub-menu is dropped:** with no log line, and nothing written. On a top-level menu it is kept, as
  `WindowList      =   -1  'True`.

**Line order:** after `Caption`, the lines come in **alphabetical order of the property names**, whatever order the
input had:

| Input order | Written order |
|---|---|
| WindowList, Visible, Shortcut, HelpContextID, Enabled, Checked (a sub-menu) | Caption, Checked, Enabled, HelpContextID, Shortcut, Visible |
| HelpContextID, Index, Checked (an array element) | Caption, Checked, HelpContextID, Index |
| WindowList, NegotiatePosition, HelpContextID, Visible, Enabled (a top-level menu) | Caption, HelpContextID, NegotiatePosition, WindowList |

Caption first is also alphabetical: "Caption" sorts before "Checked". So no row here tells "Caption first, then
alphabetical" from "all alphabetical".

**Not measured:**
- the form header's own lines, which moved because the menu bar changes the client height;
- a menu's `Tag` or `Name` changed;
- the Menu Editor's own spellings, as opposed to a hand-written `.frm`.
