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

### In HexIDE

HexIDE doesn't know MSCOMCT2.OCX. `ControlLibraries` lists only MSCOMCTL.OCX and COMCTL32.OCX, so nothing maps a
DTPicker to this issue (#1094).
