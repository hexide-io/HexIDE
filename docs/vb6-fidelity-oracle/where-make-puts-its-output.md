## Where `/make` puts its output: Path32, /outdir, a failed build, and an ActiveX DLL's registration (2026-11-18, #4538, #2503, SP6)

**The question.** HexIDE's Make with VB6 and Run with VB6 run `VB6.EXE /make`, then look for the exe beside the `.vbp`
(#4538). Which folder does `/make` actually write to, given the `.vbp`'s `Path32=` and `ExeName32=` lines and the
command line's `/outdir`? What does a failed build leave behind? What else does a build write? #2503 asks for the last
two, to decide whether Make can build into a staging folder and then swap the result in.

**The probe.**
- **The projects:** fourteen Standard EXE projects, each one form, and three ActiveX DLL projects, each one `MultiUse`
  class. Two said `Startup="(None)"`, and one had no `Startup=` line.
- **The runs:** in the Win10 guest's session 0, through PowerShell Direct. Each ran `/make "<project>.vbp" /out
  "<log>"`, adding `/outdir "<folder>"` where a row says so.
- **vb6.exe's working folder** was an empty folder elsewhere, so a relative path resolved against it would have shown
  up there. Nothing ever did.
- **What was compared:** every file under the probe's root was hashed before and after each build, and the table lists
  every file a build added, changed or removed.
- **Builds:** p-code, except the two rows marked native. The DLLs were unregistered afterwards with
  `regsvr32 /u`, and their ProgIDs were then gone.

| `.vbp` and command line | `/make` | What the build wrote |
|---|---|---|
| `ExeName32="m1.exe"`, `Path32="out"`, `out` there | succeeded | `out\m1.exe` under the `.vbp`'s folder, and nothing else |
| the same, `out` missing | `Path not found`, failed | nothing, and no folder is made |
| `Path32` absolute, the folder there | succeeded | the exe in that folder |
| `Path32="out"`, `out` there, **no `ExeName32`** | succeeded | **`m3b.exe` beside the `.vbp`**, named after the project's `Name`. `out` stayed empty |
| `ExeName32` and `Path32="out"`, with `/outdir` an existing folder | succeeded | the exe in the `/outdir` folder only; `out` stayed empty |
| no `Path32`, `/outdir` an existing folder; p-code, then native | succeeded | the exe in the `/outdir` folder: 12288 bytes, and 16384 native. Nothing else is left, native included |
| `/outdir` a folder that doesn't exist | `Path not found`, failed | nothing |
| `AutoIncrementVer=1`, `RevisionVer=0`, `/outdir` | succeeded | the exe in the `/outdir` folder, **and the `.vbp` rewritten**: see below |
| `AutoIncrementVer=0`, no `/outdir` | succeeded | the exe beside the `.vbp`; the `.vbp` unchanged |
| a compile error ("Variable not defined"), an older exe of the same name beside the `.vbp`; p-code, then native | failed | nothing: the older exe keeps its bytes |
| the same compile error, the older exe in the `/outdir` folder | failed | nothing: the older exe keeps its bytes |
| a good build over an older exe beside the `.vbp` | succeeded | the exe replaced where it was, and nothing set aside |
| ActiveX DLL, `ExeName32="HxMkA2.dll"`, no `/outdir` | succeeded | `HxMkA2.dll`, `.exp` and `.lib` beside the `.vbp`. The class registered machine-wide, in the 32-bit view, with `InprocServer32` naming that `.dll` |
| ActiveX DLL, no `Startup=` line | "Sub Main() doesn't exist", failed | nothing |
| ActiveX DLL, `/outdir` an existing folder | succeeded | `.dll`, `.exp` and `.lib` in the `/outdir` folder. **`InprocServer32` names the `.dll` in the `/outdir` folder** |

**The `.vbp` that `AutoIncrementVer=1` wrote back under `/outdir`.** The project file held fifteen lines. VB6 wrote
back thirty:
- `RevisionVer=1`;
- **`Path32="..\..\stage\m7"`**, the `/outdir` folder written relative to the `.vbp`. The file had no `Path32` line;
- `IconForm="Form1"`, `HelpContextID="0"`, `CompatibleMode="0"`, `ServerSupportFiles=0`, and the native-code options
  (`FavorPentiumPro(tm)=0` through `UnroundedFP=0`), `ThreadPerObject=0` and `MaxNumberOfThreads=1`, none of which it
  had.

**What it shows:**
- **`Path32` is the exe's folder only when `ExeName32` names the exe.** A relative `Path32` is resolved against the
  `.vbp`'s folder, not the working folder. Without `ExeName32`, `/make` ignores `Path32` and writes `<Name>.exe` beside
  the `.vbp`. That fits [What Save Project As does to the paths in a project file](what-save-project-as-does-to-the-paths-in-a-project-file.md),
  where Save Project As drops a `Path32` that has no `ExeName32`.
- **`/make` makes no folder.** A missing `Path32` or `/outdir` folder fails the build with "Path not found".
- **`/outdir` beats `Path32`.** The `.vbp` itself is left alone, unless VB6 writes it back for another reason.
- **When VB6 does write the `.vbp` back, it records the `/outdir` folder as `Path32`.** `AutoIncrementVer=1` is one
  such reason.
- **A failed compile writes nothing,** and leaves an older exe as it was, p-code and native, with or without
  `/outdir`.
- **A good build replaces the exe in place.** Nothing is staged or kept aside.
- **An ActiveX DLL registers itself where it was built.** A DLL built under `/outdir` and then moved elsewhere would
  leave its registration naming the old folder. It also writes an `.exp` and a `.lib` beside itself.

**Not measured:**
- a build interrupted while it writes the exe;
- a target that is read-only or locked by a running copy;
- an ActiveX EXE or an OCX;
- binary compatibility;
- which other settings make `/make` write the `.vbp` back.
