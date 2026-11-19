## Control arrays (2026-08-10)

Verified against `vb6.exe` with a real form-based probe (`/make` an EXE, run it, `Form_Load` writes results to a
log under `On Error Resume Next`). Form has a 2-element `CommandButton` array `Command1(0)`, `Command1(1)`.

| Probe | Result | Rule |
|---|---|---|
| `Command1.Count` | `2` | the array-name is a members-bearing object; `.Count` = element count |
| `Command1.LBound` | `0` | lowest index present |
| `Command1.UBound` | `1` | highest index present (`Count = UBound − LBound + 1`) |
| `Command1(0).Index` | `0` | each element knows its own `Index` (read-only at runtime) |
| `Command1(1).Caption` | `"B"` | indexed element property read |
| `Command1(9).Caption` (read) | **Err 340** | **Control array element doesn't exist** — a missing element |
| `Command1(9).Caption = "x"` (write) | **Err 340** | same on the write side |
| `TypeName(Command1(0))` | `"CommandButton"` | an element's type is the control type, not "Object" |

(2026-10-22, #1386: 340's message names the index, "Control array element '9' doesn't exist", read, written or unloaded.
See "A member indexed through its object".)

Compile facts (from `/make` succeeding): `.Count`/`.LBound`/`.UBound`/`(i).Index` all **compile** — the array
name is a first-class object with those members. Not probed (textbook VB6, will pin via the interpreter's own live
test): the shared event handler is `Private Sub Command1_Click(Index As Integer)` and the fired element's `Index`
is passed as that leading argument. Harness + files were under a scratch dir (never committed).

**Runtime `Load` / `Unload`** (second form probe, same method) — the array starts with design-time elements 0, 1:

| Probe | Result | Rule |
|---|---|---|
| `Load Command1(5)` (new index) | `Err 0`, `Count`→3 | creates a new element |
| loaded `Command1(5).Caption` | `"A"` | clones the **lowest-index** element's properties (index 0 = "A") |
| loaded `Command1(5).Visible` | **`False`** | a loaded element starts **hidden** — you must set `.Visible = True` to show it |
| loaded `Command1(5).Left` | `240` | inherits position from the template (overlaps index 0 until moved) |
| loaded `Command1(5).Index` | `5` | its own index |
| `Load Command1(0)` (existing) | **`Err 360`** | Object already loaded |
| `Unload Command1(5)` (loaded) | `Err 0`, `Count`→2 | removes the loaded element |
| `Unload Command1(0)` (design-time) | **`Err 362`** | Can't unload controls created at design time |
| `Unload Command1(9)` (missing) | **`Err 340`** | Control array element doesn't exist |

