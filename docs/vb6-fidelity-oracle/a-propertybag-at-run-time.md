## A PropertyBag at run time (2026-11-18, #4680, SP6)

**The rig:**
- **Build and run:** vb6.exe 6.00.9782 with MSVBVM60 6.00.9848, `/make` native and p-code in session 0 of the oracle guest, an en-GB Windows 10.
- **The answers:** both builds gave **identical output**, the `Contents` bytes included.
- **The program:** a Standard EXE starting at `Sub Main`, with no form and no UserControl. Every statement ran under `On Error Resume Next`, logged with its error or `ok`.
- **Reading back:** a read used `Set` when `IsObject(pb.ReadProperty(name))`, and a Let otherwise. Each value read back is shown as `TypeName` and value.

### Making one, and writing to it

| Probe | VB6 |
|---|---|
| `Set pb = New PropertyBag` | ok; `TypeName(pb)` is `PropertyBag` |
| `b = pb.Contents` on the new bag (`b() As Byte`) | ok: `0 To -1`, no bytes |
| `pb.WriteProperty "I", 5` | ok |
| the same for `100000`, `"hi"`, `1.5`, `CSng(2.5)`, `CCur(3.25)`, `#1/2/2003 4:05:06 AM#`, `True`, `CByte(7)`, `CDec(1.1)`, `Empty`, `Null` | ok, each |
| `pb.WriteProperty "Arr", Array(1, 2)` | **328** "Illegal parameter.  Can't write arrays" (two spaces, as VB6 writes it) |
| `pb.WriteProperty "Bytes", bb`, `bb(0 To 2) As Byte` | ok: a Byte array is not "an array" here |
| `pb.WriteProperty "Coll", New Collection` | **330** "Illegal parameter.  Can't write object because it does not support persistence." |
| `pb.WriteProperty "Font", New StdFont` | ok |
| `pb.WriteProperty "None", Nothing` | ok |
| `pb.WriteProperty "Def", 7, 7`, equal to its default | ok, **and nothing is stored** (see Read) |
| `pb.WriteProperty "Def2", 8, 7` | ok |
| `pb.WriteProperty "Dup", 1` then `pb.WriteProperty "Dup", 2` | ok |
| `pb.WriteProperty "", 1` | **5** Invalid procedure call or argument |

### Reading it back

| `pb.ReadProperty(name)` | VB6 |
|---|---|
| `I`, `L`, `S`, `Dbl`, `Sng`, `Cur` | Integer 5, Long 100000, String hi, Double 1.5, Single 2.5, Currency 3.25 |
| `Dt`, `Bool`, `Byte`, `Dec` | Date 02/01/2003 04:05:06 (en-GB), Boolean True, Byte 7, Decimal 1.1 |
| `Emp`, `Nul` | Empty, Null |
| `Bytes` | `Byte()`, `0 To 2` |
| `Font` | an object, `TypeName` **Font** |
| `None` | Nothing |
| `Arr`, `Coll` (their writes failed) | **327** "Data value named 'Arr' not found", and the same for `Coll` |
| `Def` (written equal to its default) | **327** |
| `Def2` | Integer 8 |
| `Dup` | Integer **2**, the second write |
| `""` | **5** |
| `i`, `s` | Integer 5, String hi: **names are not case-sensitive** |
| `Missing` | **327** "Data value named 'Missing' not found" |
| `ReadProperty("Missing", 99)` | Integer 99, the default |
| `ReadProperty("Def", 7)` | Integer 7 |
| `ReadProperty("I", 99)` | Integer 5: a stored value wins over the default |

### `Contents`

