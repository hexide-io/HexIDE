## A list's NewIndex and ItemData, and the List and ItemData records a form saves (2026-11-20, #5106, #5114, SP6)

**The rig.**
- **Build:** vb6.exe SP6, `/make`, native code and p-code. They logged the same line in all 131 cases of the first probe and in every later one.
- **The guest:** an en-GB Windows 10, ANSI code page 1252.
- **The controls:** one form held eight lists. Four were ListBoxes: plain, `Style = 1` (checkbox), `Sorted`, and `Sorted` with `Style = 1`. Four were ComboBoxes: `Style = 0`, `Style = 1`, `Style = 2`, and `Sorted` with `Style = 2`.
- **The method:** every case was a Function taking the list `As Object`, under `On Error Resume Next`, logging `Err.Number` after each step. Most cases began with `Clear` or a fill of `a`, `b`, `c` and so on.

**All eight lists answered alike,** apart from where a `Sorted` list put an item added without an index. So the tables give one answer for all eight.

### NewIndex

| Case | VB6 |
|---|---|
| A new, empty list | `-1`, `TypeName` `Integer` (VarType 2) |
| `c.NewIndex = 0` | **383**, and `NewIndex` keeps its value |
| `AddItem` without an index | the new item's position: the end of an unsorted list, its sorted place in a `Sorted` one |
| `AddItem` with an index, `0`, `2` or `ListCount` | that index, sorted or not |
| `AddItem "f", 99` or `AddItem "g", -1` | 5, and `NewIndex` keeps its value |
| `RemoveItem` of any item: before `NewIndex`, at it, after it, or the last | **-1** |
| `RemoveItem 6` on a six-item list | 5, and `NewIndex` keeps its value |
| `Clear` | -1, and the next `AddItem "y"` gives 0 |
| `List(0) = "z"`, `List(1) = "q"` | unchanged |
| `List(ListCount) = "w"` | no error. `w` is appended at the end, even in a `Sorted` list, and `NewIndex` is its position |

The sequence `AddItem` `m`, `b`, `z`, then `c` at 0, `d` at 2 and `e` at `ListCount` read 0, 1, 2, 0, 2, 5 in an unsorted list and 0, 0, 2, 0, 2, 5 in a sorted one.

Early-bound, `TypeName(Form1.List1.NewIndex)` is also `Integer`.

### ItemData(i)

**Its type.** A new item reads `0`, `TypeName` `Long` (VarType 3). Early-bound, `TypeName(Form1.List1.ItemData(0))` is `Long`, and `ItemData(NewIndex) = 42` reads back 42.

**What an assignment stores.** Each value was assigned to `ItemData(0)` after a sentinel of 77:

| Value | VB6 |
|---|---|
| `5`, `-1`, `2147483647` | stored |
| `2147483648#`, `-2147483649#` | **6**, and 77 stays |
| `2.5`, `3.5` | 2, 4 |
| `"12"`, `"&H10"` | 12, 16 |
| `"x"` | **13**, and 77 stays |
| `Null` | **13** (not 94), and 77 stays |
| `True`, `Empty` | -1, 0 |
| `CCur(7.5)`, `#1/2/2000#`, `CByte(200)` | 8, 36527, 200 |

**Its index**, on a list of two items holding 10 and 11:

| Index | VB6 |
|---|---|
| `0` on an empty list | **381** |
| `-1`, `-2`, `2` (= `ListCount`) | 381. Writing at `-1` and `2` is 381 too |
| `0.5`, `1.5` | 10 (index 0), then 381 (index 2): banker's rounding |
| `"1"` | 11 |
| `32768`, `65536` | **6** |
| none: `v = c.ItemData` | **385** |

**It follows its item.** In an unsorted list, `AddItem` `b`, `d`, `c`, `a`, each followed by `ItemData(NewIndex) =` 2, 4, 3, 1. Then:

