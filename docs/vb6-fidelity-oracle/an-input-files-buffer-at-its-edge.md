## An Input file's buffer at its edge: EOF, Line Input and a file that grows (2026-11-19, #4944, SP6)

**`EOF` at the buffer's end refills it.** So does a `Line Input` that meets a CR in the buffer's last byte. `LOF` doesn't,
and neither does a read that ends exactly at the buffer's end. **A refill that finds nothing sticks:** after it, `EOF`
stays True and every read is 62, even once the file has grown.

**Context.** An Input file is read through a 512-byte buffer, as oracle PR #4942 measured in
`a-file-changed-while-it-is-open.md`.
- **Filling it:** when a read needs a byte the buffer doesn't hold, the buffer takes up to 512 bytes from that byte on,
  as the file is then.
- **What it returns:** bytes it holds read as they were when it was filled.
- **What these rows add:** what happens at the buffer's last byte, where a read can stop without needing the next one.

**The probe.**
- **Build:** vb6.exe 6.00.9782 with MSVBVM60 6.00.9848, `/make` native and p-code in session 0. One program; both builds
  logged every line identically.
- **The files:** written with `Print #f, s;`. Byte *i*, from 0, is `"A"` plus *i* `Mod 26`, with CR LF where a row says.
- **The change:** made through a second Win32 handle in the same process (`CreateFileA`, then `WriteFile`). Each write
  closed its handle at once.
- **A mark** is one `#` byte.
- **Logged:** each read's `Err`, which bytes were old letters and which marks, by position, and `Loc`, `Seek`, `EOF` and
  `LOF` after the last read.

### A read that stops at the buffer's end

A 10,000-byte file of letters, `InputB(512, #1)` first, then marks at 513 to 522.

| Between the first read and the next | Next read: `InputB(10, #1)` | `Loc`, `Seek`, `EOF`, `LOF` after |
|---|---|---|
| the marks | the marks | 5, 523, False, 10000 |
| the marks, then `EOF(1)`, False, and `LOF(1)`, 10000 | the marks | 5, 523, False, 10000 |
| `LOF(1)`, 10000, then the marks | the marks | 5, 523, False, 10000 |
| **`EOF(1)`, False, then the marks** | **the old letters** | 5, 523, False, 10000 |

**So `EOF` at the buffer's end refills it,** and `LOF` doesn't. A row on the earlier page already showed `EOF` with
nothing read filling it.

### Line Input at the buffer's end

The file's lines are 510 letters and CR LF, so the first line's CR LF is at 511 and 512. After `Line Input #1, s` read
line 1, marks replaced bytes 513 to 1022, the whole of line 2.

| Between the two `Line Input`s | Line 2 reads | Then `InputB(10, #1)` | After |
|---|---|---|---|
| the marks | the marks | letters 1025 to 1034 | 9, 1035, False, 10000 |
| the marks, then `EOF(1)`, False, and `LOF(1)`, 10000 | the marks | letters 1025 to 1034 | 9, 1035, False, 10000 |
| **`EOF(1)`, False, then the marks** | **the old letters** | letters 1025 to 1034 | 9, 1035, False, 10000 |

**A line ending with its LF in the buffer's last byte doesn't refill it.** A CR in the last byte does: with the CR at
512 and its LF at 513, `Line Input` read line 1 (511 letters). Then marks went into 514 to 523, and line 2 read **the
old letters** 514 to 1022. Looking for the LF had already refilled the buffer from 513. After the `InputB(10, #1)` that
followed: 9, 1035, False, 10000.

**A line across the buffer's end.** CR LF at 501–502 and 533–534, so line 2 is 503 to 532. After line 1 was read, marks
went into 505 to 530. Line 2 read old letters 503 to 512, marks 513 to 530, and letters 531 and 532. The buffer held 1
to 512, and 513 on came from the disk.

### A file that grows at the buffer's end

A file of exactly 512 bytes, read to its end, then grown through the second handle. `s` held `"x"` before each read.

| The file, and the first read | Before the growth | Grown by | Then | Result | After |
|---|---|---|---|---|---|
| one line, 510 letters and CR LF; `Line Input` | nothing | `####` CR LF, to 518 | `EOF(1)`, `LOF(1)` | False, 518 | |
| | | | `Line Input #1, s` | `####` | |
| | | | `InputB(10, #1)` | **62**, `s` still `"x"` | 5, 519, True, 518 |
| the same | **`EOF(1)`: True** | `####` CR LF, to 518 | `EOF(1)` | **True** | |
| | | | `Line Input #1, s` | **62**, `s` still `"x"` | 4, 513, True, 518 |
| 512 letters; `InputB(512, #1)` | nothing | ten `#`, to 522 | `EOF(1)`, `LOF(1)` | False, 522 | |
| | | | `InputB(10, #1)` | the ten marks | 5, 523, True, 522 |
| the same | **`EOF(1)`: True** | ten `#`, to 522 | `EOF(1)` | **True** | |
| | | | `InputB(10, #1)` | **62**, `s` still `"x"` | 4, 513, True, 522 |

**So the end sticks once a refill has found it.** With no `EOF` before the growth, the file read on into the new bytes.
With one, `EOF` stayed True and the next read was 62, though `LOF` already counted the new bytes.

### What these rows say, read as measured

- **What refills the buffer:** a read that needs a byte it doesn't hold. That includes `EOF`, at the buffer's end or
  with nothing read, and a `Line Input` looking past a CR in its last byte.
- **What doesn't:** `LOF`, and a read that ends exactly at the buffer's end.
- **A refill that finds nothing ends the file:** `EOF` is True from then on, and reads are 62, whatever the file does
  next.

**Not measured:**
- `Input #` at the buffer's end;
- `Loc` and `Seek` as refills;
- a refill that finds the end, then a `Seek` back, then growth;
- a CR as the file's last byte;
- a second program, rather than a second handle in this one.
