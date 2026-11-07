## A form's WindowState (2026-10-17, #1152)

**The probes.** Found by the Notepad demo, whose `Form_Resize` begins `If WindowState = vbMinimized Then Exit Sub`.
Two probes, each built native and p-code, and run **in the guest's console session**. In the probe runner's session,
which has no desktop, every change read back 0, a result about that session rather than about VB6. In the first, Form1
sets and reads WindowState in `Form_Load`, loads and shows a Form2 whose file says `WindowState = 2 'Maximized`, then
from a Timer, once shown, sets each state. Its `Form_Resize` logs WindowState and Width each time. In the second,
`Form_Load` sets `vbMaximized`, and a Timer reads it once the form is shown.

| Step | Logged |
|---|---|
| `TypeName(WindowState)` in `Form_Load` | `Integer` |
| in `Form_Load`: read, then set 1, set 2, set 0, reading after each | 0, 0, 0, 0: a form not yet shown reads 0 |
| set 3, set -1 | 380, "Invalid property value", for each |
| set 1.6, set `"0"` | taken, no error |
| set Null | 94 |
| Form2, saved maximized: loaded, then shown | 0, then 2 |
| Form1 shown, then set 2, 1, 0 from the Timer | 2, 1, 0, each with a Resize: Width 15600, 2400, 4740 |
| the second probe: `vbMaximized` set in `Form_Load`, read once shown | 0 in Load, then one Resize with 2, and 2 |

So a state set before the form is shown, in code or in the file, is the state it shows in, and reads 0 until then.
2400 twips is the width Windows gives a minimized window.

**In HexIDE (#1152):** WindowState reads and writes as above, on a form's window, and a form saved with a WindowState shows
in it. Bare and qualified reads use the same getter. Resize comes from the window's size changing, except on minimizing:
that state transition raises Resize explicitly because Avalonia's Windows backend does not report a client resize.
Duplicate resize notifications while minimized are suppressed. Not reproduced: a minimized window's 2400-twip
Width, which Avalonia doesn't report, and what a form hidden after being shown reads, which was not measured.

### A person's maximize, restore, minimize and border drag (2026-10-27, #2389)

**The probe:** a sizable form, 4500 × 3000 twips of client, in the guest's console session at 15 twips a pixel, with
"Show window contents while dragging" on. It logs Load, Activate, Deactivate, GotFocus, LostFocus, Paint and Resize,
each with WindowState, Width, Height, ScaleWidth and ScaleHeight read in the handler. A driver acted on it in two ways:
- with real clicks on the caption buttons, found through `WM_GETTITLEBARINFOEX`;
- with the `WM_SYSCOMMAND` each click sends, `SC_MAXIMIZE`, `SC_RESTORE` and `SC_MINIMIZE`.

A restore from minimized was `SC_RESTORE` both times, not a click on the taskbar. Then the driver pressed on the right
border and moved 20 pixels right five times, 400 ms apart, before releasing. Then it dragged the bottom-right corner
40,30 pixels in one move. Native and p-code logged the same 48 lines, and the clicks logged what the commands did.

| Change | Events, in order | Read in Resize |
|---|---|---|
| Load and Show | Load, Resize, Activate, GotFocus, Paint | state 0, Width 4740, Height 3585, ScaleWidth 4500, ScaleHeight 3000 |
| Maximize | **one Resize**, then Paint | state 2, Width 15600, Height 11160, ScaleWidth 15360, ScaleHeight 10575 |
| Restore from maximized | **one Resize**, and no Paint | state 0, the shown size again |
| Minimize | **one Resize** | state 1, Width 2400, Height 420, ScaleWidth 0, ScaleHeight 0 |
| Restore from minimized | **one Resize**, then Paint; **no Activate** | state 0, the shown size |
| The right border, dragged in five 20-pixel moves | **one Resize and one Paint per move**, none on release | Width 5040, 5340, 5640, 5940, 6240; ScaleWidth 300 more each time |
| The corner, dragged in one move | one Resize, then Paint | Width 6840, Height 4035, ScaleWidth 6600, ScaleHeight 3450 |

So every change of the window raises exactly one Resize, reading the final state and size, and Paint follows only
when the client grows. Minimizing and restoring raise neither Deactivate nor Activate, nor LostFocus nor GotFocus:
those are for moving between the program's own forms. A drag raises Resize as the window changes, not once at the end,
with window contents shown while dragging. The setting-off case was not measured.

**Provenance:** #2389, issuecomment-6450545014; native and p-code logs recorded in PR #2279, commit `d48c00d9`.