| Probe | VB6 |
|---|---|
| `b = pb.Contents` after the writes above | `0 To 479`, 480 bytes |
| `v = pb.Contents`, a Variant | `TypeName` `Byte()` |
| `p2.Contents = b` on a new bag, then reads | `S` hi, `Dt` the same date, `Font` an object `Font`, `Bytes` `Byte()` `0 To 2` |
| `p2.Contents` read back again | the same 480 bytes as `pb.Contents` |
| `p2.Contents = bb`, three bytes that are not a bag | **5**, and **`p2` keeps what it had**: `S` still reads hi |
| `p2.Contents = "text"` | **13** |
| `p2.Contents = 5` | **13** |

**The bytes.** Each dump is hex, in rows of 32 bytes.
- **A bag with `A = 1` only:**
  `93B200001800000002000100610000000000000061000100`
- **A bag with `A = 1` then `B = "x"`:**
  `93B200002C000000020001006100000010000000610001000800010062000000F0FFFFFF6200010000007800`
- **The full bag above,** 480 bytes:

```text
93B20000E001000002000100690000001000000069000500030001006C000000
140000006C00A086010000000800010073000000180000007300020000006800
69000000050003006D400C901C000000640062006C00000000000000F83F0000
0400030066C00DCC1800000073006E0067000000204000000600030073A00E8C
1C000000630075007200F47E00000000000000000700020074800C0018000000
6400740026BF5872E55EE2400B0004006DD10DBC1800000062006F006F006C00
FFFF00001100040064B10EE4180000006200790074006500070000000E000300
62A00C90240000006400650063000E000100000000000B000000000000000000
0000030071A00D941400000065006D0070000000010003006DA00EB814000000
6E0075006C00000011200500F29C2CD624000000620079007400650073000100
03000000000000000102FF000900040075F30DBC3000000066006F006E007400
0352E30B918FCE119DE300AA004BB85101010000000080380100000009000400
64F70DBC240000006E006F006E00650000000000000000000000000000000000
0200040033F20C94180000006400650066003200080000000200030071A00E90
1400000064007500700001000200030071A00E903CFEFFFF6400750070000200
```

**What can be read off the bytes, and what can't.** This is read off the three dumps above, not measured as a format.
- **The header:** every bag starts with `93B20000`, then its total length as a 32-bit little-endian count.
  - The one-value bag is `18000000`, 24 bytes.
  - The full bag is `E0010000`, 480 bytes.
- **Each entry, in the order written, has these fields:**
  - **Two bytes that match the value's variant type** in every entry read: `0200` for the Integer `i`, `0300` for the Long `l`, `0800` for the String `s`, `0500` for the Double `dbl`.
  - **Two bytes giving the name's length in characters.**
  - **A 32-bit hash of the name.** In all 19 entries in these dumps it's this: start from 0, and for each UTF-16 character
    of the lower-cased name, rotate the 32 bits left by 13 and XOR in the character.
    - **What it gives:** a one-letter name its own character code (`61000000` for `a`), `dt` `000C8074`, `dbl` `900C406D`, `bytes` `D62C9CF2`.
    - **Untested:** it was fitted to these 19 names, not tested against a name chosen to break it. A name outside
      ASCII, or one long enough to wrap the rotation many times over, is the case to measure before relying on it.
  - **A 32-bit distance to the next entry.** It's the entry's own length rounded up to 4 bytes: 16 for `i`, 20 for `l`.
  - **The name in UTF-16, lower-cased:** `i`, `l`, `s`, `dbl`, `dup`.
  - **The value:**
    - a Byte in 1 byte;
    - an Integer or a Boolean (`FFFF` for True) in 2;
    - a Long or a Single in 4;
    - a Double, a Date or a Currency in 8;
    - a Decimal in 16;
    - Empty and Null in none;
    - a String as a 32-bit character count and its UTF-16 characters.
  - **Two values are read off the dump and not decoded further:**
    - **A Byte array,** variant type `2011`, takes 14 bytes for three elements.
    - **An object,** type `0009`, starts with its class's CLSID: `{0BE35203-8F91-11CE-9DE3-00AA004BB851}`, StdFont's, for the font. Nothing is sixteen zero bytes.
  - **Entry lengths:** these sizes give each entry's length, rounded up to 4, in all 18 entries.
