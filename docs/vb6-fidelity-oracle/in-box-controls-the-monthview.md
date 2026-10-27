## In-box controls: the MonthView (2026-10-27, #1095)

**The probes:**
- **The software:** VB6 SP6 (6.00.9782) with MSCOMCT2.OCX 6.01.9782, on Windows 10 at 96 dpi, in the en-GB user locale.
  - The form line is `Object = "{86CF1D34-0C5F-11D2-A9FC-0000F8754DA1}#2.0#0"; "MSCOMCT2.OCX"`.
  - The block is `Begin MSComCtl2.MonthView`, `_Version` 393216.
- **The form:** Text1, then MonthView1 at 1500, 120, 2700 × 2370, a hand-written block with only its position, extents and
  `_Version`.
- **The programs:** each ran in a Sub Main and logged every value with its type.
  - Every error was read straight after its statement.
  - Each scenario ran on a fresh instance of the form, loaded but not shown.
- **The builds:** `/make` as native code and as p-code, both run on the guest's console. They logged the same lines,
  apart from the clock times VisibleDays carries (below).

### The object model

From MSCOMCT2.OCX's type library, `IMonthView` has:
- **The date:** Value, and its parts Year, Month, Day, Week and DayOfWeek.
- **The selection:** MultiSelect, MaxSelCount, SelStart and SelEnd.
- **The range:** MinDate and MaxDate.
- **The layout:** MonthRows, MonthColumns, StartOfWeek, ShowToday, ShowWeekNumbers and ScrollRate.
- **The colours:** BackColor, ForeColor, MonthBackColor, TitleBackColor, TitleForeColor and TrailingForeColor.
- **The rest:** Appearance, BorderStyle, Enabled, Font, MouseIcon, MousePointer, OLEDropMode and hWnd.
- **Indexed properties:** `DayBold(dateIndex As Date)` and `VisibleDays(sIndex As Integer)`.
- **Methods:** `ComputeControlSize(Rows, Columns, Width, Height)`, `HitTest(x, y, Date)`, OLEDrag and Refresh.
- **Hidden:** `_Value` and AboutBox.

Its events are:
- DateClick(DateClicked) and DateDblClick(DateDblClicked);
- **GetDayBold**(StartDate, Count, State() As Boolean);
- **SelChange**(StartDate, EndDate, Cancel);
- Click, DblClick, the key events, the mouse events in pixels, and the OLE events.

### Defaults, from a block with no properties

| Read | Logged |
|---|---|
| Value | **today, with no time**, a Date |
| Year, Day, Week | Integers: 2026, 27, 43 on 2026-10-27 |
| Month, DayOfWeek | **Longs**: 10, and 3 on a Tuesday. **Sunday is 1** |
| SelStart, SelEnd | Value |
| MultiSelect | False |
| MaxSelCount | **7** |
| MinDate, MaxDate | **1753-01-01** and 9999-12-31. The DTPicker's MinDate is 1601-01-01 |
| MonthRows, MonthColumns | 1 |
| StartOfWeek | **1, Sunday**, a Long, though the en-GB locale starts its week on Monday |
| ShowToday, ShowWeekNumbers | True, False |
| ScrollRate | **0** |
| Appearance, BorderStyle | 1, 0 |
| Enabled | True |
| MousePointer, OLEDropMode | 0 |
| Font | MS Sans Serif |
| `v = MonthView1` | Value: **Value is the default member** |
| `_Value` | Value |
| Index, on a control that isn't in an array | 343 |

**The six colours read back blank through `CallByName`,** as the DTPicker's did: VarType 19, which VB has no type for.

**VisibleDays(1) to VisibleDays(42) are the 42 days shown, and each carries the time of day it was read.** VisibleDays(1)
was 2026-09-27 11:22:52, a Sunday, and VisibleDays(42) was 2026-11-07 at the same time. VisibleDays(0) and (43) raise 380.
**The clock time resisted explanation.** It is why the native and p-code logs differed, and only there.

### Value

