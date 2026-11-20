## A list's saved items in VB6's designer, and the order and ItemData they load with (2026-11-20, #5121, SP6)

**The rig.**
- **The guest:** an en-GB Windows 10 at 96 dpi, ANSI code page 1252, with vb6.exe SP6.
- **The records:** written by hand in the layout `a-lists-newindex-itemdata-and-saved-records.md` gives: a count, a second field, then each item as a length and its text.
- **Drawing rows:** read from screenshots of the VB6 IDE on the guest's console. The designer was opened from the `.vbp`, then run with F5 and stopped.
- **Load rows:** built with `/make` as native code and as p-code, and logged by the form's own code (`ListCount`, `List(i)` and `ItemData(i)`). The two builds logged the same lines in every probe.

### What the designer draws, and what a run draws

One form held each control below, at its default font (MS Sans Serif 8) unless the row says otherwise.

| Control | Saved items | VB6's designer | A run |
|---|---|---|---|
| ListBox | `s2 s1 s3` | the three items in saved order from the top, one per 13-pixel row, none selected | the same rows, with a focus rectangle on `s2`, which had the focus |
| ListBox, no `List` line | none | its name, `List2`, on the first row | empty |
| ListBox, `Style = 1` | `alpha beta` | an unchecked box beside each item, and `alpha` in the selection colours | the same |
| ListBox, 645 twips high | `item1` … `item12` | `item1` to `item3`, and a vertical scroll bar | the same |
| ListBox, `Columns = 2`, 645 twips high and 1935 wide | `c1` … `c6` | `c1` at the left, a clipped `c` at the right edge, and a horizontal scroll bar | `c1` and `c2` side by side, and a horizontal scroll bar |
| ListBox, `Sorted` | `b a c` | `b a c` when the designer opens; `a b c` after a run | `a b c` |
| ListBox, Arial 12 | `big1 big2` | both items, in Arial 12, one per 18-pixel row | the same |
| ComboBox, `Style = 0`, `Text = "Combo1"` | `x y` | `Combo1`, with the list closed | the same |
| ComboBox, `Style = 1`, `Text = "Combo2"`, 1215 twips high | `p q r` | `Combo2` in the box, and `p`, `q` and `r` in the list below it | the same |
| ComboBox, `Style = 2` | `m n` | its name, `Combo3` | empty |

- **The rows are the same height in the designer and in a run:** 13 pixels at MS Sans Serif 8 and 18 at Arial 12.
- **The designer doesn't sort a `Sorted` list's saved items when it opens.** It shows them sorted only after a run has come back to design. Why a run changes it isn't known.
- **The `Columns = 2` row resisted explanation.** A run puts the second column halfway across. In the designer, the only other text was a `c` cut off at the right edge, and which item it belongs to wasn't found.
- **`alpha`'s highlight is a picture, not a measured value.** `ListIndex` wasn't read.

### What the designer writes back

The form was opened, a line `Dim zz` was added to its code, and Ctrl+S saved it.

- **A save without a run** wrote the `Sorted` list as `b a c`, still in saved order.
- **A save after a run** wrote it as `a b c`.
- **Every list with items got an `ItemData` record,** written before its `List` record. Each value was the text `0`.
- **The `Style = 2` ComboBox, saved with no `ItemData` record,** was written back with one item, `n`, both with and without a run. See the load rows below.
- **The second field of each record** was the longest item's length, or 3 where that was shorter: 3 for items of one or two characters, and 4, 5 and 6 for items of those lengths. `ItemData` records of `0`s had 3.
  - That fits all 18 records this save wrote, and VB98's `group.frx`, which has 9 for `lstGroups` and 3 for its `0`.
  - It's a rule fitted to these rows. Reading ignores the field.

### The order and ItemData a list loads with

Each list was saved with `b a c`, and, where the row says so, an `ItemData` record of `1 2 3`.

