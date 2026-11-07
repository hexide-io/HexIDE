## A control where a value is wanted is its default member (2026-10-12, #790)

Measured to decide what HexIDE should do with `s = Text1` until it has default members. Real `vb6.exe` 6.00.8176
(the original release), guest locale en-GB. Two `/make` runs of a form holding `Text1`, `Label1`, `Command1` and `Check1`, each
statement under `On Error Resume Next`, with the value, its `TypeName` and `Err.Number` logged.

| Statement | Result | Err |
|---|---|---|
| `s = Text1` / `s = Label1` (s As String) | `"Text1"` / `"Label1"`: the Text and the Caption | 0 |
| `v = Text1` (v As Variant) | `"Text1"`, String | 0 |
| `v = Command1` / `v = Check1` | `False`, Boolean / `0`, Integer: the Value | 0 |
| `n = Check1` (n As Long) | `0` | 0 |
| `n = Text1` (n As Long), Text1 holding `"Text1"` | unchanged | 13 |
| `i = Text1` / `d = Text1` (Integer / Double), Text1 holding `"42"` | `42` / `42` | 0 |
| `s = Command1` / `b = Command1` (String / Boolean) | `"False"` / `False` | 0 |
| `Set o = Text1 : s = o` (o As Object) | `"42"` | 0 |
| `v = o` | `"42"`, String | 0 |
| `Set v2 = Text1` | `TypeName(v2)` is `TextBox` | 0 |
| `v2 = Text1` after that Set (a Let) | `TypeName(v2)` is `String` | 0 |
| `Label1 = "x"` / `Text1 = "y"` / `Check1 = 1` | Caption `x` / Text `y` / Value `1` | 0 |

So a Let never stores the reference: the right-hand side is read through its default member, then converted to the
target's type as any value is (`n = Text1` is 13 because `"Text1"` is not a number). Only `Set` stores the
reference. The last row is the Let side: assigning to a control assigns its default member.

**HexIDE, after #790's guardrail.** HexIDE has no default members yet. It used to store the reference, which
answered wrong without a word: `s = Text1` held `"HexIDE.Runtime.BuiltinControls.VBTextBox"` and `v = Text1` kept
the control. A Let of a control or of an in-box object (`Err`, `App`, a control array) now stops the run with
`NotImplementedException` naming #790, whatever its target. `Set`, `For Each` and passing a control as an argument
are unaffected. `Label1 = "x"` is still a compile error ("Method or data member not found"). `n = Err` reads
`Err.Number` in VB6 by the documentation; that was not measured.

**Superseded for the in-box objects by #840**, which measured them ("An object used as a value"): `n = Err` stores
Err.Number, `s = App` raises 438, and `Debug` and a control array used as values are compile errors. The #790
refusal now covers controls only.

