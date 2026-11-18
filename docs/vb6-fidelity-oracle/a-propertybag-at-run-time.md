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
