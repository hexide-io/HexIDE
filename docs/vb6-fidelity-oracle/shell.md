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

### Every window style, and documents (2026-11-16, #4146)

**How it was read.** A made EXE ran one `Shell` call from its command line, in the oracle VM's console session, so the
launched window was real. A script then read that window's state through Win32, by the process ID `Shell` returned,
and stopped the process. The program was `notepad.exe` unless a row says otherwise.

**Window styles:**

| WindowStyle | Result | The window | It took the foreground |
|---|---|---|---|
| 0 | the ID | hidden | no |
| 1 | the ID | normal | yes |
| 2 | the ID | minimized | yes |
| 3 | the ID | maximized | yes |
| 4 | the ID | normal | no |
| **5** | the ID | normal | **yes** |
| 6 | the ID | minimized | no |
| 7 | the ID | minimized | no |
| **8** | the ID | normal | **no** |
| **9** | the ID | normal | **yes** |
| 10, **11**, **12**, -1, **100** | error 5 | | |
| omitted | the ID | **minimized** | yes, as 2 |
| `"1"`, a String | the ID | normal | yes, as 1 |
| 1.6 | the ID | minimized | yes, as 2 (converted as CInt) |

- **The valid styles are 0 to 9,** Windows' own show states from SW_HIDE to SW_RESTORE. Anything else raises 5.
- **An omitted style is 2,** vbMinimizedFocus. That's what #2563 left unmeasured.

**Launch failures, all with style 1:**

| PathName | Result |
|---|---|
| `C:\nope\nothere.exe`, and a bare `nothere.exe` | 53, "File not found" |
| `C:\Windows`, a folder | 5 |
| a file with an extension nothing is associated with | 5 |
| a `.txt` file, which Notepad is associated with | **5**: Shell doesn't open documents |
| `""` | 5 |
| `notepad`, with no `.exe` | the ID: it's found |
| `notepad.exe C:\…\x.txt` | the ID, with the file opened |
| a program path containing a space, quoted or not | the ID |
| `cmd.exe /c exit 3` | the ID; the exit code isn't Shell's business |

The folder, empty-path and missing-program rows repeat #2522's above and agree with them.

**Not measured:**
- a non-String PathName, such as a number (`VB6BuiltIns.Shell.cs` refuses it, citing #2522);
- a program that exists but can't start, such as a 16-bit or wrong-architecture one;
- access denied.

### A non-String PathName, access denied, and programs that can't start (2026-11-18, #4406)

**The rig:**
- **Build and run:** vb6.exe 6.00.9782 with MSVBVM60 6.00.9848, `/make` native and p-code in session 0 of the oracle guest. Native code and p-code gave identical output in every row.
- **Each call:** `id = Shell(v, vbHide)` under `On Error Resume Next`, logging the error or the result's type.
- **The files** were put in the program's own folder before it ran:
  - copies of `attrib.exe` named `123.exe`, `True.exe` and `5.exe`;
  - a copy named `deny.exe`, whose ACL denies read and execute to Everyone;
  - `bad.exe`, five bytes of text;
  - `mz.exe`, `MZ` followed by 510 zero bytes;
  - `doc.txt` (`.txt` is associated with Notepad there) and `x.doc` (nothing is associated with `.doc` there), both text;
  - `noext`, text with no extension;
  - `bin.dat`, the bytes 0 to 255;
  - `run.bat`, which is `@exit 0`.
- **Paths:** each file was named by its full path, except the conversion rows.

**A non-String PathName goes through CStr first:**

| Call | Result |
|---|---|
| `Shell(123)`, an Integer | **the ID**: it ran `123.exe` from the program's folder. **Without that file: 53** |
| `Shell(CCur(5))` | **the ID**: `5.exe` |
| `Shell(True)` | **the ID**: `True.exe`, so True became `"True"` |
| `Shell(1.5)` | 53: there's no file of that name |
| `Shell(#1/1/2000#)` | 53 |
| `Shell(Empty)` | **5**, as `Shell("")` |
| `Shell(Null)` | 94 |
| `Shell(Array(1))` | **13** |
| `Shell(o)`, `o` an Object holding Nothing | **91** |

- **The 53 rows** came from a first run, in which the program's folder held no file of those names.
- **The `123.exe` row is the discriminating one:** a number that's converted and searched for finds the file, and one that's refused can't.

**Launch failures:**

| PathName (full path) | VB6 | `CreateProcess`'s Win32 error for the same file |
|---|---|---|
| a missing `nosuch.exe` | 53 | 2 |
| `deny.exe`, execute denied | **5** | 5 |
| `bad.exe`, text named `.exe` | **5** | 216 |
| `mz.exe`, an MZ header and zeros | **5** | 216 |
| `doc.txt`, an associated document | 5 | 193 |
| `x.doc`, with no association | 5 | 193 |
| `noext`, no extension | **5** | 193 |
| `bin.dat`, binary | **5** | 193 |
| the program's own folder | 5 | |
| `run.bat` | **the ID**: a batch file runs | none: it started |

- **How the Win32 column was read:** in the same guest, from .NET's `Process.Start` with `UseShellExecute = false`, which calls `CreateProcess`. That was a 64-bit caller, where VB6's runtime is 32-bit.
- **What the rows support:** 53 for "not found", and **5 for every other failure measured**. An access denial, a bad image and a document all raise 5, with or without an association, text or binary.
- **What they don't:** failures with other Win32 errors weren't produced, such as elevation required (740) or a missing DLL in a program that does start.

**Not measured:**
- a Byte, Long, Single or Decimal PathName, which these rows suggest go through CStr too;
- an Object holding an object with a default member, and an Error value;
- a 16-bit program, and one that needs elevation.
