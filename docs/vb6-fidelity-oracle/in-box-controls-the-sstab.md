## In-box controls: the SSTab (2026-10-27, #1090)

**The probes.**
- **The software:** VB6 SP6 (6.00.9782), with TABCTL32.OCX 6.01.9782.
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
  default height, the second line is clipped. **With WordWrap False it stays on one line, centred, and clipped at both
  ends:** `uch longer caption tha`.
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

### The saved files

**The programs:**
- **Drawn:** the form the earlier groups ran, drawn in VB6's IDE and saved there. It is in
  `corpus/inbox-controls/pending/sstab/`.
- **Re-saved:** a form of three SSTabs written by hand, opened in VB6's IDE, dirtied with a comment and saved. A fourth
  SSTab was double-clicked from the toolbox first. That save is in `pending/sstab-resaved/`.
  - SSTab1 had every property off its default, its lines in reverse order.
  - SSTab2 had only its position, extents and `_Version`.
  - SSTab3's flags didn't match its controls.
- **The flags:** seven more two-tab SSTabs, each varying one line of the file, read at run time and never opened in the
  IDE.
- **The reads:** every file was read back at run time, natively and as p-code. Each pair logged the same lines.

**VB6's drawn SSTab, with a control drawn on each tab and tab 1 left showing:**

```
   Begin TabDlg.SSTab SSTab1 
      Height          =   3195
      Left            =   120
      TabIndex        =   0
      Top             =   120
      Width           =   4995
      _ExtentX        =   8811
      _ExtentY        =   5636
      _Version        =   393216
      Tab             =   1
      TabHeight       =   520
      TabCaption(0)   =   "Tab 0"
      TabPicture(0)   =   "Form1.frx":0000
      Tab(0).ControlEnabled=   0   'False
      Tab(0).Control(0)=   "Command1"
      Tab(0).Control(0).Enabled=   0   'False
      Tab(0).ControlCount=   1
      TabCaption(1)   =   "Tab 1"
      TabPicture(1)   =   "Form1.frx":001C
      Tab(1).ControlEnabled=   -1  'True
      Tab(1).Control(0)=   "Text1"
      Tab(1).Control(0).Enabled=   0   'False
      Tab(1).ControlCount=   1
      ...tab 2 the same, holding Check1...
      Begin VB.CheckBox Check1 
         ...
         Left            =   -74370
      ...
      Begin VB.TextBox Text1 
         Left            =   480
      ...
      Begin VB.CommandButton Command1 
         Left            =   -74670
```

- **Every tab's controls are nested in the SSTab's block.** Those on tabs not showing are written 75000 twips left of
  where they are drawn.
- **Each tab writes five kinds of line, in this order:**
  - `TabCaption(n)`;
  - `TabPicture(n)`, when it has one, a `.frx` reference;
  - `Tab(n).ControlEnabled`;
  - for each control, `Tab(n).Control(i)`, naming it, then `Tab(n).Control(i).Enabled`;
  - `Tab(n).ControlCount`.
  - A tab with no controls writes `ControlCount = 0` and no `Control(i)` lines.
- **`Tab(n).ControlEnabled` is -1 on the showing tab** in every SSTab VB6 drew or added. VB6 re-saves the flags it
  holds, though:
  - SSTab2, loaded without them, was saved with -1 on all three tabs;
  - SSTab3, loaded with them wrong, was saved with -1 on two.
- **`Tab(n).Control(i).Enabled` was always written 0.** Setting it to -1 changed nothing that was measured.
- **TabHeight is always written,** in HIMETRIC, 520 by default. TabMaxWidth is HIMETRIC too: the file's 900 and 1500
  read 510 and 850 twips.
- **`TabEnabled(n)` and `TabVisible(n)` aren't saved.** Written by hand, they were ignored at run time (tab 1 stayed
  enabled, tab 3 visible) and dropped from VB6's save.
- **`TabPicture(n)` lines:**
  - Each is a 28-byte `.frx` record for an empty picture: Long 24, then CLSID_StdPicture
    `{0BE35204-8F91-11CE-9DE3-00AA004BB851}`, then `lt` 0 0, then a Long size of 0.
  - **Not every tab writes one:**
    - the drawn SSTab, each of whose tabs was clicked in the designer, wrote all three;
    - the double-clicked SSTab4 wrote one for tab 0;
    - SSTab1, selected in the designer with tab 2 showing, wrote one for tab 2;
    - SSTab2 and SSTab3, never selected, wrote none.
  
  The rule that fits all four: a tab writes its picture once the designer has read that tab's Picture, which the
  Properties window does for the showing tab whenever the SSTab is selected.
- **The SSTab's own lines** come in this order, whatever order they were read in: the extender's (Height, Left,
  TabIndex, Top, Width), `_ExtentX`, `_ExtentY`, `_Version`, then
  ```
  TabOrientation, Style, MousePointer, Tabs, Tab, TabsPerRow, TabHeight, TabMaxWidth,
  WordWrap, ShowFocusRect, BackColor, ForeColor, OLEDropMode, the Font block, then the tabs
  ```
  Each is written only when off its default, apart from TabHeight. The font is a `BeginProperty Font` block.
- **The double-clicked SSTab is small:** Height 750, Width 1845, `_ExtentX` 3254, `_ExtentY` 1323, at Left 4050, Top
  3225 on a 9600×7200 form. It has three tabs, `Tab 0` to `Tab 2`, with tab 0 showing.
- **SSTab3's children were written windowed controls first, then Label2,** though Label2 was second in the file.

**SSTab2, the minimal block, loaded with the defaults:** Tabs 3, Tab 0, TabsPerRow 3, captions `Tab 0` to `Tab 2`,
TabHeight 295 twips, TabMaxWidth 0, Style 0, TabOrientation 0, WordWrap and ShowFocusRect True, BackColor `&H8000000F`,
ForeColor 0, and the form's font.

**Loading: what the run time does with the file's lists and flags.** The seven two-tab SSTabs each had tab 1 showing,
Cmd on tab 0 and Txt on tab 1, and varied one thing. Each row gives the Left/TabStop of both, as loaded, then after
Tab = 0 and Tab = 1:

