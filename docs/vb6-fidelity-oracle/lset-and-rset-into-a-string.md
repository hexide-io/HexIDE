## LSet and RSet into a String (2026-10-28, #2517)

Measured on vb6.exe SP6, every row built native and as p-code, which logged the same lines. The oracle VM is an en-GB machine: `Format(1234.5, "Currency")` is `£1,234.50` and a date reads `28/10/2026`.

**The rule the rows give:**
- The target keeps its current length, `Len` of what it holds. `LSet` puts the value at the left and pads with spaces;
  `RSet` puts it at the right.
- **A value longer than the target is cut to its leftmost characters by both** (`RSet` too: `abc` given `123456` is `123`).
- The value is converted to a String first (12, 1.5 and True read `12`, `1.5` and `True`). Empty is `""`. **Null is 94,
  and the target is unchanged.**
- A Variant target's length is that of its String form: a Variant holding 12345 has length 5, and afterwards it holds
  a String.

| Expression | Result | Type | Err |
|---|---|---|---|
| `LSet s, abcde = "xy"` | `[xy   ] Len 5` | | 0 |
| `LSet s, abc = "123456"` | `[123] Len 3` | | 0 |
| `LSet s, abc = ""` | `[   ] Len 3` | | 0 |
| `LSet s, "" = "xy"` | `[] Len 0` | | 0 |
| `LSet s, abcde = 12` | `[12   ] Len 5` | | 0 |
| `LSet s, abcde = 1.5` | `[1.5  ] Len 5` | | 0 |
| `LSet s, abcde = True` | `[True ] Len 5` | | 0 |
| `LSet s, abcde = Null` | `[abcde] Len 5` | | 94 |
| `LSet s, abcde = Empty` | `[     ] Len 5` | | 0 |
| `LSet s, abcde = "xyz" & "w"` | `[xyzw ] Len 5` | | 0 |
| `LSet f5 (String * 5), = "ab"` | `[ab   ] Len 5` | | 0 |
| `LSet f5 (String * 5), = "1234567"` | `[12345] Len 5` | | 0 |
| `LSet f5 (String * 5), = ""` | `[     ] Len 5` | | 0 |
| `LSet v (Variant holding "abcde") = "x"` | `[x    ] String Len 5` | | 0 |
| `LSet v (Variant holding 12345) = "x"` | `[x    ] String Len 5` | | 0 |
| `RSet s, abcde = "xy"` | `[   xy] Len 5` | | 0 |
| `RSet s, abc = "123456"` | `[123] Len 3` | | 0 |
| `RSet s, abc = ""` | `[   ] Len 3` | | 0 |
| `RSet s, "" = "xy"` | `[] Len 0` | | 0 |
| `RSet s, abcde = 12` | `[   12] Len 5` | | 0 |
| `RSet s, abcde = 1.5` | `[  1.5] Len 5` | | 0 |
| `RSet s, abcde = True` | `[ True] Len 5` | | 0 |
| `RSet s, abcde = Null` | `[abcde] Len 5` | | 94 |
| `RSet s, abcde = Empty` | `[     ] Len 5` | | 0 |
| `RSet s, abcde = "xyz" & "w"` | `[ xyzw] Len 5` | | 0 |
| `RSet f5 (String * 5), = "ab"` | `[   ab] Len 5` | | 0 |
| `RSet f5 (String * 5), = "1234567"` | `[12345] Len 5` | | 0 |
| `RSet f5 (String * 5), = ""` | `[     ] Len 5` | | 0 |
| `RSet v (Variant holding "abcde") = "x"` | `[    x] String Len 5` | | 0 |
| `RSet v (Variant holding 12345) = "x"` | `[    x] String Len 5` | | 0 |
