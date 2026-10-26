## A typed parameter given a value of another type (2026-10-13, #874)

**The question.** What a parameter declared `As Boolean`, `As String` or `As Date` holds when the caller passes a
value of another type. Found in #865's review: HexIDE's `BindParameters` coerces a copied argument only into the
numeric types (`IsCoreNumeric`: Byte, Integer, Long, Single, Double, Currency), so a Boolean, String or Date parameter
held whatever it was given. That copy branch takes every `ByVal` argument and every `ByRef` argument passed as a
literal, so both are affected.

**Probe.** A Sub Main project with `On Error Resume Next`. Each procedure writes `TypeName(x)` and `x` with `Print #`.
The caller clears `Err` before each call and reads `Err.Number` after it, so a 13 is the caller's own reading, and a
call that raised it shows no line from the procedure, which is how "at the call" is known.
Built as native code and also run in the IDE, as described under "What a condition is"; **every line was identical in
the two modes**. The guest's locale is en-GB, which decides how a Date is written and how `"1/2/2000"` is read.

| Call | Parameter | VB6 |
|---|---|---|
| `B 5`, `B 0` | `ByVal x As Boolean` | Boolean True, Boolean False |
| `B "True"` | `ByVal x As Boolean` | Boolean True |
| `B "abc"` | `ByVal x As Boolean` | 13, at the call: the procedure does not run |
| `S 5`, `S True`, `S 2.5` | `ByVal x As String` | String "5", "True", "2.5" |
| `D "1/2/2000"` | `ByVal x As Date` | Date 01/02/2000 |
| `D 1`, `D 2.5`, `D True` | `ByVal x As Date` | Date 31/12/1899, 01/01/1900 12:00:00, 29/12/1899 |
| `D "abc"` | `ByVal x As Date` | 13, at the call |
| `BR 5`, `BR 0`, `BR "True"` | `x As Boolean`, ByRef | Boolean True, False, True |
| `BR "abc"` | `x As Boolean`, ByRef | 13, at the call |
| `SR 5`, `SR True`, `SR 2.5` | `x As String`, ByRef | String "5", "True", "2.5" |
| `DR "1/2/2000"`, `DR 1` | `x As Date`, ByRef | Date 01/02/2000, 31/12/1899 |
| `DR "abc"` | `x As Date`, ByRef | 13, at the call |

**What it says.** A literal passed to a typed parameter, ByVal or ByRef, is converted into the parameter's type as a
store into a variable of that type converts it, and a value that will not convert raises 13 before the procedure
runs. HexIDE passes the ByVal Boolean and String cases through unconverted (run under HexIDE: `S 5` arrives as an
Integer, and `B "abc"` runs with a String where VB6 raises 13), and its code sends the Date and ByRef cases down the
same branch. #874 is the fix.

**HexIDE, since #874.** A copied argument and an Optional default convert into the parameter's declared type through
the conversion a store into a variable uses, and the parameter's slot is typed, so a store into it converts too. Every
row above is pinned in `TypedParameterTests`, with a second probe, native and p-code identical under en-GB:

| Call or store | VB6 |
|---|---|
| `NI "12"` / `NI "2.5"` / `NI "abc"` / `NI "40000"`, `ByVal x As Integer` | Integer 12 / 2 / 13 at the call / 6 at the call |
| `NIR "12"`, `x As Integer`, ByRef | Integer 12 |
| `ND "1.5"` / `ND "&H10"`, `ByVal x As Double` | Double 1.5 / 16 |
| `Optional x As String = 5` / `As Date = "1/2/2003"` / `As Boolean = 1` / `As Integer = 2.5`, omitted | String "5" / Date 01/02/2003 / Boolean True / Integer 2 |
| `x = 5` in a `ByVal x As String` parameter / `x = "0.5"` in `ByVal x As Date` | String "5" / Date 00:05:00 |
| `x = "abc"` in a `ByVal x As Boolean` parameter, under Resume Next | 13, and `x` keeps True |

A numeric parameter given a String was read as 0: `Narrow` takes a String's number as 0, and only `CoerceOnStore`
parses it. So `NI "12"` was 0 here, a wrong value #874's own table did not reach.

