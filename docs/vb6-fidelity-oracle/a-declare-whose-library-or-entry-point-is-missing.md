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
- **The entry-point message names the entry point and the library.** Here that's the procedure's own name. With an
  `Alias` it's the Alias: see below.

**And in Excel for Mac.** In Excel for Mac 16.115 (Build 1108), arm64, macOS 27.2.1, measured the same day, there is no
kernel32. `Private Declare PtrSafe Sub Sleep Lib "kernel32" (ByVal dwMilliseconds As Long)` compiles, and `Sleep 1` raises
**53**, "File not found: kernel32", which `On Error` traps. VBA on a Mac reaches that call: see
[what `#If` reads in Excel for Mac](conditional-compilation.md#off-windows-what-if-reads-in-excel-for-mac-2026-11-18-4821).

**Measured by accident.** `Err` stays set after a procedure that resumed past an error returns. The first run of this probe
didn't clear it, and the caller read 53 after the Function had returned its string.

**An `Alias`, and a named argument (2026-11-19, #4820's review, SP6).** A second probe of the same shape, built native
and p-code, which again agreed on every row. `Side` is a Function that adds one to a module-level counter and returns it.
The counter was set to 0 before the call.

| Declare | The call | Result |
|---|---|---|
| `Private Declare Function MissAlias Lib "hexide-nosuch.dll" Alias "Whatever" () As Long` | `x = MissAlias()` | **53**, "File not found: hexide-nosuch.dll" |
| `Private Declare Function MissNamed Lib "hexide-nosuch.dll" (ByVal n As Long) As Long` | `x = MissNamed(n:=Side())` | **53**, "File not found: hexide-nosuch.dll", and the counter read 1 |
| `Private Declare Function EntryAlias Lib "kernel32" Alias "NoSuchEntry" () As Long` | `x = EntryAlias()` | **453**, "Can't find DLL entry point NoSuchEntry in kernel32" |

- **An `Alias` changes nothing while the library is missing.** The call raises the same 53 as one without it.
- **A Declare takes a named argument, by its parameter's name, and evaluates it once.** The counter read 1 after the
  trapped error.
- **The 453 names the entry point, not the procedure.** The procedure here is `EntryAlias`, and the message names its
  Alias, `NoSuchEntry`. The first probe couldn't tell the two apart, since its procedure was its own entry point, and this
  page first read its message as naming the procedure.

**Not measured:** whether a positional argument is evaluated before the error, and an Alias by ordinal (`Alias "#1"`).

**In HexIDE (#4820, #4869):** HexIDE used to skip every `Declare` (`PrePass.VisitDeclareStmt`), so a call raised "Sub or
Function not defined", and no `On Error` trapped it. #4820 makes such a call off Windows raise 53, as VBA on a Mac does,
and its PR, #4871, raises the same 53 through an `Alias`. #4869 places a named argument by the Declare's parameter name
and evaluates it once before the 53, as the row above measured. On Windows, HexIDE still refuses the call.
