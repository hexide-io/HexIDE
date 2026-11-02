## Reset and FileAttr (2026-10-28, #2521)

Measured on vb6.exe SP6, every row built native and as p-code, which logged the same lines. The oracle VM is an en-GB machine: `Format(1234.5, "Currency")` is `£1,234.50` and a date reads `28/10/2026`. The probe opened `#1` For Output, `#2` For Append, `#3` For Random, `#4` For Binary, and tried `#5` For Input on
the file `#1` held, which failed. So the Input row is **not** a measurement of Input mode: its 52 is a file not open.

**What the rows say:**
- `FileAttr(n, 1)` gives the mode as a Long: Output 2, Append 8, Random 4, Binary 32. (Input is 1 in VB6's documentation;
  unmeasured here.) The second argument defaults to 1, and anything else (0, 2, 3) is 5. A file number not open, 0
  included, is 52. The number is converted as CInt converts (1.6 is 2), and a numeric String is taken.
- `Reset` closes every file, writing what was buffered (the files' lengths are right afterwards), and with none open it
  does nothing. After it, `FreeFile` is 1, and the old numbers are 52. `Close #1` on a number Reset closed is no error.

| Expression | Result | Type | Err |
|---|---|---|---|
| `Reset with no file open` | `ok` | | 0 |
| `FileAttr(1, 1) Output` | `2` | Long | 0 |
| `FileAttr(2, 1) Append` | `8` | Long | 0 |
| `FileAttr(3, 1) Random` | `4` | Long | 0 |
| `FileAttr(4, 1) Binary` | `32` | Long | 0 |
| `FileAttr(5, 1) Input` |  |  | 52 |
| `FileAttr(1)` | `2` | Long | 0 |
| `FileAttr(1, 2)` |  |  | 5 |
| `FileAttr(1, 3)` |  |  | 5 |
| `FileAttr(1, 0)` |  |  | 5 |
| `FileAttr(9, 1) not open` |  |  | 52 |
| `FileAttr(0, 1)` |  |  | 52 |
| `FileAttr(1.6, 1)` | `8` | Long | 0 |
| `FileAttr(Null, 1)` |  |  | 94 |
| `FileAttr("1", 1)` | `2` | Long | 0 |
| `Reset with five open` | `FreeFile 1` | | 0 |
| `FileAttr(1, 1) after Reset` |  |  | 52 |
| `EOF(1) after Reset` |  |  | 52 |
| `FileLen(a.txt) after Reset` | `7` | Long | 0 |
| `FileLen(b.txt) after Reset` | `6` | Long | 0 |
| `Close #1 after Reset` | `ok` | | 0 |