| Assigned | Value read |
|---|---|
| `#2/3/2001#` | 2001-02-03 00:00:00 |
| `#2/3/2001 4:05:06 AM#` | **2001-02-03 04:05:06: the time is kept**, but SelStart and SelEnd read 2001-02-03 00:00:00 |
| `"2002-03-04"`, `"13/1/2001"` | 2002-03-04; **2001-01-13**, by VB's own String-to-Date conversion |
| `37000`, `37000.75` | 2001-04-19 00:00:00, and 18:00:00 |
| `True` | 1899-12-29 |
| `#10:20:30 AM#` | 1899-12-30 10:20:30 |
| `Empty` | **1899-12-30 00:00:00, no error** |
| `"x"`, `"2001-02-30"`, **`Null`** | **13**, "Type mismatch". The DTPicker raises 380 and 35787 for these |
| `#1/1/1753#`, `#12/31/9999#` | taken |
| `#12/31/1752#`, `#1/1/1601#` | **35773**, "A date was specified that does not fall within the MinDate and MaxDate properties." |

**No event is raised by Value from code, except GetDayBold.** That is raised only when the month shown changes.
- **The arguments:** StartDate is the first day shown, such as 2001-01-28 for February 2001, and Count is 42.
- **The array:** `State` came with bounds **0 to 587**, 588 elements for 42 days. **This resisted explanation.**

### MinDate and MaxDate

With Value 2001-06-15:
- MinDate 2001-06-01 is taken, and Value stays put.
- **MinDate 2001-07-01 pulls Value up to 2001-07-01.**
- MinDate `#6/15/2001 3:00:00 PM#` is taken with its time.
- **MaxDate 2001-06-01 pulls Value down to it.**
- MaxDate below MinDate raises **35774**, "A value was specified for the MaxDate property that is lower than the current
  value of MinDate."

### MultiSelect, MaxSelCount, SelStart and SelEnd

| Step, from Value 2001-06-15 | Result |
|---|---|
| MultiSelect False: `SelEnd = 06-18` | **35785**, "Can't set SelEnd property when MultiSelect = False." |
| MultiSelect False: `SelStart = 06-10` | **35786**, "Can't set SelStart property when MultiSelect = False." |
| MultiSelect True, then `SelEnd = 06-18` | 06-15 to 06-18, with Value still 06-15 |
| `SelStart = 06-12` | 06-12 to 06-18, and **Value follows SelStart, to 06-12** |
| `SelEnd = 06-30`: 19 days, with MaxSelCount 7 | **35770**, "An invalid date range was specified.", and nothing changes |
| `SelStart = 06-20`, after SelEnd | no error. **The selection collapses to 06-20 alone**, and Value is 06-20 |
| `Value = 06-25` | the selection collapses to 06-25 |
| MaxSelCount 1, 31, 32767, 3 | taken |
| MaxSelCount 0, -1 | 380 |

No SelChange came from any of these.

### MonthRows, MonthColumns and the size

**The control sizes itself to its months:**
- One month is 2700 × 2370.
- Each extra column adds 2730 twips of width: 5430, 8160, then 10890.
- Each extra row adds 2145 of height: 4515, then 6660.
- **ShowWeekNumbers** adds 375 twips of width. **ShowToday False** changed neither dimension.

**The errors:**
- A count outside 1 to 12 raises **35776**, "A value was specified for MonthRows or MonthColumns that is not between 1 and
  12."
- Rows × columns above 12 raises **35777**, "… that would cause the total number of months (i.e. MonthRows * MonthCols) to
  be greater than 12."
- 3 × 4 is allowed.

**`ComputeControlSize` returns fractional twips:** 2700.284 × 2369.764 for 1 × 1, and 8159.812 × 4515.024 for 2 × 3. It
raises the same two errors.

**Other sizing:**
- `Width = 500` and `Height = 6000` from code were taken as given, without snapping. MonthRows stayed 1.
- With 3 × 4 months, VisibleDays(1) was 2025-12-28. **This was first read as "the twelve months shown ended with the
  current one", and that was wrong.** 2025-12-28 is the Sunday before 1 January 2026, so the months shown were January
  to December 2026. A later probe found the same for 2001, 2004 and 2009, under "Week, VisibleDays and the edges".

