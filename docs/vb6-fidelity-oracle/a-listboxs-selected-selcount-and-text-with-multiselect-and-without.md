## A ListBox's Selected, SelCount and Text, with MultiSelect and without (2026-10-22, #1395)

One `/make` probe, built native and p-code, **all 201 lines agreeing**. Its form holds three ListBoxes:

- `List1`, with MultiSelect 0;
- `List2`, with MultiSelect 1, Simple;
- `List3`, with MultiSelect 2, Extended.

After `Me.Show` in Form_Load, each list is filled with `a`, `bc`, `b`, `c`, `b`, so `bc` starts with `b` and `b` comes twice. Then each runs the same statements under `On Error Resume Next`. After every statement, and inside every Click, the probe logs `ListIndex`, each item's `Selected`, `SelCount` and `Text`. **Simple and Extended agree on every row**, so "multi" below means either.

**Reading.** The same on all three lists:

| Read | Result |
|---|---|
| `Selected(0)` | `False`, a Boolean |
| `Selected(5)`, at `ListCount`; `Selected(-1)`, `(100)`, `(-2)` | 381, "Invalid property array index" |
| `Selected(40000)` | 6, Overflow |
| `Selected("1")`, `Selected(1.6)` | `False`, Err 0: the index is coerced |
| `Selected(0)` on an empty list | 381 |
| `SelCount` | an Integer: 0 with nothing selected, then the number selected |
| `Text`, nothing selected, MultiSelect 0 | `""`, a String |
| `Text`, nothing selected, multi | `a`: the item at `ListIndex` |

**Writing `Selected`, with MultiSelect 0.** Each row follows the one above it:

| Statement | Click | Then |
|---|---|---|
| `Selected(1) = True`, from -1 | once, `ListIndex` 1 inside it | `bc` selected |
| `Selected(1) = True`, already selected | none | |
| `Selected(3) = True` | once, `ListIndex` 3 | `c` alone: the selection moves |
| `Selected(3) = False`, the selected item | once, `ListIndex` -1 | nothing selected |
| `Selected(0) = False`, not selected | none | |
| `Selected(5) = True`, at `ListCount`; `Selected(-1) = True` or `False`; `Selected(100) = True` | none | 381, and nothing changes |
| `Selected(2) = "True"`, then `= 0`, then `= 5` | once each | selected, cleared, selected: any non-zero number is True |
| `Selected(2) = Null` / `Selected(2) = "x"` | none | 94 / 13 |
| `Selected(0) = True` on an empty list | none | 381 |

**Writing `Text`, with MultiSelect 0.** **Every `Text =` raises Click, once**, even when the selection doesn't move. Here it differs from `Selected` and `ListIndex`, which raise it only when the selection moves.

| Statement | `ListIndex` after, and inside the Click |
|---|---|
| `Text = "b"`, from -1 | 2: the first item whose whole text matches. Neither `bc`, which only starts with it, nor the second `b` |
| `Text = "B"`, from -1 | 2: without regard to case |
| `Text = "x"`, no item's text, from -1 | -1, and Click still comes |
| `Text = "x"`, from 0 | -1 |
| `Text = ""`, from 0 | -1 |
| `Text = "bc"`, then `Text = "bc"` again | 1, then 1, with a Click each time |
| `Text = 5` | -1: it is the text `5`, which no item has |
| `Text = Null` | 94, no Click |
| `Text = "a"` on an empty list | -1, with a Click |

**With MultiSelect 1 or 2, `ListIndex` is the item with the focus, not a selected one.**
- After `AddItem` it is 0 with nothing selected, and after `Clear` it is 0 with no items at all.
- `Selected(i) = True` adds item i to the selection, raises Click, and moves `ListIndex` to i. So `Selected(1) = True` then `Selected(3) = True` leaves both selected, `SelCount` 2.
- `Selected(i) = False` removes it, raises Click, and leaves `ListIndex` where it was. Setting `Selected` to what it already is raises none, as with MultiSelect 0.
- `ListIndex = n` moves the focus and raises Click, and selects nothing and clears nothing. `ListIndex = -1` leaves `Text` empty.
- **`Text = s` raises Click and changes nothing else**, whatever `s` is: no item is selected or focused, even one whose text is `s`.
- The errors are MultiSelect 0's: 381, 6, 94 and 13, for the same statements.

