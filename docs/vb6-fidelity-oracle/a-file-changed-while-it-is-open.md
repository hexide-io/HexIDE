## InputB on an Append file, and a file changed while it is open (2026-11-19, #4927, SP6)

**InputB on an Append or Output file is 54, unless its count is negative, which is 5 first.** A file changed while it is
open reads differently by mode. **A Binary file is read straight from the disk:** every read sees every change made before
it. **An Input file is read through a 512-byte buffer:** a read sees a change only past what the buffer already holds.

**The probe.**
- **Build:** vb6.exe 6.00.9782 with MSVBVM60 6.00.9848, `/make` native and p-code in session 0. Five programs; each
  logged every line identically in the two builds.
- **Run twice:** the Append rows and the reads past the end of the cut Binary file. The second run logged `s` and the
  positions after every row.
- **The files:** written with `Print #f, s;`. Byte *i* of each, counting from 0, is `"A"` plus *i* `Mod 26`: 10,000
  bytes, or 3,000 where a row changes the size.
- **The change:** made through a second Win32 handle in the same process, which VB6's own file table doesn't know about.
  That handle is `CreateFileA` with `GENERIC_WRITE` and `FILE_SHARE_READ Or FILE_SHARE_WRITE`, then `WriteFile` or
  `SetEndOfFile`.
  - It wasn't another program, so these rows don't measure one.
  - VB6's own `Open` allowed the handle in every row, Input mode included.
  - Each write closed its handle at once, except in the rows that say the writer stayed open.
- **The marks:** a mark is one `#` byte. A row reports each byte read by its position in the file: the old letter, or the
  mark that replaced it.
- **The program:** one Sub Main under `On Error Resume Next`. After each read it logs `Err`, the bytes, `Loc`, `Seek`,
  `EOF` and `LOF`.

### InputB on an Append file, and on an Output file

A 26-byte file, open `For Append As #1`. `s` held `"x"` before each row and still held it after.

| Statement | Err |
|---|---|
| `s = InputB(1, #1)` | 54 |
| `s = InputB(0, #1)` | 54 |
| `s = InputB(-1, #1)` | 5 |
| `s = InputB$(1, #1)` | 54 |
| `s = Input(-1, #1)` | 5 |
| `Seek #1, 1`, then `s = InputB(1, #1)` | 0, then 54 |
| `s = InputB(1, #1)` on a new, empty file open `For Append` | 54 |

`Loc` 0, `Seek` 1, `EOF` True and `LOF` 26 before and after each, and `LOF` 0 for the new file.

On a file open `For Output As #2`: `InputB(-1, #2)` is 5, `Input(-1, #2)` is 5, and `InputB(0, #2)` is 54.

**So a negative count is checked before the file's mode,** on an Append or Output file, for `InputB` and `Input` alike.
Any other count is 54. This settles the order `Input`'s count and an Output file's 54 were never measured in.

### A file changed while it is open, in Input mode

The 16 marks are at 11, 256, 257, 512, 513, 514, 1024, 1025, 2048, 2049, 4096, 4097, 4098, 8192, 8193 and 10000 of the
10,000-byte file. A row reads from where the first read left off to the end.

| First read | The marks | Then | Marks read | Old letters read instead |
|---|---|---|---|---|
| none | | `InputB(10000, #1)` | all 16 | none |
| `InputB(10, #1)` | | `InputB(9990, #1)` | 513 on | 11, 256, 257, 512 |
| `InputB(10, #1)` | | 99 `InputB(100, #1)` and an `InputB(90, #1)` | 513 on | 11, 256, 257, 512 |
| `InputB(600, #1)` | | `InputB(9400, #1)` | 1025 on | 1024 |
| `InputB(10, #1)` | | `Seek #1, 11`, then `InputB(9990, #1)` | all 16 | none |
| `InputB(512, #1)` | | `InputB(9488, #1)` | 513 on | none |
| `InputB(511, #1)` | | `InputB(9489, #1)` | 513 on | 512 |
| `EOF(1)`, False, and no read | | `InputB(10000, #1)` | 513 on | 11, 256, 257, 512 |
| none | | `Input(10000, #1)` | all 16 | none |
| `Input(10, #1)` | | `Input(9990, #1)` | 513 on | 11, 256, 257, 512 |
| `Input(600, #1)` | | `Input(9400, #1)` | 1025 on | 1024 |
| `Input(10, #1)` | | `Line Input #1, s` | 513 on | 11, 256, 257, 512 |
| `Input(10, #1)` | | `Input #1, v` | 513 on | 11, 256, 257, 512 |
| `InputB(10, #1)` | | `Input(9990, #1)` | 513 on | 11, 256, 257, 512 |

