## Err.Source after a run-time error and after Err.Raise (2026-10-27, #1483)

**The question.** #1471 found `Err.Source` "Probe" after every run-time error it trapped in a project named `Probe`,
where HexIDE gives `""`. Is the source the project's `Name=`, or its `Title=`? Is it the class's name for an error
raised inside a class, as is often said? What is it for an error raised inside an ActiveX DLL, and after `Err.Raise`
with and without a source?

**The probe.** An ActiveX DLL, `Name="ProbeDll"`, whose MultiUse class `Thrower` raises each error. It was built with
`/make`, which registers it. Two client projects reached it late bound through `CreateObject("ProbeDll.Thrower")`.
Each has `Name="Probe"` and `ExeName32="probe.exe"`, and the second also has `Title="Titled App"`. Each client also
has a form, and a class of its own, `Class1`, holding the same methods as `Thrower`. Every error was trapped with
`On Error GoTo` in a standard module's procedure, unless the row says "trapped inside". Both clients were built native
and p-code, and all four runs logged the same lines, Title or not.

| Raised | `Err.Number` | `Err.Source` |
|---|---|---|
| `1 / 0` in a standard module, `Err.Raise 1000` there, and `Error 5` there | 11, 1000, 5 | `Probe` |
| `Err.Raise 1001, "MySrc"`, in the module, in `Class1` and in the DLL | 1001 | `MySrc` |
| `1 / 0` and `Err.Raise 1000` in a form's procedure | 11, 1000 | `Probe` |
| In `Class1`: `1 / 0`, `Err.Raise 1000`, `Err.Raise vbObjectError + 5`, a call on Nothing | 11, 1000, -2147221499, 91 | **`Probe`**, not `Probe.Class1` |
| In `Class1`, trapped inside the class: `1 / 0` and `Err.Raise 1000` | 11, 1000 | `Probe` |
| In the DLL's class: `1 / 0`, `Err.Raise 1000`, `Err.Raise vbObjectError + 5`, a call on Nothing | 11, 1000, -2147221499, 91 | **`ProbeDll`**, the DLL's project name |
| In the DLL's class, trapped inside it: `1 / 0` and `Err.Raise 1000` | 11, 1000 | `ProbeDll` |
| A late-bound call to a member the DLL's class does not have | 438 | `Probe`, the caller's |
| After `Err.Clear` | 0 | `""` |
| `Err.Source = "Set"`, with no error | 0 | `Set` |
| Then `Err.Raise 7`, with no source given | 7 | **`Set`**: the source already set is kept |

So `Err.Source` is the `Name=` of the project whose code raised the error, whatever the module kind. It is the DLL's
for an error raised inside a DLL, and the caller's for a call the DLL refuses. The project's `Title=` plays no part. A
source given to `Err.Raise` wins. Without one, `Err.Raise` keeps a source already set; whether it ever substitutes
the project name over a non-empty one was not measured.

**What this overturned.** The obvious guess, and a common claim, is that an error raised in a class reports
`Project.Class`. vb6.exe reports the project alone, from the class's own handler and its caller's alike.
