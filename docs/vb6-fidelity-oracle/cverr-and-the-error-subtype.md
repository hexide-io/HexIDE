## CVErr and the Error subtype (2026-10-28, #2520)

Measured on vb6.exe SP6, every row built native and as p-code, which logged the same lines. The oracle VM is an en-GB machine: `Format(1234.5, "Currency")` is `£1,234.50` and a date reads `28/10/2026`.

**What the rows say:**
- The argument is converted as CLng converts (1.5 and 2.5 both give 2: to even), and must lie in 0 to 65535: 65536, -1
  and True are 5. A numeric String is taken, `"abc"` is 13, Null is 94, and Empty is `Error 0`.
- `CStr` gives `Error 5`. `CLng`, `CInt` and `CDbl` give the number.
- **Arithmetic, `&`, `Len`, `Format` and a comparison with a number are all 13**, and so is assigning one to a Long or a
  String variable. Two Error values compare by number.
- `IsError` is True only for an Error value. `IsNumeric`, `IsEmpty` and `IsNull` are False.

| Expression | Result | Type | Err |
|---|---|---|---|
| `CVErr(5)` | Error Error 5 | Error | 0 |
| `CVErr(0)` | Error Error 0 | Error | 0 |
| `CVErr(2042)` | Error Error 2042 | Error | 0 |
| `CVErr(65535)` | Error Error 65535 | Error | 0 |
| `CVErr(65536)` |  |  | 5 |
| `CVErr(-1)` |  |  | 5 |
| `CVErr(1.5)` | Error Error 2 | Error | 0 |
| `CVErr(2.5)` | Error Error 2 | Error | 0 |
| `CVErr("7")` | Error Error 7 | Error | 0 |
| `CVErr("abc")` |  |  | 13 |
| `CVErr(Null)` |  |  | 94 |
| `CVErr(Empty)` | Error Error 0 | Error | 0 |
| `CVErr(True)` |  |  | 5 |
| `CVErr(CVErr(3))` | Error Error 3 | Error | 0 |
| `IsError(e)` | `True` | Boolean | 0 |
| `VarType(e)` | `10` | Long | 0 |
| `TypeName(e)` | `Error` | String | 0 |
| `CStr(e)` | `Error 5` | String | 0 |
| `CLng(e)` | `5` | Long | 0 |
| `CInt(e)` | `5` | Integer | 0 |
| `CDbl(e)` | `5` | Double | 0 |
| `e + 1` |  |  | 13 |
| `e & "x"` |  |  | 13 |
| `e = 5` |  |  | 13 |
| `e = CVErr(5)` | `True` | Boolean | 0 |
| `e = CVErr(6)` | `False` | Boolean | 0 |
| `Len(e)` |  |  | 13 |
| `IsNumeric(e)` | `False` | Boolean | 0 |
| `IsEmpty(e)` | `False` | Boolean | 0 |
| `IsNull(e)` | `False` | Boolean | 0 |
| `Format(e)` |  |  | 13 |
| `n = e` | `0` | | 13 |
| `s = e` | `[]` | | 13 |
| `IsError(5)` | `False` | Boolean | 0 |
| `IsError(Empty)` | `False` | Boolean | 0 |

### Printing, ordering and branching on an Error value (2026-10-28, #2520)

**The question.** The table above leaves out an Error value printed to a file, two Error values ordered, an Error value as
a `Select Case` selector or an `If` condition, the unary operators and math functions, and the conversions other than
`CLng`, `CInt` and `CDbl`. Found reviewing #2604, which guessed some of them. `e` is `CVErr(5)` in a Variant, every line
runs under `On Error Resume Next`, and each value is logged through the probe's `Show`. The file is read back with
`Line Input` and logged between brackets.

| Expression, with `e = CVErr(5)` | Result | Type | Err |
|---|---|---|---|
| `e < CVErr(6)` | True | Boolean | 0 |
| `e > CVErr(4)` | True | Boolean | 0 |
| `e <> CVErr(6)` | True | Boolean | 0 |
| `e <= CVErr(5)` | True | Boolean | 0 |
| `Not e` |  |  | 13 |
| `-e` |  |  | 13 |
| `Int(e)` |  |  | 13 |
| `Fix(e)` |  |  | 13 |
| `Abs(e)` |  |  | 13 |
| `Sgn(e)` |  |  | 13 |
| `Val(e)` |  |  | 13 |
| `Str(e)` |  |  | 13 |
| `Hex(e)` |  |  | 13 |
| `CBool(e)` | True | Boolean | 0 |
| `CByte(e)` | 5 | Byte | 0 |
| `CSng(e)` | 5 | Single | 0 |
| `CCur(e)` | 5 | Currency | 0 |
| `CDec(e)` |  |  | 13 |
| `CDate(e)` |  |  | 13 |
| `CVar(e)` | Error 5 | Error | 0 |
| `IIf(True, e, 1)` | Error 5 | Error | 0 |
| `e = Null` |  |  | 13 |
| `e = Empty` |  |  | 13 |
| `e Is Nothing` |  |  | 424 |

| Statement | The line it wrote, between brackets | Err |
|---|---|---|
| `Print #f, e` | `[Error 5]` | 0 |
| `Print #f, "a"; e; "b"` | `[aError 5b]` | 0 |
| `Print #f, "a", e, "b"` | `[a             Error 5       b]` | 0 |
| `Write #f, e` | `[#ERROR 5#]` | 0 |
| `Write #f, "a", e, "b"` | `["a",#ERROR 5#,"b"]` | 0 |

| Statement | What ran, under `On Error Resume Next` |
|---|---|
| `Select Case e` with `Case 5`, then `Case Else` | took Case 5, err 13 |
| The same, after `End Select` | err 13 |
| `Select Case e` with `Case CVErr(5)`, then `Case Else` | took Case CVErr(5), err 0 |
| `Select Case e` with `Case Is > CVErr(4)`, then `Case Else` | took Case Is > CVErr(4), err 0 |
| `If e Then` ... `Else` | took Then, err 13 |

So two Error values compare by their number under every comparison operator, and a Case test against one does too; a
Case test against a number is the same 13 as `e = 5`, and under `Resume Next` the Case's body runs. `Print #` writes
`Error 5`, and `Write #` writes `#ERROR 5#`. The conversions to a numeric type other than Decimal take the number, `CBool`
takes it as non-zero, and `CDec` and `CDate` are 13. The unary operators and the math functions are 13, as arithmetic is.
