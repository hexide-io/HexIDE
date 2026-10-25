## LOF, Loc, Seek and the Input function (2026-10-23, #1645)

**The question.** #1145 made sequential files run and #1521 added `Input #` and `Write #`, but `LOF`, `Loc`, `Seek` and the
`Input` function all stopped the program, so `Input(LOF(f), f)`, the usual way to read a whole file, never ran. What do
they answer on a sequential file, and how do they meet line breaks, a Ctrl+Z, the code page, Output and Append files,
and each other?

**The probe.** Three batches, each built native and p-code with vb6.exe 6.00.8176 (the release build) and run on the
MSVBVM60 6.00.9848 (SP6) runtime, en-GB, code page 1252. Native and p-code agreed on every row. The first two batches are
each one program under `On Error Resume Next`, which makes its files byte for byte with `Put` on a Binary file, reads them
back the same way, and logs each read as its characters with a control character as `<n>`, then `LOF`, `Loc`, `Seek` and
`EOF`. The third batch is one-line projects, for the forms that might not compile.

**Reading** `a.txt`, the 14 bytes `ab` CR LF `c,d` CR LF `"q"` CR LF:

| Statement | Read | LOF, Loc, Seek, EOF after |
|---|---|---|
| (just opened) | | 14, 1, 1, False |
| `TypeName` of `LOF(1)`, `Loc(1)`, `Seek(1)`, `Input(1, #1)` | `Long Long Long String` | |
| `Input(3, #1)` | `ab<13>` | 14, 1, 4, False |
| `Input(5, 1)` | `<10>c,d<13>` | 14, 1, 9, False |
| `Line Input #1` | `<10>"q"`: the LF left by Input stays in the line | 14, 1, 15, True |
| `Input(100, #1)` at the end | 62, the variable kept | 14, 1, 15, True |
| `Input(0, #1)` at the end | `""`, no error | 14, 1, 15, True |
| `Input(LOF(1), 1)`, just opened | the whole file | 14, 1, 15, True |
| `Input(100, #1)`, just opened | 62, the variable kept | 14, 1, 15, True: the file is read to its end |
| `Input(0, #1)`, `Input(-1, #1)` | `""`; 5, the variable kept | 14, 1, 1, False |
| `Input(1.6, #1)`; then `Input(2.5)`, `Input(0.5)`, `Input(1.5)`, `Input("2")` | 2 characters; then 2, 0, 2 and 2: the count rounds to even | |
| `Seek #1, 5`, then `Input(3, #1)` | `c,d` | 14, 1, 8, False |
| `Seek #1, 1`, then `Line Input #1` | `ab` | 14, 1, 5, False |
| `Seek #1, 0`; `Seek #1, -1` | 63, "Bad record number", the position kept | |
| `Seek #1, 14`; `Seek #1, 15` | | 14, 1, 14, False; 14, 1, 15, True |
| `Seek #1, 100`, then `Input(1)`, `Line Input` | 62 each | 14, 1, 100, True |
| `Seek #1, 3`, `Input(1, #1)`, `Line Input #1` | `<13>`, then `<10>c,d` | 14, 1, 10, False |
| two Input handles on one file, `Input(3)` on the first | | the first 14, 1, 4; the second 14, 1, 1 |

**Loc counts 128-byte blocks**, read from a file of 298 `x` and CR LF:

| After reading | 0 | 127 | 128 | 129 | 256 | 257 |
|---|---|---|---|---|---|---|
| Loc | 1 | 1 | 1 | 2 | 2 | 3 |

On an Input file it is the bytes read divided by 128, rounded up and at least 1, so 1 for an empty file too. On an Output
or Append file it is the bytes written divided by 128, rounded down: 0 after 5 bytes, 2 after 307. The documentation's
"divided by 128" is right; which way it rounds, and the floor of 1, it does not say.

**A Ctrl+Z.** `z.txt` is `ab`, Ctrl+Z, `cd`. `Input(2, #1)` reads `ab`, after which EOF is True and Seek 3. `Input(1, #1)`
there is 62, and so is `Input(5, #1)` from the start: the Ctrl+Z ends the file for Input as it does for Line Input. Seek
past it, `Seek #1, 4`, and `Input(2, #1)` reads `cd`. What resisted a first reading was Seek after a read reached the
Ctrl+Z: 9, on a five-byte file. Six files settled it:

