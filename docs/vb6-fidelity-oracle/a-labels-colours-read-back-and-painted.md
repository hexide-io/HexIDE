## A Label's colours, read back and painted (2026-10-12, #795)

Measured to decide what a Label and a TextBox should read back and paint. Real `vb6.exe` 6.00.8176 (the original release), guest
locale en-GB. Two `/make` runs of a form whose own BackColor is light yellow, `&H00C0FFFF&`, so a transparent
label shows. Each run was made twice:
- once in session 0, which logs the read-backs, and where `GetPixel` returns `&HFFFFFFFF` for every point,
  because nothing is drawn there;
- once in the guest's interactive console session, through the agent described in "Reaching the VB6 *IDE*, not just the compiler" above.

In the console session, a timer reads `GetPixel` on the form's own DC 1.5 seconds after load, at a point inside
each label clear of its caption, and at the first three pixels of its left edge. A screenshot of the same moment
agrees with every pixel row.

**Defaults and read-backs:**

| Expression | Value | Type |
|---|---|---|
| `Text1.BackColor` / `Text1.ForeColor`, nothing set | `-2147483643` / `-2147483640` (`&H80000005` / `&H80000008`) | Long |
| `Label1.BackColor` / `Label1.ForeColor`, nothing set | `-2147483633` / `-2147483630` (`&H8000000F` / `&H80000012`) | Long |
| A transparent label's `BackColor` from the form (`&H000000FF&`) | `255` | Long |
| `LblRunBlue.BackColor = vbBlue`, then read | `16711680` | Long |
| `Arr(1).BackColor = vbGreen`, a control-array label; then `Arr(0).BackColor` | `65280`; `-2147483633`, unchanged | Long |
| `LblRunBlue.BackColor = vbBlue` compared with `vbBlue` | `True` | Boolean |
| `LblOpaqueRed.ForeColor = vbWhite`, then read | `16777215` | Long |

**What paints** (`GetPixel` is `&H00BBGGRR`):

| Label | Pixel | Shows |
|---|---|---|
| opaque, `BackColor` red from the form | `&HFF` | red |
| transparent, `BackColor` red from the form | `&HC0FFFF` | the form |
| opaque, `BackColor = vbBlue` at run time | `&HFF0000` | blue |
| transparent from the form, `BackStyle = 1` at run time, `BackColor` green from the form | `&HFF00` | green |
| control array: `Arr(1).BackColor = vbGreen`; `Arr(0)` untouched | `&HFF00`; `&HF0F0F0` | green; the button face |
| `ForeColor = vbWhite` at run time | white text in the screenshot | |

**A transparent label drops a `BackColor` set at run time.** No error, and nothing changes:

| Statements, on a transparent label whose `BackColor` is red from the form | `BackColor` reads | Pixel |
|---|---|---|
| `BackColor = vbBlue` | `255`, unchanged | the form |
| then `BackStyle = 1` | `255` | `&HFF`, red |
| `BackStyle = 1` first, then `BackColor = vbBlue` | `16711680` | `&HFF0000`, blue |

The same on a label with the default colour: after `BackColor = vbBlue` while transparent, it still reads
`-2147483633`, and its `BackStyle` is still 0.

**Setting `Appearance` at run time resets `BackColor`.** VB6 accepts the assignment, with no error, and it
replaces `BackColor` with that appearance's default. The border is redrawn to match.

| Label, `BackColor` white and `BorderStyle` 1 from the form | `BackColor` after | Left edge, pixels 0, 1, 2 |
|---|---|---|
| `Appearance` 0 (flat), untouched | white | `&H646464`, `&HFFFFFF`, `&HFFFFFF`: one dark line |
| `Appearance` 1 (3D), untouched | white | `&HA0A0A0`, `&H696969`, `&HFFFFFF`: sunken |
| flat, then `Appearance = 1` | `-2147483633`, the button face | `&HA0A0A0`, `&H696969`, `&HF0F0F0`: sunken |
| 3D, then `Appearance = 0` | `-2147483643`, the window colour | `&H646464`, `&HFFFFFF`, `&HFFFFFF`: flat |

### What this overturned, or found

- **A transparent label drops a run-time `BackColor`; it does not store it for later.** Our own expectation, going
  in, was that the colour would be stored and simply not painted until the label became opaque. The probe row was
  written to read it back blue. It read back the old colour, measured twice: on a colour from the form, and on the
  default. This rule is easy to "fix" by accident.
- **`Appearance` is not a pure border change.** It was probed only because the first run's flat label, made 3D at
  run time, painted the button face rather than the white the form gave it. Our expectation had been a new border
  around the same white. The second run confirmed the reset in both directions, with the read-back to match.

### HexIDE, after #795