**With real input** (#1413). A second probe has `List1` with MultiSelect 1 and `List2` with MultiSelect 2, each holding `a` to `f`, and every handler logs the list's state as above. It ran in the guest's console session, driven by `SetCursorPos`, `mouse_event` and `keybd_event`. It was built native and p-code, and **all 117 lines agree**. Each row follows the one above it, on the same list. "Click" is the list's Click, which comes between MouseDown and MouseUp, as with MultiSelect 0.

MultiSelect 1, Simple:

| Input | Events | Selected after | `ListIndex` |
|---|---|---|---|
| click `a` | MouseDown, Click, MouseUp; `a` is already selected inside MouseDown | `a` | 0 |
| click `c` | the same | `a`, `c` | 2 |
| click `a` again | the same | `c` | 0 |
| Shift+click `e` | the same: Shift does nothing | `c`, `e` | 4 |
| Ctrl+click `f` | the same: Ctrl does nothing | `c`, `e`, `f` | 5 |
| Up | KeyDown, Click | unchanged: only the focus moves | 4 |
| Space | KeyDown, Click | `e` toggled off: `c`, `f` | 4 |
| Down, then Space | Click each time | `f` toggled off: `c` | 5 |
| Shift+Up | KeyDown for each key, then Click | unchanged | 4 |
| double click `b` | MouseDown, Click, MouseUp, DblClick, MouseUp | `b` toggled once: `b`, `c` | 1 |
| press `d`, drag to `f`, release | MouseDown, then Click with `ListIndex` 5, then MouseUp | `d` toggled at the press, and nothing else: `b`, `c`, `d` | 5 |
| click below the items | MouseDown, MouseUp, no Click | unchanged | 5 |
| right click `e` | MouseDown and MouseUp with Button 2, no Click | unchanged | 5 |

MultiSelect 2, Extended:

| Input | Events | Selected after | `ListIndex` |
|---|---|---|---|
| click `a` | MouseDown, Click, MouseUp | `a` alone | 0 |
| click `c` | the same | `c` alone | 2 |
| Ctrl+click `e` | the same | `c`, `e` | 4 |
| Ctrl+click `c` | the same | `e` | 2 |
| Shift+click `f` | inside MouseDown nothing is selected; then Click and MouseUp | **`d`, `e`, `f`** | 5 |
| Up | KeyDown, Click | `e` alone | 4 |
| Shift+Up | KeyDown for each key, then Click | `d`, `e` | 3 |
| Ctrl+Up | KeyDown for each key, then Click | `c` alone, as Up alone would give | 2 |
| Ctrl+Space | KeyDown for each key, then Click | unchanged | 2 |
| Space | KeyDown, Click | unchanged | 2 |
| press `a`, drag to `d`, release | MouseDown with `a` alone, then Click, then MouseUp | `a` to `d` | 3 |
| double click `b` | MouseDown, Click, MouseUp, DblClick, MouseUp | `b` alone | 1 |
| click below the items | MouseDown, MouseUp, no Click | unchanged | 1 |
| right click `e` | MouseDown and MouseUp with Button 2, no Click | unchanged | 1 |

So:
- **A left press on an item always raises Click, at the release.** A press below the items and a right press change nothing and raise none, as with MultiSelect 0.
- **Simple toggles the item pressed** and ignores Shift and Ctrl. A drag moves only the focus, and the arrow keys move only the focus. Space toggles the focused item.
- **Extended selects the item pressed alone.** Ctrl toggles it. A drag selects from the item pressed to the one released over. Up and Down select the next item alone, and Shift with either extends the selection.
- **Every key the list takes raises Click**, whether or not anything changed: Up on a Simple list, and Space on an Extended one. With MultiSelect 0 a key raises Click only when the selection moves (*A ListBox's Click*).
- **The Shift+click looked unexplained, and a third probe explained it.** After Ctrl+click `c` had removed `c`, leaving `e`, Shift+click `f` selected `d` to `f`. That is neither `c` to `f`, from the last item clicked, nor `e` to `f`, from the one still selected. The rule, from the probe below, is that the range runs from the anchor and the anchor keeps its own state. Ctrl+Up selecting `c` alone is measured too, not inferred.

**The anchor** (a third probe, the second's form with `ListIndex` and `Text` logged before any `AddItem`). It was driven the same way, on the Extended list only, and **native and p-code agree**. Each group starts from a plain click:

| Input | Inside MouseDown | Selected after | `ListIndex` |
|---|---|---|---|
| click `c`, Shift+click `f` | `c` | `c` to `f` | 5 |
| click `a`, Ctrl+click `c`, Shift+click `f` | `c`: `a` is cleared at the press | `c` to `f` | 5 |
| click `c`, Ctrl+click `c`, so nothing is selected, then Shift+click `a` | nothing | **`a`, `b`**: the range stops short of `c` | 0 |
| click `c`, Ctrl+click `e`, Ctrl+click `c`, Shift+click `f` | nothing | **`d` to `f`**, as before | 5 |
| click `e`, Shift+click `b` | `e` | `b` to `e` | 1 |
| click `b`, Shift+Down, Shift+Down, Shift+Up | | `b`, `c`; then `b` to `d`; then `b`, `c` | 2, 3, 2 |
| click `a`, Ctrl+click `d`, Shift+click `f` | `d` | `d` to `f`: `a` is cleared | 5 |
| then Ctrl+Shift+click `b` | `d`: `e` and `f` are cleared at the press | `b` to `d` | 1 |

So **Shift selects from the anchor to the item and clears the rest.** The anchor is the last item pressed or reached without Shift: by a click, a Ctrl+click or a plain arrow key. The anchor keeps its own state: when Ctrl+click has just deselected it, the range is every item from the anchor to the one clicked, less the anchor. The press leaves only the anchor's own state, and the range appears by the Click at the release. Ctrl+Shift+click gave the same range as Shift+click here, with nothing else selected to show whether it would have kept other items.

**Before any `AddItem`** a multi list's `ListIndex` is already 0, on both lists, and `Text` is `""`.

**`MultiSelect` itself.** `List1.MultiSelect` reads 1, an Integer, and `List2.MultiSelect` 2. Assigning it, `List1.MultiSelect = 0`, is the compile error "Can't assign to read-only property", built as p-code. A form re-saved by the VB6 IDE, with hexide-frm's #120 recipe (bare values, the code dirtied, then Ctrl+S), writes `MultiSelect     =   1  'Simple` and `MultiSelect     =   2  'Extended`, after `Left` and before `TabIndex`. A list given `MultiSelect = 0` has the line dropped.

