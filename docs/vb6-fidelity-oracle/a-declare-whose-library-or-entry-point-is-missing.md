## A Declare whose library or entry point is missing (2026-11-18, #4820, SP6)

**The probe.** A Sub Main module declared three procedures, and a Function called each under `On Error Resume Next`. Each
Function read `Err.Number` and `Err.Description`, then cleared `Err` before returning. The project was built native and
p-code, and the two agreed on every row.

| Declare | Calling it |
|---|---|
| `Private Declare Function NoSuchLib Lib "hexide-nosuch.dll" () As Long` | **53**, "File not found: hexide-nosuch.dll" |
| `Private Declare Sub NoSuchBare Lib "hexide-nosuch" ()`, with no extension | **53**, "File not found: hexide-nosuch" |
| `Private Declare Sub NoSuchEntry Lib "kernel32" ()`: the library loads, the entry point isn't in it | **453**, "Can't find DLL entry point NoSuchEntry in kernel32" |

- **Nothing is reported until the call.** The project compiles, and each error is raised at the call, where `On Error` traps it.
- **The library is named as the `Lib` string wrote it,** with no `.dll` added.
- **The entry-point message names both the procedure and the library.**

**And in Excel for Mac.** In Excel for Mac 16.115 (Build 1108), arm64, macOS 27.2.1, measured the same day, there is no
kernel32. `Private Declare PtrSafe Sub Sleep Lib "kernel32" (ByVal dwMilliseconds As Long)` compiles, and `Sleep 1` raises
**53**, "File not found: kernel32", which `On Error` traps. VBA on a Mac reaches that call: see
[what `#If` reads in Excel for Mac](conditional-compilation.md#off-windows-what-if-reads-in-excel-for-mac-2026-11-18-4821).

**Measured by accident.** `Err` stays set after a procedure that resumed past an error returns. The first run of this probe
didn't clear it, and the caller read 53 after the Function had returned its string.

**Not measured:** an `Alias`, and whether the arguments are evaluated before the error.

**In HexIDE (#4820):** HexIDE skips every `Declare` (`PrePass.VisitDeclareStmt`), so a call raises "Sub or Function not
defined", and no `On Error` traps it. #4820 makes such a call off Windows raise 53, as VBA on a Mac does.
