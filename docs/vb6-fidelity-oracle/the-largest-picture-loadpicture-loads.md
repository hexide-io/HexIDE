## The largest picture LoadPicture loads (2026-11-23, #5461, SP6)

**`LoadPicture` holds a picture at its decoded size and its own bit depth, and fails once that needs about 1.7 GB.** It isn't a limit on width or height. A JPEG is always held at 24 bits, even a greyscale one. A GIF is held at its palette's depth, so a 1-bit GIF 65000 pixels square loads and a 44000-pixel 8-bit one doesn't. Past the limit, the error is the COM one, **-2147024882, "Out of memory"**, not VB6's own error 7.

#5461 asked how big a picture vb6.exe loads, so that a cap on HexIDE's decoding refuses nothing VB6 accepts.

**The probe.**
- **The build:** VB6.EXE 6.00.9782 (SP6) and MSVBVM60 6.00.9848, built with `/make` as native code. The four boundary rows were run again as p-code, and the two agreed.
- **The program:** a module whose `Sub Main` runs `Set p = LoadPicture(App.Path & "\imgs\" & Command$)` under `On Error Resume Next`, and writes `Err.Number` and `Err.Description`. On success it writes `p.Type`, `p.Width`, `p.Height` (HIMETRIC) and `p.Handle`.
- **How it was run:** one process per picture, so no load inherited another's memory, in session 0 of the Win10-C clone.
- **The guest:** Windows 10 LTSC 2021, with Hyper-V dynamic memory from 1 to 8 GB and a system-managed page file. The commit limit read 2,057 MB before the runs and 2,781 MB after.
- **Peak private bytes:** sampled every 100 ms from outside the process.
- **The pictures:** single-colour or striped, made with Pillow, so the files are small and the decode large.
  - JPEG at quality 50, RGB and greyscale (`L`).
  - GIF in two kinds: a 1-bit one, which Pillow writes when only one palette index is used, and an 8-bit one with all 256 indices in a repeating ramp. The header's global colour table, read back, has 2 entries and 256 entries respectively.

**JPEG, RGB:**

| Pixels | File | Result | Peak private | Time |
|---|---|---|---|---|
| 4000² to 20000², eight sizes | 0.25 to 6.3 MB | loaded, `Type` 1, full size in HIMETRIC | 1,149 MB at 20000² | up to 7.5 s |
| 65000 × 64 and 64 × 65000 | 64 KB | loaded | | 1 to 2 s |
| 24000² (576 M) | 9.0 MB | **loaded** | **1,654 MB** | 10.8 s |
| 28000² (784 M) | 12.3 MB | **-2147024882, Out of memory** | | 0.1 s |
| 32000² | 16.0 MB | -2147024882, Out of memory | | 0.1 s |

**JPEG, greyscale:**

| Pixels | Result | Peak private |
|---|---|---|
| 24000² | loaded | **1,653 MB**, the same as RGB: held at 24 bits |
| 28000², 32000², 40000² | -2147024882, Out of memory | |

**GIF, 1-bit:**

| Pixels | Result | Peak private | Time |
|---|---|---|---|
| 8000² to 28000², six sizes | loaded | 95 MB at 28000² | up to 33 s |
| 32000², 40000², 48000² | loaded | 124, 193 and 277 MB | 39 to 73 s |
| **65000²** (4,225 M) | **loaded** | **506 MB** | 146 s |
| 65000 × 64 and 64 × 65000 | loaded | | 1 s |

**GIF, 8-bit:**

| Pixels | File | Result | Peak private |
|---|---|---|---|
| 24000² | 102 MB | loaded | 552 MB |
| 32000² | 181 MB | loaded | 980 MB |
| 40000² (1,600 M) | 283 MB | **loaded** | **1,531 MB** |
| 44000² (1,936 M) | 342 MB | **-2147024882, Out of memory** | |

**What it shows:**
- **The peaks match a picture held whole at its own depth:**
  - 576 M pixels at 3 bytes is 1,648 MB, against 1,654 measured;
  - 1,600 M at 1 byte is 1,526 MB, against 1,531;
  - 4,225 M at 1 bit is 504 MB, against 506.
  So `LoadPicture` decodes in full, not lazily, and the `Handle` is a real bitmap.
- **Every load measured fits one rule:** it succeeds up to 1,654 MB at the picture's own depth and fails from 1,846 MB (the 44000² 8-bit GIF). The 28000² JPEG would need 2,243 MB. A 32-bit process has 2 GB of address space, which is reasoned as the cause, not checked.
- **The boundary moves with the machine.** Where between 1.65 and 1.85 GB a load fails depends on what else the process has mapped. These rows bound it; they don't fix it.
- **The failure is instant** (0.1 s), so the size is checked before decoding. The number is the HRESULT `E_OUTOFMEMORY`, not error 7.

**Not measured:**
- A picture in a form's `.frx` at load, rather than `LoadPicture`.
- BMP, ICO and CUR, and a greyscale or 1-bit JPEG held at a lower depth (none was).
- A `.png` file. VB6 refuses a PNG inside an icon with 481 (`a-forms-window-icon-no-icon-line.md:93`), and none was loaded here.
- A machine with other memory, and a large-address-aware host.
