## What VB6's Properties window shows a position in (2026-10-23, #1636, design-time, IDE)

**The question.** HexIDE's Properties window showed every Left, Top, Width and Height as the model's pixels: a new
PropertyPage read Height 240 and Width 320 where VB6's read 3600 and 4800. What unit does VB6's window use? The run time
reads a position in its container's ScaleMode (#933). Does the window do the same, and what does it show for a form's own
size, which at run time includes its frame (#1072)?

**The probes.** VB6.EXE 6.00.8176, the IDE, in the guest's console session through the agent, the same rig as #1585's.
Each project was opened in the IDE; each form's designer was opened from the Project window (Ctrl+R, Home, Down, then
Right twice, which reaches the first form whether or not the Forms folder is expanded, and Down for the next); then F4.
For each object in the Properties window's object box, the object was selected as a user's choice there selects it:
`CB_SETCURSEL` to its index, then `WM_COMMAND` with `CBN_SELCHANGE` to the box's parent. The list was then captured as
#1585's was, scrolled with `LB_SETTOPINDEX`, eight rows at a time, on its Alphabetic tab. Every form had the same client
rectangle in its file: `ClientLeft = 60`, `ClientTop = 450`, `ClientWidth = 4680` and `ClientHeight = 3090`.

**A control's position, in its container's ScaleMode.** Two forms. Form1's ScaleMode is twips, and it holds Text1 and a
PictureBox whose ScaleMode is 3, pixels, holding Text3. Form2's ScaleMode is pixels, and it holds Text5, and a Frame
holding Text6. The file's values are twips in every case:

| Object | In | File's Left, Top, Width, Height | Properties window |
|---|---|---|---|
| Text1 | a twips form | 120, 120, 1215, 495 | 120, 120, 1215, 495 |
| Picture1 | a twips form | 120, 840, 2400, 1500 | 120, 840, 2400, 1500 |
| Text3 | a pixel PictureBox | 150, 75, 1215, 285 | **10, 5, 81, 19** |
| Text5 | a pixel form | 120, 120, 1215, 495 | **8, 8, 81, 33** |
| Frame2 | a pixel form | 2640, 840, 1800, 1500 | **176, 56, 120, 100** |
| Text6 | a Frame on a pixel form | 240, 360, 1215, 285 | 240, 360, 1215, 285 |

**The other scales.** Each form below holds Text1 at 1200, 600, 1215, 495 in twips. The User form's ScaleLeft is 100,
ScaleTop 50, ScaleWidth 1000 and ScaleHeight 500. The PictureBox is 2400 by 1500 twips, has a User scale of ScaleLeft 10,
ScaleTop 5, ScaleWidth 1000 and ScaleHeight 500, and holds Text1 at 300, 150, 1215, 285. The About Dialog row is VB6's own
`About Dialog.frm` from its Template tree: a User scale of 5380.766 by 2453.724 over a 5730 by 3555 client, with
`cmdOK` at 4245, 2625, 1260, 345.

| Container | Properties window |
|---|---|
| a User-scale form | 356.41, 147.087, 259.615, 80.097 |
| a centimetre form | 2.117, 1.058, 2.143, 0.873 |
| a millimetre form | 21.167, 10.583, 21.431, 8.731 |
| VB6's About Dialog, `cmdOK` | 3986.274, 1811.822, 1183.205, 238.125 |
| a User-scale PictureBox, the default 3D Fixed Single border | 138.205, 57.083, 519.231, 98.958 |
| the same with `BorderStyle = 0` | 135, 55, 506.25, 95 |

**A form's own position.** One form for each BorderStyle, one Fixed Single with no control box and no caption, and one
with a menu:

