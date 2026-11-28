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


## Where VB6's save keeps a TextBox's Text, and the `.frx` record it writes (2026-11-28, #5843, IDE, SP6)

**The question.** #5843: HexIDE refuses to save a form whose multi-line TextBox keeps its `Text` in the `.frx`. What does
VB6's own IDE write for that text? In particular, how wide is the length in front of it, and does that change for
longer text?

**The rig.** VB6.EXE 6.00.9782 (SP6) in a clone of the oracle VM, driven on its console by the IDE step driver.
- **The forms:** each project was a one-form Standard EXE holding #5843's `Form4`, with one TextBox, `Text4`. Its `Text`
  was given each case's way: in the `.frx` as the issue gives it, or quoted in the `.frm`. In the single-line cases,
  `MultiLine` was left out.
- **Each save:** in a fresh vb6.exe, Shift+F7 opened the designer, and Tab selected `Text4`. Ctrl+Right and Ctrl+Left
  followed, then Ctrl+S.
- **Every save happened.** VB6 opened each form with no message, and wrote each `.frm` 11.9 to 12.9 s after it was
  copied in. It changed at most the `Text` line.

| Case | Text as read | `Text` line VB6 wrote | `.frx` VB6 wrote |
|---|---|---|---|
| Multi-line | `.frx`: `05` then `Hello` | `"Form4.frx":0000` | 6 bytes, identical: `05 48 65 6C 6C 6F` |
| Multi-line | `.frx`: `0C` then `Hello`, CR LF, `World` | `"Form4.frx":0000` | 13 bytes, identical |
| Multi-line | `"Hello"` in the `.frm` | `"Form4.frx":0000` | 6 bytes: `05`, then `Hello` |
| Multi-line | `""` | `""`, unchanged | none |
| Multi-line | 254 characters, quoted | `"Form4.frx":0000` | 255 bytes: `FE`, then the text |
| Multi-line | 255 characters, quoted | `"Form4.frx":0000` | 258 bytes: `FF`, then `FF 00`, then the text |
| Multi-line | 300 characters, quoted | `"Form4.frx":0000` | 303 bytes: `FF`, then `2C 01`, then the text |
| Single-line | 100 characters, quoted | unchanged | none |
| Single-line | 254 characters, quoted | `$"Form4.frx":0000` | 258 bytes: `FE 00 00 00`, then the text |
| Single-line | 255 characters, quoted | `$"Form4.frx":0000` | 259 bytes: `FF 00 00 00`, then the text |
| Single-line | 256 characters, quoted | `$"Form4.frx":0000` | 260 bytes: `00 01 00 00`, then the text |
| Single-line | 300 characters, quoted | `$"Form4.frx":0000` | 304 bytes: `2C 01 00 00`, then the text |

**A multi-line TextBox's text goes to the `.frx` whenever it isn't empty,** even five characters with no line break.
- **Up to 254 bytes,** the record is a one-byte length, then the text.
- **From 255 bytes,** the record is `FF`, then a two-byte little-endian length, then the text.
- **The empty text stays in the `.frm`,** as `Text = ""`.

So the issue's hand-written `.frx` was already in VB6's own layout, and VB6 wrote it back byte for byte.

**A single-line TextBox's long text goes to the `.frx` in another shape.** The `.frm` line gains a `$` before the
citation, and the record is a four-byte little-endian length, then the text. 100 characters stayed quoted in the `.frm`,
and 254 went to the `.frx`.

**Not measured:**
- the length at which a single-line text moves to the `.frx`, which is somewhere from 101 to 254;
- a multi-line text of 65,536 bytes or more, past what the two-byte length can hold;
- text outside ASCII, so whether a length counts bytes or characters;
- where VB6 puts a second text record in the same `.frx`, after a picture or a list, for example;
- other controls whose text can go to the `.frx`, and a TextBox's text typed in the Properties window.