| File | LOF | Ctrl+Z at | Seek after Input(LOF), or a Line Input, reaches it |
|---|---|---|---|
| Ctrl+Z | 1 | 1 | 3 |
| `a`, Ctrl+Z | 2 | 2 | 5 |
| `ab`, Ctrl+Z | 3 | 3 | 7 |
| `abc`, Ctrl+Z, `defgh` | 9 | 4 | 14 |
| `ab`, Ctrl+Z, `cd` | 5 | 3 | 9 |
| 200 `x`, Ctrl+Z, `cd` | 203 | 201 | 405, and Loc 4 |

So once a read reaches a Ctrl+Z, Seek is the length plus the Ctrl+Z's position plus 1, on all six. A Line Input that ends
at one, `a` from `a`, Ctrl+Z, reaches it as surely as a failed Input does. I would not defend a reason; the rule is what
was measured.

**The code page.** `hi.txt` is `x`, byte 233, `y`. `Input(3, #1)` reads `x<233>y`, é in code page 1252. `InputB(3, #1)` is
`LenB` 3 and `Len` 1, its bytes 120, 233 and 121: a byte string. `Input$(2, #1)` reads `x<233>`, and is a String.

**Output and Append:**

| Statement | LOF, Loc, Seek, EOF after |
|---|---|
| `Open o.txt For Output` | 0, 0, 1, True |
| `Print #2, "abc"` | 5, 0, 6, True, and `FileLen` 0 while the file is open: LOF counts what is not yet written out |
| `Input(1, #2)` | 54, "Bad file mode" |
| 300 more `y` and CR LF | 307, 2, 308, True; `FileLen` 307 after Close |
| `abcdef`, `Seek #2, 2`, `XY`, `Seek #2, 10`, `Z` | the file is `aXYdef`, three NULs, `Z`: a gap is NUL bytes |
| an empty file, `Seek #2, 5`, `a` | four NULs, `a` |
| `Open ap.txt For Append` on `xy` CR LF | 4, 0, **1**, True |
| `Print #3, "z"` | 7, 0, 4, True |
| `Input(1, #3)` | 54 |
| `Seek #3, 1`, `Print #3, "Q";` | the file is `Qy` CR LF `z` CR LF: written over its first byte |
| `Seek #3, 5` on `xy` CR LF, `z` | appended as usual, Seek 6 |
| Append to a 300-byte file, then 200 bytes | Seek 1 at the opening, then 201, Loc 1, LOF 500 |

So an Append file's Seek counts from 1 at its opening, not from where it writes, until a Seek statement makes it the
file's own position. The obvious guess was that it starts at the length plus 1.

**Refused:**
- On a number not open, `LOF`, `Loc`, `Seek`, `Seek #40, 1` and `Input(1, #40)` are each 52.
- A Binary open of a file open for Input is 55, as an Output or Append one is (#1145). So within one program no second
  handle can write to a file another is reading, and LOF of a file being written elsewhere does not arise.
- `LOF(#1)`, `EOF(#1)`, `Loc(#1)`, `Seek(#1)` and `Input(1)` are each "Syntax error". `Seek 1, 2`, with no `#`, compiles.
- **Named arguments.** `LOF(FileNumber:=1)`, `Loc(FileNumber:=1)` and `Seek(FileNumber:=1)` compile; `LOF(Number:=1)` is
  "Named argument not found" and `LOF(1, 2)` "Wrong number of arguments or invalid property assignment". `Input` and
  `InputB` take no names: `Input(Number:=2, FileNumber:=1)`, either way round, with `#1`, `Input(2, FileNumber:=1)` and
  `Input(1, 1, 1)` are each "Syntax error".

### In HexIDE (#1645)

- **`VbFiles`** answers `LOF`, `Loc`, `Seek` and `Input` as above, and the `Seek` statement moves the position. An Append
  file is opened at its end without .NET's append mode, which would refuse the seek before its end that VB6 allows.
- **The Input function** reads characters in the ANSI code page. It takes its file number with or without the `#`, and a
  `#` file number anywhere else is "Syntax error". `Input$` is `Input`.
- **`FilePositionTests`** runs each slice of the probe programs and must print vb6.exe's lines. The probe made its files
  with Binary `Put`, which HexIDE has not got (#1646), so each test writes the same bytes itself, and checks the bytes
  each Output and Append file ends with.
- **Divergences kept:**
  - `InputB` is refused as not implemented: it returns a byte string, which HexIDE has not got (#1510);
  - an Input file is read whole when it opens (#1145), so its LOF is its length then, and a file another process changes
    meanwhile reads as it was;
  - how `Input` counts characters in a double-byte code page is not measured. It reads characters, so a lead byte and
    its trail byte make one;
  - which of 5 and 54 `Input(-1, #n)` on an Output file gives is not measured: 5.