| Control | `ItemData` record | Loaded as `List(i)/ItemData(i)` |
|---|---|---|
| ListBox | yes | `b/1 a/2 c/3` |
| ListBox, `Sorted` | yes | `a/2 b/1 c/3`: each value stays with its item |
| ListBox, `Sorted` | no | `a/0 b/0 c/0` |
| ListBox, `Style = 1`, `Sorted` | yes | `a/2 b/1 c/3` |
| ListBox, `Style = 1`, `Sorted` | no | `a/0 b/0 c/0` |
| ComboBox, `Style = 0` | yes | `b/1 a/2 c/3` |
| ComboBox, `Style = 0`, `Sorted` | yes | `a/1 b/2 c/3`: the values stay in saved position |
| ComboBox, `Style = 1`, `Sorted` | yes | `a/1 b/2 c/3` |
| ComboBox, `Style = 2`, `Sorted` | yes | `a/1 b/2 c/3`: the values stay in saved position |
| ComboBox, `Style = 2` | yes | `b/1 a/2 c/3` |
| ComboBox, `Style = 2` | no | `a/0 c/0`: **the first saved item is gone** |
| ComboBox, `Style = 2`, `Sorted` | no | `a c`: the first saved item is gone |
| ComboBox, `Style = 0` | no | `b/0 a/0 c/0` |
| ComboBox, `Style = 1`, `Sorted` | no | `a b c` |

- **A `Sorted` list is sorted as it loads.**
- **A sorted ListBox keeps each `ItemData` value with its item. A sorted ComboBox leaves the values where they were saved.** Measured on plain and checkbox ListBoxes, and on ComboBoxes of all three styles.
- **A `Style = 2` ComboBox whose `List` has no `ItemData` record drops its first saved item,** sorted or not. With the record, it keeps all three.
  - Its `ListCount` read 2.
  - No other kind of list dropped an item without the record.
  - VB6 writes an `ItemData` record for every list with items, so only a file not saved by VB6 reaches this.

### The order a load sorts into isn't AddItem's

The 41 items of `the-order-a-sorted-listbox-or-combobox-keeps.md` were saved in the order they were added there: `b B a A 10 9 1 2 a10 a2 a1 coop co-op "co op" Co-op _a ~a 'a -a " a" é e f E x x "" Z z [a {a 0 Æ ß ss ae ab a-b "a b" AB aB`. They went in a `Sorted` ListBox, with `ItemData` 1 to 41, and in a `Sorted` `Style = 0` ComboBox. Both loaded in this order:

`""`, `" a"`, `[a`, `_a`, `{a`, `~a`, `0`, `1`, `10`, `2`, `9`, `a`, `'a`, `-a`, `A`, `"a b"`, `a1`, `a10`, `a2`, `ab`, `a-b`, `aB`, `AB`, `ae`, `Æ`, `b`, `B`, `"co op"`, `coop`, `co-op`, `Co-op`, `e`, `E`, `é`, `f`, `ß`, `ss`, `x`, `x`, `z`, `Z`

The ListBox's `ItemData` followed each item: `a` read 3, `A` 4, `b` 1 and `B` 2.

**The obvious guess, that a load sorts as `AddItem` does, is wrong** (hexide-frm's own first guess). The two orders differ in several places:

| | `AddItem` kept | A load kept |
|---|---|---|
| `[a` | last | among the symbols, after `" a"` |
| Items differing only in case, a hyphen or an apostrophe | `A a 'a -a`, `AB aB ab a-b`, `B b`, `E e`, `z Z` | `a 'a -a A`, `ab a-b aB AB`, `b B`, `e E`, `z Z` |
| `ss` and `ß` | `ss ß` | `ß ss` |
| `Co-op` and `co-op` | `Co-op co-op` | `co-op Co-op` |

**Windows' own comparison reproduces it.** Inserting the 41 saved items one by one, each after every item that `CompareStringEx` for `en-GB` with no flags puts at or before it, gives exactly this order. Inserting each before those items gives the same order. That was run on the host's Windows (Windows Server 2025, en-GB), not inside VB6.
- With `NORM_IGNORECASE`, which `AddItem`'s model passes, 9 of the 41 positions differ when a tie goes after, and 14 when it goes before.
- With `SORT_STRINGSORT`, 23 differ either way.

That VB6 calls this comparison at load is reasoned from the match, not seen. No rule is offered beyond these 41 items.

### Not measured

- **The designer at a DPI other than 96.**
- **What the `Columns = 2` designer row shows,** beyond the picture above.
- **Whether VB6's designer ever writes a `Sorted` list out of order** from items entered by hand, for example by setting `Sorted` after entering them. These rows began from a file written by hand.
- **The load order of strings outside these 41,** in any locale but en-GB.
