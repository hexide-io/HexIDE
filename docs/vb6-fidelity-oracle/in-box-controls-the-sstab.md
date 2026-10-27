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

### Events, with real input

**The probe:** VB6's saved form, run on the guest's console. A driver clicked and typed for real, and marked each action
in the log, which also recorded every event the SSTab and its children raised. Native and p-code logged the same lines,
apart from the window's position.

| Action | Events |
|---|---|
| Click a tab's caption | MouseDown (Tab still the old one), **Click(PreviousTab)**, MouseUp (Tab the new one) |
| Click the showing tab's caption | MouseDown, MouseUp. No Click |
| Double-click a tab's caption | MouseDown, Click, MouseUp, DblClick, MouseUp |
| Click the page, below the tabs | MouseDown, MouseUp. **No Click**: Click means the tab changed |
| Right-click a caption | MouseDown and MouseUp with Button 2. The tab doesn't change |
| Click a disabled tab's caption | MouseDown, MouseUp. The tab doesn't change, but the SSTab takes the focus |

- **Mouse X and Y are in twips:** 840 and 135 for a point 56 and 9 pixels into the SSTab.
- **Clicking a caption gives the SSTab the focus, even a disabled tab's.** With the focus in Text1, a click on disabled
  tab 0's caption raised SSTab1_GotFocus.

**The keyboard, with the focus on the SSTab:**
- **Right and Left change the tab, wrapping:**
  - Right from tab 2 went to tab 0, and Left from tab 1 to tab 0;
  - with tab 0 disabled, Left from tab 1 did nothing. It neither skipped to tab 2 nor wrapped.
- **Ctrl+Tab goes to the next tab and Ctrl+Shift+Tab to the previous one.**
- **Home, End, Up and Down do nothing** but raise KeyDown and KeyUp.
- A plain letter raises KeyDown, KeyPress and KeyUp, and doesn't change the tab, even the letter of a caption's access
  key.

**With the focus in a control on the page:**
- **Ctrl+Tab, Ctrl+Shift+Tab and Ctrl+PageDown do nothing.** Text1 got the KeyDown and the tab didn't change.
- **Alt and a caption's access key change to that tab and move the focus to the SSTab.** With TabCaption(2) `&Third`
  and the focus in Text1, Alt+T raised Click(PreviousTab 1), then SSTab1_GotFocus.

**The Tab key skips the controls on hidden tabs,** since the SSTab has set their TabStop to False. From Text1 on the
showing tab, Tab went on through the form to the SSTab itself (TabIndex 0), then straight back to Text1, past Command1
(TabIndex 1) on hidden tab 0.

### Drawing

Screenshots of VB6's saved form in each state, on Windows 10 at 96 dpi. A note on what they show, for an implementation
to match:

- **Style 0 (tabbed dialog):**
  - The tabs share the width equally.
  - The showing tab is raised, joined to the page, and **its caption is bold**.
  - The page has a raised edge with a dark shadow right and below.
- **Style 1 (property page):**
  - Each tab is as wide as its caption.
  - The tabs are packed from the left.
  - **The showing tab's caption isn't bold.**
- **Several rows:** with five tabs and three to a row, the row holding the showing tab is in front, next to the page.
  - The other row is behind it, set a few pixels to the right.
  - The page edge shows a second card's edge behind it.
  - **Showing a tab in the back row brings that row to the front** in both Styles. With Tab 4 showing, the row of tabs
    3 and 4 was in front and tabs 0 to 2 behind.
- **TabOrientation:**
  - Bottom puts the strip below the page.
  - Left and Right stand the tabs on end. **The captions aren't turned:** they're drawn across the strip, clipped to its
    thickness, so `Tab 1` showed as `Ta` on the left, and nothing legible on the right.
- **TabHeight 600:** a caption too long for its tab wraps onto a second line inside it, with WordWrap True. At the
  default height, the second line is clipped. The same caption with WordWrap False wasn't captured.
- **TabMaxWidth 900:**
  - in Style 0, every tab is 900 twips wide, packed from the left, rather than sharing the width;
  - in Style 1, the tabs look as they do without it, the captions being narrower than 900.
- **A TabPicture is drawn left of the caption,** small. `Set Picture = icon` set the showing tab's TabPicture:
  TabPicture(1) then had the same handle as Picture.
- **BackColor isn't the page's colour.** BackColor yellow showed only in the small corners behind the tabs' rounded
  ends; the page and the tabs kept the button face colour. ForeColor is the captions' colour, and Font their font, on
  every tab.
- **Enabled False** greys every caption, the showing tab's too.
- **A hidden tab leaves a gap.** With tab 1 hidden, tabs 0 and 2 kept their third of the width each, packed left, and the
  last third was empty.
- **The focus rectangle** is a dotted box round the showing tab's caption, while the SSTab has the focus. With
  ShowFocusRect False there is none.

### Layout: the SSTab moves its children when the strip moves

**Changing the tab strip's thickness or side moves the SSTab's children with the page,** hidden tabs' children too.
Measured on fresh forms, natively and as p-code. The strip's thickness is Rows × TabHeight, TabHeight rounded to whole
pixels: 295 twips is 20 pixels, so 300 twips a row.

| Change | Command1 (Left, Top), tab 0 | Text1 (Left, Top), tab 1 |
|---|---|---|
| As loaded, 3 tabs, 1 row, strip on top | -74670, 630 | 480, 1230 |
| Tabs = 4 or 5, 2 rows | -74670, **930** | 480, **1530** |
| Tabs = 7, 3 rows | -74670, 1230 | 480, 1830 |
| Tabs = 10, 4 rows | -74670, 1530 | 480, 2130 |
| Tabs = 3 again, 1 row | -74670, 630 | 480, 1230 |
| TabOrientation = 1, strip at the bottom | -74670, **330** | 480, **930** |
| TabOrientation = 2, strip on the left | **-74370**, 330 | **780**, 930 |
| TabOrientation = 3, strip on the right | -74670, 330 | 480, 930 |
| TabOrientation = 0 again | -74670, 630 | 480, 1230 |
| TabHeight 600, then Tabs = 5 | -74670, **1230**: 2 rows of 600 | 480, 1830 |
| ... then TabOrientation = 1 | -74670, **30** | 480, 630 |

- **Changing Tabs moves only the controls some tab has recorded.**
  - A Frame just moved into the SSTab didn't move with Tabs = 5.
  - Neither did Text1, once moved off the page on its own tab and so recorded nowhere.
  - Label1, once recorded for a tab, moved with the rest.
  - A control recorded for a tab and then moved to the form didn't move.
- **Changing TabOrientation moves every child,** recorded or not: the Frame moved up with the rest.
- **These change the rows or the strip and move nothing:**
  - **TabsPerRow:** at 7 tabs, TabsPerRow 2, 1, 4 and 7 changed Rows to 4, 7, 2 and 1, and every control stayed at the
    3-row position;
  - **TabVisible**, though Rows changed;
  - **TabHeight**, 600, 1000 or 150;
  - Style, Font.Size, Width and Height.

  So after those, the strip can cover the controls under it, or leave a gap.
- **The move is applied whatever the control's position.** Text1 at Top 0 went to 300 with Tabs = 5, and back to 0.

### In HexIDE

An SSTab is left out at run time (#857), and the designer doesn't model it (#1090).
