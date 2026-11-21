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
| `List(-1) = "w"` | the same: appended at the end, and `NewIndex` is its position. Every item's `ItemData` also becomes 0: see below |
| `List(-2) = "v"`, `List(ListCount + 1) = "u"` | 7 and 381, and `NewIndex` keeps its value |

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

**The two appends differ.** A second probe gave `a`, `b`, `c` the values 10, 11, 12, on five of the lists (the plain, `Sorted` and `Sorted` checkbox ListBoxes, and both `Style = 2` ComboBoxes):

| Then | VB6, on all five |
|---|---|
| `List(ListCount) = "w"` | `a=10 b=11 c=12 w=0`, `NewIndex` 3 |
| `List(-1) = "w"` | **`a=0 b=0 c=0 w=0`**, `NewIndex` 3 |
| `List(1) = "q"` | `a=10 q=11 c=12` |
| then `AddItem "n"` | unsorted: `n` last, with `ItemData` 0. Sorted: `a=10 n=0 q=11 c=12`, the binary search of #5059's page run on a list that `List(1) = "q"` left out of order |

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

### What resisted explanation

- **`List(-1) = x` zeroes every item's `ItemData`,** where `List(ListCount) = x` appends the same item and keeps them. Both were measured on five lists, native and p-code. No rule here explains it.

### Not measured

- **What VB6 writes in the ignored field.** `group.frx` has 9 in its List record, the length of its one item, and 3 in its ItemData record.
  - **Since measured for the List record** (2026-11-21, #845, SP6): VB6 writes the longest item's length in bytes. A record saved with 9 there came back from an IDE save with 3, for items of 2, 3 and 3 bytes. At code page 65001, where the first item grew to 4 bytes, it came back with 4 for items of 4, 3 and 3. So it isn't the last item's length. The ItemData record's field, saved as 3 for three items of `0`, came back as 3. The saves are in oracle PR #5227, on its new page for code page 65001.
- **Text outside ASCII** in a saved item. Since measured for code page 1252: see the last section.
- **A `Sorted` list saved in an order it wouldn't keep,** which VB6's designer doesn't produce: it sorts items as they're entered.
- **Records whose counts disagree.** Since measured: see the last section.

### In HexIDE (main `49f735cfd`, read from the code)

- **`NewIndex` and a run-time `ItemData(i)` don't exist.** Nothing in `IDE/HexIDE.Runtime` names `NewIndex`. `ItemData` is a saved property only. That's #5106.
- **Saved items aren't loaded.** Both records are kept verbatim for saving, and the list starts empty at run time. That's #5114.

### Records that disagree, run short or long, or hold odd text (2026-11-20, #5127, SP6)

Each probe was a form whose one ListBox, `G`, cited records written by hand, as above, built with `/make` as native code and as p-code. The two logged the same lines in every probe. Unless a row says otherwise, the List record held `ab`, `cde`, `fghi`.

| ItemData record, or what changed | VB6 |
|---|---|
| 2 entries, `5` `6`, for 3 items | loads: 5, 6, **0** |
| 4 entries, `5` `6` `7` `8`, for 3 items | loads: 5, 6, 7; the fourth is ignored |
| `abc` `6` `7` | **0**, 6, 7 |
| an empty entry, then `6` `7` | **0**, 6, 7 |
| `" 5"` `6x` `&H10` | **5**, **6**, **0**: a leading space is skipped, reading stops at the first character that isn't a digit, and `&H` isn't read |
| `3000000000` `-1` `1.5` | **-1294967296**, -1, **1**: past a Long it wraps, as 3000000000 - 2^32; a decimal point stops the reading |
| a byte after the 3 entries, before the List record | loads: 5, 6, 7 |
| the List record's count says 3, but only `ab` and `cde` follow, at the end of the file | **the form doesn't load**: "Property List in G could not be set" |
| 2 bytes, `zz`, after the List record's 3 items, at the end of the file | loads all 3 items |
| List items `61 81 62`, `E9`, `8D 8F 90 9D` | `AscW` reads 97 129 98, 233, and 141 143 144 157 |

**What these show:**
- **Items come from the List record alone.** The ItemData record's values are taken by position: any it doesn't reach read 0, and any beyond the items are dropped.
- **An ItemData entry reads as its leading decimal digits.**
  - Before them: spaces, then a sign.
  - Nothing readable gives 0.
  - Past a Long, the value wraps around the 32 bits.
- **Only a List record cut short stops the form loading.** Bytes after the entries that a record needs are ignored.
- **On code page 1252,** the five bytes the code page doesn't define read as the characters of the same number (U+0081, U+008D, U+008F, U+0090, U+009D). `E9` is `é`, as the code page says.

**Not measured** in these probes: a plus sign, a tab or other characters before an ItemData entry's digits (since measured, in the next section), and text in any code page other than 1252.

### Signs and whitespace before an ItemData entry's digits (2026-11-20, #5127, SP6)

These are three more probes set up as in the section above: the List record held `ab`, `cde`, `fghi`, and the ItemData record held three hand-written entries. They were built with `/make` as native code and as p-code, and the two logged the same lines.

| ItemData entries | VB6 |
|---|---|
| `+5`, a tab then `6`, `x5` | 5, 6, 0 |
| a CR then `7`, `+ 8`, `- 9` | 7, 0, 0 |
| `+-7`, an LF then `8`, `--9` | 0, 8, 0 |

**What these show:**
- **Tabs, CRs and LFs are skipped,** as spaces are.
- **`+` is a sign,** as `-` is.
- **One sign, and the digits must follow it at once.** `+ 8`, `- 9`, `+-7` and `--9` all read 0.

Put together with the section above, every entry measured fits one reading:
1. Skip spaces, tabs, CRs and LFs.
2. Take at most one `+` or `-`.
3. Read decimal digits up to the first character that isn't one, wrapping past a Long.
4. Anything else is 0.

That's a rule fitted to these rows, not a measurement of its own.

**Not measured:** a vertical tab or a form feed before the digits, and whitespace before a sign, as in ` -8`.
