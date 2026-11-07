## Chr, Asc, ChrW and AscW (2026-10-23, #1134)

Measured on vb6.exe, native and p-code identical, on a machine whose ANSI code page is 1252, under en-GB. Each program
`CharacterCodeTests` pins was made as written.

**Chr and Asc go through the ANSI code page; ChrW and AscW do not.** HexIDE had Chr and Asc working in UTF-16 units,
which agrees with VB6 only below &H80 and, by coincidence, from &HA0 to &HFF.

| probe | result |
|---|---|
| `AscW(Chr(&H80))` | 8364: "€" |
| `AscW(Chr(&H81))`, `&H8D`, `&H8F`, `&H90`, `&H9D` | 129, 141, 143, 144, 157: a byte 1252 leaves undefined is its own code point |
| `AscW(Chr(&H9F))` | 376: "Ÿ" |
| `AscW(Chr(&HA0))`, `AscW(Chr(255))` | 160, 255 |
| `Chr(256)`, `Chr(-1)`, `Chr(32767)`, `Chr(True)` | 5 each |
| `Chr(3000000000#)` | 6 |
| `Chr(Null)` | 94 |
| `Chr("x")` | 13; `Chr("66")` is "B" |
| `AscW(Chr(65.5))`, `AscW(Chr(66.5))` | 66, 66: half to even |
| `Len(Chr(0))`, `Len(Chr(Empty))` | 1, 1 |
| `Asc(ChrW(8364))` | 128 |
| `Asc(ChrW(&H81))`, `Asc(ChrW(&H178))`, `Asc(ChrW(&H2019))` | 129, 159, 146 |
| `Asc(ChrW(&H100))` | **65**: Ā best-fits to "A" |
| `Asc(ChrW(&H416))` | 63: Ж has no fit, so "?" |
| `Asc("AB")`, `Asc(65)`, `Asc(True)` | 65, 54, 84: the argument is made text first |
| `Asc("")`, `Asc(Empty)` | 5, 5 |
| `Asc(Null)` | 94 |
| `AscW(ChrW(&H20AC))` | 8364 |
| `AscW(ChrW(&HF050))`, `AscW(ChrW(61520))` | -4016, -4016: `&HF050` is an Integer, -4016, taken as its unsigned unit |
| `ChrW(-4016) = ChrW(61520)` | True |
| `AscW(ChrW(65535))`, `AscW(ChrW(-32768))`, `AscW(ChrW(True))` | -1, -32768, -1 |
| `ChrW(65536)`, `ChrW(-32769)` | 5, 5 |
| `ChrW(3000000000#)` | 6 |
| `ChrW(Null)`, `AscW(Null)` | 94, 94 |
| `AscW(ChrW(65.5))` | 66 |
| `Len(ChrW(Empty))`, `Len(ChrW(&HD800))`, `Len(ChrW(0))` | 1, 1, 1: a lone surrogate is one unit |
| `AscW("AB")`, `AscW(66)`, `AscW(1.5)` | 65, 54, 49 |
| `AscW("")`, `AscW(Empty)` | 5, 5 |
| `ChrW(CharCode:=65)`, `AscW(String:="A")` | "A", 65 |
| `ChrW(Code:=65)`, `AscW(Str:="A")` | do not compile: "Named argument not found" |
| `ChrW()`, `AscW()` | do not compile: "Argument not optional" (HexIDE: #1512) |
| `AscW$("A")` | does not compile: "Type-declaration character does not match declared data type" |
| `TypeName(AscW("A"))`, `TypeName(Asc("A"))`, `TypeName(ChrW(65))` | Integer, Integer, String |

**Typed or Variant**, by #987's probe:
- `AscW("A")` against `Case Is < 65.4` takes Case Else, and `AscW("A") * 7000` is 6: typed, as `Asc` is.
- `AscW(v)`, with `v` a Variant holding "A", behaves the same way.
- `ChrW(57)` against `Case Is > 10` takes Case Else: a Variant, as `Chr` is.

**Every `$` form that answers text answers a typed String.** "9" from each of them, against `Case Is > 10`, takes the
Case: `Chr$(57)`, `ChrW$(57)`, `Format$(9)`, `Hex$(9)`, `LCase$("9")`, `Left$("9", 1)`, `LTrim$("9")`, `Mid$("9", 1)`,
`Oct$(9)`, `Right$("9", 1)`, `RTrim$("9")`, `String$(1, "9")`, `Trim$("9")` and `UCase$("9")`. `Str$(19)`, which is
" 19", takes Case Else, which is the typed reading the other way round. Their plain forms are Variants: `Mid("9", 1)`,
`Left("9", 1)`, `Format(9)` and `Hex(9)` take Case Else, and **`Str(19)` takes the Case**, read as 19.

**Print # best-fits too.** `Print #` of Ā, Ж and ’ to a file opened For Output writes the bytes 65, 63 and 146, read back
with `Get`. So the earlier row "`ChrW$(&H416)` is written as '?'" is true, but Ж has no fit, so it could not tell a
best fit from a plain replacement. HexIDE wrote Ā as "?" until #1511.

**Not measured here:**
- **A double-byte ANSI code page** (932). VB6 is documented to give `Chr` a range of -32768 to 65535 there, a code above
  255 being a lead and a trail byte, and `Asc` a possibly negative pair. Unmeasured, so HexIDE refuses a code beyond
  ASCII under one (#1509).
- **ChrB and the rest of the byte-string family.** A first `ChrB` probe did not compile. HexIDE's strings cannot hold a
  one-byte string at all (#1510). Measured since: [The byte-string functions](the-byte-string-functions.md).

### In HexIDE (#1134)

`VB6BuiltIns.ChrInCodePage` decodes a byte with the ANSI code page's table, and `AscInCodePage` encodes with that
table's own fallbacks, best fit among them. Both take the code page from `Vb6TextEncoding.Ansi`, so a Debug build's
`--ansi-code-page` reaches them, and both refuse beyond ASCII when the table is double-byte. `CharCode` and
`FirstCharacterOf` hold the argument rules the four share.

`ExpressionExecutor`'s typed list gains `AscW`, its Variant list `ChrW` and `Str`, and its list of `$` forms that do not
compile gains `AscW`. `DollarResult` now marks its String typed.

