## In-box controls: the DTPicker (2026-10-27, #1094)

**The probes.**
- **The software:** VB6 SP6 (6.00.9782) with MSCOMCT2.OCX 6.01.9782, on Windows 10 at 96 dpi, in the en-GB user locale (system locale en-US).
  - The form line is `Object = "{86CF1D34-0C5F-11D2-A9FC-0000F8754DA1}#2.0#0"; "MSCOMCT2.OCX"`.
  - The block is `Begin MSComCtl2.DTPicker`, `_Version` 393216.
- **The form:** Text1, then DTPicker1 at 1500, 120, 2400 × 375, a hand-written block with only its position, extents and
  `_Version`.
- **The programs:** each ran in a Sub Main and logged every value with its type.
  - Every error was read straight after its statement.
  - Each scenario ran on a fresh instance of the form.
- **The builds:** `/make` as native code and as p-code, both run on the guest's console. They logged the same lines, apart
  from the clock's seconds in values taken from the time of the run.

### The object model

- **Its members,** from the type library:
  - **The value:** Value, and the hidden `_Value`, the default member.
  - **Its parts:** Year, Month, Day, Hour, Minute, Second, DayOfWeek, all Variants.
  - **The range:** MinDate, MaxDate.
  - **The look:** Format, CustomFormat, CheckBox, UpDown, Font, and five calendar colours: CalendarBackColor,
    CalendarForeColor, CalendarTitleBackColor, CalendarTitleForeColor, CalendarTrailingForeColor.
  - **The rest:** Enabled, hWnd, MousePointer, MouseIcon, OLEDropMode, OLEDrag, Refresh.
- **Its events:**
  - Change, CloseUp, DropDown;
  - CallbackKeyDown, Format and FormatSize, for callback fields in a CustomFormat;
  - Click, DblClick, KeyDown, KeyPress, KeyUp, MouseDown, MouseMove, MouseUp;
  - the OLE drag events.
- **FormatConstants:** dtpLongDate 0, dtpShortDate 1, dtpTime 2, dtpCustom 3. The library's DayConstants (mvwSunday 1 to
  mvwSaturday 7) and MonthConstants (mvwJanuary 1 to mvwDecember 12) belong to the MonthView.
- **`TypeName`** is `DTPicker`.
- **The default member is the value.** `v = DTPicker1` read a Date, and `DTPicker1 = #2/3/2001#` set it.

### Defaults, from a block with no properties

| Read | Logged |
|---|---|
| Value | **the moment the form loaded, date and time**, a Date |
| Year, Month, Day, Hour, Minute, Second | that moment's, **Integers** |
| DayOfWeek | 3, an Integer, on a Tuesday: **Sunday is 1** |
| Format | **1, dtpShortDate**, a Long |
| CustomFormat | empty |
| CheckBox, UpDown | False |
| MinDate, MaxDate | **1601-01-01 and 9999-12-31** |
| The calendar colours | &H80000005, &H80000012, &H8000000F, &H80000012, &H80000011: system colours |
| Font | MS Sans Serif 8.25 |
| Enabled, TabStop | True |
| MousePointer, OLEDropMode | 0 |

**The colours' type depends on how they are read.** Early-bound, CalendarBackColor is a Long, VarType 3. Through
`CallByName` it came back as **VarType 19**, an unsigned four-byte integer, which VB has no type for. A helper that passed
it to `TypeName` failed silently under `On Error Resume Next`.

### Value

**Value is always a Date holding a date and a time, whatever Format shows.** 2001-02-03 04:05:06, assigned once, read
back the same under Format 0, 1, 2 and 3, and a time assigned under Format 2 kept its date.

| Assigned | Value read |
|---|---|
| `#2/3/2001 4:05:06 AM#` | the same |
| `"2002-03-04"`, `"March 5, 2003 7:08 PM"` | 2002-03-04 00:00:00, 2003-03-05 19:08:00 |
| `"13/1/2001"` | **2001-01-13**: VB's own String-to-Date conversion, which swaps an impossible month and day |
| `37000`, `37000.75` | 2001-04-19 00:00:00, 2001-04-19 18:00:00 |
| `True` | **1899-12-29**: -1 as a date |
| `#10:20:30 AM#`, under Format 2 or 1 | **1899-12-30 10:20:30** |
| `#12/30/1899#`, `#12/31/9999#` | taken |
| `"x"`, `"2001-02-30"` | **380** |
| **Null or Empty, with CheckBox False** | **35787, "Can't set Value to NULL when CheckBox property = FALSE."** |
| A date outside MinDate to MaxDate | **35773, "A date was specified that does not fall within the MinDate and MaxDate properties."** |

