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
is 660: the bar takes 285 twips, 19 pixels at 96 dpi, in session 0. On the console it takes 300, 20 pixels (below). `ClientHeight` and `ScaleHeight` are the area below it. Hiding
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

**Re-run on the console, and the bar going away or coming back from code (2026-11-18, #4768, #1140).** The probes
above ran in session 0, which draws no window and has a smaller frame. One project was built p-code and native and run
on the guest's console, in the IDE (`/runexit`) and in session 0, logging its session id and `GetSystemMetrics`. Form1 is
the Fixed Single form above; Form2 is the same with its only top-level menu hidden in the .frm. `Sub Main` loads one and
runs each step, with a `DoEvents` before each read. Each cell is `ScaleHeight Height`; p-code, native and the IDE agree
except where noted.

| Step | Console | Session 0 |
|---|---|---|
| Form1 loaded | `3420 4155` | `3420 4080` |
| `Height = (Height - ScaleHeight) + 2000`, then `+ 3420` | `1995 2735`, then `3420 4160` | `1995 2660`, then `3420 4085` |
| `mnuGame.Visible = False`, not yet shown | **`3720 4160`** | **`3705 4085`** |
| `mnuGame.Visible = True` | `3420 4160` | `3420 4085` |
| `Form1.Show`, then `mnuGame.Visible = False` | **`3720 4160`** | **`3705 4085`** |
| `mnuGame.Visible = True` | `3420 4160` | `3420 4085` |
| `WindowState = 2`, then `mnuGame.Visible = False`, then `True` | `10875 11610`, **`11175 11610`**, `10875 11610` | the same as not maximized: session 0 keeps WindowState 0 |
| Form2 loaded, then shown | `3420 3855` | `3420 3795` |
| Form2's `mnuGame.Visible = True`, then `False` | **`3120 3855`**, then `3420 3855` | **`3135 3795`**, then `3420 3795` |

What these show:
- **The console's frame is larger.** A Fixed Single form with a menu bar is 735 twips taller than its ScaleHeight, 6 + 23
  + 20 pixels, against session 0's 660, 6 + 19 + 19. `GetSystemMetrics` read `SM_CYCAPTION` 23 and `SM_CYMENU` 20 on the
  console, 19 and 19 in session 0. The bar is 300 twips on the console, as "A form's Width and Height" has it.
- **The bar going away or appearing from code keeps the window's Height and moves ScaleHeight by the bar**, 300 twips
  on the console and 285 in session 0. That held loaded or shown, normal or maximized, with no error, and each change
  raised one Resize that read the new ScaleHeight.
- **Before Show, hiding the only top-level menu did change ScaleHeight here,** where the first probe above logged no
  change. The difference is when each read came: see *When the change lands*, below.
- **The IDE's first read differs.** In the IDE, Form1's Height straight after `Load` was 3855, as if it had no bar,
  and the first fit then left ScaleHeight 1695. Every later read matched the compiled builds. Measured once, with no
  rule offered.
- **Not measured:** an MDI child, and a menu bar that wraps. The wrapped bar is measured since, in *Changing the bar
  twice before it lands, and a bar that wraps*, below (#4946).

**When the change lands (2026-11-18, #1140).** Two more probes read ScaleHeight straight after the statement, then
again after a `DoEvents`, logging each Resize. On the console, the IDE, p-code and native agreeing, and session 0 the
same with its 285-twip bar:

| Form1, a Game menu; `ScaleHeight Height` | Read at once | After `DoEvents` |
|---|---|---|
| inside `Form_Load`, `mnuGame.Visible = False` | `3420 4155` | `3720 4155` |
| once `Load` has returned | `3720 4155` | |
| shown, `mnuGame.Visible = False` | `3420 4155` | `3720 4155` |
| shown, `mnuGame.Visible = True` again | `3720 4155` | `3420 4155` |
| Form2, its menu hidden in the .frm, inside `Form_Load`, `mnuGame.Visible = True` | `3420 3855` | `3120 3855` |
| Form2 shown, `mnuGame.Visible = True` | `3420 3855` | `3120 3855` |

- **The statement changes neither size.** The bar goes or comes at the next message: at a `DoEvents`, measured, and
  presumably once the running handler has returned, which is reasoned, not checked. One Resize comes then, reading
  the new ScaleHeight.
- **So the first probe above saw no change** because it read inside `Form_Load` with no `DoEvents` in between, and the
  re-run did because it read after one. Both were right about what they read.

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
- **The bar** is above the client area, 20 pixels high as on the console (#1140), and ScaleHeight is the area below it, on a form with a visible
  top-level menu; a form whose top-level menus are all hidden has no bar (#1127). On a form too narrow for its menus
  the bar wraps onto more rows, as VB6's does (#4978), measured in *Changing the bar twice before it lands, and a bar
  that wraps*, below.
- **Menus from code** work as the tables above say (#1126): each menu has its name, a menu array is a control array
  with Count, LBound, UBound and an Index on its Click, and Caption, Checked, Enabled, Visible and Tag read and write
  with the 387 and 395 refusals. Load copies the first element as it stands, Visible included, and places the new one
  after the element below it; Unload removes it; and the 340, 361 and 362 refusals hold. Choosing an item no longer
  toggles its tick, which is the program's to set, by pointer, Enter or automation alike (#1158, measured below). `TypeName` gives Menu, and Object for the array.
- **Not yet:** a menu's default member, Enabled, so `mnuNew = False` and a bare `mnuNew` read; giving a separator a
  caption, which makes it an item, refused as not implemented; and setting WindowList, refused the same way.
- **The bar appearing or going away from code** comes or goes at the next message, as *When the change lands*
  above measured: Height stays, ScaleHeight moves by 300 twips, and one Resize is raised (#1140). That covers hiding
  the last visible top-level menu, and showing one on a form whose top-level menus are all hidden, as a form that only
  pops its menus up has them. Three cases are refused as not implemented (#4946): an MDI child's bar, a bar HexIDE
  lays out as wider than the form, and a change reversed before it lands. A change that leaves a top-level menu
  visible works, and so does every change to a sub-item.
- **The text of 340** names the element in vb6.exe, "Control array element '5' doesn't exist", and doesn't here.
  The "Control arrays" rows above give it without the index, which looks abbreviated rather than measured; not
  rechecked.

## Changing the bar twice before it lands, and a bar that wraps (2026-11-19, #4946)

**The probes.** Two projects, each built native and p-code with `/make` and run in session 0 and on the guest's
console: eight runs, with native and p-code agreeing in every one. `Sub Main` loads each form, makes the change, and
reads `ScaleHeight Height` straight after the statements and again after a `DoEvents`. `Form_Resize` logs too. Every
read also logs `GetMenuItemCount` and, from `GetMenuItemRect`, where each top-level item sits. The forms are Fixed
Single, `ClientWidth = 2940` and `ClientHeight = 3420` unless noted, and every top-level menu but `mnuArr` has one
item under it:
- **Form1:** a visible Game menu, and a File menu hidden in the .frm.
- **Form2:** its only menu, Game, hidden in the .frm.
- **Form3:** six menus, Alpha, Bravo, Charlie, Delta, Echo and Foxtrot.
- **Form4:** seven menus, File, Edit, View, Project, Format, Debug and Run, with `ClientWidth = 1500`.
- **Form5:** a top-level menu array `mnuArr`, element 0 hidden in the .frm, with nothing under it. A loaded element
  copies element 0's Visible, so each `Load` below is followed by its `Visible = True`.

**Changing the bar, then changing it back before it lands.** Each cell is ScaleHeight on the console. Session 0 is
the same with its 285-twip bar: 3705 where the console reads 3720, and 3135 for 3120. The form is shown unless noted,
and Height didn't change in any row.

| Form, and the statements before the `DoEvents` | Before | After the `DoEvents` | Resizes |
|---|---|---|---|
| Form1: `mnuGame.Visible = False`, then `True` | 3420 | 3420 | 0 |
| Form1: `False`, `True`, `False` | 3420 | 3720 | 1 |
| Form1: `False`, `False` | 3420 | 3720 | 1 |
| Form1 with its bar gone: `True`, then `False` | 3720 | 3720 | 0 |
| Form1: `mnuGame.Visible = False`, then `mnuFile.Visible = True` | 3420 | 3420 | 0 |
| Form1 with File showing: `mnuFile.Visible = False`, then `mnuGame.Visible = True` | 3420 | 3420 | 0 |
| Form1 loaded, not shown: `False`, then `True` | 3420 | 3420 | 0 |
| Form1: `False`, then `True`, inside `Form_Load`, read once `Load` has returned | 3420 | 3420 | 0 |
| Form2: `mnuGame.Visible = True`, then `False` | 3420 | 3420 | 0 |
| Form5 with `mnuArr(1)` showing: `Unload mnuArr(1)`, then `mnuArr(0).Visible = True` | 3120 | 3120 | 0 |
| Form5 with no bar: `Load mnuArr(1)` and show it, then `Unload mnuArr(1)` | 3420 | 3420 | 0 |
| Form5 with `mnuArr(1)` showing: `Unload mnuArr(1)`, then `Load` and show it again | 3120 | 3120 | 0 |
| Form5 with `mnuArr(1)` showing: `Unload mnuArr(1)` | 3120 | 3420 | 1 |

What these show:
- **The menu changes at the statement, and the client area at the next message.** Straight after the statements,
  `GetMenuItemCount` already counts the items as they now stand: 0 after `False`, `True`, `False`. ScaleHeight hasn't
  moved yet. The next message gives the bar the state the menus are in at that moment.
- **If that state is the one the bar had, nothing happens.** No size changes and no Resize is raised, however many
  changes came between. That holds when the change goes through another top-level menu, and through `Load` and `Unload`
  of a menu array's element. Otherwise one Resize is raised, as for a single change.
- **A form with no bar still has a Win32 menu, with no items in it.** `GetMenu` was non-zero for Form2 throughout, and
  for Form1 with its only visible menu hidden.

**A bar that wraps.** A bar too narrow for its menus wraps them onto more rows. A bar of n rows is
n x (`SM_CYMENU` - 1) + 1 pixels high: 18 px a row and 1 over in session 0, where `SM_CYMENU` is 19, and 19 a row on
the console, where it is 20. That held for every row count measured, 1, 2, 3, 4, 6 and 7 rows, in both
sessions. The rows count before the form is shown. Form3 loads 270 twips taller in session 0 (4350 against Form1's
4080) and 285 on the console (4440 against 4155).

| Form4, client width | Rows, session 0 | ScaleHeight | Rows, console | ScaleHeight |
|---|---|---|---|---|
| 100 px, as loaded | 3 | 3420 | 4 | 3420 |
| 90 px | 4 | 3150 | 6 | 2850 |
| 80 px | 6 | 2610 | 6 | 2850 |
| 70 px | 6 | 2610 | 7 | 2565 |
| 60 px | 7 | 2340 | 7 | 2565 |

- **The rows follow the desktop's menu font,** so the same form wraps differently in the two sessions. The items are
  wider on the console: File, Edit, View, Project, Format, Debug and Run are 28, 30, 35, 45, 44, 44 and 32 pixels in
  session 0, and 32, 34, 39, 51, 52, 49 and 35 on the console. Alpha to Foxtrot are 39, 40, 44, 37, 37 and 44 in
  session 0, and 45, 44, 51, 41, 40 and 52 on the console.
- **An item is its caption's width in the menu font, plus a fixed padding.** A fourth probe read
  `NONCLIENTMETRICS.lfMenuFont` from `SystemParametersInfo`. It measured each caption, without its `&`, with
  `GetTextExtentPoint32` in that font. `DrawText` with prefix processing gave the same widths. Native and p-code agreed,
  in both sessions.
  - **Session 0:** MS Shell Dlg, `lfHeight` -11, with `SM_CXMENUCHECK` 13. All 13 items are their text plus 12 px.
    Alpha to Foxtrot measure 27, 28, 32, 25, 25 and 32; File to Run measure 16, 18, 23, 33, 32, 32 and 20.
  - **Console:** Segoe UI, `lfHeight` -12, with `SM_CXMENUCHECK` 15. All 13 are their text plus 14 px. Alpha to
    Foxtrot measure 31, 30, 37, 27, 26 and 38; File to Run measure 18, 20, 25, 37, 38, 35 and 21.
  - In both sessions the padding is `SM_CXMENUCHECK` - 1. `iMenuHeight`, 18 and 19, is `SM_CYMENU` - 1, the height
    each row adds. With only two sessions, those are consistent, not shown to be rules.
- **An item moves to the next row when its row would leave too little of the client width spare, and how little
  differs by a pixel between the two sessions.** A third probe narrowed Form4 one pixel at a time, from a 101-px
  client to 90, in both sessions, native and p-code agreeing:
  - **Session 0:** File, Edit and View, 93 px together, stayed on one row at a 99-px client, 6 px spare, and wrapped
    at 98, with 5. Format and Debug, 88 px, stayed at 94 and wrapped at 93.
  - **Console:** View and Project, 90 px, stayed at 97, 7 px spare, and wrapped at 96, with 6.
  - **Every row measured fits that.** In session 0, rows wrapped with 5, 4, 2 or 0 px spare and stayed with 6, 7, 9
    or more. On the console they wrapped with 6, 5, 4, 3 or 0 and stayed with 7, 10 or more.
  - **An item that starts a row stays on it, however little is spare.** At a 54-px console client, Project (51 px)
    and Format (52 px) each sat alone on a row, with 3 and 2 px spare.
  - **The margin is counted against the client width, whatever the BorderStyle.** A fifth probe gave Form3's six
    menus to a form of each of the six BorderStyles. It narrowed each one by 15 twips at a time, from a 231-px client
    to 195, native and p-code agreeing, in both sessions.
    - **All six styles broke at the same client width.** In session 0, Alpha to Echo (197 px) stayed on the first row
      at a 203-px client and Echo wrapped at 202. On the console (221 px) they stayed at 228 and wrapped at 227.
    - **The frames differed:** the items start where the client does, 3 px in for None, Fixed Single, Fixed Dialog and
      Fixed ToolWindow, and 4 in session 0 or 8 on the console for the two sizable styles.
    - None with a menu gets Fixed Single's frame, as *A form's Width and Height* has it.
  - Each session's margin is half its item padding, above: 6 of 12, and 7 of 14. With only two sessions, that is
    consistent, not separated from a margin that is simply different on each desktop.
- **A Width change re-wraps the bar at once.** Form3 in session 0 unwrapped when its client went from 246 to 256 px:
  ScaleHeight went from 3420 to 3690 and Height stayed 4350. In the first probe, `Width = Width + 3000` unwrapped it
  the same way, and its Resize came inside the `Width` statement, before the next line read anything. Form4 narrowing
  re-wrapped the same way, each time with Height kept.
- **Hiding or showing a menu that changes the row count lands at the next message,** as the bar going or coming does.
  Form3 in session 0 had Foxtrot hidden and was then made to hide Echo. Straight after that statement, the items sat
  on one row and ScaleHeight still read 3420. After the `DoEvents` it read 3690, with one Resize. Showing Echo again
  moved it back the same way.
- **Every change that changes the row count lands that way, and one reversed before the message changes nothing.**
  Two more probes on Form3, `Sub Main` reading `ScaleHeight Height` straight after the statements and after a
  `DoEvents`, native and p-code agreeing. These rows are the console's. Session 0 gave the same pattern wherever its
  narrower items reached the same break, and B1 and B2 with its 37 px bar.

  | Row | Statements before the `DoEvents` | Rows | `ScaleHeight Height` after | Resizes |
  |---|---|---|---|---|
  | V2 | `mnuEcho.Visible = False`, then `True`, on Form3 with Foxtrot hidden | 2, 2 | `3420 4440` | 0 |
  | V3 | `mnuEcho.Visible = False` | 2 to 1 | `3705 4440` | 1 |
  | V4 | `mnuEcho.Visible = True`, then `False` | 1, 1 | `3705 4440` | 0 |
  | C1 | at a 228-px client, Foxtrot hidden, one row: `mnuBravo.Caption = "&Bravoo"` | 1 to 2 | `3420 4440` | 1 |
  | C2 | `mnuBravo.Caption = "&Bravo"` | 2 to 1 | `3705 4440` | 1 |
  | C3 | `"&Bravoo"`, then `"&Bravo"` | 1, 1 | `3705 4440` | 0 |
  | L1 | `Load mnuArr(1)`, a top-level array hidden in the .frm, then `mnuArr(1).Visible = True` | 1 to 2 | `3420 4440` | 1 |
  | L2 | `Unload mnuArr(1)` | 2 to 1 | `3705 4440` | 1 |
  | L3 | `Load mnuArr(1)`, show it, `Unload mnuArr(1)` | 1, 1 | `3705 4440` | 0 |
  | B1 | at a 196-px client, two rows: all six menus hidden | 2 to none | `4005 4440` | 1 |
  | B2 | all six shown again | none to 2 | `3420 4440` | 1 |
  | V12 | `mnuEcho.Visible = False`, then `Width = Width + 3000` | 2 to 1 | `3705 4440`, already after the `Width` line | 1, inside the `Width` statement |
  | V13 | `mnuEcho.Visible = True`, then `Width = Width - 3000` | 1 to 2 | `3420 4440`, already after the `Width` line | 1, inside the `Width` statement |

  - **The menu changes at the statement.** Straight after it, `GetMenuItemRect` already gave the new row count.
  - **The client area follows at the next message,** taking the row count the menus give then. If that is the count
    the bar had, nothing happens. Otherwise one Resize comes, Height stays, and ScaleHeight moves by the bar's
    change: 19 px a row, or the whole 39 px bar with B1 and B2.
  - **A Width change on the way re-wraps the bar at once,** with its own Resize. The message then finds nothing left to
    change (V12, V13).
  - **Caption, Load and Unload behave as Visible does** (C1 to C3, L1 to L3).
- **The window stopped narrowing** at 56 px wide in session 0 and 60 on the console, however far Width went below
  that. Not explored further.

**Not measured:** an MDI child's menu bar, so a change to it isn't either (since measured: *An MDI child's menu bar*, below); and the bar's rows, rather than its break,
on the BorderStyles other than Fixed Single.

**In HexIDE (#4946),** on main at 1bec9d018:
- **A change reversed before it lands** changes nothing and raises no Resize (#4983). This is pinned by
  `FormOuterSizeTests.Pending_menu_bar_changes_apply_the_final_target`.
- **The bar wraps** onto rows counted from the host's menu font, and a Width change re-wraps it (#4978, PR #4999).
  - Headless at scaling 1, `FormOuterSizeTests.Wrapped_bar_counts_in_outer_and_client_sizes` reads Form3 as the
    console does: `3420 4440`, then `3705 4440` after `Width = Width + 3000`, and `3420 4440` after
    `Width = Width - 3000`.
  - Live on Windows at 150%, the window lost a pixel after a Width change, reading `3690 4425`. #4995 (PR #5028) fixes
    that.
- **A change that changes a wrapped bar's row count** is still refused as not implemented, by `CheckRows`
  (`MenuMembers.cs:185-192`), until #4946's build lands. It was measured in the rows above.

## An MDI child's menu bar (2026-11-21, #4946, SP6)

**The question.** The section above left an MDI child's menu bar unmeasured. #4946 refuses a change to one as not implemented.

**The probe.** Two projects, each built with `/make` on vb6.exe SP6 as native code and as p-code, and run on the guest's console. All four logged the same lines.
- **The MDI form** was 7500 by 4500 twips of client area.
  - In project **a** it has a File menu of its own.
  - In project **b** it has no menu.
- **The children:**
  - `Form1`, an MDI child, has a visible Game menu with one item.
  - `Form2`, another, has a Tools menu hidden in its `.frm`.
- **Sub Main** shows the MDI form, then works through the steps below under an error trap.
- **Each read** is the MDI form's `ScaleHeight` and `Height`, `Form1`'s, each form's count of Resize events, and the items of the menu bar Windows actually holds, from `GetMenu` and `GetMenuString` on the MDI form.

| Step | Project a: the bar, MDI `ScaleHeight` | Project b: the bar, MDI `ScaleHeight` |
|---|---|---|
| the MDI form shown, no child | File, `4140` | **no items, `4440`**: no bar |
| `Form1` shown | **Game**, `4140` | Game, `4140`: the bar appears, with one Resize |
| `Form1.mnuGame.Visible = False`, read at once | **no items**, still `4140` | no items, still `4140` |
| after `DoEvents` | no items, **`4440`**, one Resize | no items, `4440`, one Resize |
| `Form1.mnuGame.Visible = True`, at once | Game, still `4440` | Game, still `4440` |
| after `DoEvents` | Game, `4140`, one Resize | Game, `4140`, one Resize |
| `MDIForm1.mnuFile.Visible = False`, then `True`, with `Form1` active | Game, `4140`, no Resize, both times | |
| hiding the Game menu's only item | **387**, "'Visible' property can't be set on this control" | the same 387 |
| `Form2` shown, so it's active, with its only menu hidden | **no items, `4440`** | no items, `4440` |
| `Form2.mnuGame.Visible = True`, at once, then after `DoEvents` | Tools, `4440`, then `4140` | the same |
| `= False` again, after `DoEvents` | no items, `4440` | the same |
| `Form2` unloaded, so `Form1` is active again | Game, `4140` | the same |
| `Form1.WindowState = 2` | the child's icon, Game, and three window buttons; `4140` | the same |
| `Form1.mnuGame.Visible = False`, maximized, then after `DoEvents` | the icon and the three buttons; **`4140`, no Resize** | the same |
| `Form1.Hide`, so no child is active | **File**, `4140` | no items, `4440` |
| `Form1` unloaded | File, `4140` | no items, `4440` |

**`Form1`'s own size and Resize count didn't change with any menu step.** Read 2385 by 2970 in project a and 2685 by 3270 in b, it changed only when it was maximized and restored. Its default size followed the MDI form's client area when it was created, which was 300 twips taller in b. `MDIForm1.Height` was 5085 throughout.

**What it shows:**
- **The active child's menus replace the MDI form's on the MDI form's bar.**
- **A child with menus, all of them hidden, still replaces them, with nothing.** The bar is then empty and goes away: when `Form1`'s Game menu was hidden, and whenever `Form2` was active. The MDI form's own File menu came back only once no child was active.
- **The bar going or coming moves the MDI form's `ScaleHeight` by 300 twips, the bar, and keeps its `Height`.** That's the same as on an ordinary form (above).
  - The bar's items change at once.
  - `ScaleHeight` changes at the next message, with one Resize, again as above.
- **A maximized child keeps the bar** for its icon and window buttons. Hiding its last menu then changes nothing else.
- **The MDI form's own menus can be hidden and shown while a child's are on the bar,** with no visible effect.
- **The last visible item under a menu can't be hidden** in an MDI child either: 387, as on an ordinary form (above).

**Not measured:**
- a child whose bar wraps;
- `NegotiateMenus` and an MDI form's `WindowList` menu;
- a change that lands while the child is being activated.
