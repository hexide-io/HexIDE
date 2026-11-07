## A form's Move (2026-10-26, #1569)

**The question.** A control's `Move` was measured for #1071, and a form's stopped the program as not implemented. What
does `Move` do to a form, in which unit, and how many Resize events does it raise?

**The probe.** VB6.EXE SP6 in the oracle guest, built native and p-code with `/make` in session 0. `Sub Main` loads Form1
without showing it: VB6 raises Resize for each change of a hidden form's size, inside the statement (#994). Form1 is
sizable, with `StartUpPosition` Manual, and its Resize logs its Width, Height and WindowState. Before each case a `Base`
step moves the form to 1200, 1200, 6000 x 4500, and the case's Resize count starts from 0. Each case then logs Left,
Top, Width and Height, the count and `Err`, under `On Error Resume Next`. A second build showed the form first, and
gave the same values. Six shapes that might not compile were each built alone. **Native and p-code agreed on every
row.**

| Case | Left, Top, Width x Height | Resize |
|---|---|---|
| `Move 600` | 600, 1200, 6000 x 4500 | none |
| `Move 600, 900` | 600, 900, 6000 x 4500 | none |
| `Move 600, 900, 4800` | 600, 900, 4800 x 4500 | one, 4800 x 4500 |
| `Move 600, 900, 4800, 3600` | 600, 900, 4800 x 3600 | **one**, 4800 x 3600 |
| `Move 1200, 1200, 6000, 4500`, the size it has | unchanged | none |
| `Move 1200, 1200, 6000, 3600` | 1200, 1200, 6000 x 3600 | one |
| `Move 1201, 1207, 4001, 3001` | 1201, 1207, 4001 x 3001 | one |
| `Move 600.5, 900.5, 4800.5, 3600.5`; `Move 607.5, 907.5` | 600, 900, 4800 x 3600; 608, 908 | one; none |
| `Move -600, -900` | -600, -900 | none |
| `Move 600, 900, -10, 3600`; `Move 600, 900, 4800, -10` | unchanged | none; **Err 5** |
| `Move 600, 900, 0, 0`; `Move 600, 900, 100, 100` | 600, 900, **1680 x 405** | one |
| `ScaleMode = 3`, then `Move 100` | **100**, 1200 | none |
| `ScaleMode = 3`, then `Move 100, 100, 400, 300`; `ScaleMode = 7`, then `Move 1, 1, 5, 4` | 100, 100; 1, 1; both 1680 x 405 | one |
| `Move "600", "900"` | 600, 900 | none |
| `Move 3E+9`; `Move 600, 900, 3E+9` | unchanged | none; **Err 6** |
| `Move Null` | unchanged | none; **Err 94** |
| `Width = 4800: Height = 3600`, to compare | 4800 x 3600 | **two**, 4800 x 4500 then 4800 x 3600 |
| `Unload Form1`, then `Form1.Move 600, 900` | 600, 900, 3720 x 2805: the form is loaded again, its Load raised and not its Initialize | none |

Built alone:

| Statement | vb6.exe |
|---|---|
| `Form1.Move` | compile error "Argument not optional" |
| `Form1.Move , 900` | compile error "Argument not optional" |
| `Form1.Move Width:=4800` | compile error "Argument not optional" |
| `Form1.Move 1, 2, 3, 4, 5` | compile error "Wrong number of arguments or invalid property assignment" |
| `Form1.Move 600, , 4800` | compiles; **Err 448**, "Named argument not found", with nothing moved |
| `Form1.Move Left:=600, Top:=900` | compiles, and moves the form to 600, 900 |

