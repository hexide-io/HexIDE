## A Label's border (2026-10-17, #1077)

**The probe.** A pixel-mode form with a white BackColor, holding Labels 20 pixels square (300 twips): Fixed Single with
Appearance 3D, Fixed Single Flat, 3D with a red BackColor, no border, and two 3D Labels edge to edge. The others are
grey, &H00C0C0C0. A Timer, once the form is shown, logs `Point` for every pixel from two outside each Label to two past
it. It ran on the oracle guest's console session, Windows 10, built native and p-code, and both logged the same.

**3D Fixed Single is a sunken edge two pixels deep**, Win32's EDGE_SUNKEN. Reading a Label from its first pixel to its
last, through the middle:

| Ring | Top and left | Bottom and right |
|---|---|---|
| outer | `A0A0A0`, button shadow | `FFFFFF`, button highlight |
| inner | `696969`, 3D dark shadow | `E3E3E3`, 3D light |

So the first row is 19 pixels of shadow and one of highlight, the second is shadow, then dark shadow to the
18th pixel, then light and highlight, and the BackColor fills the 16 x 16 inside. The edge is the same with a red
BackColor, so it doesn't come from the BackColor. Two Labels side by side read highlight then shadow where they meet,
which is what makes a field of them a grid.

**Flat Fixed Single is one pixel of window frame,** `646464` on Windows 10, all the way round, with the BackColor inside
it. The obvious guess was black, which is what HexIDE drew, or tried to.

**No border is the BackColor to the edge.**

**In HexIDE:** fixed (#1077). The Label's ClassicBorderDecorator had no brushes, since its defaults come from the Classic
theme, which isn't loaded, so neither border drew at all. It now draws each ring in the VB6 system colour above, as
HexIDE resolves that colour. HexIDE's Classic palette keeps its own values for four of them: shadow `808080`, dark shadow
`404040`, and both lights `FFFFFF`, against Windows 10's `A0A0A0`, `696969`, `E3E3E3` and `FFFFFF`. So the structure is
VB6's and the shades are the palette's. Window frame had no colour at all, so `vbWindowFrame` resolved to nothing; it is
now Windows 10's `646464`. That is the Windows 10 measurement inside an otherwise Win98-era palette, whose window frame
would be black, which is why black was the obvious guess.


### A Label's BorderStyle at run time (2026-11-18, #4647, SP6)

**The probe.** A form held Label1, saved with BorderStyle 0, and Label2, saved with 1. Its Form_Load ran under
`On Error Resume Next`. Before each assignment, it set `Label1.BorderStyle = 0` and cleared Err. After it, it logged
Err.Number and what Label1.BorderStyle read. p-code and native code logged the same lines. The run was in session 0, so
nothing here was looked at on screen.

| Statement | Err | Reads after |
|---|---|---|
| read `Label1.BorderStyle` | | TypeName Integer, VarType 2, 0 |
| read `Label2.BorderStyle`, saved 1 | | 1 |
| `= 1` | 0 | 1 |
| `= 0` | 0 | 0 |
| `= 2`, `= -1` | 380 | 0 |
| `= 0.4`, `= 0.5` | 0 | 0 |
| `= 0.6` | 0 | 1 |
| `= 1.5`, `= 1.6` | 380 | 0 |
| `= "1"` | 0 | 1 |
| `= "x"` | 13 | 0 |
| `= True` | 380 | 0 |
| `= Empty` | 0 | 0 |
| `= Null` | 94 | 0 |
| `o.BorderStyle = 1`, with `o As Object` set to Label1 | 0 | 1 |
| a Label from `Controls.Add("VB.Label", "lblAdded")`: read | 0 | 0 |
| its `= 1` | 0 | 1 |
| its `= 2` | 380 | 1, unchanged |

- **The property is settable at run time,** early-bound, late-bound, and on an added Label.
- **It takes 0 and 1 only, as an Integer rounded half to even.** 0.5 is 0 and 1.5 is 2, which is 380. `"1"` is
  converted, and `"x"` is 13. True is -1, so 380. Empty is 0, and Null is 94.
- **A refused value leaves the property as it was.**
- **The same rounding as Alignment's,** on "A Label's Alignment at run time" (oracle PR #4613, #4604).

**What VB6 drew.** #4604's probe set `lblAdded.BorderStyle = 1` on an added Label, and its screenshot showed the
border. The border's pixels after a run-time change weren't compared with a saved Fixed Single's.

**HexIDE's own reading, found with it:** `Label1.BorderStyle = 1` raised 461 in a running program (#4647).