So `Load` = clone the lowest-index element's props but force `Visible=False`; the three failure modes are distinct
codes (360 already-loaded, 362 can't-unload-design-time, 340 missing).

**Later measurement, 2026-10-12:** the rows above used an element nobody had changed, so they cannot tell the
`.frm` values from the element's current ones. With element 0's `Caption` and `BackColor` changed at run time,
the loaded element took the *changed* values: `Load` clones the lowest-index element's **runtime** state. See
"A form-based game's runtime surface".

### What a `.frm` may hold for Index, and Load's bounds (2026-11-16, #2701)

**Asked because** HexIDE's tools can't make a control array (#2701), and the rules a design-time `Index` must keep
had to come from VB6 before a tool enforces them. Each case was a `.frm` written by hand and built with `VB6.EXE /make`,
native and p-code, on SP6 (6.00.9782, MSVBVM60 6.00.9848); `Sub Main` ran `Load Form1` and logged each read under
`On Error Resume Next`. **Native and p-code agreed in every case.** What the IDE's Properties window says when Index is
typed wasn't measured: that needs the IDE driven, not `/make`.

| The `.frm` held | `/make` | At run time |
|---|---|---|
| one `Command1` with `Index = 0` | built | `Command1.Count` 1, `LBound..UBound` 0..0, `Command1(0).Index` 0 |
| one `Command1` with `Index = 3` | built | `Count` 1, `LBound..UBound` **3..3**; `Command1(0)` is Err 340 |
| one `Command1` with `Index = 32767` | built | `Command1(32767).Index` 32767 |
| one `Command1` with `Index = -1`, or `32768` | **failed**: `Line 15: Property Index in Command1 had an invalid value.`, form not loaded | |
| two `Command1`, both `Index = 0` | **failed**: `Line 21: Property Index in Command1 could not be set.`, form not loaded | |
| `Command1` with `Index = 0`, then a `Command1` with no Index | **built**, with `Line 21: Did not find an index property and control Command1 already exists.` in `Form1.log` | see below |
| two `Command1`, neither with an Index | **didn't finish**: `Form1.log` held the same "Did not find an index property" line, and VB6.EXE was still running at 120 s and was killed | |

**Load's bounds,** on the one-element array from `Index = 0`:

| Probe | Result |
|---|---|
| `Load Command1(5)` | no error; `Count` 2, `UBound` 5 |
| `Load Command1(-1)` | **Err 341** |
| `Load Command1(32767)` | no error |
| `Load Command1(32768)` | **Err 6** |

So a design-time Index runs 0 to 32767. One element alone is an array, at any index. A duplicate index stops the
form loading.

**Resisted explanation:** the indexed-then-unindexed form loads both buttons. `Controls.Count` is 2 and both are
named `Command1`, but `Command1.Count` is 1. The second button, at its own `Top`, raises **Err 343** for `Index`, and
only `Controls(1)` reaches it. VB6 kept the control and logged it as an error, but put it outside the array. Why the
same mistake without the first `Index` leaves `/make` hanging, presumably on a prompt nobody can see in session 0, is
not known.

**Re-run on the console (2026-11-18, #4768).** Session 0 reads every control's Visible as False, so the loaded
element's False above needed a displayed form to mean anything. On the guest's console, the IDE, p-code and native
agreeing, the design-time `Command1(0)` read Visible True on the shown form, and `Load Command1(5)` read False with its
window's `WS_VISIBLE` off, until `Visible = True`. **So a loaded element does start hidden.**

The two unindexed buttons hang `/make` on the console too, so it is **not a prompt that session 0 hides**. VB6.EXE was
still running 15 s into `/make`, with no window title, and a screenshot of the console showed the desktop and nothing
else. Its `/out` file said "Errors during load. Refer to 'Form1.log' for details", and `Form1.log` held the one
"Did not find an index property and control Command1 already exists" line. What it waits on is still not known.

### Event procedures for an array element, in VB6's IDE (2026-11-16, #4205)

**Asked because** HexIDE can now make a control an array element (#2701). Its code window still writes handlers
without `Index`, and leaves a control's existing handlers alone when it joins or leaves an array. Before HexIDE copies
either half, what does VB6's IDE do?

**How.** VB6 SP6's IDE (VB6.EXE 6.00.9782) on the `Win10` guest's desktop, driven by mouse and keys and read by
screenshot. Each probe was a form holding one CommandButton `Command1`, in a project of its own. Each code change was
saved with Ctrl+S and read back from the `.frm`.

| Probe | What VB6's IDE did |
|---|---|
| `Command1` with `Index = 0` and no handlers; double-click it in the designer | Wrote `Private Sub Command1_Click(Index As Integer)`, an empty line, `End Sub` |
| The same, then MouseDown from the code window's procedure list | Wrote `Private Sub Command1_MouseDown(Index As Integer, Button As Integer, Shift As Integer, X As Single, Y As Single)` and `End Sub`: **`Index As Integer` first, before the event's own arguments** |
| `Command1` with no Index and a handler `Private Sub Command1_Click()`; type `0` into Index in the Properties window | **No message.** The Properties window then named it `Command1(0)`. The saved `.frm` held `Index = 0` and **the handler unchanged**, still `Command1_Click()` |
| Then F5 | **"Compile error: Procedure declaration does not match description of event or procedure having the same name"**, with OK and Help, and the handler's declaration line highlighted in the code window. Nothing ran |
| `Command1` with `Index = 0` and a handler `Private Sub Command1_Click(Index As Integer)`; clear Index in the Properties window | **No message.** The Properties window then named it `Command1`. The saved `.frm` held no Index line and **the handler unchanged**, still `(Index As Integer)` |
| Then F5 | **The same compile error,** with the declaration highlighted |
| `Command1` with `Index = 0` and `Private Sub Command1_Click(i As Integer)`, the parameter renamed; F5, then Space on the button | **Ran.** The handler wrote `i=0` |
| The same with `(Index As Long)`; F5 | **The compile error** |
| The same with `(ByVal Index As Integer)`; F5 | **The compile error** |
| The same with `(Index)`, no type and no DefType; F5 | **The compile error** |
| The same with `(Index)` and `DefInt I` in the module; F5, then Space | **Ran.** `TypeName(Index)` was Integer, the value 0 |
| The same with `(Index%)`; F5, then Space | **Ran.** Integer, 0 |

**What this settles:**
- **A new handler for an array element takes `Index As Integer` as its first parameter,** ahead of whatever the event
  itself passes.
- **Setting or clearing Index never rewrites a handler, and doesn't warn.** The mismatch surfaces at F5, as a compile
  error naming no procedure in its text, though the code window highlights the declaration.
- **What VB6 compares is the parameters' types and passing, not their names.** A renamed `Index` runs and receives
  the index. `As Long` for `As Integer` is the compile error, and so is `ByVal` on a parameter the event passes by
  reference. Only the extra parameter `Index` was varied; the event's own arguments, such as MouseDown's, weren't.
- **The type compared is the effective one,** as DefType decides it ([DefType](deftype--default-typing-by-first-letter.md)):
  an untyped `Index` is Variant and refused, but the same declaration under `DefInt I` is Integer and runs, as `Index%`
  does. The obvious reading, that a declaration must say `As Integer`, would have refused two programs VB6 runs.
- **Not measured:**
  - the same error from `/make`;
  - an element whose Index is set to something other than 0;
  - a control array made by naming or pasting a second control (that's #1397);
  - a handler whose other parameters are wrong, such as a MouseDown with a mistyped `Button`.

### For Each over a control array (2026-11-18, #4716)

**The question.** One earlier row, in [a form-based game's runtime surface](a-form-based-games-runtime-surface.md),
found that `For Each c In lblCell` visits elements 0, 1 and 5. What order does the walk take when the file, the
indices and the loads disagree? What may the loop variable be, what does it hold afterwards, and what happens when
the program loads or unloads elements during the walk?

**The probe.**
- **Build:** VB6 SP6, `/make`, native code and p-code, which logged every row identically.
- **The form:** a Label array `L`, whose blocks are written in the file as `L(5)`, `L(0)`, `L(2)`; a TextBox array
  `T(0)`, `T(1)`; and a menu array `mnuA(0)`, `mnuA(1)`.
- **The run:** a Sub Main program loads Form1 and runs each loop from a module, through `Form1.L`.
- **The String-typed loop variable** was built as a project of its own.

| Row | VB6 |
|---|---|
| `For Each c In L` (Variant `c`), printing `Index` and `TypeName` | `0:Label 2:Label 5:Label` |
| `c` after the loop runs out | `Empty` |
| `Load L(9)`, then `Load L(3)`, then walk | `0 2 3 5 9` |
| `Load L(9)`, `Load L(3)`, `Unload L(9)`, `Load L(9)`, then walk | `0 2 3 5 9` |
| `ct As Control`, `o As Object`, or `lb As Label` | `0 2 5`, and the variable is `Nothing` afterwards |
| `tb As TextBox`, over the Labels | **13** at the first element |
| `sv As String` | compile error "For Each control variable must be Variant or Object" |
| `Exit For` at index 2 | `0 2`; `c.Index` is 2 afterwards |
| `Load L(9)`; at index 2, `Unload L(9)` | `0 2 5`, then **340** "Control array element '9' doesn't exist" |
| `L(7)` and `L(9)` loaded; at index 2, `Unload L(7)`, under `On Error Resume Next` | `0 2 5 9`. The body for 9 starts with `Err.Number` 340; `c` is `Empty` after |
| at index 2, `Load L(4)` | `0 2 5` |
| `Load L(3)`; at index 3, `Unload L(3)` | `0 2 3 5` |
| `Load L(7)`; at index 2, `Unload L(7)`, `Load L(7)`, and its Caption set to `new` | `0=L0 2=L2 5=L5 7=new` |
| `Set c = Nothing` inside the body | `0 2 5` |
| a `For Each` over `L` nested in another | 9 passes |
| the TextBox array; the menu array | `0=t0 1=t1`; `0=A0 1=A1` |
| `Set a = L` (`a As Object`), then `For Each c In a`; the same through a Variant | `0 2 5`; `TypeName(a)` is `Object` |

**Readings:**
- **The walk takes the indices, sorted, when the loop starts:** not the order the file lists the blocks in, nor the
  order the program loaded them in. An element loaded during the walk isn't visited.
- **It looks each element up when the walk reaches it.** One unloaded since the start raises 340 there. One
  unloaded and loaded again is visited as the new control. Under Resume Next the 340 is recorded and the walk goes on
  to the next index: the 340 belongs to the step, not to the body.
- **The loop variable** is a Variant or an object reference, as for a Collection. A loop that runs out leaves a
  Variant `Empty` and an object variable `Nothing`, and `Exit For` leaves it holding the element it reached.
  Reassigning it inside the body doesn't disturb the walk.

**Not measured:** a CommonDialog array of more than one element (#4148).