- **The last entry's distance points back to the first:**
  - 0 in the one-value bag;
  - −16 (`F0FFFFFF`) in the two-value bag;
  - −452 (`3CFEFFFF`) in the full one.
- **`def` is absent, and `dup` appears twice,** with 1 and then 2. A read returns the second.
- **Not decoded:** the Byte array's and the font's own fields, and how a read chooses between the two `dup` entries. No rule is offered for them: the bytes are recorded for a format test to match.

**Not measured:**
- a PropertyBag passed to a UserControl's `ReadProperties` and `WriteProperties` (MISSING_LANGUAGE's rows have their own measurements);
- a long String or one outside ASCII;
- a Variant holding an object that is persistable but not a `StdFont`;

### A StdFont in a bag (2026-11-18, #4741)

**The probe.**
- **Build:** VB6 SP6, `/make`, native code and p-code, which logged every row identically.
- **Each row:**
  - a `New StdFont`, with the properties the row names set on it and then all of them read;
  - `pb.WriteProperty "F", f` into a new bag, and a dump of `pb.Contents`;
  - the bytes copied into a second bag, then `Set g = p2.ReadProperty("F")`.
- **A tenth case,** a 40-character Name, ended both runs at a time-out before its first line. It isn't counted.

| The font, as its properties read before the write | `Contents` |
|---|---|
| a fresh `New StdFont`: MS Sans Serif, 8.25, weight 400, charset 0 | `93B20000 40000000` `0900 0100 66000000 00000000 6600` `0352E30B918FCE119DE300AA004BB851` `01 0000 00 9001 44420100 0D 4D532053616E73205365726966` `0000` |
| Arial, Size set to 10, which reads 9.75 | … `01 0000 00 9001 DC7C0100 05 417269616C` `0000` |
| Arial 9.75, Bold True, which reads weight 700 | … `01 0000 00 BC02 DC7C0100 05 417269616C` `0000` |
| Arial 9.75, Italic | … `01 0000 02 9001 DC7C0100 05 …` |
| Arial 9.75, Underline | … `01 0000 04 9001 DC7C0100 05 …` |
| Arial 9.75, Strikethrough | … `01 0000 08 9001 DC7C0100 05 …` |
| Arial, Weight set to 600, which reads 700 and Bold True | … `01 0000 00 BC02 DC7C0100 05 …`, the same as Bold |
| Arial, Charset set to 2, after which Name reads **Wingdings** | … `01 0200 00 9001 DC7C0100 09 57696E6764696E6773` `0000` |
| Courier New 9.75 | … `01 0000 00 9001 DC7C0100 0B 436F7572696572204E6577`, no padding |
| Tahoma 10.5, Bold and Italic | … `01 0000 02 BC02 289A0100 06 5461686F6D61` `00` |

**The entry**, read off these dumps:
- **The usual header for an object:** variant type `0009`, the name's length, its hash, and the distance to the next entry. That distance is `00000000` here, since the only entry points back to itself.
- **The name in UTF-16,** then StdFont's CLSID, `{0BE35203-8F91-11CE-9DE3-00AA004BB851}`.
- **The font's stream, in order:**
  - **a version byte,** `01`;
  - **the charset**, 16 bits little-endian;
  - **a flag byte:** `02` Italic, `04` Underline, `08` Strikethrough. **Bold sets no flag here**: it shows as weight 700;
  - **the weight**, 16 bits: `9001` 400, `BC02` 700;
  - **the size in ten-thousandths of a point**, 32 bits: `44420100` 82,500 for 8.25, `DC7C0100` 97,500 for 9.75, `289A0100` 105,000 for 10.5;
  - **the name:** a length byte, then the name in ANSI.
- **Padding:** zeros to the next multiple of 4, which the entry's length counts.

