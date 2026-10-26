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

