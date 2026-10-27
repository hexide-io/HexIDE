## In-box controls: the SSTab (2026-10-27, #1090)

**The probes.**
- **The software:** VB6 6.00.8176 SP6, with TABCTL32.OCX 6.01.9782.
  - The form line is `Object = "{BDC217C8-ED16-11CD-956C-0000C04E4C0A}#1.1#0"; "TABCTL32.OCX"`.
  - The block is `Begin TabDlg.SSTab`.
- **The form:** drawn in VB6's IDE.
  - An SSTab of three tabs, with a CommandButton drawn on tab 0, a TextBox on tab 1 and a CheckBox on tab 2.
  - Tab 1 was left showing, and the form was saved.
  - The probes ran that saved form, with a Label and a Frame added outside the SSTab.
- **The programs:** a Sub Main showing the form.
  - Most scenarios ran on a fresh instance of the form (`New Form1`), so that no scenario inherited another's state.
  - Each logged positions as `Tab n: Command1 x, Text1 x, Check1 x`, the Left of each control.
- **The builds:** `/make` as native code and as p-code, both run on the guest's console. They logged the same lines.
- **The guest registry:** the first time VB6's IDE opened a form with an SSTab, it showed "Error accessing the system
  registry" and dropped the control. A `/make` run elevated left the IDE able to load it. This is probably the
  registry failure that blocked the container probe in `vb6-designer-geometry.md`.

### The object model

- **Its members,** from the type library:
  - Tab, Tabs, TabsPerRow and Rows;
  - Caption;
  - TabCaption(Index), TabEnabled(Index), TabVisible(Index) and TabPicture(Index);
  - Style, TabOrientation, TabHeight, TabMaxWidth, WordWrap and ShowFocusRect;
  - Picture, BackColor, ForeColor, Font, Enabled, hWnd, MousePointer, MouseIcon and OLEDropMode;
  - the methods OLEDrag and AboutBox.
  - A hidden `_Tab` is the default member.
- **Its events:** Click(PreviousTab As Integer), DblClick, KeyDown, KeyPress, KeyUp, MouseDown, MouseMove, MouseUp, and the
  OLE drag events.
- **The constants:**
  - StyleConstants: ssStyleTabbedDialog 0, ssStylePropertyPage 1.
  - TabOrientationConstants: ssTabOrientationTop 0, Bottom 1, Left 2, Right 3.
- **`TypeName`** is `SSTab`.
- **The extender:**
  - Left, Top, Container, TabIndex, TabStop and Visible all work.
  - Index is 343 when the SSTab isn't in an array.
- **Children of the SSTab work in twips whatever the form's ScaleMode.** With the form in pixels, Command1.Left still
  read -74670. The SSTab's own TabHeight, TabMaxWidth and Width read in the form's ScaleMode:
  - TabHeight 500 twips read 33;
  - TabMaxWidth 1500 read 100;
  - Width 4995 read 333.

**As VB6's save loaded it:**

| Read | Logged |
|---|---|
| Tabs | 3, an Integer |
| Tab | 1, the tab left showing |
| TabsPerRow | 3 |
| Rows | 1 |
| Caption | `Tab 1` |
| Style, TabOrientation | 0, Longs |
| TabHeight | **295**, a Single. The file says `TabHeight = 520` |
| TabMaxWidth | 0 |
| WordWrap, ShowFocusRect, Enabled | True |
| BackColor | `&H8000000F` |
| ForeColor | **0** |
| Font | MS Sans Serif 8.25, the form's |
| Picture, TabPicture(0) | a Picture whose Handle is 0 |
| TabCaption(0), TabEnabled(0), TabVisible(0) | `Tab 0`, True, True |

**Colours read late-bound come back as unsigned Longs,** VarType 19, as the ListView's do.

**The default member, `_Tab`, is inert:**
- It read 0 whichever tab was showing, so `v = SSTab1` is always 0.
- Assigning it does nothing and raises nothing. Neither `SSTab1.[_Tab] = 1` nor `SSTab1 = 0` changed Tab.

