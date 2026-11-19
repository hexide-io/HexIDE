## Complete Word, a bare word, and the selected item (2026-11-19, #3613, design-time, IDE, SP6)

**The question.** In ordinary code, with no `.` before the caret, does VB6's code window open a completion list as a word
is typed? If it doesn't, Space has nothing to commit. And when Ctrl+Space opens Complete Word's list, which item is
selected, in what order, whether case matters, and what happens when nothing matches? Asked for #3613, because HexIDE
opened a list on every letter typed, and `tot` Space became `total `.

**The obvious guess was that Auto List Members lists as you type, as modern editors do. It doesn't: no list opens for
a bare word.** `tot` Space leaves `tot `. Only a `.` (#3056) or Ctrl+Space opens a list. Complete Word's list then
holds no statement keywords or operators at all, so a typed `x` has only the declared `x` to match, and `Xor` never
competes with it.

### The probe

VB6's IDE, SP6 (VB6.EXE 6.00.9782), on the Windows 10 oracle guest, in its interactive console session. Auto List
Members was on. As a control, `Debug.Pr` opened the list by itself with `Print` selected, and Space gave `Debug.Print `,
as #3056 recorded.

The project was a Standard EXE whose only module was this. Each case was typed on the empty line in `Sub Main`, in a
freshly started IDE:

```vb
Attribute VB_Name = "Module1"
Option Explicit
Dim zeta1 As Long, zeta2 As Long, zebra As Long

Sub Main()
    Dim x As Object
    Dim total As Long

End Sub
```

**How the result was read.**
- **The list:** after each key, its item count and selection came from the list box's `LB_GETCOUNT`, `LB_GETCURSEL` and
  `LB_GETCARETINDEX`. Its items are owner-drawn and return no text, so which item was selected or outlined comes from a
  screenshot taken after each key.
- **The line:** #3056's method. After the keys under test the driver typed `q` where the caret was left. Shift+Home and
  Ctrl+C then copied from the start of the line to the caret, without leaving the line. "Read back" below is that copy.

### What VB6 did

"Selected" means the item was highlighted. "Outlined" means it had only a dotted focus rectangle and no highlight.

| Keys, in order | The list | Read back |
|---|---|---|
| `x` Space | none opened, after `x` or after Space | `x q` |
| `tot` Space | none opened | `tot q` |
| `x` Ctrl+Space Space | none: `x` was completed at once, as the only item that starts with `x` | `x q` |
| `ze` Ctrl+Space Space | 1,017 items, `zebra` selected, ahead of `zeta1` and `zeta2` | `zebra q` |
| `ZE` Ctrl+Space Space | the same, `zebra` selected | `zebra q` |
| `fo` Ctrl+Space Space | `FormArrangeConstants` selected, then `Format`, `Format$`, `FormatCurrency`, … | `FormArrangeConstants q` |
| `zq` Ctrl+Space Space | opened with nothing selected (`LB_GETCURSEL` -1), `zebra` outlined | `zq q` |
| `xo` Ctrl+Space | opened with nothing selected, `x` outlined | `xoq`, with no Space sent |
| `Debug.Pr` Space (the control) | opened by itself after the `.`, 2 items, `Print` selected | `Debug.Print q` |

The list around `x` read, in order: `WeekdayName`, `x`, `Year`, `zebra`, `zeta1`, `zeta2`, `ZOrderConstants`.

### The rule

- **No list opens by itself for a bare word** at the start of a statement, whether or not a declared name starts with
  it (`x`, `tot`). Space just types a space.
- **Complete Word's list has no statement keywords or operators.** `Xor` and `For` aren't in it: `fo` lands on
  `FormArrangeConstants`, the first name in the list that starts with `fo`. So `x` Ctrl+Space finds one match and
  completes it at once, with no list, as #3056 saw for `zeb`.
- **The order is alphabetical, ignoring case.** `x`, `Year`, `zebra`, `zeta1`, `zeta2`, `ZOrderConstants`.
- **The first item that starts with the typed text, ignoring case, is selected.** Space commits it in the item's own
  case, so `ZE` gives `zebra`.
- **With no item that starts with the typed text,** the list opens with nothing selected and the nearest item in the
  list's order outlined (`zq`: `zebra`; `xo`: `x`). Space inserts nothing.

### Not measured

- **A bare word after other text on the line,** such as `y = tot` or `Call tot`. Every case started a statement.
- **The arrow keys** in a Complete Word list with nothing selected. #3056 measured that after `Debug.`, the first Down
  selects the outlined item.
- **Auto List Members turned off.** Every case ran with it on.

### What HexIDE does with this

Decided on #3613: HexIDE still opens a list as a word is typed, which VB6 doesn't, but a list opened that way has
nothing selected. So Space and Enter type through, as they do in VB6 when nothing is selected. Tab takes the matching
item, and the first Down or Up selects it, following #3056's rule for an outlined item. Lists opened by `.` or by
Complete Word select as VB6 does.

The alphabetical order and the absence of keywords aren't followed: HexIDE orders by the server's `sortText`, and the
bundled server's list includes keywords. Both are their own decisions.