### StartOfWeek, ShowToday, ShowWeekNumbers and ScrollRate

- **StartOfWeek** takes 1 to 7, and 0 or 8 raise 380. It moves VisibleDays(1): Sunday 09-27 at 1, Monday 09-28 at 2,
  Saturday 09-26 at 7.
- **ScrollRate** takes 0, 1, 3 and 100. Only -1 raised 380.

### The parts: Day, Month, Year, Week and DayOfWeek

- **A part that would make an invalid date raises 380 and changes nothing.** That covers Month = 2 on 31 January, Day =
  29, 31 or 0 in February 2001, and Year = 2001 on 2000-02-29. Nothing is clamped.
- **DayOfWeek moves Value within its Sunday-to-Saturday week.** On Friday 2001-06-15, DayOfWeek 1 gave Sunday 06-10, 7
  gave Saturday 06-16, and 2 gave Monday 06-11. 0 and 8 raise 380.
- **Week moves Value to the same weekday in that week.** On 2001-06-15:
  - Week 1 gave 2001-01-05, and Week 30 gave 2001-07-27.
  - **Week 53 gave 2002-01-04, in the next year's week 1.**
  - 54 and 0 raise 380.
- **The week numbers:**

  | Date | Week | DayOfWeek |
  |---|---|---|
  | 2001-01-01, a Monday | 1 | 2 |
  | 2001-12-31, a Monday | **1** | 2 |
  | 2004-12-31 | **52** | 6 |
  | 2005-01-01 | 52 | 7 |

  ISO numbering would put 2004-12-31 in week 53. **What rule this follows resisted explanation here.** It was
  explained later: see "Week, VisibleDays and the edges" below.

### DayBold, VisibleDays and HitTest

- **DayBold, for a day shown:**
  - `DayBold(#6/20/2001#) = True` with June shown reads back True; its neighbour reads False.
  - **Once the month shown changes, the bold is gone.** Back on June, DayBold(06-20) read False.
- **DayBold, for a day not shown:** setting it for 2001-12-25, with June shown, raised no error. Reading it back made the
  logging statement fail, so the read raised an error whose number wasn't caught. After Value moved to December it read
  False.
- **VisibleDays(1) = …** raises **383**, "Property is read-only".
- **HitTest takes its point in the container's scale, twips here, as MouseDown reports one.** That was found in group 2,
  where MouseDown at 1140, 1575 hit-tested as 2001-06-20.
  - The points tried here were small numbers meant as pixels: 5,5, 80,80, 80,20 and 5,160. As twips they all lie in
    the title, and every one returned **7**, the title's background, with Date unchanged at 1899-12-30.
  - 9999,9999 lies outside the control and returned 6.
  - Read that way, the four 7s were right, not a hidden-form artefact.

### Week, VisibleDays and the edges (2026-10-27)

**The probe:** code only, in a shown form, with MonthView1 from a hand-written minimal block. Native and p-code logged
the same lines, the time of day VisibleDays carries apart.

**Week reads.** Value was set to every day from 25 December to 7 January, for each year end from 2000 to 2012, under
StartOfWeek 1 and 2. Spot checks covered StartOfWeek 3, 4 and 7.

**The rule: weeks start on StartOfWeek, and week 1 is the week holding 4 January,** the first week with at least four
days in the new year. Days before it belong to the old year's last week, 52 or 53. It is ISO 8601's rule with the week
starting on StartOfWeek rather than always on Monday, and it fits every day logged.

| Days | StartOfWeek 1, Sunday | StartOfWeek 2, Monday |
|---|---|---|
| 2000-12-31, a Sunday | **1**: its week holds 1 to 6 January | 52 |
| 2001-12-30, 2001-12-31 | 1, 1 | 52, **1** |
| **2003-12-28 to 2004-01-03** | **53**: the week has only 3 days of 2004. 2004-01-04 is 1 | 2003-12-29 on: 1 |
| 2004-12-26 to 2005-01-01 | 52 | 2004-12-27 to 2005-01-02: **53**, ISO's week |
| 2008-12-28 to 2009-01-03 | **53** | 2008-12-29 on: 1 |
| 2009-12-27 to 2010-01-02 | 52 | 2009-12-28 to 2010-01-03: **53** |

