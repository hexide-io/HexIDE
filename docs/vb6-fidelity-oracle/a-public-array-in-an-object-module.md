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

## A Public Type in a class or a form (2026-11-20, #4907)

**The question.** #4907 left one case refused: a class whose variable is declared above the class's own `Public Type`,
whose array field is sized by a Const. Does vb6.exe build that program, and does it build a class or a form that
declares a `Public Type` at all?

**The probe.** vb6.exe 6.00.9782 with MSVBVM60 6.00.9848, on the Win10 guest, in session 0.
- **Builds:** one Standard EXE project per row, each built with `/make` as native code and as p-code. The two builds
  agreed on every row.
- **The Type:** `T`, with one field, `a(1 To 3) As Long`, or `a(1 To N) As Long` in C2.
- **The check:** `Sub Main` creates the class, or loads the form, and calls a method that writes `LBound(x.a)` and
  `UBound(x.a)` to a file, sets `x.a(3) = 7` and reads it back, under `On Error Resume Next`.

| Row | In | Declarations, in order | vb6.exe |
|---|---|---|---|
| C0 | a class | `Private Type T`, `Private x As T` | builds; bounds 1 To 3, `x.a(3)` reads back 7, no error |
| C1 | a class | `Public Type T`, `Private x As T` | **refused:** "Cannot define a Public user-defined type within a private object module" |
| C2 | a class | `Private x As T`, `Private Const N As Byte = 3`, `Public Type T` | refused, the same |
| C3 | a class | `Type T`, with no `Public` or `Private`, then `Private x As T` | refused, the same |
| F0 | a form | as C0 | builds; bounds 1 To 3, reads back 7, no error |
| F1 | a form | as C1 | refused, the same |
| F3 | a form | as C3 | refused, the same |

- **Where each build log points:** the declaring file, at the line of the `Type` statement.
  - It gives `Line 2` for C1, C3, F1 and F3, and `Line 4` for C2.
  - The count starts at 0 on the line after the file's last `Attribute` line.

What these add:
- **A class or a form can't declare a Public Type** in a Standard EXE. The message is a different one from the
  Public Const, array, fixed-length string, UDT variable and `Declare` of the section above.
- **A Type with no `Public` or `Private` is Public there.** C3 is refused as C1 is.
- **#4907's last case never builds.** C2 is refused for declaring the Type, whatever its bounds would have been.

**Checked, not overturned.** #4907's first brief claimed, without measuring it, that a Public Type can be declared only
in a standard module. A lane stopped on that unchecked claim. For a Standard EXE's classes and forms it holds.

**Not measured:**
- `Friend Type` or `Global Type` in a class or a form;
- a public class in an ActiveX DLL or EXE project;
- a UserControl, a PropertyPage or a DataReport;
- the IDE's `/run`.

**In HexIDE**, at main 1bec9d018, run through `RunClasses` in a scratch test:
- **C1** runs, and prints 1, 3 and 7.
- **A class whose `Public Type` nothing uses** runs too.
- **C2** is refused with #4907's "not implemented" message.

Forms were not run. #5042 tracks the refusal.
