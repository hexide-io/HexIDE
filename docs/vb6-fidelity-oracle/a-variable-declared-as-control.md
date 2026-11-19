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