- A Label's `BackColor` binds to `VBLabel.BackColor`, the property `VBLabel.Render` paints. A run-time assignment
  used to write the attached property every other control's template draws, so it read back but never painted.
- `ForeColor` loads through the attached property, so it reads back and repaints.
- TextBox models `BackColor` and `ForeColor`, with the defaults above, and loads them. They were not modelled,
  so a form's text box colours were lost.
- A colour reads as its Long (#789).
- A transparent label drops a run-time `BackColor`, as above.
- `LabelColourTests` pins the read-backs and reads the painting from a real Skia frame, a control-array label
  among them.
- A Label's `BackStyle` can be set at run time, so both orders in the table above behave as measured, and
  `BackStyle = 2` is 380 (measured, below).
- Since #796, setting a Label's `Appearance` at run time resets `BackColor` to the measured default above
  and redraws the border. `LabelColourTests` checks both transitions, read-back and painted pixels.

### Measured with it: defaults, BackStyle 2, and a system colour past the palette (2026-10-12, #802)

One more `/make` run on the same guest, session 0, nothing set before each read:

| Expression | Value | Type | Err |
|---|---|---|---|
| `List1.ForeColor` / `List1.BackColor` | `-2147483640` / `-2147483643` (`&H80000008` / `&H80000005`) | Long | |
| `Combo1.ForeColor` / `Combo1.BackColor` | `-2147483640` / `-2147483643` | Long | |
| `LblT.BackStyle = 2`, on a transparent label; then `BackStyle` | `0`, unchanged | Integer | 380 |
| `LblT.BackStyle = 1`, then `LblT.BackColor = vbBlue` | `16711680` | Long | |
| `LblBad.BackColor = &H80000019` (past the last system colour) | `-2147483633`, unchanged | Long | 380 |

HexIDE had a ListBox's and ComboBox's `ForeColor` default as the button text (`&H80000012`), every other
control's. It now overrides it to `&H80000008`. A `.frm` carrying `&H80000019&`, or a palette colour, is now kept
as written and saved back, where it used to be dropped on save; creating a control with the first used to throw.


### Re-measured on SP6, with a game's order (2026-11-19, #4965)

The rows above are from the original release, 6.00.8176. These are from VB6 SP6 6.00.9782 with MSVBVM60 6.00.9848.
The runs were `/make` as native code and as p-code, in session 0, reading back only. Both builds logged the same lines.

| Label, then statements | `BackColor` reads | `BackStyle` |
|---|---|---|
| A: transparent from the form, red from the form; `BackColor = vbBlue` | `255`, unchanged, no error | 0 |
| A, then `BackStyle = 1` | `255` | 1 |
| B: opaque red from the form; `BackStyle = 0` | `255` | 0 |
| B, then `BackColor = vbBlue` | `255`, unchanged | 0 |
| B, then `BackStyle = 1` | `255` | 1 |
| B, then `BackColor = vbGreen`, now opaque | `65280` | 1 |
| C: transparent from the form; `BackStyle = 1`, then `BackColor = vbBlue` | `16711680` | 1 |
| D: `lblCell(0)` of a control array, transparent, `&H00F5E1C8&` from the form; `BackColor = &HC0C0FF`, then `BackStyle = 1` | `16114120` (`&HF5E1C8`), unchanged | 1 |
| D, then `BackStyle = 0`, `BackColor = &HC0FFC0`, `BackStyle = 1` | `16114120`, unchanged | 1 |

**SP6 drops a run-time `BackColor` on a transparent Label too.** That holds for one made transparent at run time (B),
and for a control-array element (D).

D is the order a game's grid used to fill a cell: the colour first, then opaque. Every cell filled that way showed the
colour the `.frm` gave it, not the one assigned. A cell already opaque when the next colour came took it. So a
preview drawn over transparent cells mixed the right colour with the `.frm`'s pale blue, depending on which cells had
been opaque before (#4965). Setting `BackStyle = 1` first, as row C does, gives the colour.

**Dropped, not deferred.** `BackColor` read straight after the assignment returns the old value (rows A and B), and
the later `BackStyle = 1` shows that old value. So the assignment is lost; it isn't stored to be drawn later.

**Is it a defect HexIDE should not copy?** The question was asked, since the Fidelity Principle keeps VB6's intended
behaviour, not its bugs. The answer is to keep it, reasoned rather than measured:
- **A program can see it.** The value reads back unchanged. That's unlike the Object Browser example, where VB6's
  window chrome was lost and no program could tell.
- **It is stable.** 6.00.8176 and SP6 do the same.
- **Programs were written against it.** A VB6 author who filled a transparent Label colour-first saw the old colour and
  reordered. A program that reads the colour back expects the old one.

Taking the colour would change what such programs show and read. HexIDE has dropped it since #795, and still does.
