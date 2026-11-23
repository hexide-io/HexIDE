## A ListBox's selected and focused items, drawn (2026-11-23, #5473, SP6)

**A selected item is filled with Highlight and its text drawn in Highlight Text, whether or not the list has the focus.** Only the focused list marks an item further: a dotted rectangle on its `ListIndex` item, which is drawn whether that item is selected or not.

A checkbox list (`Style = 1`) is the exception. Checking an item doesn't highlight it. The `ListIndex` item is highlighted instead, from just right of its checkbox.

**The probe.**
- **The form:** five ListBoxes, each holding Alpha, Bravo, Charlie, Delta and Echo, and a CommandButton:

  | List | Style | MultiSelect | Set in `Form_Load` |
  |---|---|---|---|
  | `lst0` | 0 | 0 | `ListIndex = 1` |
  | `lst1` | 0 | 1 | `Selected(1)` and `Selected(3)` True, then `ListIndex = 2` |
  | `lst2` | 0 | 2 | the same as `lst1` |
  | `lst3` | 1 | 0 | `Selected(1)` True, then `ListIndex = 3` |
  | `lst4` | 1 | 0 | `Selected(1)` True, then `ListIndex = 1` |

- **The read:**
  1. A Timer gave the CommandButton the focus, read every list, then gave each list the focus in turn and read every list again. Each change was followed by 0.6 seconds of `DoEvents`.
  2. A read takes each item's rectangle from `LB_GETITEMRECT`. It runs `GetPixel` on the list's own DC along three lines of that rectangle (its top row, its middle row and its bottom row) and counts each colour met.
  3. The form was the foreground window in every pass, and `Me.ActiveControl` was the control just given the focus.
- **The build:** `/make` as p-code on VB6.EXE 6.00.9782 (SP6), MSVBVM60 6.00.9848. It ran on the Win10-C clone's console, where Highlight is `0078D7`, Highlight Text `FFFFFF`, Window `FFFFFF` and Window Text `000000` ("All 25 system colours on Windows 10").

Colours are `RRGGBB`. Each item is 96 pixels wide. A Style 0 item is 13 pixels high and a Style 1 item 15.

**Style 0, at every MultiSelect, with the CommandButton focused:**

| Item | Top row | Middle row |
|---|---|---|
| selected | all `0078D7` | `0078D7`, with the text in `FFFFFF` |
| not selected | all `FFFFFF` | `FFFFFF`, with the text in `000000` |

The selection fills the whole row, edge to edge.

**The same lists, each focused in turn.** Only the focused list changed, and only on its `ListIndex` item.

| List focused | Its `ListIndex` item | Top and bottom rows | Middle row |
|---|---|---|---|
| `lst0` | 1, selected | 48 `0078D7` and 48 `FF8728`, alternating | one `FF8728` edge pixel, the rest as before |
| `lst1`, `lst2` | 2, **not** selected | 48 `FFFFFF` and 48 `000000`, alternating | as before |

So the focus mark is a one-pixel dotted rectangle, every other pixel inverted (`0078D7` XOR `FFFFFF` is `FF8728`). That pattern is Windows' `DrawFocusRect`, reasoned from the pixels, not checked. The list's other items were unchanged.

**`ListIndex` in a multi-select list moves the caret and selects nothing,** as #1395 found (`a-listboxs-selected-selcount-and-text-with-multiselect-and-without.md:56`). In `lst1` and `lst2`, `ListIndex = 2` left `Selected(2)` False and items 1 and 3 still selected, and `LB_GETCARETINDEX` read 2. The focus rectangle went to item 2.

**Style 1:**
- **A checked item isn't highlighted.** `Selected(1)` True, which is a Style 1 item's check, left item 1 in `lst3` white with black text.
- **The `ListIndex` item is highlighted,** whether or not it's checked: item 3 in `lst3`, which isn't, and item 1 in `lst4`, which is. The fill runs from pixel 15 to the right edge, so the checkbox and its margin stay `FFFFFF`. The fill is drawn with the list unfocused too.
- **Focused,** the dotted rectangle covers that same span, pixels 15 to 95, not the checkbox.
- **The checkbox** put two `A0A0A0` pixels on every item's middle row, the value of Button Shadow on this machine.

**Not measured:**
- a disabled list;
- a list whose BackColor or ForeColor isn't the default;
- `MultiSelect` with the mouse or keys rather than code;
- the IDE's design-time drawing;
- scaling other than 100%, and other Windows versions.
