## A Public array in a class or a form, under /make and in the IDE (2026-11-08, #3335)

**The question.** HexIDE ran a class module holding `Public Items(1)`, and two runtime tests pinned that it ran. Does
vb6.exe refuse it, and does the IDE's Compile On Demand put the refusal off until the class is used?

**The probe, /make.** One project per declaration, each with a `Sub Main` that creates the class, or loads and unloads
the form, under `On Error Resume Next`. Each was built with `VB6.EXE /make` as native code and as p-code on the Win10
guest, VB6 SP6. The two builds agreed on every project. Each refused build reported the declaring file, at "Line 0".

| Declaration | In | vb6.exe |
|---|---|---|
| `Public Items(1)` | a class | **refused**: "Constants, fixed-length strings, arrays, user-defined types and Declare statements not allowed as Public members of object modules" |
| `Public Items(1) As Long` | a class | the same |
| `Public Items(1 To 3) As String` | a class | the same |
| `Public Items() As Long` | a class | the same |
| `Public Items()` | a class | the same |
| `Public n, Items(1)` | a class | the same |
| `Public Items(1)` | a form | the same |
| `Public Items() As Long` | a form | the same |
| `Private Items(1)` | a class, and a form | builds and runs, `Err.Number` 0 |
| `Dim Items(1)` | a class | builds and runs |
| `Public n` | a class | builds and runs |
| `Public Items(1)` | a standard module | builds and runs |

The same message refuses the other four kinds it names, each in a class:

| Declaration | vb6.exe |
|---|---|
| `Public Const K = 1` | refused, the same message |
| `Public S As String * 5` | the same |
| `Public t As TT`, with `Public Type TT` in a standard module | the same |
| `Public Declare Function GetTickCount Lib "kernel32" () As Long` | the same |

**The probe, the IDE.** Four projects, each run with `VB6.EXE /run probe.vbp` in the guest's console session. Each
project's `Sub Main` writes a line to a file before it does anything else. Each was run under three settings:
- Compile On Demand on, with Background Compile on;
- Compile On Demand on, with Background Compile off;
- Compile On Demand off.

| Project | vb6.exe, under all three settings |
|---|---|
| a class with `Public Items(1)`, which `Sub Main` creates | the compile error above, **before `Sub Main` writes its line** |
| the same class, which nothing creates or names | the same: the compile error, before `Sub Main` runs |
| a class `Hollow` with `Implements IShape` and no `IShape_Area`, which `Sub Main` creates | "Object module needs to implement 'Area' for interface 'IShape'", before `Sub Main` runs |
| the same `Hollow`, which nothing creates or names | the same |

`/make` refuses both `Hollow` projects with the same message, native code and p-code.

What these add:
- **A Public array is refused in a class and in a form, fixed or dynamic, typed or not.** So is a Public `Const`,
  fixed-length string, UDT variable or `Declare` there. A Private or `Dim` array is not, and neither is a Public array
  in a standard module.
- **Compile On Demand doesn't put either check off.** The IDE refuses the program before `Sub Main` runs, under every
  setting measured, even when nothing uses the class. It treats the missing interface member the same way.

**Overturned.** The obvious reading of Compile On Demand is that the IDE leaves an unused class uncompiled, so that only
`/make` would refuse these programs. For a declaration that is wrong, and for a missing interface member too.

**Not measured:**
- the IDE's own Start (F5) and Start With Full Compile (Ctrl+F5): only `/run` from the command line was used;
- a UserControl, a PropertyPage or a DataReport;
- `Global` in a class or a form;
- a `Declare` in a class with no `Public` or `Private`.

**In HexIDE**, at main eb11ec902, a class or form with a Public array loaded and ran. With #3335 it is refused as the
program loads, with vb6.exe's message, before any code runs. The other four kinds, and the missing interface member of a
class nothing creates, are still accepted.
