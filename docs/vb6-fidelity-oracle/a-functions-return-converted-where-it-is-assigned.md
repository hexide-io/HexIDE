## A Function's return, converted where it is assigned (2026-10-22, #1444)

**The question.** HexIDE converted a Function's value into its declared return type only for the numeric types, and only as
the Function returned. So `FS = 5`, in a Function `As String`, returned an Integer, and `FD = "0.5"`, in one `As Date`,
returned a String. Where does VB6 convert, and for which types?

**The probes.** Each Function takes a Variant, stores it into its own name under `On Error Resume Next`, and logs
`Err.Number` before it returns. The caller, also under `On Error Resume Next`, logs `TypeName`, `CStr` and `Err.Number` of
what came back. A second project puts a Property Get and a Function in a class. Made native and p-code under en-GB; every
line was identical.

| Store, in the procedure | Inside, `Err.Number` | The caller reads |
|---|---|---|
| `FD = "0.5"`, a Function `As Date` | 0 | Date 00:05:00 (0.00347…) |
| `FD = "1/2/2003"` / `"36526"` / `2.5` | 0 | Date 01/02/2003 / 01/01/2000 / 01/01/1900 12:00:00 |
| `FD = "abc"` | 13 | Date 00:00:00, and `Err.Number` still 13 |
| `FS = 5` / `True` / `2.5` / `#1/2/2003#`, `As String` | 0 | String "5" / "True" / "2.5" / "02/01/2003" |
| `FB = 5` / `0` / `"True"`, `As Boolean` | 0 | Boolean True / False / True |
| `FB = "abc"` | 13 | Boolean False, and 13 |
| `FI = 40000`, `As Integer` | 6 | Integer 0, and 6 |
| `FI = 2.5` / `"12"` | 0 | Integer 2 / 12 |
| `FC = 1.23456`, `As Currency` | 0 | Currency 1.2346 |
| `FN = "abc"`, `As Date`, with no handler in `FN` | — | the caller's 13; its assignment is skipped |
| `FV = "0.5"`, a Function with no As clause | — | String "0.5" |
| A standard module's `Property Get PD() As Date`, `PD = "1/2/2003"` | — | Date 01/02/2003 |
| A class's `Property Get PD() As Date`, `PD = "0.5"` / `PS() As String`, `PS = 2.5` | — | Date 00:05:00 / String "2.5" |
| A class's `Property Get PI() As Integer`, `PI = 40000` | 6 | Integer 0, and 6 |
| A class's `Function FB(v) As Boolean`, given 5 / `"abc"` | 0 / 13 | Boolean True / False, and 13 |

**What it shows:**
- **A store into a typed Function's or Property Get's name converts as a store into a variable of that type does,** and
  at the store. So the error is raised inside the procedure, where its own handler traps it, and the procedure then
  returns its type's zero.
- **The caller still sees `Err.Number` 13 after the procedure returned normally,** having trapped the error itself. An End
  Function does not clear it.
- **A String into a Date return is read as `CDate` reads it** (*A String read as a Date*).

### In HexIDE (#1444)

A Function's return slot is allocated with its declared scalar type, as a `Dim`'s is, so a store into the name goes
through the same conversion as a store into a variable. Every row above is pinned in `TypedReturnTests` except the
standard module's Property Get, which HexIDE does not reach by name (#977). An Enum or UDT return is unchanged.