- **The rows the entry recorded earlier fit it.** 2001-12-31 is 1, and 2004-12-31 and 2005-01-01 are 52, all under
  StartOfWeek 1.
- **Other StartOfWeek values follow the same rule:**

  | StartOfWeek | 2004-12-31 | 2005-01-01 | 2001-12-30 and 2001-12-31 |
  |---|---|---|---|
  | 3, Tuesday | 53 | 53 | 52 |
  | 4, Wednesday | **1**: the week holds 1 to 4 January | 1 | 52 |
  | 7, Saturday | 52 | 1 | 1 |
- Week is an Integer: 24 for 2001-06-15.

**Week writes move Value to the same weekday in week n of the week-year Value is in,** counted by the same rule:

| From | Week = 1 | 2 | 26 | 51 | 52 | 53 |
|---|---|---|---|---|---|---|
| 2001-06-15, a Friday | 2001-01-05 | 01-12 | 06-29 | 12-21 | 12-28 | **2002-01-04, reading Week 1** |
| **2005-01-01, a Saturday in 2004's week 52** | **2004-01-10** | 2004-01-17 | 2004-07-03 | 2004-12-25 | 2005-01-01 | 2005-01-08, reading 1 |
| 2004-12-31, a Friday | 2004-01-09 | 01-16 | 07-02 | 12-24 | 12-31 | 2005-01-07, reading 1 |
| 2004-06-15, a Tuesday | 2004-01-06 | 01-13 | 06-29 | 12-21 | 12-28 | 2005-01-04, reading 1 |

- **The year is the week-year, not Value's calendar year.** From 2005-01-01, Week = 1 went to January 2004.
- **Week = 53 in a year of 52 weeks runs on into the next year's week 1.** It raised no error. 0 and 54 raise 380, as
  above.

**VisibleDays' index runs from 1 to the number of days shown,** and those days are contiguous:

| Months shown | Last index | Days |
|---|---|---|
| 1 × 1 | **42** | six whole weeks |
| 1 × 2 and 2 × 1 | **70 or 77** | from the first month's first row to the end of the last month's six rows |
| 2 × 2 | 133 | the same |
| 3 × 4 | 371 or 378 | the same |

- **The span is the first month's grid start to the 42nd day of the last month's grid.** With June and July 2001 shown,
  it ran from 2001-05-27 to 2001-08-11, 77 days, with no gap between the months.
- **Index 0, -1, one past the last, and 32767 raise 380.**
- **The index is rounded as VB rounds to an Integer:** 1.5 and 2.5 both read VisibleDays(2), 42.5 read (42), and 0.5
  and 42.6 raised 380. `"2"` read (2).
- **Each is a Date carrying the time of day it was read,** as recorded above.
- **Which months are shown after MonthRows or MonthColumns changes depended on what was shown before:**

  | Value | 1 × 1 | then 1 × 2 | then 2 × 2 | then 3 × 4 |
  |---|---|---|---|---|
  | 2001-06-15 | June | June, July | June to September | **January to December 2001** |
  | 2009-02-15 | February | **January, February** | January to April | January to December 2009 |
  | 2004-12-15 | December | December, January | December to March | January to December 2004 |

  **Twelve months showed Value's calendar year each time.** What decides the first month for fewer resisted explanation.

**The selection's edges,** with MultiSelect True and MaxSelCount 7, from Value 2001-06-15:

