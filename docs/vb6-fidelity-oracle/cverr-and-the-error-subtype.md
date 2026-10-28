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