| Step | Unsorted list | Sorted list |
|---|---|---|
| the four `AddItem`s | `b=2 d=4 c=3 a=1` | `a=1 b=2 c=3 d=4` |
| `AddItem "x", 1` | `b=2 x=0 d=4 c=3 a=1` | `a=1 x=0 b=2 c=3 d=4` |
| `RemoveItem 2` | `b=2 x=0 c=3 a=1` | `a=1 x=0 c=3 d=4` |
| `List(0) = "q"` | `q=2 x=0 c=3 a=1` | `q=1 x=0 c=3 d=4` |

So a new item starts at 0, an insert or a removal moves the values with their items, and `List(i) = x` keeps item i's value. After `ItemData(0) = 9`, `Clear` and `AddItem "b"`, the new item reads 0.

### The List and ItemData records in a .frx

A list's items saved at design time, and their `ItemData`, are two records in the form's `.frx`. The `.frm` cites them as `ItemData = "Form1.frx":0000` and `List = "Form1.frx":000E`. Each probe below was a form whose one ListBox cited records written by hand. Each read `ListCount`, every `List(i)` by character code, and `ItemData(i)`.

| Record bytes | VB6 |
|---|---|
| List `03 00` · `02 00 "ab"` · `03 00 "cde"` · `04 00 "fghi"` | **the form doesn't load**: "Property List in G could not be set" |
| List `03 00` · `02 00` · then the same three items | 3 items, `ab`, `cde`, `fghi` |
| List `03 00` · `09 00` · then the same three items | the same |
| List `03 00` · each item as `len len text` | doesn't load |
| ItemData `03 00` · `00 00` · `05 00 00 00` · `06 00 00 00` · `07 00 00 00` | doesn't load: "Property ItemData in G could not be set" |
| ItemData `03 00` · `05 00 00 00` · `06 00 00 00` · `07 00 00 00` | loads, and every value reads **0** |
| ItemData `03 00` · `63 00` · `01 00 "5"` · `02 00 "-7"` · `05 00 "70000"` | 5, -7, 70000 |

**So each record is:**
- a 2-byte count;
- a 2-byte field that reading ignores: 0, 2 and 9 in a List record, and 0 and 0x63 in an ItemData record, read alike;
- each entry as a 2-byte length and its text.

An `ItemData` entry's text is the value in decimal, and it reads as a Long. An empty list's records are a 2-byte count of 0 and nothing more, as in VB98's `ODBC Log In.frx`.

**A record VB6 wrote agrees.** In VB98's `Wizards\PDWizard\Setup1`, `group.frx` holds `01 00 09 00 09 00 "lstGroups"` as a ListBox's List record. Its ItemData record is `01 00 03 00 01 00 "0"`. Copied into a probe, VB6 reads one item, `lstGroups`, with ItemData 0.

**After loading.** A ListBox saved with `s2 s1 s3` (ItemData 20, 10, 30) and a `Style = 2` ComboBox saved with `t1 t2 t3` (7, 8, 9) were read:
- each listed its items in the saved order, with their values;
- each read `NewIndex` -1 and `ListIndex` -1, and the ComboBox's `Text` was "";
- a following `AddItem "n"` gave `NewIndex` 3, with ItemData 0.

### What was overturned

- **The ItemData record isn't four-byte numbers.** The obvious guess, a count and then a Long per item, fails to load with the second field in place, and loads as all zeros without it. The values are decimal text, framed as the List record's items are.
- **Neither record is "a 2-byte count" alone once it has an entry.** HexIDE's `FrxDeserializer` describes them that way, from empty records only. A record with entries has the second 2-byte field before them.

### Not measured

- **What VB6 writes in the ignored field.** `group.frx` has 9 in its List record, the length of its one item, and 3 in its ItemData record.
- **Text outside ASCII** in a saved item.
- **A `Sorted` list saved in an order it wouldn't keep,** which VB6's designer doesn't produce: it sorts items as they're entered.
- **Records whose counts disagree.**

### In HexIDE (main `49f735cfd`, read from the code)

- **`NewIndex` and a run-time `ItemData(i)` don't exist.** Nothing in `IDE/HexIDE.Runtime` names `NewIndex`. `ItemData` is a saved property only. That's #5106.
- **Saved items aren't loaded.** Both records are kept verbatim for saving, and the list starts empty at run time. That's #5114.