| Assignment | Result |
|---|---|
| `SelEnd = 06-12`, before SelStart 06-15 | **no error. The selection collapses to 06-12**, and Value follows. A SelStart after SelEnd does the same, as above |
| `SelStart = 06-01`, with SelEnd 06-18: 18 days | **35770**, and nothing changes |
| `SelEnd = 06-19`, from 06-12: 8 days | 35770 |
| **`MaxSelCount = 3`, with 06-12 to 06-18 selected** | **35770, "An invalid date range was specified."** MaxSelCount stays 7 and the selection stays |
| `MaxSelCount = 1`, with 3 days selected | 35770 |
| `MultiSelect = False`, with 06-15 to 06-18 | **the selection collapses to SelStart**, 06-15. True again keeps the single day |
| `Value = 06-25`, with 06-15 to 06-18 | **the selection collapses to Value**, whether the date is outside the range or inside it |
| `MinDate = 06-21`, with 06-20 to 06-22 | Value is pulled up to 06-21, and the selection collapses to it |
| **`MaxDate = 06-21`, with 06-20 to 06-22** | **no error. MaxDate reads 06-21, and SelEnd stays 06-22, past it.** A later `SelEnd = 06-22` raised 35770, and `SelEnd = 06-21` was taken |

- **MaxSelCount can't be set below the days already selected.**
- **Moving Value always collapses the selection.**

**MinDate and MaxDate:**

| Assignment | Result |
|---|---|
| **`MinDate = 2001-07-01`, above MaxDate 2001-06-30** | **35775**, "A value was specified for the MinDate property that is higher than the current value of MaxDate." Nothing changes |
| `MinDate = 2001-06-30`, equal to MaxDate | taken. Value is pulled to it |
| **`MinDate = 1752-12-31`, `1601-01-01`, `0100-01-01`** | **35772**, "An invalid date was specified for the MinDate property." |
| `MinDate = 1753-01-02`, `9999-12-31` | taken. 9999-12-31 pulled Value up to it |
| `MaxDate = 1752-12-31`, `1601-01-01` | **35774**, below MinDate, rather than an invalid date |
| `MaxDate = 1753-01-01`, `9999-12-30` | taken. 1753-01-01 pulled Value down to it |
| `Value = 9999-12-31` | taken. Week 52 |
| `Value = 1753-01-01` | taken. Week 1, DayOfWeek 2, a Monday |

### The rest

- Appearance and BorderStyle take 0 and 1, and 2 or -1 raise 380.
- MousePointer takes 0, 1 and 2, and -1 raises 380.
- OLEDropMode takes 0 and 1, and 2 raises 380.
- **Enabled takes any number:** 1, 2 and -1 all read True.
- `TitleBackColor = vbRed` is taken.
- `hWnd = 0` raises 383.
- **No event came from code** for MultiSelect, SelEnd, `Month = 7` or Refresh. `Month = 7` changed the month shown, and
  still raised no GetDayBold.

### Events, with real input

**The probe:**
- **The setup.** The form was shown with Value 2001-06-15, and KeyPreview took the F keys to change the setup between
  steps.
- **The map.** After each change, the program hit-tested the control every 30 twips. It wrote the screen point of each
  date and each named area to a file, so a driver on the guest's console clicked exact targets.
- **The log.** Every event was logged with Value, the selection and the first day shown, and MouseDown also logged its
  own HitTest.
- **The builds.** Native and p-code logged the same lines, apart from MouseDown's exact twips.

| Action | Events, in order |
|---|---|
| Click 2001-06-20, with Text1 focused | MouseDown (HitTest 1, 06-20), **GotFocus**, MouseUp, Click, **then SelChange 06-20 to 06-20, then DateClick 06-20** |
| Double-click 06-22 | the first click as above; then **DblClick, DateDblClick 06-22, MouseUp, SelChange 06-22 to 06-22 again, DateClick 06-22 again**. No second MouseDown |
| Click 2001-07-02, a trailing day (HitTest 2) | MouseDown, MouseUp, Click, GetDayBold for July, SelChange 07-02, DateClick 07-02. The month moves to July |
| The next-month button (HitTest 8) | MouseDown, **SelChange 07-15**: the same day a month on, then GetDayBold, MouseUp, Click. **No DateClick** |
| The previous-month button (HitTest 9) | the same, back to 06-15 |
| The Today link (HitTest 12) | MouseDown, GetDayBold, SelChange 2026-10-27, **DateClick 2026-10-27, before MouseUp**, MouseUp, Click |
| Right arrow; Down arrow | KeyDown, SelChange to the next day, then the same weekday a week on; KeyUp |
| Page Down; Ctrl+Page Up | **a month on, then a year back**, the day kept: GetDayBold, then SelChange, between KeyDown and KeyUp |
| Home; End | **the first and last of the month** |
| `k`; Esc | KeyDown, KeyPress, KeyUp, and nothing else |
| Right-click 06-12 | MouseDown and MouseUp with Button 2, **then Click**. No SelChange |
| Tab | LostFocus |