**Change is never raised from code.** Not by Value, a part, a MinDate that moved Value, or Null. That held with
`DoEvents` after each statement too.

### Format and CustomFormat

- Format takes 0 to 3; -1 and 4 are 380.
- CustomFormat takes any String, quotes and the empty string included, and reads it back as set.
- Setting CustomFormat doesn't change Format: it stayed 1.

### MinDate and MaxDate

| Statement | Logged |
|---|---|
| `MinDate` above Value | **Value pulled up to MinDate's date**, silently |
| `MaxDate` below Value | **Value pulled down to MaxDate's date, keeping its own time**: 13:02:03, on a MaxDate of midnight |
| `MaxDate` below MinDate | **35774**, "A value was specified for the MaxDate property that is lower than the current value of MinDate." |
| `MinDate` above MaxDate | **35775**, "A value was specified for the MinDate property that is higher than the current value of MaxDate." |
| `MinDate = MaxDate` | taken |
| `MinDate = #1/1/1500#`, `#1/1/100#` | **taken**: 1601 is only the default |
| Value 1000-01-01, 1752-09-02, 1600-12-31, with MinDate 100 | taken |
| MinDate or MaxDate with a time | kept: 06:07:08, 23:00:00 |

**Times on the boundary day are refused silently.**
- With MaxDate 2004-12-31 23:00, `Value = #12/31/2004 11:30 PM#` raised no error, and Value didn't change.
- With MinDate 2004-01-01 06:00, `Value = #1/1/2004 5:00 AM#` did the same.

A day outside the range is 35773. A time outside it on the boundary day is ignored without a word.

### The parts

| Statement | Logged |
|---|---|
| `Month = 2` on January 31, and on January 29, 2001 | **380**: no day is clamped |
| `Day = 0`, `Month = 13`, `Hour = 24`, `Minute = 60` | 380 |
| `Day = 30` in January, `Hour = 23`, `Second = 59` | taken |
| `Day = 2.5`, `Day = "7"` | 2 (half to even), 7 |
| **`DayOfWeek = 1`, on Friday January 7, 2005** | **Sunday January 2**: the same week, Sunday to Saturday |
| `DayOfWeek = 7` | Saturday January 8 |
| `DayOfWeek = 0`, `8` | 380 |
| `Year = 1500`, below MinDate | 380 |
| `Year = "x"`, `Year = Null` | 380 |

### CheckBox and Null

- **`CheckBox = True` leaves Value a date**: the box starts ticked.
- **`Value = Null` then reads Null, and so does every part.** VarType 1.
- **Setting a part while Null ticks the box, and takes the rest from now.**
  - `Day = 5` gave today's year and month, day 5, at the current time.
  - `Hour = 5` gave today at 05 and the current minutes and seconds.
- **`CheckBox = False` while Null brings back a date.** After Value 2006-04-05 then Null, it read 2006-04-05.
- **MinDate set while Null leaves Value Null.** `CheckBox = False` then gave MinDate's date at the current time.
- With CheckBox False again, `Value = Empty` is 35787.

### The rest

- **UpDown can be set at run time.**
- `hWnd = 1` is **383, "Property is read-only"**: the type library declares a put.
- `CalendarBackColor = vbRed` read back 255.
- MousePointer 99 is taken and 100 is 380.

### Events, with real input

**The probe:**
- **The setup.** The form above started with Value 3 February 2001 10:20:30.
- **The logging.** The program logged every DTPicker event with Value at that moment.
- **The input.** A driver on the guest's console clicked and typed for real.
- **The F keys.** The form took them through KeyPreview, set up the next step, and logged Value and the focused control.
- **The locale.** It is en-GB, so the short date reads dd/mm/yyyy and the first field is the day.
- **The builds.** Native and p-code logged the same 149 lines.

