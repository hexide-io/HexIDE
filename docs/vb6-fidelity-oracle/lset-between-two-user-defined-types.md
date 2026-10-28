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

**Still to measure before this can be built**, hence `needs-oracle`: field alignment (an Integer followed by a Long, a
Byte followed by a Double), Doubles, Currency, Booleans, variable-length Strings and arrays inside a UDT (which hold
pointers in VB6), nested UDTs, and `LSet` between UDTs of different sizes in both directions with those types. The design
question, once measured, is whether HexIDE gives UDTs a byte layout or approximates `LSet` field by field, and which
cases it should refuse rather than copy wrongly.