### What each property takes

| Assignment | Logged |
|---|---|
| Tab past the last, or -1 | **380** |
| Tab naming a hidden tab | **380** |
| Tab naming a disabled tab | taken, and Click fires |
| Tab set to the tab already showing | nothing. No Click |
| Tab with no tab visible | **380** |
| Tabs up to 99 | taken. New tabs are captioned `Tab n`, n being the index |
| Tabs 100 | **380** |
| Tabs 0, -1 | **380**. Tab 0 held a control in that test, so whether 0 is refused for itself wasn't separated |
| Tabs reduced, the removed tabs empty | taken. **If the showing tab is removed, Tab becomes 0,** not the new last tab |
| Tabs reduced past a tab holding a control | **380**, see below |
| TabsPerRow 1 to 99 | taken |
| TabsPerRow 0, -1, 100 | **380** |
| `Rows =` | **383**. Rows is read-only |
| Style 0, 1 | taken. 2 is **380** |
| TabOrientation 0 to 3 | taken. 4 is **380** |
| TabHeight 0, -1 | **380**. 100 and 2000 are taken |
| TabMaxWidth -1 | **380**. 0 and 1000 are taken |
| TabCaption, TabEnabled, TabVisible, TabPicture with an index past the last, or -1 | **381**, read or written |
| TabCaption(n) = "" | taken |
| `Caption =` | sets TabCaption(Tab). A `&` is kept as typed |
| TabEnabled(Tab) = False | taken. The tab stays showing |
| TabVisible(Tab) = False | taken. **Tab moves to the first visible tab,** counting from 0, with Click |
| TabVisible = False on the last visible tab | taken. Rows reads 0, and Tab doesn't change |
| `Set TabPicture(0) = Nothing` | taken. TabPicture(0) then reads a Picture whose Handle is 0, not Nothing |

**Rows** is the number of rows of visible tabs:
- Three tabs read 1 row with TabsPerRow 3 or more, 2 rows with TabsPerRow 2, and 3 with TabsPerRow 1.
- Ten tabs at TabsPerRow 2 read 5.
- A hidden tab isn't counted: three tabs at TabsPerRow 2, one of them hidden, read 1.

**Click** fires after the change, with the tab left as PreviousTab, an Integer. Inside the handler the controls are
already on their new tab, and their TabStops already changed.

### How the pages work at run time

**A tab's controls are the SSTab's children, all of them always loaded and visible.** Changing Tab doesn't hide anything:
it moves them.
- **The controls of the tab being left go 75000 twips to the left.**
- **The controls of the tab being shown come 75000 twips back.**
- **Visible isn't touched.** It read True for every control on every tab, and a control made invisible on the showing
  tab was moved like the others.

From VB6's save, with tab 1 showing:

| | Command1 (tab 0) | Text1 (tab 1) | Check1 (tab 2) |
|---|---|---|---|
| As loaded, Tab 1 | -74670 | 480 | -74370 |
| Tab = 0 | **330** | **-74520** | -74370 |
| Tab = 2 | -74670 | -74520 | **630** |

**Which controls move is decided by where they are, not by the file's lists.**
- **Leaving a tab:** the SSTab moves left every child that is on the page, and records those children as the tab's
  controls.
  - "On the page" means the control's right edge and bottom edge are at or past the SSTab's left and top edges:
    `Left + Width >= 0` and `Top + Height >= 0`.
  - There is no limit to the right or below.
- **Showing a tab:** it moves back the controls it recorded when that tab was last left. Until a tab has been left, its
  controls are the ones the form file put on it.

**Cases for "on the page,"** each moved there by code with tab 1 showing, then Tab = 2:

| Control | Left, Top | Moved left? |
|---|---|---|
| Command1, 1515 wide | Left -1, -100, -1514 or **-1515** | yes |
| Command1, 1515 wide | Left **-1516**, -1530, -5000, -15000, -20000 or -50000 | no |
| Command1, 1515 wide | Left 200000 | yes, to 125000 |
| Command1, 3000 wide | Left -2500 | yes |
| Command1, 3000 wide | Left -3100 | no |
| Text1, 390 high, on tab 1, Tab = 0 | Top **-390** or -389 | yes |
| Text1, 390 high, on tab 1, Tab = 0 | Top **-391** or -1000 | no |