| Action | Events, in order |
|---|---|
| Click the day field, with Text1 focused | MouseDown, **then GotFocus**, MouseUp, Click |
| Up arrow, the day selected | KeyDown 38, Change with 4 February, KeyUp. No KeyPress |
| Right arrow | KeyDown, KeyUp. The month is selected, with no Change |
| Type 1, then 5, into the month | KeyDown, KeyPress and KeyUp for each. The field shows `15`, with **no Change**: Value is still February |
| Down arrow | Change with **4 January**. The typed 15 is dropped, and the month it replaced is lowered |
| Type 9 | KeyDown, KeyPress, KeyUp. **No Change for the 1.5 seconds before the next key.** Change with 4 September came as that key, F6, reached the control |
| F4 | KeyDown 115, DropDown. No KeyUp |
| Right arrow, with the calendar down | Change with 5 September, at once. No key events: the calendar has the keys |
| Enter | CloseUp, then KeyUp 13 |
| Alt+Down | KeyDown 18 and KeyDown 40, each with Shift 4, then DropDown |
| Escape | CloseUp, then KeyUp 27 |
| Click the dropdown button | MouseDown, DropDown. **No MouseUp and no Click** |
| Click the form, away from the calendar | CloseUp, and nothing else |
| On 31 January, click the month, Up arrow | MouseDown, MouseUp, Click, then Change with **28 February**: the day is clamped |
| MaxDate 5 February, Value 3 February: click the day, Up arrow three times | Change with 4, Change with 5, then **Change again with Value still 5 February**. The field stays at `05` |
| CheckBox True, from code, then click the box | MouseDown, **Change with Value Null**, MouseUp, Click |
| Up arrow, unchecked | KeyDown, KeyUp. No Change, and Value stays Null |
| Click the box again | MouseDown, Change with the date it had (3 February 10:20:30), MouseUp, Click |
| Space | KeyDown, KeyPress 32, Change with Null, KeyUp. Space toggles the box |
| UpDown True, from code: click the up arrow, then the down arrow | Change with 4 February, then with 3 February. **No mouse events and no Click** |
| Format 2, Time: click the hour, Up arrow | MouseDown, MouseUp, Click, then Change with 11:20:30 |
| Double-click the field | MouseDown, MouseUp, Click, DblClick. **No second MouseUp** |
| Right-click | MouseDown and MouseUp with Button 2, **then Click** |
| Escape, with nothing dropped down | KeyDown, KeyPress 27, KeyUp |
| Tab | LostFocus |

- **A typed digit waits.** It shows in the field but isn't Value until something commits it. That was the next key here,
  and an arrow key drops a value that isn't a valid month. Whether a longer pause commits it is unmeasured.
- **Every step of a field raises Change**, whether it comes from the keyboard, the calendar, the spin buttons or the
  checkbox. At MaxDate the Up arrow still raises Change, with Value unchanged.
- **Setting CheckBox, UpDown or Format to a new value while the control has the focus raises LostFocus, then GotFocus**,
  once for each property set. Setting Format to the value it already had raised neither.
- **The dropdown button raises DropDown at MouseDown**, with no MouseUp and no Click. The spin buttons raise only Change.
- **A right-click on the field raises Click.**

### Drawing

These are screenshots at 96 dpi, without visual styles, since a VB6 program carries no manifest:
- **Format 1, Short Date,** reads `03/02/2001` in a sunken white field, with the dropdown button at the right.
- **The selected field** is drawn white on blue. A typed value that isn't yet Value shows there too, such as `15` in the
  month.
- **The calendar drops down below the control's left edge:**
  - a header with `September 2001` in bold, between two arrow buttons;
  - day names from Mon to Sun in pale grey;
  - six weeks, with the trailing days of the months on either side in grey and the selected day shaded;
  - `Today: 27/10/2026` at the foot, beside a red circle.

  While the calendar is down, the form's caption is drawn inactive.
- **CheckBox True, unchecked,** draws a box at the left, with a focus rectangle while it has the focus, and the date
  greyed.
- **UpDown True** draws two stacked spin buttons in place of the dropdown button.
- **Format 2, Time,** reads `11:20:30`, in 24-hour time. It **draws spin buttons although UpDown had just been set to
  False**.

### The saved files

**The passes,** each in VB6's IDE on the guest's console:
1. **Pass 1:** a hand-written form with Text1 and two minimal DTPickers, with only their position, extents and
   `_Version`.
   - DTPicker2 was selected and its Properties window read, page by page.
   - A third DTPicker was double-clicked from the toolbox.
   - The form was dirtied with a comment and saved. VB6's save is `corpus/inbox-controls/pending/dtpicker-added/`.
2. **Pass 2:** DTPicker1's properties were set in the Properties window. Its results aren't used; see the end of this
   section.
3. **Pass 3:** seven blocks were written by hand, each with its lines in reverse of VB6's order, then opened and saved
   untouched. VB6's save is `corpus/inbox-controls/pending/dtpicker/`.
   - **The read:** the hand-written form and VB6's save of it were then read at run time, natively and as p-code.
   - **The builds:** the two logged the same values, apart from the moment the form loaded.
   - **No load log:** VB6 wrote none, so every line was taken.