**Reading it back:**
- **`Set g = p2.ReadProperty("F")`** gives a new Font (`g Is f` False) whose Name, Size, Bold, Italic, Underline, Strikethrough, Weight and Charset all read as the written font's did, in every row.
- **`v = p2.ReadProperty("F")` without `Set`** gives a String, the font's Name: its default member.

**A font read back and written again (measured 2026-11-20, #4741's review).** vb6.exe SP6, `/make`, native code and
p-code logging the same bytes in all six rows. Each row made a `New StdFont`, did what the row names, wrote it to bag
`p`, copied `p.Contents` into a second bag, read `Set g = p2.ReadProperty("F")` back and wrote `g` to a third bag.

| The font before its first write | `g` before its write | `g`'s stream (version, charset, flags, weight, size, name) |
|---|---|---|
| untouched | unread | `01 0100 00 0000 80380100 00`, as loaded |
| Size set to 10 | unread | `01 0100 00 0000 A0860100 00`, as loaded |
| Name read | unread | `01 0000 00 9001 44420100 0D` "MS Sans Serif", as loaded |
| Bold set to True | unread | `01 0100 00 BC02 80380100 00`, as loaded |
| Size set to 10 | its Name read | `01 0000 00 9001 DC7C0100 0D` "MS Sans Serif": realized, 9.75 |

- **A font read back writes the stream it was loaded from** until one of its properties is read. Then it writes its
  realized values, as a `New StdFont` does.
- **A bag keeps what was written:** an untouched font written to `p`, then read (`x = f.Name`), leaves `p.Contents`
  as the untouched stream. The font is saved when it's written, not when `Contents` is read.

**Two fonts that look the same are not:** the font in this page's first dump above was written without any of its properties being read. Its stream is `01 0100 00 0000 80380100 00`: charset 1, weight 0, size 8.0, and an empty name. A `New StdFont` whose properties have been read writes MS Sans Serif, 8.25, weight 400, charset 0. **No rule I would defend** for when a font takes its realized values. Reading them is enough to make it, and these rows don't separate the properties.

**Not measured:**
- a font whose stream sets flag `01`;
- a name outside ASCII;
- a long name (the 40-character row didn't finish);
- a `Picture`, or any other persistable object.

### When a StdFont takes its realized values (2026-11-20, #4741)

vb6.exe SP6, `/make`, native code and p-code logging the same bytes in all 14 rows. **The probe:** each row made a
`New StdFont`, did what the row names to it, wrote it with `pb.WriteProperty "F", f` to a new bag, dumped
`pb.Contents`, copied the bytes into a second bag, and read `Set g = p2.ReadProperty("F")` back. This settles the
section above's "no rule I would defend": **the rule is read versus not read.** Writing realizes nothing.

**What a font writes depends on whether any of its properties has been read.** A `New StdFont` holds what it was asked for. **Reading any one property (Name, Bold, Size or Charset each measured alone) makes every field take its realized value**, and from then on the font writes those. **Writing doesn't do that:** a font written twice untouched writes the same bytes twice. Its stream, by what was done to it before the write:

| Before `WriteProperty` | The stream (version, charset, flags, weight, size, name) |
|---|---|
| nothing | `01 0100 00 0000 80380100 00`: charset 1, weight 0, size 8.0, no name |
| read Name, Bold, Size or Charset (each alone), or read Name after an untouched write | `01 0000 00 9001 44420100 0D` "MS Sans Serif" |
| set Name = "Arial" only | `01 0100 00 0000 80380100 05` "Arial" |
| set Size = 10 only | `01 0100 00 0000 A0860100 00`: 10.0, not 9.75 |
| set Bold = True only, or Weight = 700 only | `01 0100 00 BC02 80380100 00` |
| set Italic = True only | `01 0100 02 0000 80380100 00` |
| set Charset = 2 only | `01 0200 00 0000 80380100 00` |
| set Name = "Arial", then read Size | `01 0000 00 9001 44420100 05` "Arial" |

On the 11-18 page, every row set some properties and then read all of them, so each wrote realized values: Arial with Size 10 writes 9.75 (`DC7C0100`), Bold writes weight 700, and Charset 2 writes "Wingdings".

**Reading it back.**
- `Set g = p2.ReadProperty("F")`, from a bag whose `Contents` were copied, gives a new Font, so `g Is f` is False. Its properties read as realized from what was stored.

| Stored | `g`'s Name / Size / Weight / Charset / Bold |
|---|---|
| untouched; any property read; or Italic only | MS Sans Serif / 8.25 / 400 / 0 / False |
| Name "Arial" only | Arial / 8.25 / 400 / 0 / False |
| Size 10 only | MS Sans Serif / 9.75 / 400 / 0 / False |
| Bold only, or Weight 700 only | **Arial** / 8.25 / 700 / 0 / True |
| Charset 2 only | **Wingdings** / 8.25 / 400 / 2 / False |
| each 11-18 row | the written font's Name, Size, Bold, Italic, Underline, Strikethrough, Weight and Charset |

- `v = p2.ReadProperty("F")` without `Set` gives a String, the font's Name: its default member.

**What resisted explanation:** realization comes from the system's font mapping. An empty name with weight 700 realizes
as **Arial**, while an empty name alone or with Italic realizes as MS Sans Serif. No rule is offered for which name a
request maps to beyond these rows.

### Errors, defaults, argument counts, names, `Contents` and unpaired UTF-16 (2026-11-20, #4741)

**The probe.** vb6.exe SP6, `/make`, native code and p-code, on the en-GB guest. They agreed on every row except the
`Contents` ones in section 5. Each case ran under `On Error Resume Next` in a Standard EXE with `Sub Main`. A read used
`Set` when `IsObject(pb.ReadProperty(name))`, and a Let otherwise. There were 76 rows in all, 3 of them compile-time,
each built as a project of its own.

**What was guessed, and overturned.** The first reading of the default rows was "skip when the value equals the
default". Twenty rows chosen to tell that apart from a conversion settle it: `5` with a default of `5.4` isn't
written, and `"True"` with a default of `True` is.

**1. An Error value is stored.** `pb.WriteProperty "a", CVErr(5)` works. `ReadProperty("a")` gives `TypeName` `Error`, and `CStr` gives "Error 5". The one-entry bag is `93B20000 1C000000` then `0A00 0100 61000000 00000000 6100 0500 0A80 0000`: variant type 10, then the SCODE `&H800A0005` as 32 bits.

**2. The default comparison.** `WriteProperty name, value, default` skips the write when the default, converted to the value's type, equals the value.
- The conversion is OLE Automation's own, with no flags, not VB's: a Boolean becomes "-1" or "0", not "True". That's reasoned from the `"True"`/`True` row below, which shows only that the result isn't "True".
- If the conversion fails, the value is written.
- Strings compare exactly, case included.
- Numbers convert with VB's rounding, half to even.

| Value | Default | Written? | | Value | Default | Written? |
|---|---|---|---|---|---|---|
| `5` | `"5"` | no | | `5` | `5.4` | **no** (5.4 → 5) |
| `"5"` | `5` | no | | `5` | `4.6` | no |
| `5` | `5#` | no | | `5` | `5.5` | **yes** (5.5 → 6) |
| `5` | `CCur(5)` | no | | `4` | `4.5` | **no** (4.5 → 4) |
| `5` | `CLng(5)` | no | | `"5.0"` | `5` | **yes** ("5" ≠ "5.0") |
| `Empty` | `Empty` | no | | `"5"` | `5.0` | no |
| `Empty` | `0` | no | | `1` | `"1.4"` | no |
| `0` | `Empty` | no | | `True` | `1` | no |
| `""` | `Empty` | no | | `True` | `"True"` | no |
| `Empty` | `""` | no | | `"True"` | `True` | **yes** |
| `Null` | `Null` | no | | `"abc"` | `5` | yes |
| `Null` | `0` | **no** | | `5` | `"abc"` | yes |
| `0` | `Null` | **yes** | | `CCur(1.23456)` | `1.2346` | no |
| `True` | `-1` | no | | `1.2346` | `CCur(1.23456)` | no |
| `True` | `True` | no | | `CSng(0.1)` | `0.1` | no |
| `False` | `0` | no | | `0.1` | `CSng(0.1)` | **yes** (the Single widens) |
| `"abc"` | `"ABC"` | **yes** | | `#1/1/2000#` | `"1/1/2000"` | no (en-GB) |
| `#1/1/2000#` | `36526` | no | | `Null` | `"x"` | no |
| `CByte(5)` | `5` | no | | `Empty` | `Null` | **yes** |
| `"x"` | `Null` | yes | | `CByte(5)` | `261` | **yes** (overflow) |
| `5` | a Byte array | yes | | | | |

**Objects:** `Nothing` against a default of `Nothing` is not written. A `New StdFont` against `Nothing` is written, and so is `Nothing` against a font.

**3. Argument counts and unknown members.**
- **Through `As Object`, at run time:**
  - `o.WriteProperty "a"`, `o.WriteProperty "a", 1, 1, 1`, `o.ReadProperty()` and `o.ReadProperty("a", 1, 2)` all raise **450**;
  - `v = o.Contents(0)` raises **451**;
  - `o.Foo` and `v = o.Foo` raise **438**.
- **Through `As PropertyBag` (early-bound), vb6.exe refuses to compile:**
  - `pb.WriteProperty "a"` is **"Argument not optional"**;
  - `pb.ReadProperty("a", 1, 2)` is **"Wrong number of arguments or invalid property assignment"**;
  - `pb.Foo` is **"Method or data member not found"**.

**4. A name that isn't a String** converts as `CStr` does: VB's own conversion, so `True` is "True". The stored name is lower-cased as usual.

| Name | Stored as | Read back by the same value, and by `"5"` |
|---|---|---|
| `5` | "5" | Integer 1; `"5"` finds it too |
| `1.5` | "1.5" | Integer 1 |
| `True` | "true" (`7400720075006500`) | Integer 1 |
| `CByte(7)` | "7" | Integer 1 |
| `#1/2/2003#` | "02/01/2003" (en-GB short date) | Integer 1 |
| `CCur(2)` | "2" | Integer 1 |
| `Null` | **94** on `WriteProperty`; **13** on `ReadProperty` | |
| `Empty` | **5** on both, as an empty name | |

`ReadProperty(5)` after `WriteProperty "5", 9` gives Integer 9.

**5. `Contents` assigned anything other than a one-dimensional Byte array.** The p-code answer is **13**, and the bag keeps what it had (`"s"` still reads "hi"). Rows: `Array(1, 2)`, `Empty`, `Null`, `True`, `#1/1/2000#`, an Integer array, a 2-D Byte array. **Native code differs:** it ignored all seven silently.
- A Byte array of a valid bag declared `1 To n` works.
- So does a Variant holding the Byte array.

**6. Unpaired UTF-16 is stored verbatim.**
- **A name with a lone high surrogate,** `"a" & ChrW(&HD800)`, is stored, and reads back by the same name. The entry is `0200 0200 00F80C00 00000000 6100 00D8 0100 0000`. Its hash, `000CF800`, follows the page's rotate-13 rule over the code units.
- **A value with a lone low surrogate,** `"x" & ChrW(&HDC00)`, is stored as `0800 0100 61000000 00000000 6100 02000000 7800 00DC 0000`. Copied into a new bag and read back, it has `Len` 2 and last `AscW` -9216.

**Not measured:** the UserControl's own bag.