The measurements were taken at 96 dpi, 15 twips to a pixel.

**So a control moved by code follows its position, not its tab.** The commonest way to do that is resizing code that
sets Left for every control. Each case below is from a fresh form:
- **A control on a hidden tab, moved onto the page, joins the showing tab, and stays on its own tab too.** Command1, on
  tab 0, was set to Left 100 with tab 1 showing. Command1's positions, tab by tab:
  - set to 100: 100, showing on tab 1;
  - Tab = 2: -74900, hidden with tab 1's Text1;
  - Tab = 0: **100**, shown with tab 0, its own tab;
  - Tab = 1: **100**, shown with tab 1, as Text1 was;
  - Tab = 2: -74900;
  - Tab = 0: 100.
- **A control moved off the page on the showing tab is lost.** Text1, on tab 1, was set to Left -74000 with tab 1
  showing. It stayed at -74000 through Tabs 0, 1, 2 and 1, on no tab at all.
- **A control moved onto the showing tab, from the form or by Left, is that tab's from then on.**
  - `Set Label1.Container = SSTab1` with tab 1 showing put Label1 on tab 1. It went with tab 1 from then on.
  - Text1, set to Left 480 with tab 0 showing, appeared on tab 0 and tab 1 after.

**A recorded control comes back by exactly 75000, wherever it is.** In a longer sequence on one form, Command1 had been
recorded for tab 1 as well as tab 0:
- with tab 0 showing, it was set to -74000, then Tab = 1. Leaving tab 0 didn't count it as on the page, and showing
  tab 1 moved it to 1000. Until it was moved again, it showed on tab 1 only;
- set to -20000 the same way, it came back at **55000**;
- set to -60000, it came back at 15000;
- set to -76000, it came back at -1000.

**TabStop goes with the page:**
- **Leaving a tab sets each moved control's TabStop to False.** Showing it gives them back the TabStop they had.
  - As loaded with tab 1 showing: Command1 False, Text1 True, Check1 False.
  - After Tab = 0: Command1 True, Text1 False.
- **A TabStop set to False on the showing tab survives.** Text1.TabStop = False, then Tab 0 and back to 1, left
  Text1.TabStop False.
- **A TabStop set to True on a hidden tab is True at once,** and goes back to False when its tab is next left.
- **Enabled is not touched.** Every control read Enabled True, and its window enabled, on hidden tabs too. A control
  disabled on its tab stayed disabled through the changes.

**Enabled, inside a disabled container:** with `SSTab1.Enabled = False`, every child read Enabled False. Their windows
stayed enabled, and the SSTab's own window was disabled. Enabling the SSTab again, each child read its own value: Text1,
disabled beforehand, stayed False. **A Frame and a PictureBox do the same.** The oracle's "A control's Enabled inside a
disabled container" has them, and HexIDE reads a child's own value (#2245).

**Reducing Tabs:**
- **Tabs can't go below a tab holding controls.** It is refused with 380, and "holding" means the controls the SSTab last
  recorded for it.
  - `Tabs = 2`, with Check1 on tab 2, was 380, whether tab 2 was showing or not.
  - **Moving Check1 to the form while tab 2 was hidden didn't help.** `Set Check1.Container = Form1`, then Tabs = 2,
    was still 380.
  - **Moving Check1 to the form, or off the page, while tab 2 was showing, then leaving tab 2, did.** Tabs = 2 was then
    taken. Check1, left off the page, stayed a child of the SSTab at -74370, on no tab.
- **A refused reduction still changes Tab:**
  - with tab 2 showing, `Tabs = 2` moved Tab to 0, with Click, then raised 380;
  - so did `Tabs = 1` with tab 1 showing.

### In HexIDE

An SSTab is left out at run time (#857), and the designer doesn't model it (#1090).
