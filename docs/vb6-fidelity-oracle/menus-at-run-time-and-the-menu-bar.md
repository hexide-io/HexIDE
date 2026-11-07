## Menus at run time, and the menu bar (2026-10-17, #1126, #1127)

**The probe.** Found by the Minesweeper demo's Game menu (#1123). A Fixed Single form, `ClientWidth = 2940`,
`ClientHeight = 3420`, with a Game menu holding New (Shortcut F2), a separator `mnuSep1`, and a three-element
array `mnuLevel(0 To 2)`: "&Beginner" checked, "&Intermediate" and "&Expert". `Form_Load` logs each step to a file under
`On Error Resume Next`, then `End`s. Two probes, each built native and p-code; all four runs agreed.

**The menu bar is outside the client area:**

| Read | Logged |
|---|---|
| `ScaleWidth`, `ScaleHeight`, `TypeName(ScaleHeight)` | `2940 3420 Single` |
| `Width`, `Height` | `3030 4080` |
| `Height = (Height - ScaleHeight) + 2000`, then `ScaleHeight`, `Height` | `1995 2660` |
| `Height = (Height - ScaleHeight) + 3420`, then `ScaleHeight`, `Height` | `3420 4085` |
| `mnuGame.Visible = False`, then `ScaleHeight`, `Height`; and after `= True` again | `3420 4085`, both times |

The same form without a menu has `Height - ScaleHeight` = 375 ("Move, ScaleWidth and ScaleHeight"), and with one it
is 660: the bar takes 285 twips, 19 pixels at 96 dpi. `ClientHeight` and `ScaleHeight` are the area below it. Hiding
the only top-level menu from code changed nothing before the form was shown; what it does once shown is unmeasured.

**A menu hidden at design time is no bar.** A second probe, with `ClientTop = 330`, logs in `Form_Load`, again from a
Timer once the form is shown, and after `Height = (Height - ScaleHeight) + 2000`:

| The form's top-level menus | Load | Shown | After the fit |
|---|---|---|---|
| none | `3420 3795` | `3420 3795` | `1995 2375` |
| one, `Visible = False` in the .frm, with an item under it | `3420 3795` | `3420 3795` | `1995 2375` |
| that one, and a visible File menu | `3420 4080` | `3420 4080` | `1995 2660` |

Each cell is `ScaleHeight Height`. A form that keeps its only menu hidden, for `PopupMenu`, is the same size as one with
no menu at all, so VB6 shows a bar only for a visible top-level menu.

**Menus from code:**

| Probe | Logged |
|---|---|
| `TypeName(mnuNew)`, `TypeName(mnuLevel)`, `TypeName(mnuLevel(0))` | `Menu Object Menu` |
| `mnuLevel.LBound`, `.UBound`, `.Count`; `mnuLevel(1).Index` | `0 2 3`; `1` |
| `mnuLevel(0)`'s Caption, Checked, Enabled, Visible | `&Beginner True True True` |
| `TypeName` of Checked, of Caption | `Boolean String` |
| `For i = 0 To 2: mnuLevel(i).Checked = (i = 2)`, then each | `False False True` |
| `mnuLevel(1).Checked = 5`, then read | `True` |
| `mnuLevel(1).Enabled = False` and `.Caption = "&Medium"`, then read | `False &Medium` |
| `mnuNew` read bare: as it stands, then with Enabled False, Visible False and Checked True in turn | `True`, `False`, `True`, `True` |
| `mnuNew = False`, then Enabled, Visible, Checked | `False True True` |
| `mnuLevel_Click 1`, called from code | the handler runs with Index 1 |
| `mnuSep1.Caption = "Sep"`, then read | `Sep` |
| `mnuNew.Left` | compile error, "Method or data member not found" |

So a menu's **default member is Enabled**. The obvious guess was Caption, as for a Label; a Boolean that tracks
Enabled and ignores Visible and Checked settles it.

**Refusals,** each a run-time error under `On Error Resume Next`:

| Probe | Err |
|---|---|
| `mnuGame.Checked = True`, a menu with sub-items | 387, "'Checked' property can't be set on this control" |
| `mnuSep1.Checked = True`, a separator | 387 |
| `mnuNew.Caption = "-"`, making an item a separator | 395, "Cannot use separator bar as menu name for this control"; the caption stays `&New` |
| `mnuLevel(0).Caption = "-"`, an element of an array | 395 |
| `mnuSep1.Enabled = False`, a separator | 387, and Enabled still reads True |
| hiding every item of the Game menu in turn | 387, "'Visible' property can't be set on this control", at the last |
| with only the separator left visible, hiding it | 387: the last visible menu under a menu can't be hidden, separator or not |
| `mnuNew.Index`, not an array | 343, "Object not an array" |

A third probe added the rest: `mnuSep1.Caption = "-"` is taken; `mnuNew.Tag` reads "" and takes "t"; `mnuNew.WindowList`
reads False; `mnuLevel(1).Name` is `mnuLevel`; and `mnuNew.Parent.Name` is `Form1`.

**Load and Unload of a menu array:**

| Probe | Logged |
|---|---|
| `Load mnuLevel(3)`, then `Err`, Count, UBound | `0 4 3` |
| the loaded element's Caption, Checked, Enabled, Visible | `&Beginner False True True` |
| with `mnuLevel(0)` first ticked, then disabled, then hidden and captioned "Zero", each Load's element | Checked `True`; Enabled `False`; Visible `False`, Caption `Zero` |
| `Load mnuLevel(7)`, then Count, UBound | `5 7` |
| `Unload mnuLevel(3)`, then Count, UBound | `4 7` |
| `Unload mnuLevel(0)` | 362, "Can't unload controls created at design time" |
| `Load mnuNew` | 361, "Can't load or unload this object" |
| `mnuLevel(5).Checked = True`, no such element | 340, "Control array element '5' doesn't exist" |

**A loaded menu element copies element 0, Visible included.** A loaded control is created hidden whatever element 0
is ("Control arrays" above), so this is where menus and controls part. Not measured: where a loaded element sits in
its menu when the array has gaps, and whether it copies a Shortcut.

**In HexIDE:**
- **The bar** is above the client area, 19 pixels high, and ScaleHeight is the area below it, on a form with a visible
  top-level menu; a form whose top-level menus are all hidden has no bar (#1127). The bar is one line high however many
  menus it holds, where VB6's wraps onto a second line on a form too narrow for them, which is unmeasured.
- **Menus from code** work as the tables above say (#1126): each menu has its name, a menu array is a control array
  with Count, LBound, UBound and an Index on its Click, and Caption, Checked, Enabled, Visible and Tag read and write
  with the 387 and 395 refusals. Load copies the first element as it stands, Visible included, and places the new one
  after the element below it; Unload removes it; and the 340, 361 and 362 refusals hold. Choosing an item no longer
  toggles its tick, which is the program's to set, by pointer, Enter or automation alike (#1158, measured below). `TypeName` gives Menu, and Object for the array.
- **Not yet:** a menu's default member, Enabled, so `mnuNew = False` and a bare `mnuNew` read; giving a separator a
  caption, which makes it an item, refused as not implemented; and setting WindowList, refused the same way.
- **The bar appearing or going away from code is refused** as not implemented (#1140): hiding the last visible
  top-level menu, or showing one on a form whose top-level menus are all hidden, as a form that only pops its menus up
  has them. The bar's height comes off the client area, and what the window or ScaleHeight does once the form is shown
  is unmeasured; the one case measured, before it is shown, changed nothing. Wanted: ScaleHeight and Height after each
  direction on a shown form, and on a maximized form and an MDI child. A change that leaves a top-level menu visible
  works, and so does every change to a sub-item.
- **The text of 340** names the element in vb6.exe, "Control array element '5' doesn't exist", and doesn't here.
  The "Control arrays" rows above give it without the index, which looks abbreviated rather than measured; not
  rechecked.