**With MultiSelect True:**

| Action | Events, in order |
|---|---|
| Setting MultiSelect from code, with the focus | GetDayBold twice, then LostFocus and GotFocus |
| Shift+click 06-18, from 06-15 | KeyDown 16, MouseDown with Shift 1, **SelChange 06-15 to 06-18 at the press**, MouseUp, Click, **DateClick 06-15**: the selection's start, not the day clicked |
| Shift+Right arrow | the key events only. **The selection doesn't change** |
| Drag from 06-11 to 06-14 | MouseDown, then **SelChange at every step**: 06-11, 06-11 to 06-12, 06-11 to 06-14. Then MouseUp, Click, DateClick 06-11 |
| Drag from 06-04 to 06-21, 18 days, with MaxSelCount 7 | SelChange 06-04, then 06-04 to 06-10. **The range stops at 7 days, with no error** |
| Click 06-26, with SelChange setting **Cancel = True** | SelChange 06-26 to 06-26, with Value and SelStart reading 06-26 inside it. **After it, the selection is back to 06-04 to 06-10**, and DateClick reports 06-04 |

**Clicking the title's month (HitTest 10) opens a menu of the twelve months.** MouseDown came at the press. MouseUp, Click
and **only a KeyUp 27** came once Esc closed the menu.

**Not measured:**
- a click on a week number, because the map's point for one landed on a date once the week numbers had redrawn;
- the year's spin in the title (HitTest 11);
- the mouse wheel;
- holding a title button down.

### Drawing

These are screenshots at 96 dpi, without visual styles.
- **The default MonthView** draws one month:
  - a title with `June 2001` in bold, between two arrow buttons;
  - day names from Sun to Sat in pale grey, above a rule;
  - six weeks, with the trailing days in grey and the selected day shaded;
  - `Today: 27/10/2026` at the foot, beside a red circle.

  The week starts on Sunday, as StartOfWeek 1 says, even in en-GB. The DTPicker's dropdown calendar started on Monday.
- **A multi-day selection** shades every day of it.
- **After the cancelled click,** the restored range, 06-04 to 06-10, was drawn with a dotted focus rectangle on its last day.
- **DayBold** draws the day in bold.
- **ShowWeekNumbers** adds a column of week numbers in grey, with a rule, at the left: 22 to 27 for June 2001.
- **The month menu** lists January to December in a plain popup menu.

### The saved files

**The pass.** A form was written by hand and opened in VB6's IDE.
- **MonthView1:** minimal.
- **MonthView2 to 7:** each holds candidate lines in reverse of VB6's order:
  - MaxDate alone;
  - the dates and the selection: MinDate, MaxDate, CurrentDate, MultiSelect, MaxSelCount and StartOfWeek 2;
  - the appearance and the switches: Appearance 0, BorderStyle 1, Enabled, MousePointer, OLEDropMode, ShowToday False,
    ShowWeekNumbers True and ScrollRate 3;
  - MonthRows 2 and MonthColumns 2;
  - the six colours;
  - an Arial 9.75 bold `Font` block.
- **MonthView8:** minimal, with its Properties window read page by page. It has **51 rows**.
- **A ninth,** double-clicked from the toolbox.

The form was dirtied with a comment and saved, and VB6 wrote no load log. VB6's save is
`corpus/inbox-controls/pending/monthview/`. The hand-written form and the save were then read at run time, native and
p-code, which logged the same values.

**VB6's blocks, as it wrote them:**