**VB6's blocks, as it wrote them in pass 3:**

```
   Begin MSComCtl2.DTPicker DTPicker4 
      Height          =   375
      Left            =   3000
      TabIndex        =   3
      Top             =   1920
      Width           =   2400
      _ExtentX        =   4233
      _ExtentY        =   661
      _Version        =   393216
      MousePointer    =   11
      Enabled         =   0   'False
      OLEDropMode     =   1
      CalendarBackColor=   255
      CalendarForeColor=   65280
      CalendarTitleBackColor=   16711680
      CalendarTitleForeColor=   65535
      CalendarTrailingForeColor=   8421504
      CheckBox        =   -1  'True
      Format          =   110100481
      UpDown          =   -1  'True
      CurrentDate     =   36494
   End
   Begin MSComCtl2.DTPicker DTPicker3 
      …the same lines as far as _Version…
      CustomFormat    =   "dd MMM yyyy"
      Format          =   110100483
      CurrentDate     =   40000.25
      MaxDate         =   47848
      MinDate         =   36526
   End
```

DTPicker7's `BeginProperty Font {0BE35203-8F91-11CE-9DE3-00AA004BB851}` block came straight after `_Version`, before
Format. The order among the other lines is unmeasured, because no block had both.

**The order:**
1. the extents and `_Version`;
2. MousePointer, Enabled, OLEDropMode;
3. the five calendar colours;
4. CheckBox, CustomFormat, Format, UpDown;
5. CurrentDate, MaxDate, MinDate.

**What the lines hold:**
- **Every block gets a `Format` and a `CurrentDate` line, even a minimal one.** Nothing else is written at its
  default.
- **Format's low 16 bits are Format, and its high 16 bits changed from one IDE session to the next.** Format 1 was
  written as 111345665 in pass 1, 111280129 in pass 2 and 110100481 in pass 3, which is `&H6A3`, `&H6A2` and `&H690`
  above the 1.
  - A hand-written `Format = 2`, and pass 1's 111345665, were both taken, and rewritten with the session's high word.
  - At run time Format read the low word: 0, 1 or 3.
  - **What the high word is resisted explanation.**
- **CurrentDate is Value, as a Date serial,** such as `40000.25`, which read back as 2009-07-06 06:00:00.
  - **A block with no CurrentDate was saved as `36494`, 1999-11-30, in every pass.** That held for every block whose
    Value nothing in the IDE had read.
  - In pass 1, DTPicker2's Properties window had been shown. It saved `46322.4454861111`, the moment it was read on
    2026-10-27.
  - The DTPicker double-clicked from the toolbox saved `46322`: that day, with no time.
- **So opening and saving a form changes a minimal block's Value at run time.** The hand-written block read the moment
  the form loaded, as in group 1. VB6's save of the same block read 1999-11-30.
- **MaxDate and MinDate are Date serials,** written only off their defaults.
- **CustomFormat is written whenever it isn't empty,** under Format 2 as well as 3.
- **The colours are decimal Longs.**
- **The calendar colours' names, of 17 characters and more, are followed straight by `=`,** as in
  `CalendarBackColor=   255`. Shorter names are padded to 16 columns. A name of exactly 16 is unmeasured.
- **Every value read back at run time as written:** Format's low word, CustomFormat, the dates, CheckBox, UpDown, the
  colours, Enabled, MousePointer, OLEDropMode and the font (Arial 12, bold and italic, from Weight 700 and Italic True).

**Double-clicked from the toolbox,** a DTPicker is **1500 × 750**, with extents 2646 and 1323. It was centred on the
9600 × 7200 form, at 4050, 3225. VB6 wrote it first in the file, before Text1, as it did the UpDown added the same way.

**Pass 2, which isn't used.** Setting properties through the Properties window was unreliable:
- **CustomFormat:** the text typed for it never reached its row.
- **MaxDate:** typing `31/12/2030` into its row showed that date in both the MaxDate and MinDate rows. The save then had
  `MinDate = 47848` and no MaxDate line.
- **Value:** typing a date below that MinDate raised 35773, "A date was specified that does not fall within the MinDate
  and MaxDate properties."

That pass's blocks are not in the corpus. Pass 3's hand-written lines are what this section rests on.

### In HexIDE

HexIDE doesn't know MSCOMCT2.OCX. `ControlLibraries` lists only MSCOMCTL.OCX and COMCTL32.OCX, so nothing maps a
DTPicker to this issue (#1094).
