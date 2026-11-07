## Shell (2026-10-28, #2522)

Measured on vb6.exe SP6, every row built native and as p-code, which logged the same lines. The oracle VM is an en-GB machine: `Format(1234.5, "Currency")` is `£1,234.50` and a date reads `28/10/2026`.

**What the rows say:**
- The result is a Double, the process id, greater than 0.
- `PathName` is the program and its arguments, split as Windows splits a command line; a quoted program path works. A
  program not found is 53. `""` is 5, and so is a folder. Null is 94.
- `WindowStyle` 0 to 4 and 6 are VB6's vbHide to vbMinimizedNoFocus; 7 was taken too. 10 and -1 are 5. It is converted as
  CInt converts (2.6 is 3), and Null is 94.

| Expression | Result | Type | Err |
|---|---|---|---|
| `Shell("cmd /c exit", vbHide) > 0` | `True` | Boolean | 0 |
| `TypeName(Shell("cmd /c exit", vbHide))` | `Double` | String | 0 |
| `VarType(Shell("cmd /c exit", 0))` | `5` | Long | 0 |
| `Shell("nosuchprogram_x.exe")` |  |  | 53 |
| `Shell("")` |  |  | 5 |
| `Shell(Null)` |  |  | 94 |
| `Shell("cmd /c exit", 7) > 0` | `True` | Boolean | 0 |
| `Shell("cmd /c exit", 10)` |  |  | 5 |
| `Shell("cmd /c exit", -1)` |  |  | 5 |
| `Shell("cmd /c exit", 2.6) > 0` | `True` | Boolean | 0 |
| `Shell("cmd /c exit", Null)` |  |  | 94 |
| `Shell("""cmd"" /c exit", vbHide) > 0 (quoted path)` | `True` | Boolean | 0 |
| `Shell(App.Path) (a folder)` |  |  | 5 |
| `Shell "cmd /c echo x> shelled.txt", vbHide (statement)` | `ok` | | 0 |
