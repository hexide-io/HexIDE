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

### Icons, cursors, named arguments and languages (2026-11-14, #2626)

**The question.** What the rows above left out: icon and cursor groups, the returned picture's property types, named
arguments, and one string table held in two languages.

**The probe.** VB6 SP6, `/make`, native code and p-code, which logged the same lines. The guest's user locale was en-GB,
its UI language en-US, and its display 96 DPI. A new `.res`, built byte by byte as before, held:
- `RT_ICON` 1, 16×16 at 32 bits, and `RT_ICON` 2, 32×32 at 32 bits;
- `RT_GROUP_ICON` 401 holding both, and `RT_GROUP_ICON` 402 holding the 16×16 alone;
- `RT_CURSOR` 4, 32×32 at 1 bit with its hotspot at (3, 5), in `RT_GROUP_CURSOR` 501;
- bitmap 301 and `RCDATA` 201 as before;
- string block 7 in en-US with 101 `Hello`, block 7 in de-DE with 101 `Hallo` and 105 `Nur105`, and block 8 in de-DE
  alone, with 113 `DE113`.

A second `.res` listed the two block-7 entries in the other order. A picture was read through `Dim p As StdPicture`, and
a byte array is logged as its type, its bounds and its first 14 bytes.

| Probe line | vb6.exe |
|---|---|
| `LoadResPicture(401, vbResIcon)` | Picture, `.Type` 3 Integer, `.Width` 847 Long, `.Height` 847 Long, `.Handle<>0` True |
| `LoadResPicture(402, vbResIcon)`, the 16×16 alone | Picture, `.Type` 3 Integer, `.Width` 847 Long, `.Height` 847 Long |
| `LoadResPicture(501, vbResCursor)` | Picture, `.Type` 3 Integer, `.Width` 847 Long, `.Height` 847 Long |
| `LoadResPicture(301, vbResBitmap)` | Picture, `.Type` 1 Integer, `.Width` 53 Long, `.Height` 53 Long |
| `LoadResPicture(401, vbResBitmap)` | err 326 |
| `LoadResPicture(501, vbResIcon)` | err 326 |
| `LoadResPicture(401, vbResCursor)` | err 326 |
| `LoadResPicture("401", vbResIcon)` | err 326 |
| `LoadResPicture(2, vbResIcon)`, an image's own ID | err 326 |
| `LoadResData(401, 14)` | Byte() 0 to 33: 0 0 1 0 2 0 16 16 0 0 1 0 32 0 |
| `LoadResData(2, 3)` | Byte() 0 to 4263: 40 0 0 0 32 0 0 0 64 0 0 0 1 0 |
| `LoadResData(501, 12)` | Byte() 0 to 19: 0 0 2 0 1 0 32 0 64 0 1 0 1 0 |
| `LoadResData(4, 1)` | Byte() 0 to 307: 3 0 5 0 40 0 0 0 32 0 0 0 64 0 |
| `LoadResString(101)`, in en-US and de-DE | `Hello`, in either order of the two entries |
| `LoadResString(105)`, empty in en-US, `Nur105` in de-DE | err 326, in either order |
| `LoadResString(113)`, in de-DE only | `DE113` |
| `LoadResString(id:=101)` | `Hello` |
| `LoadResData(id:=201, type:=10)`, and `(type:=10, id:=201)` | Byte() 0 to 3 |
| `LoadResPicture(id:=301, restype:=0)`, and `(restype:=0, id:=301)` | Picture |
| `LoadResString(index:=101)`, `LoadResData(index:=201, format:=10)`, `LoadResPicture(index:=301, format:=0)` | compile error: Named argument not found |
| `LoadResData(id:=201)`, `LoadResPicture(restype:=0)` | compile error: Argument not optional |

VB6.OLB's type library names the parameters the same way: `LoadResString(id)`, `LoadResData(id, type)` and
`LoadResPicture(id, restype)`, none of them optional.

**Readings:**
- **An icon or cursor group comes back as a Picture of Type 3, 32 pixels square** (847 HiMetric at 96 DPI), whatever
  sizes the group holds. The group with only a 16×16 image read 847 too. Which image a two-size group draws wasn't read.
- **`.Type` is an Integer, and `.Width` and `.Height` are Longs,** read through `StdPicture`. That settles the return types
  the rows above left open.
- **A String ID is a resource name for `LoadResPicture`.** `"401"` is not found, where `LoadResString("101")` reads 101.
- **Only a group is found through `vbResIcon` and `vbResCursor`.** The image's own ID, a group of the other kind, and an
  icon group read as a bitmap are all 326.
- **`LoadResData` returns groups, images and cursors as stored,** a cursor's hotspot first.
- **The string table's language.** With the user locale en-GB and the UI language en-US, the en-US block won over the
  de-DE one in both orders of the file. Where that block holds the ID's entry empty, the call is 326, though de-DE has
  the entry. An ID whose block exists only in de-DE is read from it. Both of the guest's languages are English, so which
  of the two chose en-US isn't separated here.
- **The parameters are `id`, `type` and `restype`,** in any order, and every one is required. `index` and `format` are
  not names VB6 compiles.

**Not measured:** which image a group with several sizes draws; animated cursors; a guest whose locale and UI language
differ in language; an ID held in two languages, neither of them the guest's.
