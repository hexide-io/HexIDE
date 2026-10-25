## A form-based game's runtime surface (2026-10-12)

Measured to decide what a form front end for `demo/battleship` may rely on. Real `vb6.exe` 6.00.8176 (the original release).
One Standard EXE, `Startup="Form1"`, with a `.bas` (`Public Const K = 5`, `Public G As Long`, a logging
`Sub L` that opens `App.Path & "\log.txt"` For Append per line so nothing is lost to buffering) and a `Source`
class raising `Event ResultV(ByVal r As Integer)` and `Event ResultR(r As Integer)`. Form1 holds `Label1`
(`.frm` Left 240, Top 360), a Label array `lblCell(0)`/`lblCell(1)` (BackColor white), `lblStatus`, `Check1`,
`List1`, `Command1` (no `Style` line, BackColor `&H000000FF&`), `Command2` (`Style = 1 'Graphical`, same
BackColor), `Picture1` and a 1000 ms `Timer1`. `Form_Load` runs every probe under `On Error Resume Next` and
logs `Err.Number`; tick 1 of the timer sends synthetic mouse messages and reads pixels with
`GetDC`/`GetPixel` on each window's own DC; tick 3 does `Unload Me`.

`/make` ran headless over PowerShell Direct. The **exe ran in the guest's interactive console session**
(through the long-lived agent's `exec:` action described above), because painting, `Form_Activate` and
`Form_Paint` need a real desktop; a screenshot taken mid-run confirms the pixel rows visually.

| Probe | `vb6.exe` result |
|---|---|
| `Label1.Left` (`.frm` 240) / `TypeName` | `240` / **`Single`** |
| `Label1.Top` (`.frm` 360) | `360` |
| `Label1.Left = 1200`, then read | `1200` — read and written in **twips** at the default ScaleMode |
| `Me.ScaleMode = 3`, then `Label1.Left` / `Me.ScaleWidth` | `80` / `400` — pixels once ScaleMode is pixels |
| `Screen.TwipsPerPixelX` | `15` |
| `Label1.Move 0, 0` | Err 0, Left `0`, Top `0` |
| `lblCell(0).BackColor` read, never written (`.frm` `&H00FFFFFF&`) | `16777215` / **`Long`** |
| `lblCell(0).BackColor = vbRed`, then `If lblCell(0).BackColor = vbRed` | **`eq`**, Err 0 |
| ...and on screen (form DC inside the label) | `GetPixel` = `FF` — **red is painted** (a Label is windowless; VB draws it on the form) |
| `lblCell(0).Caption = "changed"` + `BackColor = vbRed`, then `Load lblCell(5)` | lblCell(5) Caption **`changed`**, BackColor **`255`**, Visible `False`, Left `840` |
| `lblCell(1) = "D"` (default member) | Caption `D`; `TypeName(lblCell(1))` still **`Label`** |
| `lblcell(0).caption = "x"`, read `LBLCELL(0).CAPTION` (Option Explicit) | compiles; `x` |
| `Dim Abc As Integer`, `abc = 1`, read `ABC` / `TypeName(ABC)` (Option Explicit) | compiles; `1` / `Integer` |
| `Controls.Count` / `Controls("lblStatus").Caption` | `11` / `Ready` |
| `For Each c In lblCell` (elements 0, 1, 5) | 3 iterations, Err 0 |
| `For i = lblCell.LBound To lblCell.UBound`, `lblCell(i).Tag = "t"` | i = 0, 1, 5 Err 0; i = 2, 3, 4 **Err 340** |
| `Module1.K` / unqualified `K` from the form | `5` / `5` |
| `Module1.G = 8` (qualified write) | Err 0, `Module1.G` = 8 |
| `G = 7` (unqualified write, Option Explicit) | Err 0, `Module1.G` = 7 — it IS the module's variable |
| `Private Const N = 10` + `Private a(0 To N - 1) As Integer` (form level) | compiles; `a(9) = 3` works, `UBound(a)` = 9 |
| bare `Print "hello"` in `Form_Load` | compiles, Err 0 |
| `Check1.Value = 1` | **`Check1_Click` fires once** (Value already 1 inside it) |
| `Check1.Value = 1` again | no Click |
| `List1.ListIndex = 2` | **`List1_Click` fires**, ListIndex 2 inside it |
| `List1.AddItem "z", 0` (after a, b, c with c selected) | `List(0)` = `z`, ListCount 4, **ListIndex 3** — the selection follows its item |
| form `Private WithEvents mSrc As Source`, `RaiseEvent ResultV(x)` with x = 3, handler sets `r = 99` | handler got `3`; raiser's `x` still **3** |
| same, `ResultR(x)` (ByRef) | handler got `3`; raiser's `x` **99** |
| `Private Static Sub Bump()` with `Dim n As Integer: n = n + 1`, called twice | `1`, `2` — a Static **Sub** makes its locals static too |
| `Randomize 42: r1 = Rnd: Randomize 42: r2 = Rnd` | `0.3391077`, `0.2562481` — **not** equal |
| `r = Rnd(-1): Randomize 42: r1 = Rnd`, twice | `0.9078093` both times — only `Rnd(-1)` first makes it repeat |
| `App.Path` | `C:\zzprobe\main` — no trailing separator |
| `lblCell(5).Index` | `5` |
| `Command1.Style` / `Command1.BackColor` / `Command2.Style` | `0` / `255` / `1` |
| `Command1` (Style 0) pixels at (6,6) and (20,20) | **`F0F0F0`** — button face; BackColor is **not painted** |
| `Command2` (Style 1) pixels at (6,6) and (20,20) | **`FF`** — red |
| `WM_LBUTTONDOWN`/`UP` sent to `Picture1` at client (10, 5) px | `MouseDown Button=1 Shift=0 X=150 Y=75`, `MouseUp X=150 Y=75` — **twips**, the container's ScaleMode |
| `WM_RBUTTONDOWN`/`UP` at the same point | `MouseDown Button=2 ...` |
| event order on show | `Form_Initialize`, `Form_Load`, `Form_Resize`, `Form_Activate`, `Form_Paint` |
| `Unload Me` from the timer | `Form_QueryUnload` UnloadMode **1** (vbFormCode), `Form_Unload`, `Form_Terminate`; the program ends |

A compile-only pair settled one more question. A class whose method calls a form's `Public Sub ShowResult`
**unqualified** fails `/make`: `Compile Error in File '...\Class1.cls', Line 3 : Sub or Function not defined`
(Line 3 is the call, 0-based in the code view, as the section on `/out` describes). `Form1.ShowResult 1` builds.

A Single's display width was measured with `scripts/vb6-oracle.ps1` (the `CByte` sanity pair reproduced
first): `CStr(CSng(0.76414126))` = `0.7641413`, `CSng(0.76414126)` = `0.7641413 | Single`,
`CStr(CSng(1) / 3)` = `0.3333333`, `CStr(CSng(0.1) + CSng(0.2))` = `0.3`, `CStr(CSng(123456.78))` =
`123456.8`. **Seven significant digits.** The full rule, with where E notation starts, how a tie rounds,
and the other types, is in [Turning a number or a date into text](turning-a-number-or-a-date-into-text.md#turning-a-number-or-a-date-into-text-2026-10-12-772-771).

### What this overturned

- **Load clones runtime state, not the `.frm`.** The control-arrays section above says `Load` clones "the
  lowest-index element's properties", measured on an element nobody had changed. With a changed element the
  answer is its *current* Caption and BackColor. Only `Visible` is forced (to `False`).
- **`Randomize n` is not a restart.** The plausible reading of `Randomize 42` is "seed the generator with
  42". The same argument twice gave two different draws, and only `Rnd(-1)` beforehand made it repeat —
  consistent with the argument being folded into part of the *current* seed rather than replacing it (the
  mechanism is inferred; the two rows above are what was measured). The oracle's Rnd row already said
  HexIDE's fold is not bit-identical; this is a different, larger point: in HexIDE `Randomize 42` twice gives
  the *same* draw.
- **A Style 0 CommandButton ignores BackColor on screen while still reporting it.** `Command1.BackColor`
  reads 255 and the button paints button-face grey. The value is stored, not drawn.

### What resisted explanation

The synthetic `WM_LBUTTONDOWN`/`WM_LBUTTONUP` pair raised `MouseDown` and `MouseUp` but **no
`Picture1_Click`**. A PictureBox does have a Click event, so this is most likely about how VB6 synthesises
Click (capture, or the cursor's real position) rather than about Click itself — but that is a guess, and no
real-input click was measured here. It means this probe says nothing either way about whether a
right-button release raises Click.

### Two harness lessons

- `Bump: Bump: Bump` on one line made **two** calls, not three: a leading `Bump:` is a **line label**. The
  probe logged `n=1`, `n=2`, which looked at first like a missing call.
- A fixed `Start-Sleep` before the agent's screenshot missed the window entirely (the exe took several seconds
  to appear). Poll for a non-zero `MainWindowHandle` first, then shoot.


