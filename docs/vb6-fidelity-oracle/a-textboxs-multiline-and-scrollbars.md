## A TextBox's MultiLine and ScrollBars (2026-10-22, #1340)

**The probe.** Found by the Notepad demo: Enter added nothing to its text, and Word Wrap, which swaps a box that has a
horizontal scroll bar for one that has none, changed nothing. A form with a control array of six TextBoxes, each 3000 by
1100 twips: single-line, then MultiLine with ScrollBars 0, 1, 2 and 3, then single-line with ScrollBars 3. `Form_Load`
reads both properties and tries to set them. A Timer then reads each box's window style with `GetWindowLong` and its
line count with `EM_GETLINECOUNT`, for a 368-character line with spaces in it and for `"a" & vbCrLf & "b" & vbLf & "c"
& vbCr & "d"`. Last, it types into the boxes with `SendKeys`, logging KeyDown and KeyPress. Built native and p-code,
and every row agreed. The styles and line counts came out the same in the probe runner's session and the console
session, but the keys reach nothing without a desktop, so they were run in the console session (see the
`ForegroundLockTimeout` trap above).

| Box | Window style | Lines for the long line | Lines for the CR, LF, CR LF string |
|---|---|---|---|
| single-line | `&H540100C0`: `ES_AUTOHSCROLL`, `ES_AUTOVSCROLL` | 1 | 1 |
| MultiLine, ScrollBars 0 | `&H54010044`: `ES_MULTILINE`, `ES_AUTOVSCROLL` | 18 | 2 |
| MultiLine, ScrollBars 1 (horizontal) | `&H541100C4`: and `ES_AUTOHSCROLL`, `WS_HSCROLL` | 1 | 2 |
| MultiLine, ScrollBars 2 (vertical) | `&H54210044`: and `WS_VSCROLL` | 18 | 2 |
| MultiLine, ScrollBars 3 (both) | `&H543100C4`: and both scroll bars | 1 | 2 |
| single-line, ScrollBars 3 | `&H540100C0`, the same as the first | 1 | 1 |

So a MultiLine box wraps unless it has a horizontal scroll bar, and a single-line box ignores ScrollBars. Only CR LF
breaks a line. A lone LF or CR doesn't, and `Text` reads back all 8 characters in every box. No box has
`ES_WANTRETURN`, so Enter is VB6's own handling, below, not the edit control's.

| Step | Logged |
|---|---|
| `TypeName` of MultiLine and ScrollBars | `Boolean`, `Integer` |
| `o.MultiLine = False` with `o` an `Object` holding the box | **382**, "'MultiLine' property cannot be set at run time"; still True |
| `o.ScrollBars = 0` | **382**, "'ScrollBars' property cannot be set at run time"; unchanged |
| `Text1.MultiLine = True`, early-bound | the compile error "Can't assign to read-only property"; the build fails |
| `x`, Enter, `y` in the single-line box | KeyDown 13, KeyPress 13; `xy` |
| `x`, Enter, `y` in a MultiLine box | KeyDown 13, KeyPress 13; `x` CR LF `y` |
| `x`, Tab, `y` in a MultiLine box | the `y` went to the next box: focus moved, no tab inserted |
| a Default CommandButton, then `x`, Enter, `y` in a MultiLine box | the button's Click, no KeyDown or KeyPress for the Enter; `xy` |
| the same, with Ctrl+Enter | KeyDown 13 with Shift 2, KeyPress **10**; `x` CR LF `y` |

**In HexIDE (#1340):** a box is laid out from its file's MultiLine and ScrollBars as the first table says. Enter and
Ctrl+Enter break a MultiLine box's line with CR LF on every host, a bar VB6 asks for is drawn even with nothing to
scroll, and a direction with no bar is either scrolled without one or not at all, which also keeps a single-line box
from showing part of a vertical bar (#914). Both read back with their types, and setting either is the 382 with VB6's
text, raised when the statement runs, also where VB6 refuses it at compile time. Not reproduced:
- a CommandButton's `Default`, which HexIDE doesn't model, so Enter always breaks the line;
- KeyDown and KeyPress, which a TextBox doesn't raise here yet;
- a lone LF or CR, which Avalonia draws as a line break where VB6 draws none. `Text` still reads back as set.

