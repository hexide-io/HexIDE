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

## A Columns ListBox in VB6's designer and in a run (2026-11-21, #5133, SP6)

**The question.** The section above saw VB6's designer draw a `Columns = 2` ListBox with `c1` at the left, a `c` cut
off at the right edge, and a horizontal scroll bar, where a run drew `c1` and `c2` side by side. What decides the
designer's column width, and which item is the cut-off `c`?

**The probe.** Two forms of ListBoxes, each saved with items `c1`, `c2`, … in its `.frx`, were built with `/make`, run on
the guest's console, then opened in VB6's designer (VB6.EXE 6.00.9782, SP6, 96 dpi). Screens came through the host's
framebuffer. For each list, the table gives the x of each column's first ink pixel, counted from the list's top-left
inner border pixel. A list holding the focus draws a dotted rectangle round its first item, which hides where the
second column starts, so its run is not read.

| List | Font | Columns | Width | Height | Items | Designer | Run |
|---|---|---|---|---|---|---|---|
| L1 | MS Sans Serif 8.25 | 2 | 1935 | 645 | 6 | 2, 122 | the focus |
| L2 | MS Sans Serif 8.25 | 2 | 2500 | 645 | 6 | 2, 122 | 2, 84 |
| L3 | MS Sans Serif 8.25 | 2 | 3000 | 645 | 6 | 2, 122 | 2, 100 |
| L4 | MS Sans Serif 8.25 | 3 | 2500 | 645 | 9 | 2, 122 | 2, 56, 110 |
| L5 | MS Sans Serif 8.25 | 2 | 1935 | 645 | 4 | 2, 122 | 2, 65 |
| L6 | MS Sans Serif 8.25 | 1 | 1935 | 645 | 6 | 2, 122 | 2 |
| L7 | MS Sans Serif 8.25 | 2 | 1935 | 645 | 12 | 2, 122 | 2, 65 |
| L8 | MS Sans Serif 8.25 | 4 | 3000 | 645 | 8 | 2, 122 | 2, 51, 100, 149 |
| A1 | Arial 12 | 2 | 2500 | 900 | 6 | 2, 122 | the focus |
| A2 | Times New Roman 14.25 | 2 | 3000 | 900 | 6 | 2, 122 | 2, 100 |
| A3 | Courier New 9.75 | 3 | 3000 | 900 | 9 | 2, 122 | 2, 67, 132 |
| A4 | MS Sans Serif 8.25 | 2 | 2500 | 1500 | 12 | 2, 122 | 2, 84 |
| A5 | Arial 8.25 | 2 | 1935 | 645 | 6 | 2, 122 | 2, 65 |
| LC1 | MS Sans Serif 8.25 | 4 | 2970 | 645 | 8 | 2, 122 | 2, 50, 98, 146 |
| LC2 | MS Sans Serif 8.25 | 4 | 3030 | 645 | 8 | 2, 122 | 2, 51, 100, 149 |

LC1 and LC2 came from a third form, with a TextBox holding the focus. They were added to tell apart the rules that fit
the first two forms.

- **The designer draws its columns 120 pixels apart in every list.** That holds for every width, font, item count and
  Columns value, 1 included: L6, with `Columns = 1`, draws a second column in the designer too.
- **Items run down a column, then on to the next, the same way in both places.** A1 shows `c1` and `c2` in its first
  column and `c3` and `c4` in its second. A4 shows `c1` to `c5`, then `c6` to `c10`.
- **So the cut-off `c` is `c2`.** At Height 645 one row shows above the scroll bar. The second column starts 120
  pixels in, and the list is 129 pixels wide, so one letter of `c2` fits.
- **A run spaces its columns by `(width − 3) \ Columns` pixels,** truncated. The pitches are:
  - 63 for 129 pixels across 2 columns;
  - 82 for 167 across 2, and 54 for 167 across 3;
  - 98 for 200 across 2, 65 for 200 across 3, and 49 for 200 across 4;
  - 48 for 198 across 4, and 49 for 202 across 4.

  All eight fit. The widths are the twips over 15, rounded half up, as [a control's size](a-controls-minimum-size.md)
  is. What the 3 pixels stand for wasn't established.
- **L6 runs with one column the full width of the list,** so only `c1` shows.

**What was overturned.** The first two forms' six pitches also fit "(width − 4) / Columns, rounded half up". That was
hexide-frm's own first rule. LC1 and LC2 were added because the two rules part there: it gives 49 for 198 pixels and
50 for 202, and the run drew 48 and 49. Rounding half to even fails on 202 (50) and on 129 (62).

**What resisted explanation.** Why the designer uses 120 pixels wasn't found. It doesn't follow the font or the width,
so the designer seems not to apply Columns' width at all. Whether 120 is a list box's own default column width wasn't
checked.

### Not measured

- **The designer at a DPI other than 96.**
- **A `Style = 1` (checkbox) list with Columns.**
- **IntegralHeight False,** and a Height that leaves part of a row.
- **Columns changed in the Properties window** rather than loaded from the file.
- **Both builds of one form.** The first two forms were built as p-code (`CompilationType=-1`) and the third as native
  code. The drawing is the runtime's either way.
