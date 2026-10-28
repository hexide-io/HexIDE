## LoadResString, LoadResData and LoadResPicture (2026-10-28, #2626)

**The question.** HexIDE has none of the three, and doesn't read a project's `.res` at all (#2625). What does vb6.exe read
from one, and what does it raise?

**The probe.** A `.res` built byte by byte from the Win32 RES format's published layout, named by `ResFile32="probe.res"`.
It holds a string block (IDs 96 to 111) with 101 `Hello`, 102 `café€`, 103 empty and 104 `x`; `RCDATA` 201, bytes
1 2 3 250; a `CUSTOM` resource with ID 202, bytes 65 66, and one named `NAMED`, byte 9; and bitmap 301, a 2×2 24-bit
DIB. Every entry is language 0x0409. vb6.exe linked it without complaint. A string is logged as its length and each
character's `AscW`; a byte array as its type, bounds and bytes. A second project has no `ResFile32=` line.

| Probe line | vb6.exe |
|---|---|
| `LoadResString(101)` | 5: 72 101 108 108 111 err 0 |
| `LoadResString(102)` | 5: 99 97 102 233 8364 err 0 |
| `LoadResString(103), an empty entry` | unset err 326 |
| `LoadResString(104)` | 1: 120 err 0 |
| `LoadResString(999)` | unset err 326 |
| `LoadResString("101")` | 5: 72 101 108 108 111 err 0 |
| `LoadResString(101.4)` | 5: 72 101 108 108 111 err 0 |
| `LoadResString(0)` | unset err 326 |
| `LoadResString(-1)` | unset err 326 |
| `TypeName(LoadResString(101))` | String err 0 |
| `LoadResData(201, 10)` | Byte() 0 to 3: 1 2 3 250 err 0 |
| `LoadResData(202, "CUSTOM")` | Byte() 0 to 1: 65 66 err 0 |
| `LoadResData(202, "custom")` | Byte() 0 to 1: 65 66 err 0 |
| `LoadResData("NAMED", "CUSTOM")` | Byte() 0 to 0: 9 err 0 |
| `LoadResData(999, 10)` | unset err 326 |
| `LoadResData(201, "CUSTOM")` | unset err 326 |
| `LoadResData(7, 6), the string block` | unset err 326 |
| `LoadResData(301, 2), the bitmap` | Byte() 0 to 55: 40 0 0 0 2 0 0 0 2 0 0 0 1 0 24 0 0 0 0 0 16 0 0 0 19 11 0 0 19 11 0 0 0 0 0 0 0 0 0 0 0 0 255 0 255 0 0 0 255 0 0 255 255 255 0 0 err 0 |
| `LoadResData(201, 10.4)` | Byte() 0 to 3: 1 2 3 250 err 0 |
| `LoadResPicture(301, vbResBitmap)` | err 0 Picture; .Type 1 .Width 53 .Height 53 .Handle<>0 True |
| `LoadResPicture(999, vbResBitmap)` | err 326 Nothing |
| `LoadResPicture(301, vbResIcon)` | err 326 Nothing |
| `LoadResPicture(301, 5)` | err 485 Nothing |
| `r = LoadResPicture(301, 0), without Set` | a Long, the picture's handle, which differs between builds |
| `LoadResString 101 as a statement` | err 0 |
| `Err.Description after 999` |  |
| `Err.Description for LoadResString(999)` | Resource with identifier '999' not found |
| `no .res, LoadResString(101)` | [unset] String/8 err 326 Resource with identifier '101' not found |
| `no .res, LoadResData(201, 10)` | [unset] String/8 err 326 |

So a string table entry comes back as stored, UTF-16, and an empty entry is "not found", 326, as an absent one is. An ID
or a type given as a String or a fraction is taken as a number. A named type matches without regard to case. A string
block isn't reachable through `LoadResData`, while a bitmap comes back as its stored bytes. `LoadResPicture` with the
wrong kind is 326 and with an unknown kind 485. Without a `.res`, every call is 326. Not measured: icons and cursors, and
one resource held in two languages.