| Form | Left, Top | Width, Height |
|---|---|---|
| 2 Sizable (both forms above, whatever their ScaleMode) | -60, -15 | 4920, 3675 |
| 0 None | 60, 450 | 4680, 3090 |
| 1 Fixed Single | 15, 60 | 4770, 3525 |
| 3 Fixed Dialog | 15, 60 | 4770, 3525 |
| 4 Fixed ToolWindow | 15, 60 | 4770, 3525 |
| 5 Sizable ToolWindow | -60, -15 | 4920, 3675 |
| 1 Fixed Single, `ControlBox = 0`, `Caption = ""` | 45, 435 | 4710, 3120 |
| 2 Sizable, with a menu | **105, 105** | 4920, 3975 |

**What it shows:**
- **A control's position is in its container's ScaleMode**, the rule a running program reads it by (#933). It is the
  form's for a control on the form, a PictureBox's inside it, and twips inside a Frame, which has no ScaleMode, even on a
  pixel form. A Frame's own position is in its form's ScaleMode.
- **A User scale maps the container's client area onto ScaleLeft, ScaleTop, ScaleWidth and ScaleHeight.** Text1's 1200
  twips is 100 + 1200 × 1000 / 4680 = 356.41, and its width 1215 × 1000 / 4680 = 259.615. A PictureBox's client area is
  inside its border. The default border takes 60 twips off each way: 300 twips is 10 + 300 × 1000 / 2340 = 138.205.
  BorderStyle 0 takes nothing: 10 + 300 × 1000 / 2400 = 135.
- **Centimetres and millimetres are 1440 / 2.54 and 1440 / 25.4 twips to the unit** at the window's precision. The run
  time's centimetre reading, 0.2116669 for 120 twips where that factor gives 0.2116667 (#933), differs below it.
- **A value that isn't whole shows three decimals at most**: 356.41, 147.087, 3986.274. Not seven significant digits, as
  a Single prints.
- **A form's own Width and Height are twips whatever its ScaleMode, and are its client size plus its frame.** That frame
  is the one a running form has on the themed desktop (`FormFrame`, #1072): 240 across and 585 down for a Sizable form or
  a Sizable ToolWindow; 90 and 435 for the fixed styles; 30 and 30 for a Fixed Single with no caption bar; none for
  None; and 300 more down for a menu. "Me.Width and Me.Height" earlier in this file read 4800 and 3495 for this client
  area, 120 and 405 more. That is not a contradiction: that row is a session-0 EXE, which is unthemed, as `FormFrame`
  records.
- **A form's Left and Top are its client's less the frame's left and top edges**: ClientLeft less half the border across,
  and ClientTop less the rest of the frame down. For a Sizable form that is 60 − 120 = −60 and 450 − 465 = −15. Nine of the
  ten forms fit.
- **Measured and not explained: the form with a menu read Left 105 and Top 105**, where that rule gives −60 and −315. Its
  Width and Height fit the frame. One guess is that VB6 places the designer window itself when the form's own Top would
  put its menu above the designer's area. It was not measured.
- A UserControl's Width and Height, in #1585's capture, are its client area, 4800 and 3600, since it has no frame.

**Not measured:** whether a value typed into the window is rounded to whole twips, as an assignment at run time is.

### In HexIDE (#1636)

- **The Properties window shows each position as VB6's does**, through `DesignPositions`:
  - a control's in its container's ScaleMode, User, centimetres and millimetres included;
  - a form's own size with the frame `FormFrame` gives a running form;
  - a UserControl's and PropertyPage's with none;
  - three decimals at most.

  A value typed in is in the same units, and is rounded to whole twips, half to even, as the run time rounds an
  assignment.
- **`get_form_controls`' rows and `set_control_property` follow the window**, as #923 has them, and each row's note
  names its unit. The summary's `left`, `top`, `width` and `height`, `add_control` and `move_control` stay in pixels, and
  say so.
- **Not followed: the menu form's Left and Top.** HexIDE shows the rule's −60 and −315.
- **Not followed: the toolbar's position and size readout**, which still shows pixels. VB6's wasn't measured (#1641).

