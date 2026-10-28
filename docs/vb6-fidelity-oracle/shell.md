# Shell

Provenance: hexide-io/HexIDE#2522's supplied measurements, vb6.exe SP6 on an en-GB oracle VM,
2026-10-28. Native and p-code builds logged identical results.

| Probe | Value / error | Type |
|---|---|---|
| `Shell("cmd /c exit", vbHide) > 0` | True | Boolean |
| `TypeName(Shell("cmd /c exit", vbHide))` | Double | String |
| `VarType(Shell("cmd /c exit", 0))` | 5 | Long |
| `Shell("nosuchprogram_x.exe")` | error 53 | |
| `Shell("")` | error 5 | |
| `Shell(Null)` | error 94 | |
| `Shell("cmd /c exit", 7) > 0` | True | Boolean |
| `Shell("cmd /c exit", 10)` | error 5 | |
| `Shell("cmd /c exit", -1)` | error 5 | |
| `Shell("cmd /c exit", 2.6) > 0` | True (style converts to 3 through CInt) | Boolean |
| `Shell("cmd /c exit", Null)` | error 94 | |
| `Shell("""cmd"" /c exit", vbHide) > 0` | True | Boolean |
| `Shell(App.Path)` with a directory | error 5 | |
| `Shell "cmd /c echo x> shelled.txt", vbHide` | file created; no error | |

The brief also records acceptance of styles 0–4 and 6, Windows command-line splitting, a quoted
program path, and a Double process ID greater than zero. It requires Windows show-state mapping;
Unix hosts have no corresponding Windows show state.

Not measured here: other style values, other OS launch failures, non-string PathName conversions, and the show state
when WindowStyle is omitted.
