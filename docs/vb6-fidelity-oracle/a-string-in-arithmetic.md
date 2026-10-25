## A String in arithmetic (2026-10-14, #1047, #770)

What does VB6 do with a String operand to `+ - * / \ Mod ^` and unary minus? Before this, HexIDE raised Type
mismatch for every one, so `Text1.Text * 2` stopped the run. These runs were made:
- 76 cells, run as native code, as p-code and in the IDE (`/runexit`), under the guest's en-GB locale and again under
  de-DE, the locale switched with `Set-Culture` and restored afterwards;
- then 135 cells of result type, native and p-code.

**Native code, p-code and the IDE agree on every cell, in both locales.**

**A numeric String is read as a number, by CDbl's own reader.** Each of 22 edge strings was run as `s * 1` and as
`CDbl(s)`. The two agreed every time:

| String | en-GB | de-DE |
|---|---|---|
| `"1e3"`, `"1D3"` | 1000 | 1000 |
| `" 6 "`, `"+6"` | 6 | 6 |
| `"-6"`, `"6-"`, `"(6)"` | -6 | -6 |
| `"&H10"`, `"&O10"`, `"&HFFFF"` | 16, 8, 65535 | the same |
| `"1,000"` | 1000 | **1** |
| `"6,5"` | **65** | 6.5 |
| `"6.5"` | 6.5 | **65** |
| `"6.5e1"` | 65 | **650** |
| `"abc"`, `""`, `"  "`, `"True"`, `"0x10"`, `"$6"` | Err 13 | Err 13 |
| `"1e400"` | Err 6 | Err 6 |

The locale's thousands separator is skipped wherever it falls, and its decimal separator is the point. So under de-DE
`"1.5" * 2` is 30. `"£6"` was meant to be measured and wasn't: the harness read the probe as UTF-8 where it had been
written as Latin-1, so the cell ran as `"?6"`.

**The String takes a Double's place in the result table.** Against each partner, both ways round:

| partner | `+`, `-` | `*` | `/` | `\`, `Mod` | `^` |
|---|---|---|---|---|---|
| Byte, Integer, Long, Single, Double, Boolean | Double | Double | Double | Long | Double |
| Currency | **Currency** | Double | Double | Long | Double |
| Decimal | Decimal | Decimal | **Decimal** | Long | Double |
| Date | **Date** | Double | Double | Long | Double |

That is the table a Double operand gets, Currency's dominance under `+` and `-` included. `i = "6" * 2` stores Integer 12.

**`+` concatenates a String beside a String or beside Empty, and adds otherwise:**
- `"6" + "2"` is the String `"62"`, and so is the same pair held in two Variants;
- `"6" + Empty` and `Empty + "6"` are the String `"6"`;
- `"6" + 2`, `2 + "6"`, a Variant 6 + `"2"`, and a Variant `"6"` + a Variant 2 are all Double 8;
- `"6" + True` is Double 5, and `"6" + Null` is Null.

`"6" - Empty` is Double 6, and `"6" * Empty` is Double 0.

**Resisted explanation: a Boolean divided by a String.** `True \ "6"` is the Boolean **True**, and `True Mod "6"` is the
Boolean **False**, where `"6" \ True` is the Long -6 and `True / "6"` the Double -0.166666666666667. With a Boolean
on the left, `\` and `Mod` behave as if the String were read as a Boolean: -1 \ -1 is 1, and -1 Mod -1 is 0.
That fits both cells, but it is a fit to two cells, not a rule that has been tested.

### HexIDE

Since #1047, every arithmetic operator reads a String operand through `ToNum`, the reader `CDbl`, a store into a number
and a property assignment already share. It reads it as a Double, and the existing result table does the rest. `ToNum`
itself now reads:
- the locale's thousands separator, which also fixes #770, where `Label1.Left = "1,5"` raised 13 and VB6 stores 15;
- a trailing sign;
- parentheses;
- a `D` exponent.

`+` gives back the String when the other operand is Empty. `True \ "6"` and `True Mod "6"` are refused as not
implemented, because read as a Double they would be a Long 0, a wrong value. Pinned in `StringOperandArithmeticTests`,
each cell under a stated culture. The `"0" / "0"` cell left out of #234 is pinned now as well: Err 6.

What it still does not do:
- **`"6" * CCur(2)` and `"6" / CDec(2)`** come out as Currency and Double, where VB6 gives Double and Decimal. HexIDE gets
  the same two pairs wrong for any Double operand, which is #230.
- **A String compared to a number** is #211.

