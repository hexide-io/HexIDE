## RGB, QBColor and MsgBox's style (2026-10-12)

**The questions.** What `RGB` and `QBColor` return, and of what type, at their edges. And what `MsgBox` does with a
style that is not an Integer. HexIDE read the style only when it arrived as an Integer, and every in-box constant
is a Long, so `vbYesNo + vbQuestion` showed OK alone with no icon.

**Probe.** A Standard EXE built with `vb6.exe /make` on the Win10 guest (session 0) and run. Each call ran under
`On Error Resume Next`, logging value, `TypeName` and `Err.Number`. For `MsgBox`, a form Timer found the open
box by its title, recorded which of the control IDs 1 to 7 (IDOK to IDNO), 10 and 11 (Try Again and Continue)
and 20 (the icon) it held, and posted a click on No, else Cancel, else Ignore, else OK. For the dismissal rows it
first posted `WM_CLOSE` or `WM_SYSCOMMAND`/`SC_CLOSE` three times, then reported whether the box was still open.
Timers keep running under `MsgBox` in a compiled program, which is what makes this possible without a person.

**Re-run on the console (2026-11-18, #4768).** Session 0 displays no window, so this probe was rebuilt and run again on
the guest's console, p-code and native, logging its session id. The dismissal rows, three style rows (`vbYesNo +
vbQuestion`, 22 and 4100) and the Timer under `MsgBox` gave the same. The rows about a hidden owner did not: see
*Hiding the box's owner* below.

### RGB

| Call | Value | Type | Err |
|---|---|---|---|
| `RGB(1, 0, 0)` | 1 | Long | |
| `RGB(255, 255, 255)` | 16777215 | Long | |
| `RGB(256, 0, 0)` | 255 | Long | |
| `RGB(1.5, 2.5, 0)` | 514 | Long | |
| `RGB("10", 0, 0)` | 10 | Long | |
| `RGB(Empty, 0, 0)` | 0 | Long | |
| `RGB(-1, 0, 0)`, `RGB(0, -1, 0)` | | | 5 |
| `RGB(40000, 0, 0)`, `RGB(300, 1000, 70000)`, `RGB(3000000000#, 0, 0)` | | | 6 |
| `RGB("x", 0, 0)` | | | 13 |
| `RGB(Null, 0, 0)` | | | 94 |

So each component is an **Integer, coerced as `CInt` does**: banker's rounding (1.5 and 2.5 are both 2, so
514), a numeric string read, `Null` 94, a non-numeric string 13, and outside Integer's range 6. Only then is a
negative component 5 and one above 255 clamped to 255. The overflow is the surprise: 40000 is not clamped, it
fails before the clamp is reached. The result is a **Long** even when small.

**Which error wins when two arguments are bad.** All three are coerced before any is range-checked:

| Call | Err |
|---|---|
| `RGB(-1, 40000, 0)` | 6 |
| `RGB(40000, -1, 0)` | 6 |
| `RGB(0, -1, 40000)` | 6 |
| `RGB("x", -1, 0)` | 13 |
| `RGB(-1, "x", 0)` | 13 |
| `RGB(Null, "x", 0)` | **13** |
| `RGB(0, Null, "x")` | **13** |
| `RGB(0, "x", Null)` | **94** |
| `RGB(Null, 0, "x")` | **13** |
| `RGB(300, -1, 0)` | 5 |

The rows mixing `Null` with `"x"` decide the order, and every one of them says **last first**:
- the third argument is coerced before the second (`RGB(0, Null, "x")` is 13, and `RGB(0, "x", Null)` is 94);
- the second before the first (`RGB(Null, "x", 0)` is 13).

Left to right would give the opposite in each.

### QBColor

| n | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
|---|---|---|---|---|---|---|---|---|
| `QBColor(n)` | 0 | 8388608 | 32768 | 8421376 | 128 | 8388736 | 32896 | 12632256 |

| n | 8 | 9 | 10 | 11 | 12 | 13 | 14 | 15 |
|---|---|---|---|---|---|---|---|---|
| `QBColor(n)` | 8421504 | 16711680 | 65280 | 16776960 | 255 | 16711935 | 65535 | 16777215 |

All Longs. `QBColor(7.5)` and `QBColor(8.5)` are both colour 8 (banker's rounding), and `QBColor("3")` is
colour 3. `QBColor(16)` and `QBColor(-1)` raise 5, and `QBColor(Null)` raises 94. `QBColor(40000)` raises
**6**, not 5: the argument overflows as an Integer before its range is checked.

### MsgBox's style

| Style | Controls found (IDs) | Returned | Type | Err |
|---|---|---|---|---|
| `vbYesNo + vbQuestion` | 6 7, icon | 7 | Long | |
| `vbOKCancel` | 1 2 | 2 | Long | |
| `vbOKOnly` | 2 | 1 | Long | |
| `4&` | 6 7 | 7 | Long | |
| `36` | 6 7, icon | 7 | Long | |
| `"4"` | 6 7 | 7 | Long | |
| `4.5` | 6 7 | 7 | Long | |
| `2.5` | 3 4 5 | 5 | Long | |
| `Empty` | 2 | 1 | Long | |
| `Null` | no dialog | | | 94 |
| `"abc"` | no dialog | | | 13 |
| `vbAbortRetryIgnore + vbCritical` | 3 4 5, icon | 5 | Long | |
| `vbYesNoCancel + vbExclamation` | 2 6 7, icon | 7 | Long | |
| `vbRetryCancel + vbInformation` | 2 4, icon | 2 | Long | |
| `70000` | 2 | 1 | Long | |
| `-1` | 2 | 1 | Long | |
| `7` | 2 | 1 | Long | |

A second and third probe settled what a style out of range does:

Every answer below is a Long.

| Style | What it holds | Controls found (IDs) | Returned |
|---|---|---|---|
| `16` | vbCritical | 2, icon | 1 |
| `17` | vbOKCancel + vbCritical | 1 2, icon | 2 |
| `6` | Windows' Cancel/Try Again/Continue | 2 | 1 |
| `8`, `9`, `12`, `13`, `15` | buttons 8 to 15 | 2 | 1 |
| `22` | 6 + vbCritical | 2, **no icon** | 1 |
| `38` | 6 + vbQuestion | 2, no icon | 1 |
| `84` | vbYesNo + icon 80 | 2 | 1 |
| `116` | vbYesNo + icon 112 | 2 | 1 |
| `70004` | vbYesNo + 70000 | 2 | 1 |
| `1028` | vbYesNo + default button 1024 | 2 | 1 |
| `260` | vbYesNo + vbDefaultButton2 | 6 7 | 7 |
| `772` | vbYesNo + vbDefaultButton4 | 6 7 | 7 |
| `4100` | vbYesNo + vbSystemModal | 6 7 | 7 |
| `65540` | vbYesNo + vbMsgBoxSetForeground | 6 7 | 7 |
| `524292` | vbYesNo + vbMsgBoxRight | 6 7 | 7 |

**Readings:**
- **The style is coerced as `CLng` does.** A Long, a numeric string and a Double all work: 4.5 is 4 (Yes/No)
  and 2.5 is 2 (Abort/Retry/Ignore). `Null` and `"abc"` raise before any dialog appears.
- **The answer is a Long.** HexIDE returned an Integer. That is the overturn: VB6's `vbYes` and friends are Longs, and
  so is what `MsgBox` gives back.
- **Any field out of range makes the whole box a plain OK box, with no icon.** The three fields are:
  - the buttons, in the low **nibble**, valid 0 to 5;
  - the icon, valid 0, 16, 32, 48 and 64;
  - the default button, valid 0, 256, 512 and 768.

  The buttons field is four bits, not three: 12 would be vbYesNo under a three-bit mask, and is OK alone.

  That one rule covers everything earlier rows read as "no button set":
  - 7 has buttons 7;
  - 70000 (`&H11170`) has buttons 0 but icon 112;
  - -1 has buttons 15 and icon 240.

  None of the three is "no button set, so OK". An out-of-range field discards the valid fields beside it: 6 +
  vbCritical shows no icon, and vbYesNo beside icon 112 shows OK.
- **Windows' own style 6 is not honoured.** Windows defines 6 as Cancel, Try Again and Continue (`MB_CANCELTRYCONTINUE`). VB6 shows OK
  alone, so VB6 does not pass the style straight through.
- **The flags above the default button leave the box alone** where measured: 4096, 65536 and 524288.
  - **Corrected 2026-11-14 (#3014):** 524288 right-aligns the text. These rows read the buttons and the answer, not
    the layout, which is in the section on the two flags below.
- **An OK-only box's single button has control ID 2, not 1.** Windows gives it IDCANCEL's ID, so Escape closes
  it. Pressing it still returns 1, vbOK. Only the ID differs, and it is recorded so nobody mistakes it for a
  Cancel button.

### vbMsgBoxRight and vbMsgBoxRtlReading: the box's layout (2026-11-14, #3014)

**The question.** What do 524288 (`vbMsgBoxRight`) and 1048576 (`vbMsgBoxRtlReading`) do to the box, and does VB6 pass
them to Windows as they are?

**The probe.** A Sub Main program, built native and p-code, run in the guest's interactive console at 96 DPI. It showed
each style with `MsgBox` and then with `MessageBoxA`, with no owner and the same text and title. A watcher read each box:
- the dialog's class, style and extended style, and the same for every child, with its control ID and screen rectangle;
- a screenshot.

Then it closed the box with IDCANCEL. Every box returned 2. Native code and p-code, `MsgBox` and `MessageBoxA` all gave
the same lines, row for row.

| Style | Dialog extended style | The text's static | Laid out | Buttons, left to right | Title |
|---|---|---|---|---|---|
| 35, vbYesNoCancel + vbQuestion | `0x00010101` | `SS_LEFT`, style `0x50022080` | icon left, text beside it | Yes, No, Cancel | left |
| 35 + 524288 | `0x00011101`, adding `WS_EX_RIGHT` | **`SS_RIGHT`**, style `0x50022082`, with `WS_EX_RIGHT` | icon left, **text right-aligned** | Yes, No, Cancel | left |
| 35 + 1048576 | `0x00410101`, adding **`WS_EX_LAYOUTRTL`**, which every child carries too | `SS_RIGHT` | **mirrored:** icon right, text at the left | **Cancel, No, Yes** | **right**, close box left |
| 35 + 1572864, both | `0x00410101`: `WS_EX_LAYOUTRTL`, no `WS_EX_RIGHT` | `SS_LEFT` | mirrored: icon right, text beside it | Cancel, No, Yes | right |

The rectangles, in screen pixels across the 290-pixel box (371 to 661):

| | icon | text | Yes | No | Cancel |
|---|---|---|---|---|---|
| 35 | 395–427 | 436–628 | 401–476 | 484–559 | 568–643 |
| 35 + 524288 | 395–427 | 451–643 | 401–476 | 484–559 | 568–643 |
| 35 + 1048576 | 605–637 | 389–581 | 556–631 | 473–548 | 389–464 |
| 35 + 1572864 | 605–637 | 404–596 | 556–631 | 473–548 | 389–464 |

**Readings:**
- **VB6 passes both flags straight to Windows.** `MessageBoxA` with the same style gave identical styles and rectangles.
- **`vbMsgBoxRight` right-aligns the text and nothing else.** The icon, buttons and title stay where they were.
- **`vbMsgBoxRtlReading` mirrors the whole box,** with `WS_EX_LAYOUTRTL`, rather than only setting the reading order:
  `WS_EX_RTLREADING` appears on no window. The title moves right, the close box left, the icon to the right of the
  text, and the buttons reverse.
- **With both, the two cancel out for the text alone.** In a mirrored box, the static is `SS_LEFT`, so the text sits
  beside the icon on the right.
- **This corrects a reading above.** "The flags above the default button leave the box alone where measured" covered
  the buttons and the answer, not the layout. 524288 right-aligns the text.

**Not measured:** a Windows whose UI language reads right to left; a text of several lines; the box with an owner form.

### Closing a message box without pressing a button

Each kind of box was closed by posting `WM_CLOSE`, and again by `WM_SYSCOMMAND` with `SC_CLOSE`, which is what the
title-bar close and Alt+F4 send. Both gave the same rows:

| Buttons | Closed? | Returned |
|---|---|---|
| vbOKOnly | yes | 1, vbOK, a Long |
| vbOKCancel | yes | 2, vbCancel, a Long |
| vbYesNoCancel | yes | 2, vbCancel, a Long |
| vbRetryCancel | yes | 2, vbCancel, a Long |
| vbYesNo | **no, still open** | |
| vbAbortRetryIgnore | **no, still open** | |

A box with a Cancel button answers vbCancel when closed. OK alone answers vbOK. Yes/No and Abort/Retry/Ignore have
no close, which is Windows' own rule for a message box. **HexIDE answered vbOK for every kind**, so closing the box
in `If MsgBox("Delete all?", vbOKCancel) = vbOK Then` took the OK branch.

**Hiding the box's owner does not answer it.** A form Timer set `Me.Visible = False` while a box was open, then
looked for the box twice before pressing a button:

| Buttons | After `Me.Visible = False` | Pressed | Returned | on the console |
|---|---|---|---|---|
| vbYesNo | the box still exists, **not visible** | No | 7, a Long | **shown**, owned by the hidden form; No → 7 |
| vbOKCancel | the box still exists, not visible | OK | 1, a Long | **shown**, owned by the hidden form; Cancel → 2 |

So VB6 hides the box with its owner and keeps `MsgBox` waiting. A person cannot answer a box they cannot see, so in
practice that program hangs.

**A form hidden before the box opens is the same.** A later probe set `Me.Visible = False` and then called `MsgBox`
from that form, with the Timer looking twice before pressing:

| Buttons | While open | Pressed | Returned | on the console |
|---|---|---|---|---|
| vbYesNo | the box exists, **not visible**; the form stays hidden | No | 7, a Long | **shown, with no owner**; the form stays hidden; No → 7 |
| vbOKCancel | the box exists, not visible | OK | 1, a Long | **shown, with no owner**; Cancel → 2 |

**Re-run on the console (2026-11-18, #4768): the box stays shown.** Hiding the owner while the box was open left the
box shown, `IsWindowVisible` 1, still owned by the now hidden form, and a click on No or Cancel answered it as
usual. Hiding the form first gave a box shown with **no owner** (`GW_OWNER` 0). In session 0 the same probe read
the box as not visible before the owner was hidden as well as after, which is the tables' reading. So *VB6 hides the
box with its owner*, and the hang, describe session 0: on the console the program goes on.

HexIDE's dialog refused a hidden owner outright ("Cannot show window with non-visible owner") and ended the run as
Internal error 51. It now goes straight to the unowned box.

**Restart is the case that matters for HexIDE, not for VB6.** HexIDE's Restart ends a run and begins the next in
one turn. The old run's box closes, and its answer arrives after the new run has begun, when nothing about the new
run says the old one ended. So the old run's statement is abandoned on the strength of the run *it* belonged to:
the debugger's run generation, taken when the box opened. Read when the answer arrived, the check missed it: an
OK/Cancel box answered a Cancel nobody pressed and its Form_Load ran on beside the new run, and a Yes/No box
faulted and stopped the new run. HexIDE keeps it waiting too, but shows the box again, unowned, so it can still be
answered, and closes it unanswered if the owner closes. Avalonia hid the box and finished the dialog with no answer,
which answered Cancel for OK/Cancel and stopped a Yes/No handler in silence.

### A message box is modal to the whole program (2026-11-21, #834, SP6)

**The question.** #834 asked what a program's other windows do while its message box waits:
- whether a click on another visible form of the program raises its Click event;
- the same for the box's own form, after that form is hidden and shown again while the box waits;
- whether a Timer still fires.

**The probe:**
- **The program** showed two forms, side by side. `Form1` opened `MsgBox "case n", vbOKCancel` from its own code. `Form2` held a Timer that drove each case while the box waited:
  1. it read which windows were enabled (`IsWindowEnabled`) and which was in the foreground;
  2. it pressed and released the left button inside a form's client area (`SetCursorPos` and `mouse_event`);
  3. it closed the box with `EndDialog(box, 2)`.
- **Each form counted its MouseDown and Click events.** `Form1` also had its own Timer counting ticks.
- **The control case,** with no box open, gave the same clicks to both forms. Each raised MouseDown and Click once.
- **The builds:** `/make` on vb6.exe SP6, as native code and as p-code, run on the guest's console. The two logged the same lines.

| While the box waits | Form1 and Form2 enabled | Events on the clicked form | Foreground |
|---|---|---|---|
| a click on Form2 | **both disabled** | **none:** no MouseDown, no Click | the box |
| a click on Form1, the box's form | both disabled | none | the box |
| Form1 hidden, shown again, then clicked | still both disabled | none | the box |
| Form1 hidden, then Form2 clicked, then Form1 shown again | Form2 disabled while Form1 was hidden | none | Form1, the box still open |

In every case the box was still open when the driver closed it, and `MsgBox` returned 2, vbCancel. Form1's Timer ticked 12 times while the box waited, and Form2's drove the case.

**What it shows:**
- **A message box disables every form of the program while it waits,** not only its own.
- **Neither form takes a click, so no MouseDown or Click runs.** Hiding and showing the box's own form again doesn't re-enable it.
- **Timers keep firing on every form,** as earlier probes assumed.
- **In the last row, showing Form1 again made it the foreground window,** disabled, with the box still open behind it.

**Not measured:**
- the keyboard reaching another form while the box waits;
- a form shown for the first time while the box waits.

### An argument that is not a number

Through `CInt`, `CLng`, `RGB`, `QBColor` and `MsgBox`'s style alike, with `o` an object variable holding
`Nothing`, `va` a Variant holding an Integer array, `k` an object variable holding a `Class1` with no default
member, and `Text1` a TextBox whose `Text` is "12":

| Argument | Err | Value |
|---|---|---|
| `o` | 91 | |
| `va` | 13 | |
| `k` | 438 | |
| `k3`, a `Class3` whose `Value` carries `Attribute Value.VB_UserMemId = 0` and returns 42 | | `CInt(k3)` is 42 (Integer), `RGB(k3, 0, 0)` 42 (Long) |
| `k4`, a `Class4` whose Property **Let** `Value` carries the attribute, written before the Get, which returns 42 | | `CInt(k4)` is 42 (Integer), `CLng(k4)` 42 (Long) |
| `k5`, a `Class5` whose `Public Value As Long` carries `Attribute Value.VB_VarUserMemId = 0`, set to 7 | | `CInt(k5)` is 7 (Integer) |
| `Text1` | | `CInt(Text1)` is 12 (Integer), `RGB(Text1, 0, 0)` is 12 (Long), `QBColor(Text1)` is 255 |
| an omitted `Optional` Variant, `w` | | `CLng(w)` is **448**, a Long; `CInt(w)` is **448**, an Integer |

Each raises before any dialog. A control stands for its default member: `Text1` is `Text1.Text`.

A class or a control with a default member stands for that member, as `Text1` stands for `Text1.Text`.

**The omitted argument is the surprise.** It is not 13, and not 0. VB6 passes a missing argument as an error
value, `DISP_E_PARAMNOTFOUND`, whose VB error number is 448, "Named argument not found". Converted to a number, it is
that number: `Function Area(Optional w): Area = CLng(w) * 10` called as `Area()` returns 4480. The obvious guess,
from the same value raising 13 in a concatenation, was 13. It was measured through `CLng` and `CInt` only, each
returning its own type with no error. `RGB` and `QBColor` coerce as `CInt` does, so they take 448 by that rule rather
than by a measurement of their own.

Three forms do not compile at all:
- `CInt(Nothing)`, with a literal `Nothing`: "Invalid use of object".
- `CInt(a)`, with `a` a fixed Integer array named directly: "Type mismatch".
- `CInt(u)`, with `u` a user-defined type: "Type mismatch".

So the run time rows above are the variable cases.

HexIDE read every one of these as 0, so `RGB(o, 0, 0)` was black and `CInt(o)` was 0.

**HexIDE, after this.**
- The style goes through the same `CLng` narrowing, so the in-box constants select their buttons and icon.
- A style with any field out of range is a plain OK box, as above.
- `MsgBox` returns a Long.
- A box closed without a button answers as the table above says, and the hosts refuse a person's close of a
  Yes/No or Abort/Retry/Ignore box. A close HexIDE makes itself, ending the run, still goes through. The statement
  waiting on the box is then abandoned, as the run is.
- `RGB` and `QBColor` exist, with the coercion, the order of errors, the clamping and the errors above.
- The numeric conversions follow the table above:
  - `Nothing` raises 91, an array 13 and a class with no default member 438;
  - an omitted argument is 448;
  - a UDT raises 13, which is VB6's compile-time error raised at run time.
- A control, an in-box object, or a class with a `VB_UserMemId = 0` member refuses, untrappably, because HexIDE does not
  read default members yet: #790 for controls, #174 for classes. (Superseded for the in-box objects by #840: Err used as a
  value is its Number and App raises 438, as measured there.) A trappable error there would let
  `On Error Resume Next` carry on without the value VB6 gives.
- A box hidden with its owner keeps waiting, shown again unowned, and so does a box whose owner was hidden before it
  opened. The waiting statement is abandoned only when the run that opened the box has ended, which survives a
  Restart.
- A class's default member is found wherever VB6 writes its attribute: a Function, Sub, Property Get, Let or Set, or
  a Public variable.
- Still not honoured by the dialog: the default-button, help-button, modality and right-to-left bits, and Escape
  and Enter (#827). Its buttons are in English whatever the language (#828).

