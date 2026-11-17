## LSet between two user-defined types (2026-10-28, #2518)

Measured on vb6.exe SP6, every row built native and as p-code, which logged the same lines. The oracle VM is an en-GB machine: `Format(1234.5, "Currency")` is `£1,234.50` and a date reads `28/10/2026`.

**What the rows show:**
- It copies `min(LenB(a), LenB(b))` bytes from the start, and **leaves the rest of the target as it was** (`Big(7, 8, 9)`
  given `OneLong(5)` is `5, 8, 9`; nothing is padded).
- Integers and Longs are little-endian: `TwoInts(1, 2)` read as one Long is 131073.
- **A fixed-length String inside a UDT is held as UTF-16 in memory**, two bytes a character: `Str4("ABCD")` read as a Long
  is `&H420041`, which is "AB". (`??` in the fourth row is two CJK characters the log's code page couldn't print.)

| Copy | Result |
|---|---|
| `LSet OneLong = TwoInts(1, 2)` | `x 131073` |
| `LSet TwoInts = OneLong(&H30004)` | `a 4 b 3` |
| `LSet OneLong = Str4(ABCD)` | `x 4325441 (&H420041)` |
| `LSet Str4 = OneLong(&H44434241)` | `s [??CD]` |
| `LSet OneLong = Big(7, 8, 9)` | `x 7` |
| `LSet Big(7, 8, 9) = OneLong(5)` | `a 5 b 8 c 9` |
| `LSet Str4 = Mixed(65, xyz)` | `s [Axyz] Asc 65` |

**Still to measure before this can be built** (all measured on 2026-11-16, in the next section), hence `needs-oracle`: field alignment (an Integer followed by a Long, a
Byte followed by a Double), Doubles, Currency, Booleans, variable-length Strings and arrays inside a UDT (which hold
pointers in VB6), nested UDTs, and `LSet` between UDTs of different sizes in both directions with those types. The design
question, once measured, is whether HexIDE gives UDTs a byte layout or approximates `LSet` field by field, and which
cases it should refuse rather than copy wrongly.

### Alignment, the other types, and what doesn't compile (2026-11-16, #2518)

**The question.** The rows above left #2518's list unmeasured: field alignment, Doubles, Currency, Booleans, Strings and
arrays inside a UDT, nested UDTs, and sizes in both directions.

Measured on vb6.exe SP6, native and p-code, which agree on every row. Each row copies one UDT over another with `LSet`. Most targets are a type of N bytes, `b(0 To N-1) As Byte`, pre-filled with `&HEE` so that untouched bytes show.

#### Layout: size and alignment

| Type | `Len` / `LenB` |
|---|---|
| Integer, Long | 6 / **8** |
| Byte, Double | 9 / **12** |
| Byte, Integer | 3 / **4** |
| Integer, Long, Integer | 8 / **12** |
| (Integer, Long), Integer, a nested type | 8 / **12** |
| Double; Currency; Date | 8 |
| Single | 4 |
| Boolean | 2 |
| `a(0 To 1) As Long`, a fixed array | 8 |
| `s As String` | 4 / 4: a pointer |
| `v As Variant` | 16 |

| Copied from | Bytes |
|---|---|
| Integer 1, Long 2 | `01 00 00 00 02 00 00 00` |
| Byte 7, Double 1.5 | `07 00 00 00 00 00 00 00 00 00 F8 3F`, then `EE EE EE EE` untouched |
| Byte 7, Integer 258 | `07 00 02 01`, then untouched |
| Integer 1, Long 2, Integer 3 | `01 00 00 00 02 00 00 00 03 00 00 00` |
| the nested type, with the same values | the same 12 bytes |

**The rules:**
- **Each field is aligned to its own size, capped at 4.**
  - A Long sits at a multiple of 4.
  - An Integer sits at a multiple of 2.
  - **A Double sits at a multiple of 4, not 8:** after a Byte it's at offset 4.
- **A type's size rounds up to a multiple of its largest alignment,** so Integer, Long, Integer is 12.
- **A nested type lays out inline**, exactly as its fields would.
- **The padding is copied too:** it came across as `00` over the `EE`.
- **`Len` is the sum of the fields; `LenB` is the laid-out size.** `LSet` copies `min(LenB, LenB)` bytes, as the first rows found.

#### Values, byte for byte

| Copied from | Bytes |
|---|---|
| Double 1.5 | `00 00 00 00 00 00 F8 3F`: IEEE, little-endian |
| Currency 1.5 | `98 3A 00 00 00 00 00 00`: 15000, a 64-bit integer of ten-thousandths |
| Date 2000-01-02 12:00 | `00 00 00 00 F0 D5 E1 40`: the Double 36527.5 |
| Single 1.5 | `00 00 C0 3F` |
| Boolean True; False | `FF FF`; `00 00` |
| `a(0) = 1`, `a(1) = &H1020304` | `01 00 00 00 04 03 02 01`: a fixed array is inline |

| Copied into | Result |
|---|---|
| a Double, from `… F8 3F` | Double 1.5 |
| a Currency, from `98 3A …` | Currency 1.5 |
| a Single, from `00 00 C0 3F` | Single 1.5 |
| Integer and Long, from bytes 1 to 8 | `a &H201`, `b &H8070605`: the Long skips the two padding bytes |
| `a(0 To 1) As Long`, from `01 00 00 00 02 …` | 1, 2 |
| the nested type, from bytes 1 to 12 | `x.a &H201`, `x.b &H8070605`, `y &HA09` |
| a Double 1.5, from just 2 bytes `01 02` | 1.50000000000011: only the low two bytes are replaced |
| Integer, Long, from Integer 1, Long 2, Integer 3 | 1, 2 |
| Integer, Long, Integer, from the nested type | 1, 2, 3 |

#### A Boolean holding something other than 0 or -1

`LSet` can put any 16 bits in a Boolean. VB6 then uses them as they are:

| Bytes | `CStr` | `CInt` | in `If` | `CInt(Not b)` | `b = True` |
|---|---|---|---|---|---|
| `01 00` | True | **1** | true | **-2** | **False** |
| `FF FF` | True | -1 | true | 0 | True |
| `00 00` | False | 0 | false | -1 | False |
| `00 80` | True | **-32768** | true | 32767 | False |

**So a Boolean is a 16-bit integer underneath.** It's true when non-zero, `Not` is bitwise, and `= True` compares with -1.

#### What doesn't compile

`LSet` between UDTs is a **compile error, "Type mismatch"**, when either side holds a pointer:
- a variable-length String in the **source**, and in the **target**;
- a Variant in the source, and in the target;
- a dynamic array, `a() As Long`, in the source;
- an Object in the source.

Each error lands on the `LSet` line itself. Fixed-length Strings and fixed arrays are inline, and copy, as the first rows showed.

**For the design:**
- **Every case that compiles is plain data:** numbers, Booleans, Dates, fixed-length Strings, fixed arrays and nested types of those.
- **VB6 itself refuses the cases with pointers.** So HexIDE can give such UDTs a byte layout by these rules, with no case where copying would mean faking a pointer. A UDT with a pointer gets VB6's error, raised when the statement runs, rather than a wrong copy.
- **A Boolean that isn't 0 or -1 needs keeping,** or `CInt` and `= True` come out wrong after a copy.
