## The code window's Object and Procedure boxes follow the caret (2026-11-21, #936, design-time, IDE, SP6)

**The question.** Do VB6's Object and Procedure boxes change as the caret moves through the code? HexIDE's don't, so
they show a stale choice (#936). If VB6's do, what does each kind of line select? The kinds are an event procedure, a
general procedure, the declarations, `Form_Load`, and the blank lines and comments between two procedures.

**The probe.** VB6's IDE, SP6 (VB6.EXE 6.00.9782), in the console session of a clone of the oracle guest, through the
agent. A Standard EXE project held one form, `Form1`, with one CommandButton, `Command1`, and this code:

```vb
Option Explicit
Dim m As Long

Private Sub Command1_Click()
    m = 1
End Sub

Private Sub Foo()
    m = 2
End Sub

' comment above Form_Load
Private Sub Form_Load()
    m = 3
End Sub
```

1. The form's code window was opened with Ctrl+R, End and F7. It showed the whole module, with separator lines.
2. Ctrl+Home put the caret on the first line.
3. Down moved the caret one line at a time. After each move, both boxes' current entries were read with `CB_GETCURSEL`
   and `CB_GETLBTEXT`.
4. Screenshots at four of the lines show the toolbar's `Ln` and where VB6 draws its separators.

| Ln | Line | Object box | Procedure box |
|---|---|---|---|
| 1 | `Option Explicit` | (General) | (Declarations) |
| 2 | `Dim m As Long` | (General) | (Declarations) |
| 3 | blank | Command1 | Click |
| 4 | `Private Sub Command1_Click()` | Command1 | Click |
| 5 | `m = 1` | Command1 | Click |
| 6 | `End Sub` | Command1 | Click |
| 7 | blank | (General) | Foo |
| 8 | `Private Sub Foo()` | (General) | Foo |
| 9 | `m = 2` | (General) | Foo |
| 10 | `End Sub` | (General) | Foo |
| 11 | blank | Form | Load |
| 12 | `' comment above Form_Load` | Form | Load |
| 13 | `Private Sub Form_Load()` | Form | Load |
| 14 | `m = 3` | Form | Load |
| 15 | `End Sub` | Form | Load |

### Readings

- **Both boxes follow the caret on every line**, including the declarations, where they read `(General)` and
  `(Declarations)`.
- **A line between two procedures belongs to the procedure below it.** That holds for blank lines and for a comment,
  and for the blank line under the declarations. The screenshots draw each separator directly under the last
  declaration or the previous `End Sub`, which is the same boundary.
- **An event procedure shows its object and its event:** `Command1` and `Click`, and `Form` and `Load` for `Form_Load`. A
  general procedure shows `(General)` and its name.
- **While closed, each box held only the entry it showed** (`CB_GETCOUNT` was 1). This agrees with #4783, where the
  Procedure box was filled when dropped down.

**Not measured:**
- moving the caret with the mouse, Page Down or Ctrl+Down;
- Procedure View, rather than Full Module View;
- whether choosing the entry already shown moves the caret to that procedure (#936's "picking it again does nothing");
- a class module, a `.bas`, and a procedure whose name contains an underscore but isn't an event.

**In HexIDE.** #936 tracks the boxes following the caret.
