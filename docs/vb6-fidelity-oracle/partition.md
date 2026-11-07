## Partition (2026-10-28, #2526)

Measured on vb6.exe SP6, every row built native and as p-code, which logged the same lines. The oracle VM is an en-GB machine: `Format(1234.5, "Currency")` is `£1,234.50` and a date reads `28/10/2026`.

**The rule the rows give:**
- Each side is right-aligned to the width of `Stop + 1`'s digits: `Stop` 100 gives width 3, and `Stop` 999 width 4.
- Below `Start` is `"   :" & (Start - 1)`, and above `Stop` is `(Stop + 1) & ":   "`, padded the same way.
- The ranges start at `Start` and step by `Interval`; the last one ends at `Stop`. An interval wider than the range gives
  one range.
- The arguments are converted as CLng converts (24.6 is 25; an interval of 10.6 is 11, so 25 falls in `22:32`). A numeric
  String is taken, `"x"` is 13, and **Null in any argument gives Null**.
- A negative `Start`, `Stop` not above `Start`, and an `Interval` below 1 are all 5.

| Expression | Result | Type | Err |
|---|---|---|---|
| `Partition(25, 0, 100, 10)` | ` 20: 29` | String | 0 |
| `Partition(0, 0, 100, 10)` | `  0:  9` | String | 0 |
| `Partition(100, 0, 100, 10)` | `100:100` | String | 0 |
| `Partition(101, 0, 100, 10)` | `101:   ` | String | 0 |
| `Partition(-1, 0, 100, 10)` | `   : -1` | String | 0 |
| `Partition(5, 0, 9, 1)` | ` 5: 5` | String | 0 |
| `Partition(5, 0, 99, 5)` | `  5:  9` | String | 0 |
| `Partition(1000, 0, 999, 100)` | `1000:    ` | String | 0 |
| `Partition(50, 10, 90, 25)` | `35:59` | String | 0 |
| `Partition(24.6, 0, 100, 10)` | ` 20: 29` | String | 0 |
| `Partition(25, 0, 100, 10.6)` | ` 22: 32` | String | 0 |
| `Partition(-5, -10, 10, 5)` |  |  | 5 |
| `Partition(25, 100, 0, 10)` |  |  | 5 |
| `Partition(25, 0, 100, 0)` |  |  | 5 |
| `Partition(25, 0, 0, 10)` |  |  | 5 |
| `Partition(25, -1, 100, 10)` |  |  | 5 |
| `Partition(Null, 0, 100, 10)` | Null | Null | 0 |
| `Partition(25, Null, 100, 10)` | Null | Null | 0 |
| `Partition("25", 0, 100, 10)` | ` 20: 29` | String | 0 |
| `Partition("x", 0, 100, 10)` |  |  | 13 |
| `Partition(25, 0, 100, 200)` | `  0:100` | String | 0 |
| `Partition(99999, 0, 100000, 10)` | ` 99990: 99999` | String | 0 |
