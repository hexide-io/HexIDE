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


