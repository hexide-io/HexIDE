## A ListBox's Click: a left press on an item, or code or a key that moves the selection (2026-10-22, #1374)

Where does a ListBox raise Click? Four `/make` probes, each built native and p-code, all eight builds agreeing line for
line. Each holds List1, 2400 by 1200 twips at (120, 120) on a twips form, given `a`, `b` and `c` in Form_Load. With
integral height the list draws five rows of 13 pixels, so its client area ends 65 pixels down and the empty part below
`c` starts at 39. Every handler appends its name and `ListIndex` to a file.

**Code**, in Form_Load, after `Me.Show` unless the row says otherwise, with `ListIndex` 1 to start:

| Statement | VB6 |
|---|---|
| `ListIndex = 1`, before `Me.Show`, from -1 | **Click**, `ListIndex` 1 inside it |
| `ListIndex = 2` | Click, 2 inside it |
| `ListIndex = 2` again | no Click |
| `ListIndex = -1` | Click, -1 inside it |
| `ListIndex = -1` again | no Click |
| `AddItem "z", 0`, with `c` selected at 2 | no Click; `ListIndex` 3 after: the selection follows its item |
| `RemoveItem 0`, then | no Click; `ListIndex` 2 after |
| `RemoveItem 2`, the selected item | no Click; `ListIndex` -1 after |
| `ListIndex = 0` (Click), then `Clear` | no Click from `Clear`; `ListIndex` -1 after |
| `Selected(1) = True`, from -1 | Click, 1 inside it; Err 0 |
| `Selected(1) = False`, the selected item | Click, -1 inside it |
| `Text = "x"`, from -1, `x` the first item | Click, 0 inside it |
| `ListIndex = 5`, with two items | Err 380, no Click, `ListIndex` unchanged |

**The keyboard**, the list focused, with `c` selected at 2:

| Input | VB6 |
|---|---|
| Up arrow | KeyDown 38, **Click** (1), KeyUp |
| Up arrow again | KeyDown, Click (0), KeyUp |
| Up arrow on the first item | KeyDown, KeyUp: no Click |

**The mouse**, real input in the console session, as in *A click with real input*:

| Input | VB6 |
|---|---|
| left click on `a`, nothing selected | MouseDown (`ListIndex` already 0), **Click**, MouseUp |
| left click on `a` again, already selected | MouseDown, Click, MouseUp: Click though nothing changed |
| left click in the empty area below `c`, 52 pixels down | MouseDown, MouseUp: no Click, selection unchanged |
| left click 65 pixels down, on the list's bottom edge | nothing at all, on the list or the form |
| press on `a`, move to `c`, release | MouseDown (0), MouseMove (**2**), Click (2), MouseUp (2) |
| press on `c`, move into the empty area, release | MouseDown (2), MouseMove (2), Click (2), MouseUp (2) |
| press in the empty area, move onto `a`, release | MouseDown (2), MouseMove (2), MouseUp (2): no Click, and `a` not selected |
| right click on `a`, nothing selected | MouseDown `2` (`ListIndex` -1), MouseUp: no Click, nothing selected |
| right click on `b`, selected; on `c`, not selected | MouseDown, MouseUp: no Click, the selection stays on `b` |
| middle click on `a` | MouseDown `4`, MouseUp: no Click |
| left double click on `a` | MouseDown (0), Click, MouseUp, DblClick, MouseMove, MouseUp |

What these show:
- **From code or a key, Click follows the selection.** It comes when `ListIndex`, `Selected` or `Text` moves it, and
  when an arrow key does, with `ListIndex` already the new value; it does not come when the value is set again, or
  when the key can go no further.
- **AddItem, RemoveItem and Clear never raise it,** though each can shift or empty the selection.
- **From the mouse, Click follows a left press on an item, not the selection.** It comes on the release, before
  MouseUp, whether or not the selection changed and wherever the release lands. The press selects at once, so MouseDown
  already reads the new index, and the selection follows the pointer over the items while the button is held.
- **A left press in the empty area, and any right or middle press,** raises MouseDown and MouseUp only. It selects
  nothing, and the selection does not follow it onto an item.

What resisted explanation:
- **A click on the bottom edge raised nothing anywhere,** not even the form's MouseDown. The point is just past the
  client area, on what is presumably the list's border, but no rule here says why that border swallows a click.
- **The double click's MouseMove** with the button held, between DblClick and the last MouseUp, has no matching move
  in the driver, which sets the cursor once before both clicks. It was logged in all four builds.

### HexIDE

Before #1374, a ListBox raised Click on every release, after MouseUp: for a press in the empty area and a right or
middle click too, and never from code or a key. A right press selected the item under it.

Since #1374, `VBListBox` raises Click from the two places VB6 does.
- **A left press on an item** is remembered, and its release raises Click before MouseUp, unless it is a double click's
  second (#1031). While the button is held, the item under the pointer is selected. The release may land anywhere,
  off the list included, as VB6's may (measured in *And a press released somewhere else*, #1373).
- **A right or middle press** is marked handled on its way down, so Avalonia's ListBox selects nothing for it. Its
  MouseDown and MouseUp still come, since those take handled presses.
- **An arrow key** raises Click when the selection it leaves differs from the one it found.
- **`ListIndex` set from code** raises Click when the value changes. A selection change in general does not raise it,
  so AddItem and Clear raise none.
- **Pinned** in `MouseEventTests`: a click on an item and again on it, a click below the items, a press moved onto
  another item, off the items, and from below them onto one, a right and a middle click on an item, the double click,
  and the arrow keys. `ListBoxClickTests` pins the `ListIndex` rows, a plain `AddItem` and `Clear`.

What it still does not do:
- **`RemoveItem` and `AddItem`'s index** are #821's, and **`Selected(i)` and `Text`** #1395's, so their rows wait on
  those.

