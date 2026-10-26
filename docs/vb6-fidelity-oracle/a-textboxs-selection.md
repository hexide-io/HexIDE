## A TextBox's selection (2026-10-17, #1150)

**The probe.** Found by the Notepad demo, which sets `SelStart` straight after reading a file, and selects with
SelStart and SelLength to find text. A form with three TextBoxes: Text1, single-line with Text "Text1"; Text2,
MultiLine; and Text3, Locked. Its `Form_Load` logs each step under `On Error Resume Next`. Built native and p-code,
and every row agreed.

| Step | Text, SelStart, SelLength, SelText, Err |
|---|---|
| `TypeName` of SelStart, SelLength, SelText | `Long Long String` |
| at design time | `Text1`, 0, 0, empty |
| `Text1.Text = "Hello world"` | 0, 0: setting Text resets the selection |
| `SelStart = 6` | 6, 0 |
| `SelLength = 5` | 6, 5, `world` |
| `SelText = "there"` | `Hello there`, 11, 0: the caret ends after what was put in |
| SelStart 2, SelLength 3, then `SelStart = 4` | 4, 0: setting SelStart drops the selection |
| `SelStart = 100` | 11, 0, no error: the end |
| `SelStart = -1` | 11, 0, **380** |
| `SelStart = 6`, `SelLength = 100` | 6, 5, `there`, no error |
| `SelLength = -1` | 6, 5, **380** |
| at 0 with nothing selected, `SelText = ">"` | `>Hello there`, 1, 0 |
| SelStart 3, SelLength 2, then `Text = "new"` | `new`, 0, 0 |
| `SelStart = 1.6`, then `SelStart = "2"` | 2, then 2 |
| `SelText = Null`, `SelStart = Null` | **94** each, nothing changed |
| `Text2.Text = "ab" & vbCrLf & "cd"`, SelStart 3, SelLength 2 | 3, 2, LF and `c`: the caret can sit between the CR and the LF |
| SelStart 4, SelLength 1 | `c` |
| `SelStart = Len(Text)`, `SelText = vbCrLf & "ef"` | `ab CRLF cd CRLF ef`, 10 |
| Text3 (Locked) `"locked"`, start 0, length 1, `SelText = "L"` | `Locked`, 1: Locked keeps out the user, not code |
| `Text1.MaxLength = 5`, Text `12345`, SelStart 5, `SelText = "678"` | `12345`, 5: SelText keeps to MaxLength |

**In HexIDE (#1150):** every row runs as measured but the last, since HexIDE's TextBox doesn't model MaxLength. Avalonia
moves a caret placed inside a line break past it, so the selection code sets is remembered and read back while
Avalonia's has not moved. Once the user moves it, the selection is Avalonia's.

### Keys after a selection set from code (2026-10-22, #1385)

**The probe.** Found by the Notepad demo: Select All from its menu, then Ctrl+End and Ctrl+V, pasted at the start in
HexIDE. A form with a single-line box (Text1) and two MultiLine ones (Text2, Text3). A Timer sets each selection from code,
straight after `SetFocus`, then sends the key with VB6's own `SendKeys`, and the next tick logs `SelStart` and
`SelLength`. It ran in the guest's console session, where the form has the foreground. Native and p-code agreed on every
row. The same build run in session 0, with no foreground, logged nothing changed; read that as no input, not as VB6's
answer.

| Box, text `abcdef` | Selection from code | Key | Then SelStart, SelLength, Text |
|---|---|---|---|
| Text1 | 0, 6 | Ctrl+End | 6, 0 |
| Text2 (MultiLine) | 0, 6 | Ctrl+End | 6, 0 |
| Text1 | 2, 0 | Ctrl+End | 6, 0 |
| Text1 | 2, 2 | Ctrl+Home | 0, 0 |
| Text2 (MultiLine) | 2, 2 | Ctrl+Home | 0, 0 |
| Text1 | 0, 6 | Ctrl+Home | 0, 0 |
| Text1 | 2, 2 | `X` typed | 3, 0, `abXef` |

And the Notepad's own sequence, in Text3: `Text = "ab" & vbCrLf & "cd"`, `SelStart = 0`, `SelLength = Len(Text)`,
`Clipboard.SetText SelText`, then the keys.

| After | SelStart, SelLength | Text |
|---|---|---|
| the selection and the copy | 0, 6 | `ab CRLF cd` |
| Ctrl+End | 6, 0 | `ab CRLF cd` |
| `e` typed | 7, 0 | `ab CRLF cde` |
| Ctrl+V | 13, 0 | `ab CRLF cdeab CRLF cd` |

None of this is surprising: it's the Win32 edit control's own behaviour. It's recorded because HexIDE got the first row
wrong.

**In HexIDE (#1385):** every row runs as measured. Avalonia keeps its caret at a selection's anchor, `SelectionStart`, and
moves `SelectionEnd`. Code's selection used to leave the caret at its end, and with that at the end of the text, Ctrl+End
found nothing to move and collapsed the selection to its start, 0. The issue first read this as `SelStart` reading the
wrong place. It read the right one: the paste had gone in at 0, and with the whole text copied, the result looks the same
from either end. Typing a character after Ctrl+End, as the probe does, tells them apart.
