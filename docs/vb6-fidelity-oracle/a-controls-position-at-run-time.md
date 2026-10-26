## A control's position at run time (2026-10-14, #933)

What unit does a running program read and write a control's Left, Top, Width and Height in, as what type, and how
are they rounded? Seven projects, each built with `/make` and run, native and p-code agreeing on every row. Each logs
`what = value TypeName`, from `Form_Load`. Form1 is ClientWidth 4680 and ClientHeight 3090, and holds a TextBox, a
Label and a CommandButton unless the table says otherwise.

**A twips form**, the default:

| Form1 does | VB6 |
|---|---|
| `Text1.Left`, `.Top`, `.Width`, `.Height` (.frm 120, 120, 1215, 495) | `120`, `120`, `1215`, `495`, each a Single |
| `Label1.Left` (.frm 240) | `240` Single |
| `Text1.Left = 1201`; `Label1.Left = 1201` | `1201`; `1201`: no snapping to whole pixels, windowed or not |
| `Text1.Width = 1001`; `Command1.Left = 7` | `1001`; `7` |
| `Text1.Left = 1.5`; `= 2.5` | `2`; `2`: whole twips, half to even |
| `Text1.Top = -30` | `-30` |
| `Label1.Width = 7`; `= 0`; `Label1.Height = 7` | `15` each |
| `Label1.Width = 20`; `= 22`; `= 30`; `= 1001` | `20`; `22`; `30`; `1001` |
| `Text1.Width = 7`; `Text1.Height = 7` | `150`; `285` |
| `Command1.Width = 7`; `= 0` | `75`; `75` |
| `Me.Width`, `Me.Height` (ClientWidth 4680, ClientHeight 3090) | `4800`, `3495`: the outer size |
| `Me.ScaleWidth`, `Me.ScaleHeight` | `4680`, `3090` |
| `Screen.TwipsPerPixelX` | `15` Single |

**A form whose .frm says `ScaleMode = 3 'Pixel`**, with its TextBox's `.frm` Left 8 and Width 81, and in a second
project a Frame at `.frm` Left 16 holding a TextBox at 240:

| Form1 does | VB6 |
|---|---|
| `Me.ScaleMode` (.frm `ScaleMode = 3`) | `3` Integer |
| `Text1.Left`, `Text1.Width` (.frm 8, 81) | `0.5333334`, `5.4`: the .frm held twips |
| `Text1.Left = 10.5`, then read | `10.53333` |
| then `Me.ScaleMode = 1`: `Text1.Left`, `Text1.Width` | `158`, `150` |
| `Me.Width`, `Me.Height`; `Me.ScaleWidth`, `Me.ScaleHeight` | `4800`, `3495`; `312`, `206` |
| `Me.Width = 6000`, then `Me.Width`, `Me.ScaleWidth` | `6000`, `392` |
| `Frame1.Left` (.frm 16) | `1.066667` |
| `Text2.Left`, `Text2.Width`, inside Frame1 (.frm 240, 60) | `240`, `60` |
| then `Me.ScaleMode = 1`: `Frame1.Left`, `Text2.Left` | `16`, `240` |

**A twips form holding a PictureBox whose .frm says `ScaleMode = 3`**, with a TextBox inside:

| Form1 does | VB6 |
|---|---|
| `Picture1.ScaleMode`; `Picture1.Left` (.frm 120) | `3` Integer; `120` |
| `Text3.Left`, `.Top`, `.Width` inside it (.frm 150, 75, 1215) | `10`, `5`, `81` |
| `Text3.Left = 20`, then `Picture1.ScaleMode = 1` and read | `300` |

**A twips form whose ScaleMode is changed at run time:**

