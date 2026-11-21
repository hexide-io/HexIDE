## A list's members: ListCount, List(i), AddItem's index and RemoveItem (2026-10-22, #821)

What do a ListBox's and a ComboBox's list members do, in range and out of it? Three `/make` probes, each built native
and p-code, all six builds agreeing line for line. Each runs in Form_Load after `Me.Show`, under `On Error Resume Next`,
on four controls: a ListBox, a ComboBox with Style 0 (Dropdown Combo), one with Style 2 (Dropdown List), and a ListBox
with `Sorted = True`. **The four agree on every row** but the two marked, so the table gives one answer.

Each control starts with `c`, `a` and `b` added, in that order, which the sorted list keeps as `a`, `b`, `c`. A row's
`ListIndex` and `ListCount` are read after it.

| Statement | VB6 |
|---|---|
| `ListCount`, empty, then with three items | `0`, `3`, an **Integer** |
| `ListCount = 5` | compile error **"Can't assign to read-only property"** |
| `List(0)`, empty | `""`, Err 0 |
| `List(0)`, `List(2)` | `c`, `b`, Strings (sorted: `a`, `c`) |
| `List(3)` (= ListCount), `List(-1)`, `List(-2)`, `List(100)` | `""` each, **Err 0** |
| `List("1")`, `List(1.6)` | `a`, `b`: the index is converted to an Integer, rounding |
| `List(40000)` | Err **6** Overflow |
| `List("q")` | Err 13 |
| `List(Null)` | Err 94 |
| `List()` | compile error "Argument not optional" |
| `ListIndex = 1`, then `List(1) = "B"`, the selected item | replaced; **no Click**, `ListIndex` 1; a ComboBox's `Text` becomes `B` |
| `List(3) = "d"` (= ListCount) | **appended**, ListCount 4, Err 0 |
| `List(-1) = "z"` | **appended**, at the end, Err 0 |
| `List(10) = "e"`, past ListCount | Err **381** "Invalid property array index" |
| `List(-2) = "y"` | Err **-2147024882** "Out of memory"; nothing added. A **ComboBox's `ListIndex` moves from 1 to 2** |
| `AddItem "z", 0`, with the item at 1 selected | inserted at 0; **`ListIndex` 2**, the selection follows its item; no Click |
| `AddItem "y", ListCount` | appended |
| `AddItem "w", ListCount + 1`, `AddItem "v", -1`, `AddItem "w", 10` | Err **5** "Invalid procedure call or argument" |
| `AddItem "u", "1"` | inserted at 1: the index is converted |
| `AddItem "x", 40000` | Err 6 |
| `AddItem "n", Null` | Err **13**, not 94 |
| `AddItem Null` | Err 94 |
| `AddItem "z", 0` on the **sorted** list | inserted at 0 all the same: `z` first, before `a` |
| `AddItem "q", 0`, empty; `AddItem "r", 2`, one item | the first inserted; the second Err 5 |
| `RemoveItem ListCount`, `RemoveItem -1`, `RemoveItem 10`, `RemoveItem 0` empty | Err 5 |
| `RemoveItem 40000` | Err 6 |
| `RemoveItem 0`, with the item at 3 selected | removed; `ListIndex` 2; no Click |
| `ListIndex = 0`, then `RemoveItem 0`, the selected item | removed; `ListIndex` **-1**; no Click from the removal; a ComboBox's `Text` becomes `""` |
| `ListIndex = 1`, then `RemoveItem 0` | removed; `ListIndex` 0 |

What these show:
- **A ListBox and a ComboBox of either style are one list.** Only `Text` and the one row below tell them apart.
- **Reading is forgiving and writing is not.** Any `List(i)` read that converts to an Integer is `""` outside the
  list, while a write outside it is an error. The exceptions are `ListCount` and `-1`, which both append.
- **AddItem takes 0 to ListCount and RemoveItem 0 to ListCount - 1**, and both raise 5 outside that. An index converts
  as an Integer argument does: a String that reads as a number is taken, past 32767 is Overflow.
- **The selection follows its item** through AddItem and RemoveItem, and empties when its item is removed. None of them
  raises Click.
- **`Sorted` does not override AddItem's index.**

What resisted explanation:
- **`List(-2) = "y"` raises "Out of memory"**, -2147024882 (`0x8007000E`, E_OUTOFMEMORY), where 381 would be the
  obvious error, and on a ComboBox only, it moves `ListIndex` from 1 to 2 though nothing was added. Presumably the
  write reaches the window as an insert at position -2, which the control refuses as if memory had run out; no rule
  here explains the ComboBox's selection moving.
- **`List(-1) = "z"` appends** rather than raising, as if -1 meant "at the end".
- **`AddItem "n", Null` is 13 where `List(Null)` and `AddItem Null` are 94.**

### HexIDE

Before #821, `ListCount` and `List(i)` were "Method or data member not found", `RemoveItem` was not implemented, and
`AddItem` appended whatever its index said.

Since #821, `ListMembers` gives a ListBox and a ComboBox every row above but two.
- **`List(i)`** is caught by name with its index, on the read path in `ExpressionExecutor` and the write path in
  `StatementExecutor`, before the property lookup that would find the design-time `List` instead.
- **An index converts** by `VbNumeric.CoerceOnStore` to an Integer, which gives 6, 13 and 94 as measured. AddItem's
  Null index is 13 before it.
- **The selection** follows its item through AddItem and RemoveItem because Avalonia's does. A replaced item loses its
  selection in Avalonia, so `List(i) =` sets it back.