```
   Begin MSComCtl2.MonthView MonthView3 
      Height          =   2370
      Left            =   5920
      TabIndex        =   2
      Top             =   120
      Width           =   2700
      _ExtentX        =   4763
      _ExtentY        =   4180
      _Version        =   393216
      Appearance      =   1
      MaxSelCount     =   14
      MultiSelect     =   -1  'True
      StartOfWeek     =   110493698
      CurrentDate     =   37057
      MaxDate         =   47848
      MinDate         =   36526
   End
   Begin MSComCtl2.MonthView MonthView6 
      …the same lines as far as _Version…
      ForeColor       =   65280
      BackColor       =   255
      Appearance      =   1
      MonthBackColor  =   16711680
      StartOfWeek     =   110493697
      TitleBackColor  =   65535
      TitleForeColor  =   16711935
      TrailingForeColor=   8421504
      CurrentDate     =   36494
   End
```

**The order:**
1. ForeColor, BackColor;
2. BorderStyle, Appearance;
3. MousePointer, Enabled, OLEDropMode;
4. **the control's own properties, alphabetically:** MaxSelCount, MonthBackColor, MonthColumns, MonthRows, MultiSelect,
   ScrollRate, ShowToday, ShowWeekNumbers, StartOfWeek, TitleBackColor, TitleForeColor, TrailingForeColor;
5. **then the dates:** CurrentDate, MaxDate, MinDate.

The `Font` block came after Appearance and before StartOfWeek. The DTPicker's lines follow the same plan: its stock lines,
then CalendarBackColor to UpDown in alphabetical order, then its three dates.

**What the lines hold:**
- **Every block gets `Appearance`, `StartOfWeek` and `CurrentDate`,** even at their defaults.
- **StartOfWeek holds a high word, as the DTPicker's Format does.**
  - Sunday was written as 110493697, `&H696` above 1, in this session.
  - At run time StartOfWeek read 1 or 2: the low word.
- **The toolbox MonthView was written with StartOfWeek 2, Monday, the en-GB week.** A block without the line loads with
  1, Sunday, as in group 1. So **the designer takes the locale's first day, and a missing line doesn't.**
- **CurrentDate:**
  - **A minimal block is saved as `36494`, 1999-11-30, as the DTPicker's is,** and VB6's save of it then reads
    1999-11-30 at run time. The hand-written block read the day it loaded.
  - **Showing MonthView8's Properties window didn't change that,** unlike the DTPicker: it was saved as 36494 too.
  - The toolbox MonthView saved `46322`, that day.
- **MaxDate and MinDate are serials,** written only off their defaults.
- **The colours are decimal Longs.** `TrailingForeColor`, 17 characters, runs straight into its `=`.
- **The MonthView sizes itself at load, in the IDE and at run time alike:**

  | Block | Hand-written size | Size read and saved |
  |---|---|---|
  | ShowWeekNumbers True, ShowToday False | 2700 × 2370 | **3045 × 2310** |
  | 2 × 2 months | 2700 × 2370 | **5430 × 4515** |
  | Arial 9.75 bold | 2700 × 2370 | **3330 × 2820** |

  **A block that resized was also saved at a whole-pixel position.** Top 2620 became 2625, Left 3020 became 3015 and Top
  5120 became 5115: each is the nearest multiple of 15. A block that kept its size kept Top 2620.
- **The defaults at run time,** read early-bound since group 1 couldn't read them through `CallByName`:

  | Colour | Default |
  |---|---|
  | BackColor | `&H8000000F` |
  | ForeColor | `&H80000012` |
  | MonthBackColor | `&H80000005` |
  | TitleBackColor | `&H8000000F` |
  | TitleForeColor | `&H80000012` |
  | TrailingForeColor | `&H80000011` |

- **Every line read back as written.**

**Double-clicked from the toolbox,** a MonthView is 2700 × 2370, centred on the 9000 × 7800 form at 3150, 2715. It was
written first in the file.

### In HexIDE

HexIDE doesn't know MSCOMCT2.OCX. `ControlLibraries` lists only MSCOMCTL.OCX and COMCTL32.OCX, so nothing maps a
MonthView to this issue (#1095).
