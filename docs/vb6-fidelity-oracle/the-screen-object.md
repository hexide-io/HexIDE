## The Screen object (2026-10-28, #2431)

**The question.** HexIDE refuses `Screen` as a built-in object it does not implement (#871). Real programs reach it
early: they centre a form with `(Screen.Width - Me.Width) / 2`, and they set `Screen.MousePointer = vbHourglass` around
slow work. What does each member give, of what type, and when? What does a write do?

**The probe.** A Sub Main program and four one-statement projects, built native and p-code, run in the guest's console
session on a 1024 x 768 desktop at 96 dpi. Form1 holds Text1 (TabIndex 0) and Command1 (1). The program reads every
member before Form1 is loaded, after `Load`, after `Show`, and after `Unload`. Each read is under
`On Error Resume Next`, logging `TypeName` and the value or the error. Native and p-code logged the same lines. The
probe runner's session, which has no interactive desktop, gave the same values except for ActiveForm and ActiveControl,
which only the console can answer.

| Member | vb6.exe |
|---|---|
| `TypeName(Screen)` | "Screen" |
| `Width`, `Height` | Single 15360 and 11520: the screen in twips |
| `TwipsPerPixelX`, `TwipsPerPixelY` | Single 15 each |
| `MousePointer` | Integer 0 |
| `MouseIcon` | an object, TypeName "Picture", with no icon set |
| `ActiveForm` before a form is shown, after `Load` alone, and after `Unload` | **Nothing**, no error |
| `ActiveControl` with no form in the project at all | **Nothing**, no error |
| `ActiveForm` once Form1 is shown | Form1 |
| `ActiveControl` once Form1 is shown; after `Command1.SetFocus` | Text1; Command1 |
| `ActiveControl` with a form shown that holds only a Label | **Nothing**, no error; `Form1.ActiveControl` too |
| `FontCount` | **Integer**, 154 on this guest |
| `Fonts(0)`, `Fonts(FontCount - 1)` | a String each: "System" first |
| `Fonts(FontCount)`, `Fonts(-1)` | **""**, no error |
| `Fonts(1.5)`, `Fonts(2.5)`; `Fonts("1")` | `Fonts(2)` both, rounded to even; `Fonts(1)` |
| `Fonts(40000)`, `Fonts("abc")`, `Fonts(Null)` | 6, 13, 94 |
| `MouseIcon.Handle` | 0 |
| `Dim s As Screen: Set s = Screen` | compiles and runs: `s.Width` 15360 |

**MousePointer**, each write under `On Error Resume Next`, then a read:

| Written | Err | Reads |
|---|---|---|
| 11, 0, 1, 15, 99 | 0 | the value written |
| 16, -1, 100 | **380** | unchanged |
| 1.6, 2.5, 3.5 | 0 | 2, 2, 4: rounded to even |
| "11", a String | 0 | 11 |
| True | **380**, as -1 | unchanged |
| "abc" | 13 | unchanged |
| Null | not logged: the log line's own `IIf` evaluated `CStr(Null)`, which raised 94 | |
| 32768, 1E+100 | **6** | unchanged |

Throughout, `Form1.MousePointer` read 0: setting the Screen's does not set the form's. A value set stays after the
last form unloads: `Screen.MousePointer = 11`, `Unload Form1`, then a read gives 11.

An active form with nothing that can take the focus gives Nothing for `ActiveControl`, as the form's own
`ActiveControl` does. That was a second probe, the form holding only a Label, run on the console, native and p-code
agreeing.

**Overturned by a third probe, and the first probe's fault.** This entry first said `ActiveControl` is 91 when no form
is active. A third probe, a Sub Main project with no form at all, read `TypeName(Screen.ActiveControl)` right after
`Err.Clear` and got "Nothing" with Err 0. The first probe logged each object read as
`L ... & IIf(o Is Nothing, "", " " & o.Name)`. `IIf` evaluates both arms, so with `o` Nothing, `o.Name` raised 91 and
`On Error Resume Next` skipped the line. The stale 91 was then logged against the read that came next, which was
`ActiveControl`'s. So the missing `ActiveForm` lines are what show `ActiveForm` was Nothing at those three points.
`ActiveControl` after `Load` alone and after `Unload` was not read cleanly, and the third probe's no-form case is the
only clean reading of it with no form active. In the probe runner's session the same defect made the shown-form
`ActiveForm.Caption` row read 91; that row reads "Form1" on the console. The third probe also gave the `FontCount` type,
the index and pointer conversions, and `MouseIcon.Handle`, native and p-code agreeing.

**Writes to the read-only members:**

| Statement | vb6.exe |
|---|---|
| `Screen.Width = 100`, `Screen.FontCount = 1` | compile error **"Can't assign to read-only property"** |
| the same through `Set s = Screen`: `s.Width = 100`, `s.FontCount = 1` | **382** "'Width' property cannot be set at run time", and FontCount's |
| `s.TwipsPerPixelX = 1` | **383** "'TwipsPerPixelX' property is read-only" |
| `Set s.ActiveForm = Nothing` | 382 |
| `s.NoSuch = 1`, `v = s.NoSuch` | 438 |
| `Set Screen = Nothing` | the build wrote no log line, success or failure; not measured further |
