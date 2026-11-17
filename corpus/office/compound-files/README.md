# Compound-file corpus

Compound files ([MS-CFB]) for testing HexIDE's read-only reader (#4329, the first slice of #1119). **Windows' own
structured storage wrote them** (ole32, through pywin32's `StgCreateDocfile` and `StgCreateStorageEx`), not anything in
HexIDE. So a reader that agrees with them agrees with another implementation, not only with a writer of our own.

`make_cfb_fixtures.py` is the generator, run with Python 3 and pywin32 on Windows: `py -3 make_cfb_fixtures.py <dir>`.
These files were made with it on 2026-11-16, on Windows Server 2025 (build 26100), and `manifest.json` gives each
stream's path, length and SHA-256. The streams hold a pattern, `(i * 7 + seed) % 251` for byte `i`, except `PROJECT`
and `PROJECTwm`, which hold a line or two of placeholder text, so no byte here came from anywhere else. Windows also
records times in the directory, so a rerun makes different files with the same streams.

| File | Covers |
|---|---|
| `v3-mixed.cfb` | Version 3, 512-byte sectors. Streams of 0, 100, 4095 (mini stream), 4096 (FAT) and 20000 bytes; 24 siblings, so each storage's tree has depth; a nested storage, one nested again, and an empty storage |
| `v4-mixed.cfb` | The same tree, version 4, 4096-byte sectors |
| `v3-partial-last-sector.cfb` | `v3-mixed.cfb` without the 320 unused bytes at the end of its last sector, the mini stream's tail, so the file ends inside a sector. Not written by Windows, but Windows' `StgOpenStorage` reads every stream of it, identical to `v3-mixed.cfb`'s (checked 2026-11-16). Real files end this way: a cached Windows Installer package ends 1,777 bytes into its last 4096-byte sector, and Windows reads it |
| `v3-difat.cfb.gz` | 117 FAT sectors, more than the header's 109, so the DIFAT continues in a DIFAT sector. Gzipped, since it is ~7.6 MB of pattern |
| `xls-shaped.cfb` | An `.xls`'s shape: a `Workbook` stream and a `_VBA_PROJECT_CUR` storage holding `PROJECT`, `PROJECTwm` and `VBA` (`dir`, `_VBA_PROJECT`, `Module1`) |
| `doc-shaped.cfb` | A `.doc`'s shape: `WordDocument`, `1Table`, and the same project in a `Macros` storage |
| `no-vba.cfb` | A `.doc`'s shape with no project |
| `xlsm-shaped.zip` | An `.xlsm`'s shape: `[Content_Types].xml` gives `/xl/vbaProject.bin` the type `application/vnd.ms-office.vbaProject`, and that part is a compound file whose root is the project |
| `xlsx-no-vba.zip` | The same with no project part |

**The project streams hold placeholder bytes**, not [MS-OVBA] content. They test only where a container keeps its
project. Reading the project needs real Office files, which #4330 adds.

**Not VB6 files**, so no corpus lane walks this folder.