| Form1 does | VB6 |
|---|---|
| `Me.ScaleMode = 2`: `Text1.Left`, `Text1.Width` (.frm 120, 1215) | `6`, `60.75` |
| `Me.ScaleMode = 5`: `Text1.Left` | `8.333334E-02` |
| `Me.ScaleMode = 7`: `Text1.Left` | `0.2116669` |
| `Me.ScaleMode = 6`: `Text1.Left` | `2.116669` |
| `Me.ScaleMode = 4`: `Text1.Left`, `Text1.Top` | `1`, `0.5` |
| `Me.ScaleMode = 2`, `Text1.Left = 10.3`, then `Me.ScaleMode = 1` and read | `206` |

What these show:
- **A .frm holds every position in twips, whatever the ScaleMode.** The obvious reading, that a pixel form's file
  holds pixels, is wrong: its `Left = 8` is 8 twips, which reads 0.5333334 pixels. So a reader that divides every
  position by 15 is right, and only the run time converts.
- **A program sees its container's ScaleMode.** A control on the form reads and writes in the form's, one inside a
  PictureBox in the PictureBox's, and one inside a Frame in twips: a Frame has no ScaleMode, and its children stay
  in twips even on a pixel form. The form's own Width and Height are twips whatever its ScaleMode, and are the outer
  size, frame included.
- **The value is a whole number of twips, read as a Single.** An assignment is rounded to whole twips, half to even,
  in whatever unit it was given: 10.5 pixels is 158 twips, 10.3 points 206. There is no snapping to whole pixels, for
  a windowed control or a Label.
- **The other ScaleModes are divisions of those twips:** 20 a point, 1440 an inch, 120 across and 240 down a
  character. Centimetres and millimetres resisted that: 120 twips read 0.2116669 cm and 2.116669 mm, where
  1440 / 2.54 twips a centimetre gives 0.2116667. Measured, and no rule found for it.
- **A Label is at least a pixel wide and high**, so 7 and 0 read 15, while 20 reads 20: a floor, not a snap. A
  TextBox and a CommandButton have floors of their own, 150 wide and 285 high for the TextBox and 75 wide for the
  button, which look like the Win32 controls' minimum sizes. No rule is defended for those.

### HexIDE

Before #933, a running program read a control's position in pixels, as a Double: `Text1.Left` from a .frm Left of
120 read 8, and `Text1.Left = 1200` moved it 1200 pixels. An assignment kept its fraction (#791).

Since #933 the conversion is at the property (`ControlPositions`). A position is read as whole twips divided by the
container's ScaleMode, as a Single, and an assignment is rounded to whole twips, half to even, checked against a
Long, and laid out in pixels at 15 twips a pixel. The container's ScaleMode is the form's from its .frm, a
PictureBox's from the line HexIDE preserves for it, and twips inside a Frame. A Label's one-pixel floor is applied.
Every row above that HexIDE can reach is pinned in `ControlPositionTests`, through a .frm read by a program. The
ScaleMode rows are pinned with the mode declared in the .frm, because HexIDE has no run-time ScaleMode (#990).

What it still does not do:
- **A run-time ScaleMode, and User scales** (#990). A User, centimetre or millimetre container refuses its controls'
  positions, naming #990, rather than guess.
- **The form's own size is the client size**, now in twips: VB6's 4800 is HexIDE's 4680, the frame being #852's, as
  is the form's own Left and Top.
- **A TextBox's and CommandButton's floors** (#1029): `Text1.Width = 7` reads 7, where VB6 gives 150.
- **Mouse X and Y**, `Move`, and the Scale* properties are not converted here: #974, and #990.


## A Timer's designer position (#2069)

Issue #2069 records the vb6.exe SP6 measurements made for #1556, with native and p-code agreeing.
Through `Dim o As Object`, reading `o.Left` or `o.Top` raises run-time error **393**, for both a Timer
saved in the form and one created with `Controls.Add`. The recorded description is
"'Left' property cannot be read at run time". Early-bound `Form1.Timer1.Left` fails compilation with
"Method or data member not found". A Timer's designer position is not a readable run-time position.
Writes were not measured.
