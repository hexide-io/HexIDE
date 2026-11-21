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

### Chr and Asc under code page 932 (2026-11-21, #1509, SP6)

**The question.** The section above was measured on a code page 1252 machine. VB6's documentation gives `Chr` a range of -32768 to 65535 under a double-byte code page, with a code above 255 read as a lead and a trail byte, and has `Asc` return such a pair as one Integer. HexIDE refuses both beyond ASCII under a double-byte code page until they're measured.

**The probe.** Each row was its own function under `On Error Resume Next`, with results logged as `AscW` codes in hex, so nothing depended on the log's code page. It ran three times on a clone of the oracle VM, which was restored afterwards:
- **as it stands:** system locale en-US (ANSI code page 1252), user locale en-GB;
- **after `Set-WinSystemLocale ja-JP` and a restart:** code page 932 (`GetACP`), user locale still en-GB;
- **then with `Set-Culture ja-JP` too:** user locale 1041.

Built with `/make` on vb6.exe SP6 as native code and as p-code, the two logged the same lines in every run.

| Expression | Code page 1252, user en-GB | 932, user en-GB | 932, user ja-JP |
|---|---|---|---|
| `Chr(&H8140&)` | **5** | **5** | U+3000 |
| `Chr(-32448)`, the same pair as an Integer | **5** | **5** | U+3000 |
| `Chr(&H81)`, a lead byte alone | U+0081 | U+30FB | U+30FB |
| `Chr(&H82A0&)` | **5** | **5** | U+3042 |
| `Chr(&H889F&)` | **5** | **5** | U+4E9C |
| `Chr(&HB1)`, a half-width katakana byte | U+00B1 | U+FF71 | U+FF71 |
| `Chr(&HA0)` | U+00A0 | U+F8F0 | U+F8F0 |
| `Chr(&H80)` | U+20AC | U+0080 | U+0080 |
| `Chr(&HFF)` | U+00FF | U+F8F3 | U+F8F3 |
| `Chr(256)` | **5** | **5** | U+0001 |
| `Chr(&H8100&)`, a lead byte and 0 | **5** | **5** | U+30FB |
| `Chr(&H80FF&)`, no valid pair | **5** | **5** | U+0080 |
| `Chr(&H7FFF)` | **5** | **5** | U+007F |
| `Chr(&HFFFF&)` | **5** | **5** | U+F8F3 |
| `Chr(65536)` | **5** | **5** | **5** |
| `Chr(-1)` | **5** | **5** | U+F8F3 |
| `Chr(-32768)` | **5** | **5** | U+0080 |
| `Chr(-32769)` | **5** | **5** | **5** |
| `Chr$(&H8140&)` | **5** | **5** | U+3000 |
| `ChrB(&H81)` | 1 byte, `Len` 0 | 1 byte, `Len` 0 | 1 byte, `Len` 0 |
| `Asc(ChrW(&H3042))`, あ | `63` | `-32096` | `-32096` |
| `Asc(ChrW(&HFF71))`, half-width ｱ | `63` | `177` | `177` |
| `Asc(ChrW(&H3000))`, the ideographic space | `32` | `-32448` | `-32448` |
| `Asc(ChrW(&H4E00))`, 一 | `63` | `-30486` | `-30486` |
| `Asc(ChrW(&HE9))`, é | `233` | `101` | `101` |
| `Asc(ChrW(&H20AC))`, € | `128` | `63` | `63` |
| `Asc(ChrW(&HA5))`, ¥ | `165` | `92` | `92` |
| `Asc("\")` | `92` | `92` | `92` |
| `Asc("A")` | `65` | `65` | `65` |
| `Asc("")` | **5** | **5** | **5** |
| `Asc(ChrW(&H3042) & "A")` | `63` | `-32096` | `-32096` |
| `Asc(ChrW(&HD800))`, a lone surrogate | `63` | `63` | `63` |
| `AscB(ChrW(&H3042))` | `66` | `66` | `66` |
| `TypeName(Asc(ChrW(&H3042)))` | `Integer` | `Integer` | `Integer` |
| `Hex(AscW(Chr(Asc(ChrW(&H3042)))))` | `3F` | **5** | `3042` |
| `StrConv(ChrW(&H3042), vbFromUnicode)` | 1 byte, `Len` 0 | 1 byte, `Len` 0 | U+A082 |
| `LenB(StrConv(ChrW(&H3042) & "A", vbFromUnicode))` | `2` | `2` | `3` |

`U+` is the character the result holds. A bold number is the error raised.

**What it shows:**
- **`Chr`'s double-byte range needs both settings.** With code page 932 and a Japanese user locale, `Chr` takes -32768 to 65535 and reads a code above 255 as a lead byte and a trail byte. The pair `&H8140` is U+3000, and `&H82A0` is あ. With code page 932 but an en-GB user locale, `Chr` still raised 5 above 255, as on code page 1252.
- **A code that isn't a valid pair** gives what code page 932 decodes from its first byte:
  - `&H8100` and a lone lead byte give `・`, U+30FB;
  - `&H80FF` gives U+0080, from its first byte;
  - `&H7FFF` gives U+007F;
  - `256`, bytes 1 and 0, gives U+0001.
- **Codes past the documented range raise 5:** 65536 and -32769.
- **A negative code is the same pair as its unsigned twin:** `-1` and `&HFFFF` gave U+F8F3, and `-32768` gave U+0080.
- **`Asc` used code page 932 under both user locales.** It returns a double-byte character as one Integer, negative from `&H8000` up (あ is -32096, `&H82A0`). A character code page 932 has no bytes for gives 63, `?`, or a best fit: é gives 101, `e`, and ¥ gives 92, `\`. A half-width katakana gives its one byte, 177.
- **`StrConv(..., vbFromUnicode)` followed the user locale,** not code page 932. Under en-GB it gave あ as one byte, `?`. Under ja-JP it gave あ's two bytes, `&H82 &HA0`.

**Overturned:** the documentation's double-byte `Chr` assumes a double-byte system, and doesn't say that the user locale matters as well. A program on a Japanese code page with a Western user locale gets the single-byte `Chr` and the double-byte `Asc`.

**Not measured:**
- code pages 936, 949 and 950;
- a Japanese user locale on code page 1252;
- `ChrB` and `AscB` of double-byte strings beyond the rows above;
- what HexIDE's `--ansi-code-page 932` should take as the user locale.
