## A UDT field passed ByRef (2026-10-22, #1451)

**The question.** HexIDE copied a UDT field given to a ByRef parameter and never wrote the copy back, so `PL u.l`, with
`PL(x As Long)` storing `x = "9"`, left `u.l` at 7. Does VB6 pass the field itself, or copy it in and out? The two differ
while the procedure runs, and when it fails after storing.

**The probe.** A Sub Main, under `On Error Resume Next`, passes fields of `Private Type T` (`l As Long`, `s As String`,
`v As Variant`, and a nested Type with `y As Long`) to Subs that store into their parameter, and logs each field after the
call. One Sub stores into its parameter and then reads the module-level variable the field belongs to. Made native and
p-code under en-GB; every line was identical.

| Call | The procedure | The field after |
|---|---|---|
| `PL u.l`, `u.l` 7 | `PL(x As Long)`: `x = "9"` | Long 9 |
| `PS u.s`, `u.s` "a" | `PS(x As String)`: `x = x & "!"` | String "a!" |
| `PV u.l` | `PV(x As Variant)`: `x = 12` | Long 12 |
| `PVS u.l` | `PVS(x As Variant)`: `x = "hello"` | 13, and `u.l` keeps 7 |
| `PVS u.v` | the same, given a Variant field | String "hello" |
| `PU u.l` | `PU(x)`, untyped: reads `x`, then `x = 2.5` | it reads Long 7; then Long 2 |
| `PL u.inner.y` | `PL`, given a nested Type's field | Long 9 |
| `PE u.l` | `PE(x As Long)`: `x = 5`, then `Err.Raise 5` | Long 5, and 5 |
| `PA g.l`, `g` a module's `T` | `PA(x As Long)`: `x = 3`, then reads `g.l` | it reads 3 inside; Long 3 |
| `PBV u.l` | `PBV(ByVal x As Long)`: `x = 99` | Long 7 |
| `PL (u.l)` | `PL`, the argument in parentheses | Long 7 |
| `PL2 u.l` | `PL2(y As Long)`: `PL y` | Long 9 |
| `PI u.l` | `PI(x As Integer)` | compile error, "ByRef argument type mismatch" |

**What it shows:**
- **VB6 passes the field itself.** A store through the parameter is in the field at once: the procedure reads it back
  through the module's variable while it is still running. The store survives an error raised after it.
- **A store through the parameter converts into the field's type,** whatever the parameter's: a Variant parameter given a
  Long field stores 12 as a Long and 2.5 as 2, and refuses "hello" with 13.
- **ByVal, or the argument in parentheses, passes a copy.** A parameter of another type is refused at compile time, as it
  is for a variable.
- **The parameter reads as the field does.** A second probe, under the same conditions, passed fields holding 5 to Subs
  that evaluate `x * 7000` under `On Error Resume Next`:

  | Argument | `PI(x As Integer)` | `PV(x As Variant)` | `PU(x)` |
  |---|---|---|---|
  | `u.i`, `i As Integer` | 6 | 35000 | 35000 |
  | `u.v`, `v As Variant` | refused | 35000 | 35000 |
  | `n`, `Dim n As Integer` | 6 | 35000 | not probed |

  So a typed parameter given a typed field is typed, as it is given a variable, and a Variant parameter reads a Variant's
  either way (#1001).

The same probe, for an array element, native and p-code:

| Call | The element after |
|---|---|
| `PL a(1)`, `a` a Long array, `a(1)` 7 | Long 9 |
| `PV w(1)`, `w` a Variant array | String "v" |
| `PV a(1)`, the Long array | 13, and `a(1)` keeps 7 |
| `PV v(1)`, `v` a Variant holding `Array(1, 2, 3)` | String "v" |

So an array element is passed by reference too, and converts into the array's element type the same way.

### In HexIDE (#1451)

A field argument carries the Type instance holding it and its name. A ByRef parameter of the field's type, or an untyped
or Variant one, gets a slot that reads and writes the field itself, linked for as long as the call runs, so every store
converts through the field's own `TrySet`. The slot is declared as the field is, typed or a Variant, which is what makes
`x * 7000` overflow in `PI u.i`. Every field row above is pinned in `ByRefFieldTests`. `PI u.l` is copied rather
than refused, as a variable of another type is. Not done here: a field reached through `With`, an index or an array of a
Type (#178), and the array-element rows, which #1455 passes by reference (*An array element passed ByRef*). `PL2 u.l` is 9, the field passed on through a second ByRef
parameter.