- **381's text** was HexIDE's "Invalid property-array index", unmeasured; it is now VB6's "Invalid property array
  index".
- **Pinned** in `ListMembersTests`, on a ListBox and a ComboBox.

What it still does not do:
- **A ComboBox's `ListIndex` moving 1 to 2 after `List(-2) = "y"`.** HexIDE leaves it where it was. The error is
  raised, and no program is likely to read the selection after it.
- **A Dropdown List's `Text`** (#1428). A Dropdown Combo's two `Text` rows run as measured since #926.
- **`Sorted`,** which HexIDE has no property for.
- **`ListCount = 5`** is the compile error's text, raised when the statement runs, not before the program does.

### Called directly, or through As Object (2026-11-20, #5117, SP6)

**The error a list member raises can depend on how the list is reached.** In the probes, a list called directly as `Form1.List1`, and one held in `Dim t As ListBox` (or `As ComboBox`), gave one answer. A list passed as `c As Object` gave another, in four rows. Every probe ran on vb6.exe SP6 with `/make`, native code and p-code, under `On Error Resume Next`, and the two builds logged the same lines.

| Statement | Direct, or a typed variable | Through `As Object` |
|---|---|---|
| `List(-2) = "v"` | -2147024882 "Out of memory", as the table above | **7**. `As Control` gives 7 too |
| `ItemData(0) = v`, `v` Null | 94 | **13** |
| `NewIndex = 0` | compile error "Can't assign to read-only property" | run-time **383** |
| `v = ItemData`, no index | compile error "Argument not optional" | run-time **385** |

**The lists, row by row:**
- **`List(-2)`** was measured three ways, on a ListBox, a `Style = 2` ComboBox and a `Style = 0` ComboBox, each seeded `c a b` with `ListIndex = 1`. The rows were taken before and after `Form1.Show`, with the same answer; `Form1.Visible` read False after the `Show`, so that isn't a check of a visible form. Five more lists gave 7 through `As Object` in another probe.
- **The `ItemData` and `NewIndex` rows** were measured directly on the ListBox and the `Style = 2` ComboBox, and through `As Object` on eight lists (four ListBox and four ComboBox kinds).

Everything else measured in both ways agreed: `ItemData`'s other conversions and index rows, and `List(-1) = x` zeroing every item's `ItemData`. Those rows are on the page for #5106 and #5114, `a-lists-newindex-itemdata-and-saved-records.md`.

So the "Out of memory" in the table above is the direct call's. Reasoned, not checked: an `As Object` call goes through `IDispatch`, which hands the control's failure back as VB's own error 7, where the direct call surfaces the control's raw `HRESULT`. Not measured in this probe: `As Control` for the other three rows, and a list held in a Variant. The next probe measured both.

**`As Control` and `As Variant` answer as `As Object` does** (2026-11-20, a second probe, native code and p-code agreeing). The three lists were reached through `Dim c As Control`, `Dim c As Variant` and `Dim c As Object`, each given `Set c = Form1.List1` (or `Combo2`, `Combo0`). Each was seeded `c a b` with `ListIndex = 1` and `ItemData(0) = 77`. All nine combinations logged the same four answers:

| Statement | Through `As Control`, `As Variant` or `As Object` |
|---|---|
| `c.List(-2) = "v"` | 7; `ListCount` stays 3 |
| `c.ItemData(0) = v`, `v` Null | 13, and 77 stays |
| `c.NewIndex = 0` | 383, and `NewIndex` stays 2 |
| `v = c.ItemData` | 385 |

**After the failed `List(-2)`,** each ComboBox's `ListIndex` read 2, and the ListBox's stayed 1. That's the ComboBox-only move in the first table above, here through every late-bound way in.

### Where a failed `List(i) = x` leaves the selection (2026-11-21, #5134, SP6)

**The probe.** Each list was cleared and given three items, `c`, `d` and `e`. Its `ListIndex` was set, then `List(i) = "v"` was written under `On Error Resume Next`. The list was reached through a typed parameter, `c As ComboBox` or `c As ListBox`. Click and Change handlers counted events from just before the write. It was built with `/make` as native code and as p-code, and the two logged the same lines.

| Selected before | `List(-2) = "v"` on a ComboBox (Style 0, 1, 2, and Style 2 `Sorted`) | On a ListBox |
|---|---|---|
| none (`ListIndex` -1) | -1 | -1 |
| `c` (0) | **1**, `Text` `d` | not measured |
| `d` (1) | **2**, `Text` `e` | 1 |
| `e` (2), the last | 2 | 2 |
| the only item (0 of 1) | 0 | not measured |

- **The error:** every write raised -2147024882 "Out of memory", and `ListCount` stayed 3.
- **No events:** no Click came, and on Style 0 no Change, although the selection and `Text` moved.
- **Below -2:** `List(-3) = "v"` with `d` selected moved it to `e`, as `-2` did.
- **Past `ListCount`:** `List(5) = "v"` and `List(4) = "v"`, with 3 items and `d` selected, raised 381 and left `ListIndex` 1, on a ComboBox and a ListBox alike.

**What these show:** on a ComboBox, a write below -1 that fails with "Out of memory" moves a selection to the next item if there is one, and leaves it alone otherwise. The move raises no event. A ListBox's selection doesn't move. Neither does a ComboBox's when the write fails with 381.

**Not measured:**
- a ListBox with its first item selected;
- the late-bound path's other starting points. #5117's probe measured only 1 to 2 there.
- `MultiSelect`.