After each of them `Loc` is 79, `Seek` 10001, `EOF` True and `LOF` 10000.

**Reading from the middle.** `Seek #1, 300` and `InputB(1, #1)`, then marks at 100, 400, 520, 812, 1024 and 1025:
- `InputB(700, #1)`, bytes 301 to 1000, reads the mark at 812, and old letters at 400 and 520.
- After it, `Loc` is 8, `Seek` 1001 and `EOF` False.
- Then `Seek #1, 100` and `InputB(1, #1)` reads the mark at 100.

**A writer still open.** After `InputB(10, #1)`, marks at 11 and 601 from a handle not yet closed. `InputB(990, #1)` reads
the mark at 601, and the old letter at 11.

**The size changed**, on the 3,000-byte file after `InputB(10, #1)`. `s` held `"x"` before each read.

| Change | Read | Err | Bytes read | `Loc`, `Seek`, `EOF`, `LOF` after |
|---|---|---|---|---|
| Grown to 3,010 by ten marks | `InputB(2990, #1)` | 0 | letters, 11 to 3000 | 24, 3001, False, 3010 |
| | then `InputB(10, #1)` | 0 | the ten marks | 24, 3011, True, 3010 |
| Cut to 100 | `InputB(90, #1)` | 0 | letters, 11 to 100 | 1, 101, False, 100 |
| | then `InputB(10, #1)` | 0 | **letters 101 to 110, past the new end** | 1, 111, False, 100 |
| | then `InputB(2000, #1)` | 62 | none: `s` still `"x"` | 4, **513**, True, 100 |
| Rewritten as 5,000 lower-case letters | `InputB(2990, #1)` | 0 | **upper-case 11 to 512**, lower-case 513 to 3000 | 24, 3001, False, 5000 |
| | then `InputB(2000, #1)` | 0 | lower-case, 3001 to 5000 | 40, 5001, True, 5000 |
| Rewritten, then `Seek #1, 11` | `InputB(2990, #1)` | 0 | lower-case, 11 to 3000 | 24, 3001, False, 5000 |
| | then `InputB(2000, #1)` | 0 | lower-case, 3001 to 5000 | 40, 5001, True, 5000 |

The `Input` function on the file cut to 100 gave the same as `InputB`:
- `Input(90, #1)` read letters 11 to 100.
- `Input(10, #1)` then read letters 101 to 110.
- `Input(2000, #1)` was 62, with `Loc` 4, `Seek` 513 and `EOF` True.

**A rule fitted to these rows:** an Input file is read through a 512-byte buffer.
- **Filling it:** when a read needs a byte the buffer doesn't hold, the buffer takes up to 512 bytes from that byte on,
  as the file is then.
- **What it returns:** bytes the buffer holds read as they were when it was filled, even past a new end.
- **The end:** a fill that finds nothing is 62, and leaves the file at the buffer's end.
- **Emptying it:** `Seek` empties it. `EOF` with nothing read yet fills it.
- **Who reads through it:** `InputB`, `Input`, `Line Input` and `Input #` alike.

**The obvious guess was 512-byte blocks counted from the file's start,** and the rows reading from position 1 can't tell
the two apart. The row reading from 300 can: an aligned block would have held 1 to 512, and the mark at 520 would have
read. It read the old letter, because the buffer held 300 to 811.

### A file changed while it is open, in Binary mode

The same 16 marks.

