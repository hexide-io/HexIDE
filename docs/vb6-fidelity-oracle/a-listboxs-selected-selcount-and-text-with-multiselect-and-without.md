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

### More keys, Shift with a drag, Ctrl+Shift+click, Selected() from code, and items added or removed around the focus (2026-11-21, #1424, SP6)

**The question.** #1424 listed what the rows above left unmeasured on a list with MultiSelect 1 or 2:
- Home, End, PageUp, PageDown and a letter key;
- Shift with a drag;
- Ctrl+Shift+click with other items selected;
- `Selected(i) = True` from code;
- `AddItem` and `RemoveItem` around the focused item;
- `List(i) =` on a selected item.

**The probe:**
- **The program drives itself on the guest's console,** as the second probe above was driven: `keybd_event`, `SetCursorPos` and `mouse_event`, at the centre of each item's rectangle (`LB_GETITEMRECT`).
- **The lists:**
  - an Extended and a Simple list of 20 items showing 6 rows, `alpha` to `tango` with `mango` at 15;
  - an Extended, a Simple and a MultiSelect 0 list of 12 items, `a1 b1 c1 a2 b2 c2 d1 e1 f1 d2 e2 f2`, all showing.
- **After each step** it logged:
  - the selected indexes;
  - `ListIndex`;
  - Windows' own focus (caret) index and anchor index, read with `LB_GETCARETINDEX` and `LB_GETANCHORINDEX`;
  - `TopIndex`.
- **The form** was the foreground window throughout.
- **The builds:** `/make` on vb6.exe SP6, as native code and as p-code. The two logged the same lines.

**The keys, from a click on item 2 of the 20-item lists.** Each row follows the one before:

| Key | Extended: selected, focus, anchor | Simple: selected, focus |
|---|---|---|
| Home | 0 alone; 0; 0 | 2, unchanged; 0 |
| End | 19 alone; 19; 19, scrolled to top 14 | 2; 19 |
| PageUp | 14 alone: the top row | 2; 14 |
| PageUp again | 9 alone: a page is five rows, one less than shown | 2; 9 |
| PageDown | 14 alone: the bottom row | 2; 14 |
| M | 15, `mango`: the next item starting with M after the focus | 2; 15 |
| M again | 12, `mike`: the search wraps | 2; 12 |
| D | 3, `delta` | 2; 3 |
| Shift+Home | 0 to 3; focus 0; **anchor stays 3** | 2; 0 |
| Shift+End | 3 to 19; focus 19 | 2; 19 |
| Shift+PageUp | 3 to 14; focus 14 | 2; 14 |
| Ctrl+Home, Ctrl+End, Ctrl+PageUp | 0, 19 and 14 alone, as without Ctrl | 2; the focus moves |
| Space | unchanged | **2 and 14**: Space toggles the focused item, and the anchor becomes 14 |
| Shift+K | **10 to 14**, `kilo` to the anchor | 2 and 14; focus 10 |

- **On an Extended list every one of these keys selects the item it reaches alone,** as Up and Down do. With Shift, it selects from the anchor to that item, and the anchor stays. Shift with a letter extends too.
- **On a Simple list they move only the focus.**

**Shift with a drag,** on the 12-item lists:

| Input | Selected after | Focus | Anchor |
|---|---|---|---|
| Extended: click 1, then Shift+press 3, drag to 6, release | 1 to 6 | 6 | 1 |
| Extended: click 1, then Shift+drag from 6 to 3 | 1 to 3 | 3 | 1 |
| Extended: click 1, Ctrl+click 3, then Shift+drag from 5 to 8 | 3 to 8: 1 is cleared | 8 | 3 |
| Simple: click 1, then Shift+drag from 3 to 6 | 1 and 3: 3 toggled at the press, as without Shift | 6 | 3 |

**On an Extended list a Shift+drag selects from the anchor to the item released over.** The item pressed doesn't matter. On a Simple list Shift changes nothing.

