## A variable declared As Control (2026-11-19, #4864, SP6)

**The obvious guess is that `As Control` is `As Object` for controls: a late-bound view that takes whatever it is
given. It is late-bound, but it takes only a control.** A form, a class instance, a Collection, a Font or the form's
`Controls` collection is error 13, and the variable keeps what it held. #4737 measured the late-bound half: named
arguments through `As Control` are 448, as through `Object`. HexIDE took that to mean `Control` was `Object` in every
respect, and stores anything in it.

**The probe.**
- **Build:** vb6.exe 6.00.9782 with MSVBVM60 6.00.9848, `/make` native and p-code in session 0. The two builds
  logged every row identically.
- **The project:** `Form1`, the startup form, has a TextBox `Text1` and a Timer `Timer1`. The project also has a
  second form, `Form2`, whose Load sets a flag, and a class, `Class1`.
- **Each row:** one `Set` under `On Error Resume Next`, with the error it raised. In `Form1`'s Load the variables
  are `Dim c As Control, o As Object, v As Variant`; the last three rows run in a standard module's Sub, with its
  own `Dim c As Control`.

| Statement | VB6 |
|---|---|
| `Set c = Text1` | ok |
| `Set c = Me`, `c` holding `Text1` | **13**, and `c Is Text1` is still True |
| `Set c = New Class1` | **13**, and `c` still holds `Text1` |
| `Set c = New Collection` | **13** |
| `Set c = Text1.Font` | **13** |
| `Set c = Controls` | **13** |
| `Set c = Form2` | **13**; Form2's Load doesn't run |
| `Set o = Me: Set c = o` | **13** |
| `Set v = Me: Set c = v` | **13** |
| `Set v = New Class1: Set c = v` | **13** |
| `Set o = Timer1: Set c = o` | ok, and `TypeName(c)` is `Timer` |
| `Set c = Nothing` | ok, and `c Is Nothing` is True |
| `Set c = Me`, `c` Nothing | **13**, and `c` is still Nothing |
| In a standard module: `Set c = Form1` | **13** |
| In a standard module: `Set c = Form1.Text1` | ok |
| In a standard module: `Set c = Form1`, `c` holding `Form1.Text1` | **13**, and `c` still holds `Form1.Text1` |

- **`Control` acts as a class that every intrinsic control belongs to,** and nothing else does. A form isn't one, as
  `TypeOf Me Is Control` being False already said (#1060).
- **The check is made at the `Set`, on the object itself.** Through `As Object` or a Variant, a form or class
  instance is 13 and a control is accepted, as #1563 measured for `As TextBox`.
- **A failed `Set` leaves the variable as it was,** holding a control or Nothing.
- **`As VB.Control` is the same,** measured in a second build of the same form: `Text1`, and `Timer1` through
  `As Object`, are accepted; `Me` (leaving `Text1` in place), `New Class1`, `New Collection` and `Me` through
  `As Object` are 13.

**In HexIDE (#4864):** on main at 1b061986e every row stores with `Err.Number` 0, measured with a throwaway test in
`FormControlsTests` that used `Command1` for `Text1` and `New Collection` for `New Class1`.

**Not measured:**
- a UserControl, an OCX control or a control array's element;
- a control array itself, such as `Set c = Text1` where `Text1` is an array;
- a parameter, Type field or array declared `As Control`.

### Beyond a local, New StdFont, and For Each (2026-11-19, #4864, #4878)

**A scalar declared `As Control` checks the same way wherever it is declared. So does a For Each into one, element by
element, and after a 13 at a later element the loop variable is Nothing.** A plain `Set` that fails keeps the old
reference, so the loop's store differs from a `Set` there, and that holds for `As TextBox` and a class too. Two more
builds, same toolchain, native and p-code logging every line identically.

**The first build** adds three declarations to the project above:
- `Private mc As Control` at `Form1`'s module level;
- `Public gc As Control` in a standard module;
- `Public c As Control` in `Class1`, reached through `Set k = New Class1`.

Each For Each runs under `On Error Resume Next`. Its body logs `Err.Number` and `TypeName` of the variable, then
clears Err, and the probe logs Err after `Next`.

| Statement | VB6 |
|---|---|
| `Set mc = Text1`, then `Set mc = Me` | ok, then **13**, and `mc Is Text1` is still True |
| `Set gc = Text1`, then `Set gc = Me` | ok, then **13**, and `gc Is Text1` is still True |
| `Set k.c = Text1`, then `Set k.c = Me` | ok, then **13**, and `k.c Is Text1` is still True |
| `Set c = New StdFont`, `c` a local | **13**, and `c Is Nothing` is True |
| `For Each c In col`, `col` = {Text1, Me, Timer1} | the body runs once, with Err 0 and `TextBox`; after `Next`, Err **13** and `TypeName(c)` **Nothing** |
| `For Each c In col`, `col` = {Me, Text1}, `c` Nothing before | the body runs with Err **13** and `Nothing`, then with 0 and `TextBox`; after `Next`, Err 0 and `TypeName(c)` Nothing |
| `For Each c In Forms`, only `Form1` loaded | the body runs once, with Err **13** and `Nothing`; after `Next`, Err 0 |

`Form2` was still not loaded at the end.

**The second build** is a form holding `Text1`, `Timer1` and `Text2`, in that order, which is the order its
`Controls` loop visits. The project has classes `Class1` and `Class2`.

| Loop or statement | VB6 |
|---|---|
| `For Each t In col`, `t As TextBox`, `col` = {Text1, Timer1, Text2} | the body runs once, with Err 0 and `TextBox`; after `Next`, Err **13** and `TypeName(t)` **Nothing** |
| `For Each t In Controls`, `t As TextBox` | the same |
| `For Each k In col`, `k As Class1`, `col` = {Class1, Class2, Class1} | the body runs once, with Err 0 and `Class1`; after `Next`, Err **13** and `TypeName(k)` **Nothing** |
| `Set t = Text1: Set t = Timer1`, `t As TextBox` | **13**, and `TypeName(t)` is still `TextBox` |

- **A 13 at a later element ends the loop.** Under Resume Next the program goes on after `Next`, and the third
  element is never reached.
- **The loop variable is then Nothing, whatever it is declared as.** The obvious guess, and HexIDE's, was that it keeps
  the last element the body saw, as a failed `Set` keeps its old reference.
- **A 13 at the first element runs the body with the variable unset, and the loop goes on,** as #1563 measured for
  `As TextBox` over `Controls`.

**In HexIDE (#4878):** a throwaway test on #4864's branch ran the {Text1, Me, Timer1} loop, an `As TextBox` loop
over {Text1, Timer1} and the `As Class1` loop. Each ended after one body with Err 13, as VB6 does, but `TypeName` after
the loop was `TextBox`, `TextBox` and `Class1`. #4864 pins the error and where the loop stops; #4878 is the variable.
HexIDE refuses `New StdFont` before any store (#4781).

**Not measured:**
- a loop variable that held an object before its first element failed;
- a later element that isn't an object at all;
- the same failures with an error handler in place of Resume Next.