What these show:
- **Move sets what it is given**, as a control's does: Left alone, then Top, Width and Height in turn. Left is required.
- **A form's Move is in twips whatever its ScaleMode.** A form has no container, and its own ScaleMode doesn't apply:
  `Move 100` on a pixel form puts Left at 100 twips. A control's Move takes its container's ScaleMode (#1071).
- **Move checks every value before it moves anything.** A negative size, a value past a Long, and Null each leave the
  form where it was, Left and Top included.
- **One Move is one Resize.** A Move that changes the size raises a single Resize with both new values, inside the
  statement, where assigning Width and then Height raises two. A Move to the size the form has raises none.
- **Values are whole twips, not pixels.** 1201 and 4001 read back as given, and a half twip goes to even, as an
  assignment to a control's Left does.
- **The window system has a smallest size.** Asked for less, the sizable form became 1680 x 405, 112 x 27 pixels in
  session 0. That is Windows' minimum tracking size for the window, not VB6's rule.
- **Not measured: Move on a minimized or maximized form.** Session 0 ignored `WindowState = 2` and `1`: the form kept
  WindowState 0 and its size, and Move went ahead. It needs the interactive console.

### In HexIDE (#1569)

- **A form's Move** is built as measured above: the argument shapes, the checks before anything moves, twips whatever
  the ScaleMode, and a single Resize for a change of size (`VBFormRuntime.ResizeOnce`).
- **Left and Top move the window** and retain assigned twips between pixels (#852). A twip is a fifteenth of a
  device-independent pixel, as `Screen`'s size is, so at 150% scaling `Left = 1200` puts the window 120 of the
  screen's own pixels across, and CenterScreen centres it in the units `Screen.Width` reports.
- **Not modelled:** the window system's smallest size. HexIDE's form takes the size asked for, as an assignment to Width
  does.
- **Refused:** Move on a minimized or maximized form, which wasn't measured. Named arguments to Move are refused
  with every method's, #1474.
- **`FormMoveTests`** runs the probe less the rows that are the window system's or need a run-time ScaleMode, which
  HexIDE can't set yet (#990). Every expected line is vb6.exe's.

### Left and Top read and written at run time (2026-10-27, #852)

**The probe:** two forms, each with a 4500 x 3000 client at ClientLeft 2000, ClientTop 1500. FormM is
`StartUpPosition = 0 'Manual`, and FormC is `2 'CenterScreen`. Each logs Left, Top, Width, Height and ScaleMode in
`Form_Load`, after `Show`, and after each write below. The probe ran in the guest's console session, a 1024 x 768
desktop at 15 twips a pixel with Windows 10's frame, and in session 0, whose frame is the classic one. Native and
p-code logged the same lines in each.

| Step | Console: Left, Top | Session 0: Left, Top |
|---|---|---|
| FormM in `Form_Load`, and after `Show` | 1875, 1035 | 1935, 1155 |
| FormC in `Form_Load` | 1875, 1035 | 1935, 1155 |
| FormC after `Show` | 5310, 3960 | 5370, 4050 |
| `Me.Left = 1200`, then `Me.Top = 900` | 1200, 900 | the same |
| `Me.Left = 1207`, not a whole pixel | **1207**, 900 | the same |
| `Me.ScaleMode = 3`, then a read | 1207, 900: still twips | the same |
| `Me.Left = 600` at ScaleMode 3 | 600: **twips**, not pixels | the same |
| `Me.Move 300, 450` at ScaleMode 3 | 300, 450 | the same |
| `Me.Left = -3000` | -3000 | the same |

- **Width and Height** read the outer size throughout: 4740 x 3585 on the console, and 4620 x 3405 in session 0.
- **`TypeName(Me.Left)` is Single.**
- **Before `Show`,** Left and Top are the client position less the frame: 125 and 465 on the console, 65 and 345 in
  session 0. A CenterScreen form is placed only by `Show`: (15360 - 4740) / 2 = 5310, and 3960 is
  (11520 - 3585) / 2 in whole pixels.
- **A form's own Left, Top, Width and Height are twips whatever its ScaleMode.** A written value reads back as written,
  1207 included, not rounded to the pixel the window moved to.