**Ctrl+Shift+click,** on the Extended list:

| Before | Then | Selected after | Focus, anchor |
|---|---|---|---|
| click 0, Ctrl+click 8, Ctrl+click 5: 0, 5 and 8 | Ctrl+Shift+click 3 | **0, 3, 4, 5 and 8** | 3, 5 |
| the same | Shift+click 3 | 3, 4 and 5 | 3, 5 |
| click 0, Ctrl+click 2, Ctrl+click 6: 0, 2 and 6 | Ctrl+Shift+click 8 | **0, 2, 6, 7 and 8** | 8, 6 |
| click 6, Ctrl+click 1, Ctrl+click 6: only 1, and the anchor 6 not selected | Ctrl+Shift+click 9 | **1**, unchanged | 9, 6 |

**Ctrl+Shift+click adds the range from the anchor to the item, and keeps what was selected outside it.** Shift+click alone clears the rest. When the anchor isn't selected, the range takes the anchor's state: nothing was added. The obvious guess, that Ctrl+Shift+click gives the range alone as Shift+click does, is wrong.

The last row of "The anchor" above fits the same rule. There, the items the previous Shift+click had added beyond the anchor were cleared at the press. That's reasoned from the two probes, not measured as one sequence.

**`Selected(i)` from code,** on the Extended list:

| Steps | Selected after | Focus | Anchor |
|---|---|---|---|
| click 1, then `Selected(5) = True` | 1 and 5 | **5** | **5** |
| then Shift+Down | 5 and 6 | 6 | 5 |
| click 1, `Selected(5) = True`, then Shift+click 8 | 5 to 8 | 8 | 5 |
| click 1, then `Selected(1) = False` | none | 1 | 1 |
| then Shift+click 4 | **2 to 4**: the deselected anchor isn't in the range | 4 | 1 |
| click 3, then `Selected(7) = True` and `Selected(9) = True` | 3, 7 and 9 | 9 | 9 |
| then Down | 10 alone | 10 | 10 |

**`Selected(i) = True` moves the focus and the anchor to item i,** and `ListIndex` reads i. A following Shift key or Shift+click ranges from there. `Selected(i) = False` on the anchor leaves the anchor there, deselected.

**`AddItem` and `RemoveItem` around the focus,** on the 12-item lists, each from a click on item 4:

| Step | Simple and Extended: selected, focus, anchor | MultiSelect 0: selected and `ListIndex` |
|---|---|---|
| `AddItem "zz", 1` | **5**, the same item; focus **4**, the index kept; anchor 4 | 5; ListIndex **5**: it follows the item |
| then `RemoveItem 0` | 4; 4; anchor **-1** | 4; 4 |
| then `AddItem "yy"`, at the end | unchanged | unchanged |
| `RemoveItem 4`, the focused item | none; focus 3 | none; ListIndex **-1**, focus 3 |
| from a click on 11, the last: `RemoveItem 11` | none; focus 10, the new last | none; ListIndex -1, focus 10 |

- **The selection follows its items.**
- **On a multi list the focus keeps its index,** so it lands on another item when one is added or removed before it.
- **Removing the focused item moves the focus to the one before it.**
- **Each `RemoveItem` reset the anchor to -1:** one before it, the anchor itself, and the last item. An `AddItem` before it left the anchor's index at 4, now on another item.
- **On a MultiSelect 0 list `ListIndex` follows its item, and becomes -1 when that item is removed.**

**`List(i) =` on a selected item:**
- After a click on 4, `List(4) = "new"` left 4 selected, with the focus on 4, on all three lists.
- With 2 and 4 selected and the focus on 4, `List(2) = "new2"` left both selected **and moved the focus and the anchor to 2**, on the Simple and the Extended list.

**Not measured:**
- **The focus rectangle's drawing.** The focus index above says which item has it.
- **A list scrolled away from its top during a drag.**
