# Real RDCore DAP acceptance

This opt-in console check exercises HexIDE's session client against a real, separately built RDCore platform.
It does not change F5 or the debugger windows. No RDCore code or binaries are bundled in HexIDE.

Build RDCore's LanguageServer, ParseServer and CLI from the same revision, following that project's build
instructions. Supply its platform manifest (`rdcore.json`) and set `RDCORE_PLATFORM_ROOT` to the directory
containing it. The manifest must resolve `LangService`, `ParseServer` and `HostService` to the respective
executables (the runtime host is `rdc`). Keep its configuration and logs isolated from your normal platform.

Create an isolated workspace containing a `.rdproj` file:

```json
{
  "Version": "0.1.0",
  "Configuration": [],
  "ProjectInfo": {
    "Name": "DapSmoke",
    "References": [],
    "Modules": [{ "RelativeUri": "Module1.bas" }],
    "OtherFiles": [],
    "Folders": [],
    "PrecompilerConstants": {}
  }
}
```

Add `Module1.bas` with these exact lines:

```vb
Attribute VB_Name = "Module1"
Option Explicit
Public Sub Main()
    Dim answer As Long
    answer = 41
    answer = answer + 1
    Debug.Print answer
End Sub
```

Run from the HexIDE repository, substituting absolute paths:

```text
dotnet run --project IDE/HexIDE.Dap.Smoke -- <adapter-executable> <workspace-directory> Module1 <Module1.bas-path> 6
```

The check has a 90-second overall deadline, plus bounded process cleanup. It requires a verified breakpoint,
a breakpoint stop at the requested line, a stack frame, at least one visible variable, a terminated event
after continue, and adapter exit code zero without forced termination or cleanup failure. It prints stack,
variables and output for semantic inspection. Nonzero exit means acceptance failed; stderr is printed on failure.
The tool deliberately accepts an existing workspace rather than converting an IDE project. It can execute
that workspace's program, so use the isolated example for reproducible acceptance.

## Measured result

On Windows, 2026-10-18, against RDCore revision `b55abe3a0e9584807d917406976493e963b9b086`, built in Release:

- Real parsing server and runtime host launched successfully.
- Breakpoint verified at line 6; stop reason `breakpoint`.
- Stack: frame ID `0`, `Main`, `Module1.bas`, line 6, column 5.
- Locals: `answer`, type `Long`, value `41`.
- Continue produced output `42` and exited/terminated events.
- Explicit disconnect completed; adapter exit code `0`, forced termination `false`, no cleanup failure.

RDCore keeps the adapter alive after terminated until disconnect or transport closure. Waiting for process
exit without disconnect reached the smoke deadline; explicitly disconnecting completed cleanly. The session
must distinguish program termination from adapter process completion.

This is a Windows interoperability measurement, not a claim of Linux RDCore acceptance, IDE UI integration,
or hosted form support. The independent session/wire tests run on Windows and Linux.