| First read | Then | Marks read |
|---|---|---|
| none | `InputB(10000, #1)` | all 16 |
| `InputB(10, #1)` | `InputB(9990, #1)` | all 16 |
| `InputB(10, #1)` | 99 `InputB(100, #1)` and an `InputB(90, #1)` | all 16 |
| `InputB(600, #1)` | `InputB(9400, #1)` | all from 1024 |
| `InputB(10, #1)` | `Seek #1, 11`, then `InputB(9990, #1)` | all 16 |
| `Input(10, #1)` | `Input(9990, #1)` | all 16 |
| `Input(10, #1)` | `Line Input #1, s` | all 16 |
| `InputB(10, #1)` | `Input(9990, #1)` | all 16 |
| `InputB(10, #1)` | `Input #1, v` | all 16 |
| `InputB(10, #1)` | `Get #1, , g`, `g` 9,990 spaces | all 16 |
| `Get #1, , g`, `g` 10 spaces | `Get #1, , g`, `g` 9,990 spaces | all 16 |

A second handle of the program's own also changed the file. `Open` `For Binary As #2` after the first read, then `Put #2`
of the 16 marks: `InputB(9990, #1)` reads all 16. And with the writer still open, marks at 11 and 520 after
`InputB(10, #1)`: `InputB(600, #1)` reads both.

**The size changed**, on the 3,000-byte file after `InputB(10, #1)`:

| Change | Read | Err | Bytes read | `Loc`, `Seek`, `EOF`, `LOF` after |
|---|---|---|---|---|
| Grown to 3,010 by ten marks | `InputB(2990, #1)` | 0 | letters, 11 to 3000 | 3000, 3001, False, 3010 |
| | then `InputB(10, #1)` | 0 | the ten marks | 3010, 3011, False, 3010 |
| Cut to 100 | `InputB(90, #1)` | 0 | letters, 11 to 100 | 100, 101, False, 100 |
| Rewritten as 5,000 lower-case letters | `InputB(2990, #1)` | 0 | lower-case, 11 to 3000 | 3000, 3001, False, 5000 |
| | then `InputB(2000, #1)` | 0 | lower-case, 3001 to 5000 | 5000, 5001, False, 5000 |
| Rewritten, then `Seek #1, 11` | the same two reads | 0 | the same | the same |

**So a Binary file has no buffer that a change can hide behind:** in every row, every read saw every change made before
it, whether or not the writer had closed.

**Measured, but no rule I would defend:** a read past the end of the file cut to 100 is no error, as on the
[InputB page](the-byte-string-functions.md#inputb-by-file-mode-2026-11-19-4895-sp6). The bytes past the end are stale,
and they weren't the same twice:
- **`InputB(10, #1)` at 101:** 31 00 32 00 36 00 00 00 00 00 in the first run, and 30 00 33 00 32 00 00 00 00 00 in the
  second.
- **Then `InputB(2000, #1)`:** 2,000 upper-case letters. In the second run, which checked each against the file's old
  letter at its position from 101, none matched.
- **Neither read moved the file:** `Loc` stayed 100 and `Seek` 101, and `EOF` turned True.

**Not measured:**
- a second program, rather than a second handle in this one;
- a change near a Ctrl+Z in an Input file;
- `Line Input` and `Input #` on a file whose size changed;
- Random files;
- a change to a file open for Output or Append.

**In HexIDE (#4927),** read from the code on main at 4cf20ffbb, not run:
- **The refusals:** `ReadBytes` refuses `InputB` on an Append file (`VbFiles.cs:519`). It refuses it too on an Input or
  Binary file whose length or last-write time has changed since it opened (`:521`). A read past the end of a Binary file
  is refused (`:526`).
- **Input mode:** an Input file is read whole when it opens (`VbFiles.cs:181`). `Input`, `Line Input` and `Input #` read
  that copy, so after a change they return the file as it was at the `Open`.
- **Binary mode:** `Input`, `Line Input` and `Input #` read a copy taken by the first of them (`VbFiles.Records.cs:75`). It
  is kept until this program writes to the file, so after another handle's change they too return the copy.
- **`Get`** reads through a stream with no buffer (`VbFiles.Records.cs:30`).
