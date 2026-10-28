## App.HelpFile, App.StartMode, App.TaskVisible and IMEStatus (2026-10-28, #2551)

Measured on vb6.exe SP6, every probe built native and as p-code, which logged the same lines. Each probe is a Standard EXE `Sub Main` under `On Error Resume Next`, logging each statement with `Err.Number`; `Show(v)` logs a value as `[value] TypeName/VarType`.

**What the rows say:**
- `App.HelpFile` is a String, "" when the project names no help file. It may be assigned, and reads back as assigned, with no path added. (The probe's project named none, so the value a `.vbp`'s `HelpFile=` gives is unmeasured.)
- `App.StartMode` is an **Integer**, 0 (`vbSModeStandalone`) in a Standard EXE. Assigning it is the compile error "Can't assign to read-only property".
- `App.TaskVisible` is a Boolean, and reads back as assigned. **Its starting value depends on the session:** True when the program runs on the desktop, measured in the guest's console session (`TaskVisible [True] Boolean/11`), and False in session 0, where the batch runs, as below. A program run from HexIDE is on a desktop, so it starts True.
- `IMEStatus` is a **Long**, 0, on this en-GB machine with no input method editor.

```
HelpFile [] String/8 err 0
HelpFile set err 0
HelpFile after [probe.hlp] String/8 err 0
LogMode [1] Long/3 err 0
LogPath [] String/8 err 0
StartMode [0] Integer/2 err 0
TaskVisible [False] Boolean/11 err 0
TaskVisible set False err 0
TaskVisible after [False] Boolean/11 err 0
TaskVisible set True err 0
IMEStatus [0] Long/3 err 0
LogEvent with no StartLogging err 0
LogMode after a LogEvent [3] Long/3 err 0
app-props: done
```
