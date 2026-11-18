## App.Path and App.EXEName under F5, and in the made exe (2026-11-17, #4504)

**The question.** HexIDE's Run with VB6 makes the project's exe and runs it. That stands in for F5, but F5 writes no exe. Does the made program see the same `App.Path`, `App.EXEName` and `CurDir` as F5 does, and what changes when the exe is run from somewhere else?

**The probe.**
- **The project:** `Proj.vbp` with `Name="Proj"` and `ExeName32="Built.exe"`, so the two names differ. Its one module's `Sub Main` appends `App.Path`, `App.EXEName`, `App.Title` and `CurDir` to a log at a fixed absolute path.
- **The runs:**
  1. F5 in VB6 SP6's IDE on the Win10 guest, with the IDE started from another folder;
  2. `VB6.EXE /make`, then the made `Built.exe` started from the project's folder;
  3. the same exe started with `C:\` as its working directory;
  4. the exe copied to another folder as `Renamed.exe` and started there.

| Run | `App.Path` | `App.EXEName` | `App.Title` | `CurDir` |
|---|---|---|---|---|
| F5 | the project's folder | **`Proj`**, the project's Name | `Proj` | the IDE's own working folder |
| `Built.exe` in the project's folder, started there | the project's folder | **`Built`**, the exe's file name | `Proj` | the project's folder |
| the same exe, started with `C:\` as its working directory | the project's folder | `Built` | `Proj` | `C:\` |
| the exe copied elsewhere as `Renamed.exe` | that folder | `Renamed` | `Proj` | `C:\` |

**What it shows:**
- **`App.Path` is the folder the exe sits in,** and under F5 it is the project's folder. So an exe made in the project's folder sees the same `App.Path` as F5, and one made anywhere else does not.
- **`App.EXEName` under F5 is the project's Name, and in an exe it is the exe's own file name.** The two differ whenever `ExeName32` isn't the project's Name, wherever the exe is.
- **`App.Title` stayed the project's Name in every run,** since the probe set no Title.
- **`CurDir` is the working directory the process was started with.** Under F5 that's the IDE's, not the project's folder.

**Unmeasured:** `App.EXEName` under F5 with an `ExeName32` but no `Name` line, and with the project saved under another file name.
