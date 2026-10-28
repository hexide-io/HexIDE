## What a Timer has at run time (2026-10-27, #801)

**The question.** A Timer is drawn on a form like any other control. Does it have the members a drawn control has, such
as Width, Visible and Font?

**The probe:**
- **The software:** VB6 SP6 (6.00.9782), on Windows 10, in the en-GB user locale.
- **The form:**
  - Timer1, with Enabled False, Interval 1000, Left 600, Top 1200 and Tag `t1`;
  - Timer2, element 0 of a control array, with Interval 250.
- **The reads.** Each member was read three ways, under `On Error Resume Next`, with `Err.Number` and
  `Err.Description` logged straight after:
  - early-bound, as `v = Timer1.Member`, one project per member, to see whether it compiles;
  - late-bound, as `v = o.Member`, with `Dim o As Object` set to Timer1;
  - with `CallByName(Timer1, "Member", VbGet)`, on Timer1 and on Timer2(0).

  Font, Parent and Container were read with `Set`.
- **The writes:** late-bound, and early-bound as one project each.
- **The builds:** native code and p-code.
  - Both builds logged the same lines.
  - Every early-bound project compiled, or failed, the same way both ways.

**The reads:**

| Member | `Timer1.Member`, early-bound | `o.Member`, late-bound | `CallByName` |
|---|---|---|---|
| Interval | compiles | 1000, Long | 1000, Long |
| Enabled | compiles | False, Boolean | False, Boolean |
| Tag | compiles | `t1`, String | `t1`, String |
| Name | compiles | `Timer1`, String | `Timer1`, String |
| Parent | compiles | Form1 | Form1 |
| Index | compiles, and raises 343 "Object not an array" at run time | 343 "Object not an array" | 343 "Application-defined or object-defined error"; 0, Integer, on Timer2(0) |
| Left, Top | compile error "Method or data member not found" | 393 "'Left' property cannot be read at run time" | 393 "Get not supported at runtime" |
| Width, Height, BackColor, Visible, Font, MousePointer, RightToLeft, Container, hWnd, ToolTipText, TabIndex | compile error "Method or data member not found" | 438 "Object doesn't support this property or method" | 438, the same text |

Timer2(0) answered every other row as Timer1 did. Its own values came back: Interval 250, Enabled True, Tag empty and
Name `Timer2`. `TypeName(Timer1)` is `Timer`, and `Me.Controls("Timer1")` answered Width with 438 and Left with 393, as
`o` did.

**The writes:**
- **Left and Top:**
  - `o.Left = 50` and `o.Top = 70` raise 382 "'Left' property cannot be set at run time", and "'Top' …" for Top.
  - `CallByName o, "Left", VbLet, 50` raises 382 "Set not supported at runtime".
- **No such member:** `o.Width = 100`, `o.Height = 100`, `o.Visible = False` and `o.Move 10, 20` raise 438.
- **Interval:**
  - `o.Interval = 500` sets it.
  - `-1` and `65536` raise 380 "Invalid property value" and leave it at 500.
- **Tag:** `o.Tag = 5` stores the String `5`.
- **Early-bound:** `Timer1.Left = 50`, `Timer1.Width = 100`, `Timer1.Visible = False` and `Timer1.Move 10, 20` are each
  the compile error "Method or data member not found".

**What this overturns.** The obvious guess is that a control drawn on the form has a drawn control's members. Of the 19
members tried, a Timer has six: Enabled, Index, Interval, Name, Parent and Tag. Left and Top exist only for the designer,
as [a Timer's designer position](a-controls-position-at-run-time.md#a-timers-designer-position-2069) found.

**One error, two descriptions.** The number is the same by every route, but the text depends on the route:
- 393 and 382 read "'Left' property cannot be read at run time" and "… set at run time" when late-bound. Through
  `CallByName` they read "Get not supported at runtime" and "Set not supported at runtime".
- 343 reads "Object not an array" early-bound and late-bound. Through `CallByName` it reads "Application-defined or
  object-defined error".

**Unmeasured:** members outside these 19, such as WhatsThisHelpID and the drag members; and what the IDE's Locals
window shows for a Timer.