| | The file | As loaded | Tab = 0 | Tab = 1 |
|---|---|---|---|---|
| A | as VB6 writes it, but Txt written off the page, at -74520 | Cmd -74670/False, **Txt -74520/True** | Cmd 330/True, Txt -74520 | Cmd -74670, **Txt -74520, lost** |
| B | as VB6 writes it, but Cmd written on the page, at 330 | **Cmd 330/False**, Txt 480/True | Cmd 330/True, Txt -74520/False | **Cmd 330**/False, Txt 480/True |
| C | `ControlEnabled` -1 on tab 0, not tab 1 | Cmd -74670/True, **Txt -74520/False** | **Cmd -74670**, Txt 480/True | Cmd -74670, Txt -74520 |
| D | no `ControlEnabled` or `Control(i).Enabled` lines | as C | as C | as C |
| E | `Tab(0).Control(0).Enabled = -1` | as VB6's | as VB6's | as VB6's |
| F | `Tab(1).Control(0).Enabled = -1` | as VB6's | as VB6's | as VB6's |
| G | no `Tab(n).Control` lines | Cmd -74670/**True**, Txt 480/True | **Cmd -74670**, Txt -74520/False | Cmd -74670, Txt 480/True |

"As VB6's" means as for a file VB6 wrote: Cmd -74670/False and Txt 480/True as loaded, and the two swapping with each
change of tab.

- **The run time trusts the positions written.** Nothing is moved into place:
  - A: Txt, written off the page on the showing tab, stays there, and is lost once tab 1 is left.
  - B: Cmd, written on the page for a hidden tab, shows on tab 1, and then on both tabs.
- **The lists say which controls a hidden tab brings back.** Without them, Cmd was never shown (G).
- **A listed control of a tab whose `ControlEnabled` is False is given TabStop False.** A control in no list keeps its
  own (G, and Check2 below).
- **When `Tab(0).ControlEnabled` is True, or missing, and the file's Tab isn't 0, loading leaves tab 0.** Every child on
  the page is moved left and recorded as tab 0's, and nothing is brought back for the showing tab (C, D).
  - **So in C and D, Txt is shown on tab 0, and tab 0's own Cmd is never shown.**
  - An SSTab VB6 drew, or loaded with consistent flags, is saved with `ControlEnabled` False on tab 0 whenever another
    tab shows, so it loads as VB6's designer showed it.
  - One it loaded with the flags missing or wrong, it saves with them still wrong, as SSTab3 shows.

**SSTab3, whose flags didn't match:** tab 1 showing, `ControlEnabled` -1 on tab 0 instead.
- Tab 0's list named Check2, written off the page; tab 1's named Text2, on the page.
- Label2, on the page, and Option2, off it, were in no list.
- **Run directly:** as C. Text2 and Label2 were moved off at load and showed only on tab 0, and Check2 and Option2 never
  showed.
- **Opened in VB6's IDE and saved:**
  - tabs 0 and 1 were both listed with Label2 and Text2, with `ControlEnabled` -1 on both;
  - Text2 and Label2 were written on the page, as the designer showed them on tab 1;
  - Check2 and Option2 were written in no list.
  
  Run, that save did exactly what the hand-written file did. So VB6 saved a file that runs differently from what its
  designer showed.

**TabIndex is renumbered in the saved file.** This answers the open question in `vb6-designer-geometry.md`.
- The hand-written values were:
  - SSTab1 0, and its children 1 to 3;
  - SSTab2 10;
  - SSTab3 20, and its children 21 to 25.
- VB6 saved:
  - SSTab1 and its children 0 to 3, as written;
  - SSTab3 4, its children 5 to 9 **in file order**, the reverse of their written TabIndexes;
  - SSTab2 10, as written;
  - the new SSTab4 11.
- One rule fits this and the geometry note's `0,0,9,9,4,(absent)` → `0..5`:
  - each in-range TabIndex keeps its place;
  - a repeated one takes the next place free;
  - the out-of-range ones fill the remaining places in file order.

  It has been checked against those two forms only.

### TabPicture at run time (2026-11-16, #3426, SP6)

**The question.** HexIDE stopped on any run-time `TabPicture(n)` (#3426). What does a tab's picture read as, what does it
take, and what does the strip draw?

Measured on vb6.exe SP6, native and p-code, which agree on every row. The probe used an SSTab (TABCTL32.OCX 1.1) with `Tabs = 3`, and a 16 by 16 pixel PictureBox with `AutoRedraw`, whose `.Image` was the picture assigned. One more run, in the IDE, looked at the strip.

#### Reading

| Read | Result |
|---|---|
| `TabPicture(0)`, never set | a **Picture object** with `Handle` 0 and `Type` 0 (vbPicTypeNone). Never Nothing. |
| `v = TabPicture(0)`, into a Variant | Long 0, the Handle: the default member |
| `TabPicture(3)` with `Tabs = 3`, and `TabPicture(-1)` | **381**, "Invalid property array index" |
| `TabPicture(1.6)`, `TabPicture("1")` | read as indexes 2 and 1 |

#### Assigning

| Assignment | Result |
|---|---|
| `Set TabPicture(0) = Picture1.Image` | taken: Handle non-zero, Type 1 (bitmap) |
| `TabPicture(0) = Picture1.Image`, Let | **taken**, as Set |
| `Set TabPicture(0) = Nothing` | no error. **Reads back as an empty Picture**, Handle 0, not Nothing |
| `Set TabPicture(0) = LoadPicture()` | empty, as Nothing |
| `TabPicture(0) = "x"` through an `As Object` reference | **13** |
| `TabPicture(0) = 5`, the same | **13** |
| `Set TabPicture(0) = Form1` | **13** |
| `Set TabPicture(0) = Picture1`, the PictureBox | no error, reads empty. Picture1's own `Picture` was empty, so this doesn't show whether it took that default property. |
| `TabPicture(3)` or `TabPicture(-1)` assigned | **381** |
| `Set TabPicture(2.5) = …` | no error, at index 2: rounded half to even, as CInt |
| `TabPicture(0) = "x"`, early-bound on the control | a **compile error**, "Type mismatch" |

#### The object and the strip

- **The same object comes back.** `Set TabPicture(1) = a` then `TabPicture(1) Is a` is True, and two reads are `Is` each other.
- **It's a reference, not a copy.** In the IDE run, `TabPicture(0)` was set from `Picture1.Image` while the PictureBox was red, and `TabPicture(2)` after it was redrawn blue. **Both tabs drew blue.** The tab shows what the picture holds when it's painted.
- **On the strip:**
  - each picture is drawn at its own pixel size, 16 wide and 15 visible at `TabHeight` 520, left of the caption;
  - the picture and caption are centred on the tab together;
  - a tab without a picture is unchanged.
- **`SSTab.Picture` is the current tab's `TabPicture`.** With `Tab = 0`, `Picture` reads `TabPicture(0)`'s object. Setting `Picture` with `Tab = 2` sets `TabPicture(2)`.
- **Reducing `Tabs` drops the pictures beyond it.** With `TabPicture(2)` set, `Tabs = 2` makes `TabPicture(2)` raise 381. `Tabs = 3` again reads it back empty.

### Saved TabHeight and TabMaxWidth, and reading them in each ScaleMode (2026-11-19, #4953, SP6)

**The probe.**
- **The software:** VB6 SP6 (6.00.9782), MSVBVM60 6.00.9848 and TABCTL32.OCX 6.01.9782. `Screen.TwipsPerPixelX` and
  `Y` were 15.
- **The form:** written by hand, not by VB6's designer, on a 9600 × 7200 twip client. It holds 22 SSTabs of three tabs
  each, every one 9000 × 6600 twips. Each saves the same number for TabHeight and TabMaxWidth: 1, 2, 3, 13, 26, 27,
  100, 254, 300, 500, 519, 521, 527, 528, 600, 1000, 2540, 5000 or 10000. Three more save 520 with no TabMaxWidth
  line, 900, and 1500.
- **The program:** a Sub Main that loads and shows the form, then reads both properties late-bound and logs each
  value with its `TypeName`. One early-bound read agreed.
- **Also built:** the same form saved with `ScaleMode = 3`, and five projects of one SSTab each for the edge values
  below.
- **The builds:** `/make` as native code and as p-code, run in a PowerShell Direct session. Every project logged the
  same lines both ways, and none wrote a load log.

**TabHeight and TabMaxWidth agreed in every row**, so the tables give one value for both. Every read is a Single.

#### In twips: the nearest whole twip

A saved value reads as HIMETRIC × 1440 / 2540, rounded to the nearest whole twip. There's no minimum: saved 1 reads 1.

The table also gives two other candidate rules. 520 alone already ruled both out, and more
rows do here:
- **truncating** gives a different read in 9 of the 23 rows, shown in bold;
- **going through whole pixels**, at 15 twips a pixel, gives a different read in 18.

| Saved | Exact twips | Read | Truncated | Through whole pixels |
|---|---|---|---|---|
| 1 | 0.567 | **1** | 0 | 0 |
| 2 | 1.134 | 1 | 1 | 0 |
| 3 | 1.701 | **2** | 1 | 0 |
| 13 | 7.370 | 7 | 7 | 0 |
| 26 | 14.740 | **15** | 14 | 15 |
| 27 | 15.307 | 15 | 15 | 15 |
| 100 | 56.693 | **57** | 56 | 60 |
| 254 | 144 | 144 | 144 | 150 |
| 300 | 170.079 | 170 | 170 | 165 |
| 500 | 283.465 | 283 | 283 | 285 |
| 519 | 294.236 | 294 | 294 | 300 |
| 520 | 294.803 | **295** | 294 | 300 |
| 521 | 295.370 | 295 | 295 | 300 |
| 527 | 298.772 | **299** | 298 | 300 |
| 528 | 299.339 | 299 | 299 | 300 |
| 600 | 340.157 | 340 | 340 | 345 |
| 900 | 510.236 | 510 | 510 | 510 |
| 1000 | 566.929 | **567** | 566 | 570 |
| 1500 | 850.394 | 850 | 850 | 855 |
| 2540 | 1440 | 1440 | 1440 | 1440 |
| 5000 | 2834.646 | **2835** | 2834 | 2835 |
| 10000 | 5669.291 | 5669 | 5669 | 5670 |
| 100000 | 56692.913 | **56693** | 56692 | 56700 |

The last row is from the edge projects. No saved whole number can sit exactly half-way between two twips, since
HIMETRIC × 72 / 127 never ends in .5.

#### In pixels: the nearest whole pixel

A form switched to `ScaleMode = 3` at run time, and the form saved in pixels, read the same:

| Saved | 1, 2, 3, 13 | 26, 27 | 100 | 254 | 300 | 500 | 519 to 528 | 600 | 900 | 1000 | 1500 | 2540 | 5000 | 10000 | 100000 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Pixels read | 0 | 1 | 4 | 10 | 11 | 19 | 20 | 23 | 34 | 38 | 57 | 96 | 189 | 378 | 3780 |

That is HIMETRIC × 96 / 2540, rounded. Rounding the twips read to pixels gives the same in every row. At 15 twips a
pixel the two can't differ, so these rows don't say which one the control does.

#### In every other ScaleMode: exact, and across

**The read isn't rounded in points, characters, inches, millimetres, centimetres or a user scale.** It's the saved
HIMETRIC converted exactly, as a Single. Saved 1 read:
- 0.02834646 points, which is 1 × 72 / 2540, where the whole twip would give 0.05;
- 0.01000001 mm, 0.001000001 cm and 3.937008E-04 inches.

**Both properties convert with the horizontal scale, TabHeight included.** The obvious guess for a height is the
vertical scale, as Height uses.
- **In characters** (120 twips across, 240 down): saved 2540 read 12 for TabHeight as well as TabMaxWidth. The
  vertical scale would give 6.
- **In a user scale** of `ScaleWidth = 1000` and `ScaleHeight = 100`: that is 9.6 twips across and 72 down. Saved 2540
  read 150 for both. The vertical scale would give 20.

#### Assigning at run time

These were read back after assigning SSTab19's TabHeight, in twips and in pixels:

| Assigned | Twips read | Pixels read |
|---|---|---|
| 300, 301, 307 twips | 300, 301, 307 | 20 |
| 308 twips | 308 | 21 |
| 300.4, 300.5 and **300.6** twips | **300** | 20 |
| 1, 0.4 and 0.6 twips | 1 | 0 |
| 20 pixels | 300 | 20 |
| 21 pixels | 315 | 21 |
| 20.4 pixels | 306 | 20 |
| 20.5 pixels | 307 | 20 |
| 20.6 pixels | 309 | 21 |
| 1 pixel | 15 | 1 |
| 0.4 pixels | 6 | 0 |

TabMaxWidth gave the same for 301, 307, 308, 300.5 and 0.6 twips, and for 20.4, 20.5 and 20.6 pixels. None of these
assignments raised an error.

**300.6 twips reads back 300, not 301.** So the control doesn't keep twips. It keeps a whole number of HIMETRIC:
- 300.6 twips is 530.225 HIMETRIC, kept as 530, which is 300.47 twips;
- 20.6 pixels is 309 twips, which is 545.042 HIMETRIC, kept as 545, which is 308.98 twips;
- 0.4 twips is 0.706 HIMETRIC, kept as 1, which is 0.567 twips.

**Rounding to the nearest fits every row.**
- Truncating would keep 0.4 twips as 0, which reads 0, not 1.
- Rounding up would keep 300.5 twips as 531, which reads 301.

Assignments were made only in twips and pixels.

#### Edge values in the file

| Saved | Twips read | Pixels read |
|---|---|---|
| `TabHeight = 0` | 295 | 20 |
| `TabHeight = -1` | 295 | 20 |
| no TabHeight line | 295 | 20 |
| `TabMaxWidth = -1` | 0 | 0 |
| no TabMaxWidth line | 0 | 0 |
| both 100000 | 56693 | 3780 |

**A TabHeight of 0 or less loads as the default, 520.** A negative TabMaxWidth loads as 0. Neither is an error, and
`/make` logged nothing. Whether VB6 refuses these values or treats them as "no value" isn't separated: both read the
same.

#### The rule these fit

- **Storage:** the SSTab keeps TabHeight and TabMaxWidth as whole numbers of HIMETRIC.
- **Loading:** a saved value is kept as written, except a TabHeight of 0 or less and a negative TabMaxWidth. Each of
  those takes its default.
- **Reading:** the value is converted to the container's ScaleMode with the horizontal scale, for both properties.
  Only in twips and pixels is it rounded, to the nearest whole unit.
- **Assigning:** in twips or pixels, the value is converted to HIMETRIC and rounded to the nearest whole number.

**Not measured:**
- a display other than 15 twips a pixel;
- an assignment in any other ScaleMode;
- an assignment exactly half-way between two HIMETRIC values;
- a TabHeight that doesn't fit the control.

### In HexIDE

HexIDE runs the SSTab (#1090). Forms VB6 designed load their pages and the tab rows draw. Clicks, the arrows, Ctrl+Tab
and Alt mnemonics switch tabs in the order measured above, and a page's controls move 75000 twips left as it is left.
Both saved forms round-trip byte for byte. The designer shows an SSTab form read-only until its edits can keep each
page's controls (#2301).

### Assigning in every ScaleMode, the edges, a flipped scale, and 1,100 reads (2026-11-21, #4970, SP6)

**The question.** "Saved TabHeight and TabMaxWidth" above measured assignments in twips and pixels only, and left five things open:
- an assignment in another ScaleMode;
- an assignment that rounds to 0 HIMETRIC;
- an exact half;
- a flipped user scale;
- reads in millimetres, centimetres and a user scale, where no formula fitted all of the saved values.

**The probe.** It used a form like the one above: a 9600 × 7200 twip client holding one SSTab, 9000 × 6600, with TABCTL32.OCX. `Screen.TwipsPerPixelX` was 15. Each assignment was made in its ScaleMode after both properties were set to 5 mm. Each was then read back in the same mode, in millimetres, in points and in twips. A millimetre read of a whole HIMETRIC value is that value over 100, so it shows what was kept. Built with `/make` on vb6.exe SP6 as native code and as p-code, the two logged the same lines.

**Assignments keep the nearest whole HIMETRIC in every ScaleMode,** with no error:

| Assigned | Exactly, in HIMETRIC | Kept |
|---|---|---|
| 10, 11, 12, 13, 14 points | 352.78, 388.06, 423.33, 458.61, 493.89 | 353, 388, 423, 459, 494 |
| 14.9, 15, 15.01, 15.05 points | 525.64, 529.17, 529.52, 530.92 | 526, 529, 530, 531 |
| 0.75, 1, 2, 2.5 characters | 158.75, 211.67, 423.33, 529.17 (120 twips across) | 159, 212, 423, 529 |
| 0.2, 0.2083, 0.5 inches | 508, 529.08, 1270 | 508, 529, 1270 |
| 5.27, 5.274, 5.276 mm | 527, 527.4, 527.6 | 527, 527, 528 |
| 0.527, 0.5274, 0.5276 cm | 527, 527.4, 527.6 | 527, 527, 528 |
| 31.25, 1, 2.5, 0.05 in a 1000 × 100 user scale | 529.17, 16.93, 42.33, 0.85 (9.6 twips across) | 529, 17, 42, 1 |

**Characters and the user scale convert across, for TabHeight as for TabMaxWidth:** 1 character kept 212, the 120 twips across, not the 240 down. That's how reads convert too.

**An assignment that rounds to 0 is kept as 0, with no error.** `TabHeight = 0.2` twips (0.35 HIMETRIC) and `= 0.28` (0.49) read 0 in every mode, and `0.29` (0.51) read 1 HIMETRIC. So did 0.004 and 0.005 mm. Assigning 0 itself raises 380 (above), so this is the only way to get a TabHeight of 0.

**Exact halves went down in all three cases measured.** 1.5 characters (317.5) kept 317, 36 twips (63.5) kept 63, and 900 twips (1587.5) kept 1587. TabMaxWidth did the same as TabHeight. These are all odd numbers, so this isn't rounding half to even. Reasoned, not checked: the conversion may land just below the half before it rounds.

**A flipped user scale reads negative.** After `ScaleWidth = -1000` on a twips form, ScaleMode read 0, ScaleWidth -999.9999 and ScaleHeight 7200. TabHeight (520 HIMETRIC) read -30.70866, and TabMaxWidth (1000) read -59.05512: the HIMETRIC converted with the negative scale across.

**The reads: 1,100 values, and still no formula for millimetres and centimetres.** For each `h` from 1 to 1100, TabMaxWidth was assigned `h / 100` mm, which keeps `h` HIMETRIC. It was then read in millimetres, centimetres, the 1000 × 100 user scale and points.
- **Points:** #4970's formula, `(float)(h * (float)(1440d / 2540) / 20)`, gives VB's text for all 1,100.
- **Millimetres and centimetres:** no candidate fitted all 1,100. VB's millimetre text sits about 1.1 parts per million above `h / 100`: `527` reads `5.270006`. The twips per millimetre implied by each read lie between 56.692833 and 56.692875, against 1440 / 25.4 = 56.692913. The best constant tried, 56.6928558 in that formula, matched 975 of 1,100.
- **The user scale:** the best of ten orders tried matched 1,063 of 1,100.

So there's still no rule here I'd defend. The data is below, so a later fit needn't take a new probe.

<details><summary>The 1,100 reads: h, then millimetres, centimetres and the user scale, as VB printed them</summary>

```text
1 1.000001E-02 1.000001E-03 5.905512E-02
2 2.000002E-02 2.000002E-03 0.1181102
3 3.000003E-02 3.000003E-03 0.1771654
4 4.000004E-02 4.000004E-03 0.2362205
5 5.000005E-02 5.000005E-03 0.2952756
6 6.000006E-02 6.000006E-03 0.3543307
7 7.000007E-02 7.000007E-03 0.4133858
8 8.000008E-02 8.000009E-03 0.472441
9 9.000009E-02 9.000009E-03 0.531496
10 0.1000001 1.000001E-02 0.5905512
11 0.1100001 1.100001E-02 0.6496063
12 0.1200001 1.200001E-02 0.7086614
13 0.1300001 1.300001E-02 0.7677165
14 0.1400001 1.400001E-02 0.8267717
15 0.1500002 1.500002E-02 0.8858268
16 0.1600002 1.600002E-02 0.9448819
17 0.1700002 1.700002E-02 1.003937
18 0.1800002 1.800002E-02 1.062992
19 0.1900002 1.900002E-02 1.122047
20 0.2000002 2.000002E-02 1.181102
21 0.2100002 2.100002E-02 1.240157
22 0.2200002 2.200002E-02 1.299213
23 0.2300002 2.300002E-02 1.358268
24 0.2400002 2.400002E-02 1.417323
25 0.2500003 2.500003E-02 1.476378
26 0.2600003 2.600003E-02 1.535433
27 0.2700003 2.700003E-02 1.594488
28 0.2800003 2.800003E-02 1.653543
29 0.2900003 2.900003E-02 1.712598
30 0.3000003 3.000003E-02 1.771654
31 0.3100003 3.100003E-02 1.830709
32 0.3200003 3.200004E-02 1.889764
33 0.3300003 3.300003E-02 1.948819
34 0.3400004 3.400004E-02 2.007874
35 0.3500004 3.500004E-02 2.066929
36 0.3600004 3.600004E-02 2.125984
37 0.3700004 3.700004E-02 2.185039
38 0.3800004 3.800004E-02 2.244095
39 0.3900004 3.900004E-02 2.30315
40 0.4000004 4.000004E-02 2.362205
41 0.4100004 4.100004E-02 2.42126
42 0.4200004 4.200004E-02 2.480315
43 0.4300005 4.300005E-02 2.53937
44 0.4400004 4.400004E-02 2.598425
45 0.4500005 4.500005E-02 2.65748
46 0.4600005 4.600005E-02 2.716536
47 0.4700005 4.700005E-02 2.775591
48 0.4800005 4.800005E-02 2.834646
49 0.4900005 4.900005E-02 2.893701
50 0.5000005 5.000005E-02 2.952756
51 0.5100005 5.100005E-02 3.011811
52 0.5200005 5.200005E-02 3.070866
53 0.5300006 5.300006E-02 3.129921
54 0.5400006 5.400006E-02 3.188976
55 0.5500005 5.500006E-02 3.248032
56 0.5600006 5.600006E-02 3.307087
57 0.5700006 5.700006E-02 3.366142
58 0.5800006 5.800006E-02 3.425197
59 0.5900006 5.900006E-02 3.484252
60 0.6000006 6.000006E-02 3.543307
61 0.6100006 6.100006E-02 3.602362
62 0.6200007 6.200007E-02 3.661417
63 0.6300007 6.300007E-02 3.720473
64 0.6400006 6.400007E-02 3.779528
65 0.6500007 6.500006E-02 3.838583
66 0.6600007 6.600007E-02 3.897638
67 0.6700007 6.700007E-02 3.956693
68 0.6800007 6.800007E-02 4.015748
69 0.6900007 6.900007E-02 4.074803
70 0.7000007 7.000007E-02 4.133858
71 0.7100008 7.100008E-02 4.192914
72 0.7200007 7.200007E-02 4.251968
73 0.7300007 7.300007E-02 4.311024
74 0.7400008 7.400008E-02 4.370079
75 0.7500008 7.500008E-02 4.429134
76 0.7600008 7.600008E-02 4.488189
77 0.7700008 7.700008E-02 4.547244
78 0.7800008 7.800008E-02 4.606299
79 0.7900008 7.900009E-02 4.665354
80 0.8000008 8.000008E-02 4.72441
81 0.8100008 8.100008E-02 4.783464
82 0.8200008 8.200008E-02 4.84252
83 0.8300009 8.300009E-02 4.901575
84 0.8400009 8.400009E-02 4.96063
85 0.8500009 8.500009E-02 5.019685
86 0.8600009 8.600009E-02 5.07874
87 0.8700009 8.700009E-02 5.137795
88 0.8800009 8.800009E-02 5.19685
89 0.8900009 8.900009E-02 5.255906
90 0.9000009 9.000009E-02 5.31496
91 0.9100009 0.0910001 5.374016
92 0.920001 0.0920001 5.433071
93 0.930001 0.0930001 5.492126
94 0.940001 0.0940001 5.551181
95 0.950001 0.0950001 5.610236
96 0.960001 0.0960001 5.669291
97 0.970001 0.0970001 5.728346
98 0.980001 0.0980001 5.787402
99 0.990001 0.0990001 5.846457
100 1.000001 0.1000001 5.905512
101 1.010001 0.1010001 5.964567
102 1.020001 0.1020001 6.023622
103 1.030001 0.1030001 6.082677
104 1.040001 0.1040001 6.141732
105 1.050001 0.1050001 6.200788
106 1.060001 0.1060001 6.259842
107 1.070001 0.1070001 6.318898
108 1.080001 0.1080001 6.377953
109 1.090001 0.1090001 6.437008
110 1.100001 0.1100001 6.496063
111 1.110001 0.1110001 6.555118
112 1.120001 0.1120001 6.614173
113 1.130001 0.1130001 6.673228
114 1.140001 0.1140001 6.732284
115 1.150001 0.1150001 6.791338
116 1.160001 0.1160001 6.850394
117 1.170001 0.1170001 6.909449
118 1.180001 0.1180001 6.968504
119 1.190001 0.1190001 7.027559
120 1.200001 0.1200001 7.086614
121 1.210001 0.1210001 7.145669
122 1.220001 0.1220001 7.204724
123 1.230001 0.1230001 7.26378
124 1.240001 0.1240001 7.322834
125 1.250001 0.1250001 7.38189
126 1.260001 0.1260001 7.440945
127 1.270001 0.1270001 7.5
128 1.280001 0.1280001 7.559055
129 1.290001 0.1290001 7.61811
130 1.300001 0.1300001 7.677166
131 1.310001 0.1310001 7.73622
132 1.320001 0.1320001 7.795276
133 1.330001 0.1330001 7.854331
134 1.340001 0.1340001 7.913386
135 1.350001 0.1350001 7.972441
136 1.360001 0.1360001 8.031496
137 1.370001 0.1370001 8.090551
138 1.380001 0.1380001 8.149607
139 1.390001 0.1390001 8.208661
140 1.400001 0.1400001 8.267716
141 1.410002 0.1410002 8.326772
142 1.420002 0.1420002 8.385827
143 1.430001 0.1430002 8.444882
144 1.440001 0.1440001 8.503937
145 1.450001 0.1450001 8.562992
146 1.460001 0.1460001 8.622047
147 1.470002 0.1470001 8.681103
148 1.480002 0.1480002 8.740157
149 1.490002 0.1490002 8.799212
150 1.500002 0.1500002 8.858268
151 1.510002 0.1510002 8.917323
152 1.520002 0.1520002 8.976378
153 1.530002 0.1530002 9.035433
154 1.540002 0.1540002 9.094488
155 1.550002 0.1550002 9.153543
156 1.560002 0.1560002 9.212599
157 1.570002 0.1570002 9.271653
158 1.580002 0.1580002 9.330709
159 1.590002 0.1590002 9.389764
160 1.600002 0.1600002 9.448819
161 1.610002 0.1610002 9.507874
162 1.620002 0.1620002 9.566929
163 1.630002 0.1630002 9.625984
164 1.640002 0.1640002 9.68504
165 1.650002 0.1650002 9.744095
166 1.660002 0.1660002 9.803149
167 1.670002 0.1670002 9.862205
168 1.680002 0.1680002 9.92126
169 1.690002 0.1690002 9.980315
170 1.700002 0.1700002 10.03937
171 1.710002 0.1710002 10.09842
172 1.720002 0.1720002 10.15748
173 1.730002 0.1730002 10.21654
174 1.740002 0.1740002 10.27559
175 1.750002 0.1750002 10.33465
176 1.760002 0.1760002 10.3937
177 1.770002 0.1770002 10.45276
178 1.780002 0.1780002 10.51181
179 1.790002 0.1790002 10.57087
180 1.800002 0.1800002 10.62992
181 1.810002 0.1810002 10.68898
182 1.820002 0.1820002 10.74803
183 1.830002 0.1830002 10.80709
184 1.840002 0.1840002 10.86614
185 1.850002 0.1850002 10.9252
186 1.860002 0.1860002 10.98425
187 1.870002 0.1870002 11.04331
188 1.880002 0.1880002 11.10236
189 1.890002 0.1890002 11.16142
190 1.900002 0.1900002 11.22047
191 1.910002 0.1910002 11.27953
192 1.920002 0.1920002 11.33858
193 1.930002 0.1930002 11.39764
194 1.940002 0.1940002 11.45669
195 1.950002 0.1950002 11.51575
196 1.960002 0.1960002 11.5748
197 1.970002 0.1970002 11.63386
198 1.980002 0.1980002 11.69291
199 1.990002 0.1990002 11.75197
200 2.000002 0.2000002 11.81102
201 2.010002 0.2010002 11.87008
202 2.020002 0.2020002 11.92913
203 2.030002 0.2030002 11.98819
204 2.040002 0.2040002 12.04724
205 2.050002 0.2050002 12.1063
206 2.060002 0.2060002 12.16535
207 2.070002 0.2070002 12.22441
208 2.080002 0.2080002 12.28346
209 2.090002 0.2090002 12.34252
210 2.100002 0.2100002 12.40158
211 2.110002 0.2110002 12.46063
212 2.120002 0.2120002 12.51968
213 2.130002 0.2130002 12.57874
214 2.140002 0.2140002 12.6378
215 2.150002 0.2150002 12.69685
216 2.160002 0.2160002 12.75591
217 2.170002 0.2170002 12.81496
218 2.180002 0.2180002 12.87402
219 2.190002 0.2190002 12.93307
220 2.200002 0.2200002 12.99213
221 2.210002 0.2210002 13.05118
222 2.220002 0.2220002 13.11024
223 2.230002 0.2230002 13.16929
224 2.240002 0.2240002 13.22835
225 2.250002 0.2250002 13.2874
226 2.260002 0.2260002 13.34646
227 2.270002 0.2270002 13.40551
228 2.280002 0.2280002 13.46457
229 2.290002 0.2290002 13.52362
230 2.300002 0.2300002 13.58268
231 2.310002 0.2310002 13.64173
232 2.320002 0.2320002 13.70079
233 2.330002 0.2330002 13.75984
234 2.340003 0.2340003 13.8189
235 2.350003 0.2350002 13.87795
236 2.360003 0.2360002 13.93701
237 2.370003 0.2370002 13.99606
238 2.380002 0.2380002 14.05512
239 2.390002 0.2390002 14.11417
240 2.400002 0.2400002 14.17323
241 2.410002 0.2410002 14.23228
242 2.420002 0.2420003 14.29134
243 2.430002 0.2430003 14.35039
244 2.440002 0.2440003 14.40945
245 2.450002 0.2450003 14.4685
246 2.460003 0.2460003 14.52756
247 2.470003 0.2470003 14.58661
248 2.480003 0.2480003 14.64567
249 2.490003 0.2490003 14.70472
250 2.500003 0.2500003 14.76378
251 2.510003 0.2510003 14.82283
252 2.520003 0.2520003 14.88189
253 2.530003 0.2530003 14.94094
254 2.540003 0.2540003 15
255 2.550003 0.2550003 15.05906
256 2.560003 0.2560003 15.11811
257 2.570003 0.2570003 15.17717
258 2.580003 0.2580003 15.23622
259 2.590003 0.2590003 15.29528
260 2.600003 0.2600003 15.35433
261 2.610003 0.2610003 15.41339
262 2.620003 0.2620003 15.47244
263 2.630003 0.2630003 15.5315
264 2.640003 0.2640003 15.59055
265 2.650003 0.2650003 15.64961
266 2.660003 0.2660003 15.70866
267 2.670003 0.2670003 15.76772
268 2.680003 0.2680003 15.82677
269 2.690003 0.2690003 15.88583
270 2.700003 0.2700003 15.94488
271 2.710003 0.2710003 16.00394
272 2.720003 0.2720003 16.06299
273 2.730003 0.2730003 16.12205
274 2.740003 0.2740003 16.1811
275 2.750003 0.2750003 16.24016
276 2.760003 0.2760003 16.29921
277 2.770003 0.2770003 16.35827
278 2.780003 0.2780003 16.41732
279 2.790003 0.2790003 16.47638
280 2.800003 0.2800003 16.53543
281 2.810003 0.2810003 16.59449
282 2.820003 0.2820003 16.65354
283 2.830003 0.2830003 16.7126
284 2.840003 0.2840003 16.77165
285 2.850003 0.2850003 16.83071
286 2.860003 0.2860003 16.88976
287 2.870003 0.2870003 16.94882
288 2.880003 0.2880003 17.00787
289 2.890003 0.2890003 17.06693
290 2.900003 0.2900003 17.12598
291 2.910003 0.2910003 17.18504
292 2.920003 0.2920003 17.24409
293 2.930003 0.2930003 17.30315
294 2.940003 0.2940003 17.36221
295 2.950003 0.2950003 17.42126
296 2.960003 0.2960003 17.48031
297 2.970003 0.2970003 17.53937
298 2.980003 0.2980003 17.59842
299 2.990003 0.2990003 17.65748
300 3.000003 0.3000003 17.71654
301 3.010003 0.3010003 17.77559
302 3.020003 0.3020003 17.83465
303 3.030003 0.3030003 17.8937
304 3.040003 0.3040003 17.95276
305 3.050003 0.3050003 18.01181
306 3.060003 0.3060003 18.07087
307 3.070003 0.3070003 18.12992
308 3.080003 0.3080003 18.18898
309 3.090003 0.3090003 18.24803
310 3.100003 0.3100003 18.30709
311 3.110003 0.3110003 18.36614
312 3.120003 0.3120003 18.4252
313 3.130003 0.3130003 18.48425
314 3.140003 0.3140003 18.54331
315 3.150003 0.3150003 18.60236
316 3.160003 0.3160003 18.66142
317 3.170003 0.3170003 18.72047
318 3.180003 0.3180003 18.77953
319 3.190003 0.3190003 18.83858
320 3.200003 0.3200003 18.89764
321 3.210003 0.3210003 18.95669
322 3.220003 0.3220003 19.01575
323 3.230003 0.3230003 19.0748
324 3.240003 0.3240003 19.13386
325 3.250003 0.3250003 19.19291
326 3.260003 0.3260003 19.25197
327 3.270003 0.3270003 19.31102
328 3.280003 0.3280003 19.37008
329 3.290004 0.3290004 19.42913
330 3.300004 0.3300003 19.48819
331 3.310004 0.3310004 19.54725
332 3.320004 0.3320003 19.6063
333 3.330003 0.3330003 19.66535
334 3.340003 0.3340003 19.72441
335 3.350003 0.3350003 19.78346
336 3.360003 0.3360004 19.84252
337 3.370003 0.3370003 19.90158
338 3.380003 0.3380004 19.96063
339 3.390003 0.3390003 20.01969
340 3.400003 0.3400004 20.07874
341 3.410004 0.3410003 20.1378
342 3.420004 0.3420004 20.19685
343 3.430004 0.3430004 20.25591
344 3.440004 0.3440004 20.31496
345 3.450004 0.3450004 20.37402
346 3.460004 0.3460004 20.43307
347 3.470004 0.3470004 20.49213
348 3.480004 0.3480003 20.55118
349 3.490004 0.3490004 20.61024
350 3.500004 0.3500004 20.66929
351 3.510004 0.3510004 20.72835
352 3.520004 0.3520004 20.7874
353 3.530004 0.3530004 20.84646
354 3.540004 0.3540004 20.90551
355 3.550004 0.3550004 20.96457
356 3.560004 0.3560004 21.02362
357 3.570004 0.3570004 21.08268
358 3.580004 0.3580004 21.14173
359 3.590004 0.3590004 21.20079
360 3.600004 0.3600004 21.25984
361 3.610004 0.3610004 21.3189
362 3.620004 0.3620004 21.37795
363 3.630004 0.3630004 21.43701
364 3.640004 0.3640004 21.49606
365 3.650004 0.3650004 21.55512
366 3.660004 0.3660004 21.61417
367 3.670004 0.3670004 21.67323
368 3.680004 0.3680004 21.73228
369 3.690004 0.3690004 21.79134
370 3.700004 0.3700004 21.85039
371 3.710004 0.3710004 21.90945
372 3.720004 0.3720004 21.9685
373 3.730004 0.3730004 22.02756
374 3.740004 0.3740004 22.08661
375 3.750004 0.3750004 22.14567
376 3.760004 0.3760004 22.20473
377 3.770004 0.3770004 22.26378
378 3.780004 0.3780004 22.32283
379 3.790004 0.3790004 22.38189
380 3.800004 0.3800004 22.44094
381 3.810004 0.3810004 22.5
382 3.820004 0.3820004 22.55906
383 3.830004 0.3830004 22.61811
384 3.840004 0.3840004 22.67717
385 3.850004 0.3850004 22.73622
386 3.860004 0.3860004 22.79528
387 3.870004 0.3870004 22.85433
388 3.880004 0.3880004 22.91339
389 3.890004 0.3890004 22.97244
390 3.900004 0.3900004 23.0315
391 3.910004 0.3910004 23.09055
392 3.920004 0.3920004 23.14961
393 3.930004 0.3930004 23.20866
394 3.940004 0.3940004 23.26772
395 3.950004 0.3950004 23.32677
396 3.960004 0.3960004 23.38583
397 3.970004 0.3970004 23.44488
398 3.980004 0.3980004 23.50394
399 3.990004 0.3990004 23.56299
400 4.000004 0.4000004 23.62205
401 4.010004 0.4010004 23.6811
402 4.020004 0.4020004 23.74016
403 4.030004 0.4030004 23.79921
404 4.040004 0.4040004 23.85827
405 4.050004 0.4050004 23.91732
406 4.060004 0.4060004 23.97638
407 4.070004 0.4070004 24.03543
408 4.080004 0.4080004 24.09449
409 4.090004 0.4090004 24.15354
410 4.100004 0.4100004 24.2126
411 4.110004 0.4110004 24.27165
412 4.120004 0.4120004 24.33071
413 4.130004 0.4130004 24.38976
414 4.140004 0.4140004 24.44882
415 4.150004 0.4150004 24.50787
416 4.160004 0.4160004 24.56693
417 4.170004 0.4170004 24.62598
418 4.180004 0.4180004 24.68504
419 4.190004 0.4190004 24.74409
420 4.200005 0.4200004 24.80315
421 4.210004 0.4210005 24.86221
422 4.220005 0.4220004 24.92126
423 4.230004 0.4230005 24.98031
424 4.240005 0.4240004 25.03937
425 4.250004 0.4250004 25.09842
426 4.260005 0.4260004 25.15748
427 4.270004 0.4270004 25.21654
428 4.280005 0.4280005 25.27559
429 4.290004 0.4290004 25.33465
430 4.300004 0.4300005 25.3937
431 4.310005 0.4310004 25.45276
432 4.320004 0.4320005 25.51181
433 4.330005 0.4330004 25.57087
434 4.340004 0.4340005 25.62992
435 4.350005 0.4350004 25.68898
436 4.360004 0.4360005 25.74803
437 4.370005 0.4370005 25.80709
438 4.380004 0.4380005 25.86614
439 4.390005 0.4390005 25.9252
440 4.400004 0.4400004 25.98425
441 4.410005 0.4410005 26.04331
442 4.420004 0.4420004 26.10236
443 4.430005 0.4430005 26.16142
444 4.440005 0.4440005 26.22047
445 4.450005 0.4450005 26.27953
446 4.460005 0.4460005 26.33858
447 4.470005 0.4470005 26.39764
448 4.480005 0.4480005 26.45669
449 4.490005 0.4490005 26.51575
450 4.500005 0.4500005 26.5748
451 4.510005 0.4510005 26.63386
452 4.520005 0.4520005 26.69291
453 4.530005 0.4530005 26.75197
454 4.540005 0.4540005 26.81102
455 4.550005 0.4550005 26.87008
456 4.560005 0.4560005 26.92913
457 4.570005 0.4570005 26.98819
458 4.580005 0.4580005 27.04725
459 4.590005 0.4590005 27.1063
460 4.600005 0.4600005 27.16535
461 4.610005 0.4610005 27.22441
462 4.620005 0.4620005 27.28346
463 4.630005 0.4630005 27.34252
464 4.640005 0.4640005 27.40158
465 4.650005 0.4650005 27.46063
466 4.660005 0.4660005 27.51969
467 4.670005 0.4670005 27.57874
468 4.680005 0.4680005 27.6378
469 4.690005 0.4690005 27.69685
470 4.700005 0.4700005 27.75591
471 4.710005 0.4710005 27.81496
472 4.720005 0.4720005 27.87402
473 4.730005 0.4730005 27.93307
474 4.740005 0.4740005 27.99213
475 4.750005 0.4750005 28.05118
476 4.760005 0.4760005 28.11024
477 4.770005 0.4770005 28.16929
478 4.780005 0.4780005 28.22835
479 4.790005 0.4790005 28.2874
480 4.800005 0.4800005 28.34646
481 4.810005 0.4810005 28.40551
482 4.820005 0.4820005 28.46457
483 4.830005 0.4830005 28.52362
484 4.840005 0.4840005 28.58268
485 4.850005 0.4850005 28.64173
486 4.860005 0.4860005 28.70079
487 4.870005 0.4870005 28.75984
488 4.880005 0.4880005 28.8189
489 4.890005 0.4890005 28.87795
490 4.900005 0.4900005 28.93701
491 4.910005 0.4910005 28.99606
492 4.920005 0.4920005 29.05512
493 4.930005 0.4930005 29.11417
494 4.940005 0.4940005 29.17323
495 4.950005 0.4950005 29.23228
496 4.960005 0.4960005 29.29134
497 4.970005 0.4970005 29.35039
498 4.980005 0.4980005 29.40945
499 4.990005 0.4990005 29.4685
500 5.000005 0.5000005 29.52756
501 5.010005 0.5010005 29.58661
502 5.020005 0.5020005 29.64567
503 5.030005 0.5030005 29.70473
504 5.040005 0.5040005 29.76378
505 5.050005 0.5050005 29.82283
506 5.060005 0.5060005 29.88189
507 5.070005 0.5070005 29.94094
508 5.080005 0.5080006 30
509 5.090005 0.5090005 30.05906
510 5.100005 0.5100005 30.11811
511 5.110005 0.5110005 30.17717
512 5.120005 0.5120006 30.23622
513 5.130005 0.5130005 30.29528
514 5.140006 0.5140005 30.35433
515 5.150005 0.5150005 30.41339
516 5.160006 0.5160005 30.47244
517 5.170005 0.5170006 30.5315
518 5.180006 0.5180005 30.59055
519 5.190005 0.5190005 30.64961
520 5.200006 0.5200005 30.70866
521 5.210005 0.5210006 30.76772
522 5.220006 0.5220006 30.82677
523 5.230005 0.5230005 30.88583
524 5.240005 0.5240005 30.94488
525 5.250005 0.5250006 31.00394
526 5.260005 0.5260006 31.06299
527 5.270006 0.5270005 31.12205
528 5.280005 0.5280005 31.1811
529 5.290006 0.5290006 31.24016
530 5.300005 0.5300006 31.29921
531 5.310006 0.5310006 31.35827
532 5.320005 0.5320005 31.41732
533 5.330006 0.5330005 31.47638
534 5.340005 0.5340006 31.53543
535 5.350006 0.5350006 31.59449
536 5.360005 0.5360005 31.65354
537 5.370006 0.5370005 31.7126
538 5.380006 0.5380006 31.77165
539 5.390006 0.5390006 31.83071
540 5.400006 0.5400006 31.88976
541 5.410006 0.5410005 31.94882
542 5.420006 0.5420006 32.00787
543 5.430006 0.5430006 32.06693
544 5.440006 0.5440006 32.12598
545 5.450006 0.5450006 32.18504
546 5.460006 0.5460005 32.24409
547 5.470006 0.5470006 32.30315
548 5.480006 0.5480006 32.36221
549 5.490005 0.5490006 32.42126
550 5.500006 0.5500005 32.48032
551 5.510006 0.5510006 32.53937
552 5.520006 0.5520006 32.59843
553 5.530006 0.5530006 32.65748
554 5.540006 0.5540006 32.71654
555 5.550006 0.5550006 32.77559
556 5.560006 0.5560006 32.83464
557 5.570006 0.5570006 32.8937
558 5.580006 0.5580006 32.95275
559 5.590006 0.5590006 33.01181
560 5.600006 0.5600006 33.07087
561 5.610006 0.5610006 33.12992
562 5.620006 0.5620006 33.18898
563 5.630006 0.5630006 33.24803
564 5.640006 0.5640006 33.30709
565 5.650006 0.5650006 33.36614
566 5.660006 0.5660006 33.4252
567 5.670006 0.5670006 33.48425
568 5.680006 0.5680006 33.54331
569 5.690006 0.5690006 33.60236
570 5.700006 0.5700006 33.66142
571 5.710006 0.5710006 33.72047
572 5.720006 0.5720006 33.77953
573 5.730006 0.5730006 33.83858
574 5.740006 0.5740006 33.89764
575 5.750006 0.5750006 33.95669
576 5.760006 0.5760006 34.01575
577 5.770006 0.5770006 34.0748
578 5.780006 0.5780006 34.13386
579 5.790006 0.5790006 34.19291
580 5.800006 0.5800006 34.25197
581 5.810006 0.5810006 34.31102
582 5.820006 0.5820006 34.37008
583 5.830006 0.5830006 34.42913
584 5.840006 0.5840006 34.48819
585 5.850006 0.5850006 34.54725
586 5.860006 0.5860006 34.6063
587 5.870006 0.5870006 34.66536
588 5.880006 0.5880006 34.72441
589 5.890006 0.5890006 34.78347
590 5.900006 0.5900006 34.84252
591 5.910006 0.5910006 34.90158
592 5.920006 0.5920006 34.96063
593 5.930006 0.5930006 35.01968
594 5.940006 0.5940006 35.07874
595 5.950006 0.5950006 35.13779
596 5.960006 0.5960006 35.19685
597 5.970006 0.5970006 35.25591
598 5.980006 0.5980006 35.31496
599 5.990006 0.5990006 35.37402
600 6.000006 0.6000006 35.43307
601 6.010006 0.6010006 35.49213
602 6.020006 0.6020007 35.55118
603 6.030006 0.6030006 35.61024
604 6.040006 0.6040006 35.66929
605 6.050006 0.6050006 35.72835
606 6.060006 0.6060006 35.7874
607 6.070006 0.6070006 35.84646
608 6.080006 0.6080006 35.90551
609 6.090006 0.6090006 35.96457
610 6.100007 0.6100006 36.02362
611 6.110006 0.6110007 36.08268
612 6.120007 0.6120006 36.14173
613 6.130006 0.6130006 36.20079
614 6.140007 0.6140006 36.25984
615 6.150006 0.6150007 36.3189
616 6.160007 0.6160007 36.37795
617 6.170006 0.6170006 36.43701
618 6.180007 0.6180006 36.49606
619 6.190006 0.6190007 36.55512
620 6.200006 0.6200007 36.61417
621 6.210006 0.6210006 36.67323
622 6.220006 0.6220006 36.73228
623 6.230007 0.6230006 36.79134
624 6.240006 0.6240007 36.8504
625 6.250007 0.6250007 36.90945
626 6.260006 0.6260006 36.96851
627 6.270007 0.6270006 37.02756
628 6.280006 0.6280007 37.08661
629 6.290007 0.6290007 37.14567
630 6.300006 0.6300007 37.20472
631 6.310007 0.6310006 37.26378
632 6.320006 0.6320007 37.32283
633 6.330007 0.6330007 37.38189
634 6.340007 0.6340007 37.44094
635 6.350007 0.6350006 37.5
636 6.360007 0.6360006 37.55906
637 6.370007 0.6370007 37.61811
638 6.380007 0.6380007 37.67717
639 6.390007 0.6390007 37.73622
640 6.400007 0.6400006 37.79528
641 6.410007 0.6410007 37.85433
642 6.420007 0.6420007 37.91339
643 6.430007 0.6430007 37.97244
644 6.440007 0.6440006 38.0315
645 6.450006 0.6450007 38.09055
646 6.460007 0.6460007 38.1496
647 6.470007 0.6470007 38.20866
648 6.480007 0.6480007 38.26772
649 6.490007 0.6490007 38.32677
650 6.500007 0.6500007 38.38583
651 6.510007 0.6510007 38.44488
652 6.520007 0.6520007 38.50394
653 6.530007 0.6530007 38.56299
654 6.540007 0.6540007 38.62205
655 6.550007 0.6550007 38.6811
656 6.560007 0.6560007 38.74016
657 6.570007 0.6570007 38.79921
658 6.580007 0.6580007 38.85827
659 6.590007 0.6590007 38.91732
660 6.600007 0.6600007 38.97638
661 6.610007 0.6610007 39.03543
662 6.620007 0.6620007 39.09449
663 6.630007 0.6630007 39.15355
664 6.640007 0.6640007 39.2126
665 6.650007 0.6650007 39.27165
666 6.660007 0.6660007 39.33071
667 6.670007 0.6670007 39.38976
668 6.680007 0.6680007 39.44882
669 6.690007 0.6690007 39.50787
670 6.700007 0.6700007 39.56693
671 6.710007 0.6710007 39.62598
672 6.720007 0.6720007 39.68504
673 6.730007 0.6730007 39.74409
674 6.740007 0.6740007 39.80315
675 6.750007 0.6750007 39.86221
676 6.760007 0.6760007 39.92126
677 6.770007 0.6770007 39.98032
678 6.780007 0.6780007 40.03937
679 6.790007 0.6790007 40.09843
680 6.800007 0.6800007 40.15748
681 6.810007 0.6810007 40.21654
682 6.820007 0.6820007 40.27559
683 6.830007 0.6830007 40.33464
684 6.840007 0.6840007 40.3937
685 6.850007 0.6850007 40.45275
686 6.860007 0.6860007 40.51181
687 6.870007 0.6870007 40.57087
688 6.880007 0.6880007 40.62992
689 6.890007 0.6890007 40.68898
690 6.900007 0.6900007 40.74803
691 6.910007 0.6910007 40.80709
692 6.920007 0.6920007 40.86614
693 6.930007 0.6930007 40.9252
694 6.940007 0.6940007 40.98425
695 6.950007 0.6950007 41.04331
696 6.960007 0.6960007 41.10236
697 6.970007 0.6970007 41.16142
698 6.980007 0.6980007 41.22047
699 6.990007 0.6990007 41.27953
700 7.000007 0.7000007 41.33858
701 7.010007 0.7010008 41.39764
702 7.020007 0.7020007 41.45669
703 7.030007 0.7030007 41.51575
704 7.040007 0.7040007 41.5748
705 7.050007 0.7050008 41.63386
706 7.060008 0.7060007 41.69291
707 7.070007 0.7070007 41.75197
708 7.080008 0.7080007 41.81102
709 7.090007 0.7090008 41.87008
710 7.100008 0.7100008 41.92913
711 7.110007 0.7110007 41.98819
712 7.120008 0.7120007 42.04725
713 7.130007 0.7130007 42.1063
714 7.140007 0.7140008 42.16536
715 7.150007 0.7150007 42.22441
716 7.160007 0.7160007 42.28347
717 7.170007 0.7170007 42.34252
718 7.180007 0.7180008 42.40158
719 7.190008 0.7190008 42.46063
720 7.200007 0.7200007 42.51968
721 7.210008 0.7210007 42.57874
722 7.220007 0.7220008 42.63779
723 7.230008 0.7230008 42.69685
724 7.240007 0.7240008 42.75591
725 7.250008 0.7250007 42.81496
726 7.260007 0.7260007 42.87402
727 7.270008 0.7270008 42.93307
728 7.280007 0.7280008 42.99213
729 7.290008 0.7290007 43.05118
730 7.300008 0.7300007 43.11024
731 7.310008 0.7310008 43.16929
732 7.320008 0.7320008 43.22835
733 7.330008 0.7330008 43.2874
734 7.340008 0.7340007 43.34646
735 7.350008 0.7350008 43.40551
736 7.360008 0.7360008 43.46457
737 7.370008 0.7370008 43.52362
738 7.380008 0.7380008 43.58268
739 7.390007 0.7390008 43.64173
740 7.400008 0.7400008 43.70079
741 7.410007 0.7410008 43.75984
742 7.420008 0.7420008 43.8189
743 7.430008 0.7430007 43.87795
744 7.440008 0.7440008 43.93701
745 7.450008 0.7450008 43.99606
746 7.460008 0.7460008 44.05512
747 7.470008 0.7470008 44.11417
748 7.480008 0.7480008 44.17323
749 7.490008 0.7490008 44.23228
750 7.500008 0.7500008 44.29134
751 7.510008 0.7510008 44.3504
752 7.520008 0.7520008 44.40945
753 7.530008 0.7530008 44.46851
754 7.540008 0.7540008 44.52756
755 7.550008 0.7550008 44.58661
756 7.560008 0.7560008 44.64567
757 7.570008 0.7570008 44.70472
758 7.580008 0.7580008 44.76378
759 7.590008 0.7590008 44.82283
760 7.600008 0.7600008 44.88189
761 7.610008 0.7610008 44.94094
762 7.620008 0.7620008 45
763 7.630008 0.7630008 45.05906
764 7.640008 0.7640008 45.11811
765 7.650008 0.7650008 45.17717
766 7.660008 0.7660008 45.23622
767 7.670008 0.7670008 45.29528
768 7.680008 0.7680008 45.35433
769 7.690008 0.7690008 45.41339
770 7.700008 0.7700008 45.47244
771 7.710008 0.7710008 45.5315
772 7.720008 0.7720008 45.59055
773 7.730008 0.7730008 45.64961
774 7.740008 0.7740008 45.70866
775 7.750008 0.7750008 45.76772
776 7.760008 0.7760008 45.82677
777 7.770008 0.7770008 45.88583
778 7.780008 0.7780008 45.94488
779 7.790008 0.7790008 46.00394
780 7.800008 0.7800008 46.06299
781 7.810008 0.7810008 46.12205
782 7.820008 0.7820008 46.1811
783 7.830008 0.7830008 46.24016
784 7.840008 0.7840008 46.29921
785 7.850008 0.7850008 46.35827
786 7.860008 0.7860008 46.41732
787 7.870008 0.7870008 46.47638
788 7.880008 0.7880008 46.53543
789 7.890008 0.7890008 46.59449
790 7.900008 0.7900008 46.65355
791 7.910008 0.7910008 46.7126
792 7.920008 0.7920008 46.77165
793 7.930008 0.7930008 46.83071
794 7.940008 0.7940008 46.88976
795 7.950008 0.7950009 46.94882
796 7.960008 0.7960008 47.00787
797 7.970008 0.7970008 47.06693
798 7.980008 0.7980008 47.12598
799 7.990008 0.7990009 47.18504
800 8.000009 0.8000008 47.24409
801 8.010009 0.8010008 47.30315
802 8.020008 0.8020008 47.36221
803 8.030008 0.8030008 47.42126
804 8.040009 0.8040009 47.48032
805 8.050009 0.8050008 47.53937
806 8.060008 0.8060008 47.59843
807 8.070008 0.8070008 47.65748
808 8.080009 0.8080009 47.71654
809 8.090009 0.8090008 47.77559
810 8.100008 0.8100008 47.83464
811 8.110008 0.8110008 47.8937
812 8.120008 0.8120009 47.95275
813 8.130009 0.8130009 48.01181
814 8.140009 0.8140008 48.07087
815 8.150008 0.8150008 48.12992
816 8.160008 0.8160009 48.18898
817 8.170009 0.8170009 48.24803
818 8.180009 0.8180009 48.30709
819 8.190008 0.8190008 48.36614
820 8.200008 0.8200008 48.4252
821 8.210009 0.8210009 48.48425
822 8.220009 0.8220009 48.54331
823 8.230008 0.8230008 48.60236
824 8.240008 0.8240008 48.66142
825 8.250009 0.8250009 48.72047
826 8.260009 0.8260009 48.77953
827 8.270009 0.8270009 48.83858
828 8.280008 0.8280008 48.89764
829 8.290009 0.8290009 48.95669
830 8.300009 0.8300009 49.01575
831 8.310009 0.8310009 49.0748
832 8.320008 0.8320009 49.13386
833 8.330009 0.8330008 49.19291
834 8.340009 0.8340009 49.25197
835 8.350009 0.8350009 49.31102
836 8.360008 0.8360009 49.37008
837 8.370008 0.8370008 49.42913
838 8.380009 0.8380009 49.48819
839 8.390009 0.8390009 49.54725
840 8.400009 0.8400009 49.6063
841 8.410008 0.8410009 49.66536
842 8.420009 0.8420009 49.72441
843 8.430009 0.8430009 49.78347
844 8.440009 0.8440009 49.84252
845 8.450008 0.8450009 49.90158
846 8.460009 0.8460009 49.96063
847 8.470009 0.8470009 50.01968
848 8.480009 0.8480009 50.07874
849 8.490009 0.8490009 50.13779
850 8.500009 0.8500009 50.19685
851 8.510009 0.8510009 50.25591
852 8.520009 0.8520009 50.31496
853 8.530009 0.8530009 50.37402
854 8.540009 0.8540009 50.43307
855 8.550009 0.8550009 50.49213
856 8.560009 0.8560009 50.55118
857 8.570009 0.8570009 50.61024
858 8.580009 0.8580009 50.66929
859 8.590009 0.8590009 50.72835
860 8.600009 0.8600009 50.7874
861 8.610009 0.8610009 50.84646
862 8.620009 0.8620009 50.90551
863 8.630009 0.8630009 50.96457
864 8.640009 0.8640009 51.02362
865 8.650009 0.8650009 51.08268
866 8.660009 0.8660009 51.14173
867 8.670009 0.8670009 51.20079
868 8.680009 0.8680009 51.25984
869 8.690009 0.8690009 51.3189
870 8.700009 0.8700009 51.37795
871 8.710009 0.8710009 51.43701
872 8.720009 0.8720009 51.49606
873 8.730009 0.8730009 51.55512
874 8.740009 0.8740009 51.61417
875 8.75001 0.8750009 51.67323
876 8.760009 0.8760009 51.73228
877 8.770009 0.8770009 51.79134
878 8.780009 0.8780009 51.8504
879 8.790009 0.8790009 51.90945
880 8.800009 0.8800009 51.96851
881 8.810009 0.8810009 52.02756
882 8.820009 0.8820009 52.08662
883 8.830009 0.8830009 52.14567
884 8.840009 0.8840009 52.20472
885 8.850009 0.8850009 52.26378
886 8.860009 0.8860009 52.32283
887 8.870009 0.8870009 52.38189
888 8.88001 0.8880009 52.44094
889 8.890009 0.889001 52.5
890 8.900009 0.8900009 52.55906
891 8.910009 0.8910009 52.61811
892 8.92001 0.8920009 52.67717
893 8.930009 0.8930009 52.73622
894 8.940009 0.8940009 52.79528
895 8.950009 0.8950009 52.85433
896 8.96001 0.8960009 52.91339
897 8.97001 0.8970009 52.97244
898 8.980009 0.898001 53.0315
899 8.990009 0.8990009 53.09055
900 9.00001 0.9000009 53.14961
901 9.01001 0.9010009 53.20866
902 9.020009 0.902001 53.26772
903 9.030009 0.903001 53.32677
904 9.040009 0.9040009 53.38583
905 9.05001 0.9050009 53.44488
906 9.060009 0.906001 53.50394
907 9.070009 0.907001 53.56299
908 9.080009 0.9080009 53.62205
909 9.09001 0.9090009 53.6811
910 9.10001 0.9100009 53.74016
911 9.110009 0.911001 53.79921
912 9.120009 0.912001 53.85827
913 9.13001 0.9130009 53.91732
914 9.14001 0.9140009 53.97638
915 9.150009 0.915001 54.03543
916 9.160009 0.916001 54.09449
917 9.17001 0.9170009 54.15355
918 9.18001 0.9180009 54.2126
919 9.190009 0.919001 54.27165
920 9.200009 0.920001 54.33071
921 9.21001 0.921001 54.38976
922 9.22001 0.9220009 54.44882
923 9.23001 0.9230009 54.50787
924 9.240009 0.924001 54.56693
925 9.25001 0.925001 54.62598
926 9.26001 0.926001 54.68504
927 9.27001 0.9270009 54.74409
928 9.280009 0.928001 54.80315
929 9.290009 0.929001 54.86221
930 9.30001 0.930001 54.92126
931 9.31001 0.9310009 54.98032
932 9.320009 0.932001 55.03937
933 9.330009 0.933001 55.09843
934 9.34001 0.934001 55.15748
935 9.35001 0.935001 55.21654
936 9.36001 0.936001 55.27559
937 9.370009 0.937001 55.33464
938 9.38001 0.938001 55.3937
939 9.39001 0.939001 55.45275
940 9.40001 0.940001 55.51181
941 9.410009 0.941001 55.57087
942 9.42001 0.942001 55.62992
943 9.43001 0.943001 55.68898
944 9.44001 0.944001 55.74803
945 9.45001 0.945001 55.80709
946 9.46001 0.946001 55.86614
947 9.47001 0.947001 55.9252
948 9.48001 0.948001 55.98425
949 9.49001 0.949001 56.04331
950 9.50001 0.950001 56.10236
951 9.51001 0.951001 56.16142
952 9.52001 0.952001 56.22047
953 9.53001 0.953001 56.27953
954 9.540009 0.954001 56.33858
955 9.55001 0.955001 56.39764
956 9.56001 0.956001 56.45669
957 9.57001 0.957001 56.51575
958 9.58001 0.958001 56.5748
959 9.59001 0.959001 56.63386
960 9.60001 0.960001 56.69291
961 9.61001 0.961001 56.75197
962 9.62001 0.962001 56.81102
963 9.63001 0.963001 56.87008
964 9.64001 0.964001 56.92913
965 9.65001 0.965001 56.98819
966 9.66001 0.966001 57.04725
967 9.67001 0.967001 57.1063
968 9.68001 0.968001 57.16536
969 9.69001 0.969001 57.22441
970 9.70001 0.970001 57.28347
971 9.710011 0.971001 57.34252
972 9.72001 0.972001 57.40158
973 9.73001 0.973001 57.46063
974 9.74001 0.974001 57.51968
975 9.75001 0.975001 57.57874
976 9.76001 0.976001 57.63779
977 9.77001 0.977001 57.69685
978 9.78001 0.978001 57.75591
979 9.79001 0.979001 57.81496
980 9.80001 0.980001 57.87402
981 9.81001 0.981001 57.93307
982 9.82001 0.982001 57.99213
983 9.83001 0.983001 58.05118
984 9.840011 0.984001 58.11024
985 9.85001 0.985001 58.16929
986 9.86001 0.986001 58.22835
987 9.87001 0.987001 58.2874
988 9.880011 0.988001 58.34646
989 9.89001 0.989001 58.40551
990 9.90001 0.990001 58.46457
991 9.91001 0.991001 58.52362
992 9.920011 0.9920011 58.58268
993 9.930011 0.993001 58.64173
994 9.94001 0.994001 58.70079
995 9.95001 0.995001 58.75984
996 9.960011 0.9960011 58.8189
997 9.970011 0.9970011 58.87795
998 9.98001 0.998001 58.93701
999 9.99001 0.999001 58.99606
1000 10.00001 1.000001 59.05512
1001 10.01001 1.001001 59.11417
1002 10.02001 1.002001 59.17323
1003 10.03001 1.003001 59.23228
1004 10.04001 1.004001 59.29134
1005 10.05001 1.005001 59.3504
1006 10.06001 1.006001 59.40945
1007 10.07001 1.007001 59.46851
1008 10.08001 1.008001 59.52756
1009 10.09001 1.009001 59.58662
1010 10.10001 1.010001 59.64567
1011 10.11001 1.011001 59.70472
1012 10.12001 1.012001 59.76378
1013 10.13001 1.013001 59.82283
1014 10.14001 1.014001 59.88189
1015 10.15001 1.015001 59.94094
1016 10.16001 1.016001 60
1017 10.17001 1.017001 60.05906
1018 10.18001 1.018001 60.11811
1019 10.19001 1.019001 60.17717
1020 10.20001 1.020001 60.23622
1021 10.21001 1.021001 60.29528
1022 10.22001 1.022001 60.35433
1023 10.23001 1.023001 60.41339
1024 10.24001 1.024001 60.47244
1025 10.25001 1.025001 60.5315
1026 10.26001 1.026001 60.59055
1027 10.27001 1.027001 60.64961
1028 10.28001 1.028001 60.70866
1029 10.29001 1.029001 60.76772
1030 10.30001 1.030001 60.82677
1031 10.31001 1.031001 60.88583
1032 10.32001 1.032001 60.94488
1033 10.33001 1.033001 61.00394
1034 10.34001 1.034001 61.06299
1035 10.35001 1.035001 61.12205
1036 10.36001 1.036001 61.1811
1037 10.37001 1.037001 61.24016
1038 10.38001 1.038001 61.29921
1039 10.39001 1.039001 61.35827
1040 10.40001 1.040001 61.41732
1041 10.41001 1.041001 61.47638
1042 10.42001 1.042001 61.53543
1043 10.43001 1.043001 61.59449
1044 10.44001 1.044001 61.65355
1045 10.45001 1.045001 61.7126
1046 10.46001 1.046001 61.77165
1047 10.47001 1.047001 61.83071
1048 10.48001 1.048001 61.88976
1049 10.49001 1.049001 61.94882
1050 10.50001 1.050001 62.00787
1051 10.51001 1.051001 62.06693
1052 10.52001 1.052001 62.12598
1053 10.53001 1.053001 62.18504
1054 10.54001 1.054001 62.24409
1055 10.55001 1.055001 62.30315
1056 10.56001 1.056001 62.36221
1057 10.57001 1.057001 62.42126
1058 10.58001 1.058001 62.48032
1059 10.59001 1.059001 62.53937
1060 10.60001 1.060001 62.59843
1061 10.61001 1.061001 62.65748
1062 10.62001 1.062001 62.71654
1063 10.63001 1.063001 62.77559
1064 10.64001 1.064001 62.83465
1065 10.65001 1.065001 62.8937
1066 10.66001 1.066001 62.95275
1067 10.67001 1.067001 63.01181
1068 10.68001 1.068001 63.07087
1069 10.69001 1.069001 63.12992
1070 10.70001 1.070001 63.18898
1071 10.71001 1.071001 63.24803
1072 10.72001 1.072001 63.30709
1073 10.73001 1.073001 63.36614
1074 10.74001 1.074001 63.4252
1075 10.75001 1.075001 63.48425
1076 10.76001 1.076001 63.54331
1077 10.77001 1.077001 63.60236
1078 10.78001 1.078001 63.66142
1079 10.79001 1.079001 63.72047
1080 10.80001 1.080001 63.77953
1081 10.81001 1.081001 63.83858
1082 10.82001 1.082001 63.89764
1083 10.83001 1.083001 63.95669
1084 10.84001 1.084001 64.01575
1085 10.85001 1.085001 64.07481
1086 10.86001 1.086001 64.13386
1087 10.87001 1.087001 64.19292
1088 10.88001 1.088001 64.25197
1089 10.89001 1.089001 64.31103
1090 10.90001 1.090001 64.37008
1091 10.91001 1.091001 64.42914
1092 10.92001 1.092001 64.48819
1093 10.93001 1.093001 64.54724
1094 10.94001 1.094001 64.6063
1095 10.95001 1.095001 64.66535
1096 10.96001 1.096001 64.72441
1097 10.97001 1.097001 64.78346
1098 10.98001 1.098001 64.84252
1099 10.99001 1.099001 64.90157
1100 11.00001 1.100001 64.96063
```

</details>

**Not measured:** a display other than 15 twips a pixel; a TabHeight that doesn't fit the control.
