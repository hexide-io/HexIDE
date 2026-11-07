## A ComboBox's Click, Change and DropDown, with real input (2026-10-22, #1429)

**The probe.** A form with a Dropdown Combo (Combo1), a Simple Combo (Combo2) and a Dropdown List (Combo3), each holding
`apple`, `Banana` and `cherry`. It drives itself from a Timer, run in the guest's console session where it has the
foreground:
- the mouse with `SetCursorPos` and `mouse_event`;
- the arrow button and each list item found with `GetComboBoxInfo`, `GetWindowRect` and `CB_GETITEMHEIGHT`;
- the keys with VB6's own `SendKeys`.

Each handler logged its event with `ListIndex` and `Text`. Native and p-code gave byte-identical logs, in both runs.

| Combo1, a Dropdown Combo | VB6 raised |
|---|---|
| click the arrow | **DropDown**, before the list shows |
| click `cherry` in the list | **Click**, `ListIndex` 2, Text `cherry`; **no Change** |
| type `x` | **Change**, Text `x`: the item's text was selected, so the key replaced it; `ListIndex` -1 |
| Backspace | Change, Text `""` |
| Up, list closed, nothing selected | **Click**, `ListIndex` 0, `apple`; no Change |
| Down, list closed | Click, `ListIndex` 1 |
| Home, Shift+End, type `apple` | **a Change per key**, `a` to `apple`, and **`ListIndex` -1 throughout**: typing an item's text selects nothing |
| F4 | DropDown |
| Down, list open | **Click at once**, `ListIndex` 2 |
| Down again, on the last item | nothing |
| Enter | **nothing**: the Click came with the move |
| F4, Up, list open, Escape | DropDown, Click at the Up; **Escape undoes nothing**, `ListIndex` stays 1 |
| Alt+Down, then Escape | DropDown, and nothing else |
| click the arrow, then the item already selected | DropDown, then **Click all the same** |

| Combo2, a Simple Combo | VB6 raised |
|---|---|
| click `Banana` in its list | Click, `ListIndex` 1 |
| type `x` | Change, Text `x`, `ListIndex` -1 |
| Down | Click, `ListIndex` 0 |
| click `Banana`, then click it again | Click, and **Click again** |

| Combo3, a Dropdown List | VB6 raised |
|---|---|
| click the arrow | DropDown |
| click `Banana` | Click, `ListIndex` 1 |
| Down, list closed | Click, `cherry` |
| type `a` | **Click**, `apple`: the first item starting with the letter |
| F4, Down, Down, Enter | DropDown, then a Click at each Down, `apple` then `Banana`; nothing at Enter |
| F4, Up, Escape | DropDown, Click at the Up, `apple`; Escape undoes nothing |
| click the arrow, then the item already selected | DropDown, Click |

What these show:
- **Click is a choice, not a change.** It comes from every pick of an item by the mouse, even of the item already
  selected, and from every arrow key that moves the selection, open or closed.
- **Change is the edit box's,** from typing and from code's Text. Choosing an item puts its text in the box with no
  Change.
- **Typing selects nothing** in a Dropdown or Simple Combo, even an item's whole text. A Dropdown List takes a letter as
  a pick.
- **DropDown comes as the list opens,** by the mouse, F4 or Alt+Down.
- Code: `ListIndex = n` raises Click when it moves the selection, and not when it doesn't. `Text = s` raises Change
  unless `s` is the Text already. Neither `Clear`, `AddItem`, `RemoveItem` nor `List(i) =` raises either (the oracle's
  "A ComboBox's Text" and "A list's members").

What resisted explanation:
- **F4 then Down, with Text `apple` and nothing selected, selected `Banana`,** not `apple`. So the open list seems to
  start from the item the edit box's text matches, though `ListIndex` read -1 in DropDown. One case is not a rule:
  whether the match is exact, without regard to case or by prefix is not measured.

### In HexIDE (#1429)

- **Every row above runs as measured** but that one, which is left out of the tests: there HexIDE's Down selects the
  first item.
- **Click is raised where the input is taken,** not from Avalonia's selection, which also moves for a list edit that
  raises nothing.
- **The arrow keys are the combo's own,** list open or closed. Avalonia's move only the focus in an open list, and from
  no selection its Up selects nothing.
- **Avalonia's editable combo selects the item a new Text matches,** without regard to case. Typing therefore reads every
  item as a text no one can type, and an item chosen whose text another shares, `Banana` and `banana`, stays chosen.
- **Not measured:** a Simple Combo's list, always shown in VB6, which HexIDE draws as a dropdown (#1428), and whether
  arrow keys, like the mouse, leave the chosen item's text selected in the edit box.

