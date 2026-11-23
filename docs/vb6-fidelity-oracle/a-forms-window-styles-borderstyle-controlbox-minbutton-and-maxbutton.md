## A form's window styles: BorderStyle, ControlBox, MinButton and MaxButton (2026-10-23, #1575)

**The question.** A running form was a sizable window with a title bar and every button, whatever its file said. What
window does vb6.exe make for each combination of BorderStyle, ControlBox, Caption, MinButton, MaxButton, ShowInTaskbar
and a menu?

**The probe.** One project of 20 forms built by `/make`, native and p-code, in session 0 on the Windows 10 guest. Sub
Main loads and shows each form in turn and reads:
- `GetWindowLong(hWnd, GWL_STYLE)` and `GWL_EXSTYLE`;
- the form's own MinButton, MaxButton, ControlBox, ShowInTaskbar and BorderStyle;
- `WM_GETICON` for the small and big icons, and the class's icons.

A window's styles are set as it is made, whatever desktop it is on, so session 0 answers this.
One bit is about the desktop: a shown form's `WS_VISIBLE` is set in session 0 and on the console alike, and only
`IsWindowVisible` differs, 0 in session 0 and 1 on the console (re-run 2026-11-18, #4768). **Native and p-code
agree on every row.**

| Form | The file says | Style | Extended style |
|---|---|---|---|
| B0 | BorderStyle 0 | `16000000` | `00040000` |
| B0m | BorderStyle 0, a menu | `16CB0000` | `00040100` |
| B1 | BorderStyle 1 | `16CB0000` | `00040100` |
| B1x0 | 1, MinButton and MaxButton False | `16C80000` | `00040100` |
| B1x1 | 1, MinButton and MaxButton True | `16CB0000` | `00040100` |
| B2 | nothing (Sizable) | `16CF0000` | `00040100` |
| B2n0 | MinButton False | `16CD0000` | `00040100` |
| B2a0 | MaxButton False | `16CE0000` | `00040100` |
| B2c0 | ControlBox False | `16C70000` | `00040100` |
| B2c0e | ControlBox False, Caption empty | `16870000` | `00040100` |
| B2c0x1 | ControlBox False, MinButton and MaxButton True | `16C70000` | `00040100` |
| B3 | BorderStyle 3 | `16C80080` | `00040101` |
| B3x1 | 3, MinButton and MaxButton True | `16C80080` | `00040101` |
| B4 | BorderStyle 4 | `16CB0000` | `00040180` |
| B5 | BorderStyle 5 | `16CF0000` | `00040180` |
| B5x1 | 5, MinButton and MaxButton True | `16CF0000` | `00040180` |
| B1c0e | 1, ControlBox False, Caption empty | `16830000` | `00040000` |
| B2t0 | ShowInTaskbar False | `16CF0000` | `00000100` |
| B3t1 | 3, ShowInTaskbar True | `16C80080` | `00040101` |
| B4t1 | 4, ShowInTaskbar True | `16CB0000` | `00040180` |

Every form read its properties back as the file gave them, and all four are True where the file leaves them out, for
every BorderStyle: B3 and B4 read ShowInTaskbar True, and B3 reads MinButton and MaxButton True although its window has
neither box.

**Decoded.** The style's `16000000` is the visible, clip-siblings and clip-children bits every form has. Above them:
`00800000` border, `00400000` dialog frame (the two together are the caption), `00080000` system menu, `00040000`
resizing frame, `00020000` minimize box, `00010000` maximize box, and in the low word `00000080`. The extended style's
`00040000` is the taskbar's, `00000100` the window edge, `00000080` the tool window's and `00000001` the dialog modal
frame. So:
- **None has no frame at all**, and a menu gives it Fixed Single's window (B0m reads as B1).
- **Every other style has a border, and a caption** unless ControlBox is False and the Caption empty.
- **The resizing frame goes with the two sizable styles**, 2 and 5.
- **The system menu goes with ControlBox**, and so the close box does.
- **The minimize and maximize boxes follow MinButton and MaxButton**, on the tool windows too, and with ControlBox False:
  B2c0 has both and no system menu.
- **A Fixed Dialog never has either box**, whatever the file says, and has `00000080` and the dialog modal frame
  instead.
- **The two tool windows have the tool-window bit.**
- **ShowInTaskbar is the taskbar bit and nothing else** (B2t0).
- **The window edge** is there wherever there is a caption or a resizing frame, and not on B0 or B1c0e.

**Every form has a big icon and no small one**, Fixed Dialog included, and its class has neither.

**What resisted explanation.** `00000080` in the style's low word is `DS_MODALFRAME` on a dialog's class. What it means
on a form's, which is not a dialog class, is not established; it is replicated as read.

**In HexIDE (#1575):** `FormChrome` works the styles out from the file, once, as the window is made. On Windows they
reach the window through Avalonia's style callback, which replaces only the frame bits above and the tool-window and
dialog-modal-frame bits. Avalonia keeps the rest: visibility, clipping, the window's state, the taskbar bit from
ShowInTaskbar, and the window edge, which Windows sets itself. On other hosts the same rules drive Avalonia's own
CanResize, WindowDecorations, CanMinimize and CanMaximize.

**Read back from HexIDE's own windows** on 2026-10-23, with `GetWindowLong` on the running forms of a nine-form project
(B0, B1, B1x0, B2c0, B2c0e, B3x1, B4, B5, B1c0e) and its Sizable Form1: every style was the table's, and every client
area was the file's 2520 × 1500 twips. The extended styles matched too, apart from Avalonia's no-redirection-bitmap bit,
`00200000`. A screen capture of the same windows showed:
- no frame on B0;
- no icon and no buttons where there is no system menu;
- a small caption with a close box alone on the tool windows;
- a close box alone on the Fixed Dialog;
- a border alone where there is no caption.

`WM_SYSCOMMAND SC_MAXIMIZE`, what the maximize box sends, maximized B1, whose file leaves MaxButton out.

**Not followed:** HexIDE's windows carried both a small and a big icon, Avalonia's own, whatever the form's Icon. #1594
measured what VB6 does instead: "A form's window icon", below. Off Windows, a Fixed Single form whose
MaxButton is True has no maximize box, because Avalonia allows one only on a window it can resize; on Windows the box is
drawn and maximizes. Nor does Avalonia have a tool window's caption, or a caption bar with no close box.

**Not measured:**
- What Windows draws for these styles on the console, against vb6.exe's own window. Above, only HexIDE's windows were
  seen.
- That a Fixed Dialog's caption shows no icon because there is no small icon. That was the usual account, and #1594
  measured it wrong: "A form's window icon", below.
- That the menu keeps the caption bar on a borderless form whose ControlBox is False and whose Caption is empty, as
  #1072's frame has always taken it to. B0m had both.
- What VB6's designer sets when BorderStyle changes
  ([#1596](https://github.com/hexide-io/HexIDE/issues/1596)).
- A Caption or ControlBox changed at run time. HexIDE keeps the window the file gave it, as it keeps the frame.


## What VB6's designer changes when a form's BorderStyle changes (2026-11-21, #1596, design-time, IDE, SP6)

**The question.** The section above measured what vb6.exe does with each combination once it is in the file. Does VB6's
designer change MinButton, MaxButton, ControlBox or ShowInTaskbar when BorderStyle is changed in the Properties
window? And does a value set by hand survive a later change of style?

**The probe.** Form1 from a hand-written `.frm` that sets only the Sizable defaults, opened in VB6's designer on the
guest's console (VB6.EXE 6.00.9782, SP6). Each step clicked the BorderStyle row's value cell in the Properties window,
typed the value's number, pressed Return, then saved with Ctrl+S, and the saved `.frm` was read back. Caption, the
client size, LinkTopic, the Scale lines and StartUpPosition never changed, and are left out below. A line VB6 left out
is the default, True.

**From Sizable, to each style and back:**

| Changed to | Lines the save wrote |
|---|---|
| 3 Fixed Dialog | `BorderStyle = 3`, `ClientLeft = -15`, `ClientTop = 270`, `MaxButton = 0`, `MinButton = 0`, `ShowInTaskbar = 0` |
| 1 Fixed Single | `BorderStyle = 1`, `ClientLeft = -15`, `ClientTop = 270`, `MaxButton = 0`, `MinButton = 0` |
| 0 None | `BorderStyle = 0`, `ClientLeft = -60`, `ClientTop = -120`, `ShowInTaskbar = 0` |
| 4 Fixed ToolWindow | `BorderStyle = 4`, `ClientLeft = -15`, `ClientTop = 270`, `MaxButton = 0`, `MinButton = 0`, `ShowInTaskbar = 0` |
| 5 Sizable ToolWindow | `BorderStyle = 5`, `ClientLeft = 60`, `ClientTop = 345`, `MaxButton = 0`, `MinButton = 0`, `ShowInTaskbar = 0` |
| 2 Sizable, from each of these | `ClientLeft = 60`, `ClientTop = 345`, and nothing else |

**With values set by hand first:**

| Before | Set by hand | Then chose | Saved |
|---|---|---|---|
| Sizable | ControlBox, MinButton and ShowInTaskbar False | Fixed Single | `ControlBox = 0`, `MaxButton = 0`, `MinButton = 0`; ShowInTaskbar back to True |
| that | | Sizable | `ControlBox = 0` only; MinButton back to True |
| Fixed Dialog | MaxButton True, which drops its line | Fixed Single | `MaxButton = 0` again |
| Fixed Dialog, with ControlBox False | | None | `MaxButton = 0`, `MinButton = 0`, `ShowInTaskbar = 0` |
| Sizable | MinButton False | None | `MinButton = 0`, `ShowInTaskbar = 0`; MaxButton stays True |
| Sizable | MinButton False | Sizable again | `MinButton = 0`, kept |
| Fixed Single | ShowInTaskbar False | Fixed Single again | `ShowInTaskbar = 0`, kept |

ControlBox, set False by hand at the start, stayed False through every change. The Properties window showed what the
file said wherever it was looked at: MaxButton False at Fixed Dialog, and True once set by hand.

**So choosing a different BorderStyle sets three properties to fixed values for the new style, whatever they were
before:**

| BorderStyle | MinButton | MaxButton | ShowInTaskbar |
|---|---|---|---|
| 0 None | unchanged | unchanged | False |
| 1 Fixed Single | False | False | True |
| 2 Sizable | True | True | True |
| 3 Fixed Dialog | False | False | False |
| 4 Fixed ToolWindow | False | False | False |
| 5 Sizable ToolWindow | False | False | False |

- **ControlBox is never changed.**
- **Choosing the style the form already has changes nothing,** so a value set by hand survives that.
- **A value set by hand doesn't survive a change to another style.** Each of the three is written again from this
  table, except None's buttons.
- **None leaves MinButton and MaxButton alone.** It was measured from both buttons False (after Fixed Dialog) and from
  MinButton False with MaxButton True.
- **ClientLeft and ClientTop change with the style too.** The client size stays, and the saved client origin moves:
  -15, 270 for Fixed Single, Fixed Dialog and Fixed ToolWindow; -60, -120 for None; 60, 345 for Sizable and Sizable
  ToolWindow. These are the values measured, not a rule for other frames.

**What was overturned.** #1596 gave the common account: Fixed Dialog sets MinButton, MaxButton and ShowInTaskbar
False, and the tool-window styles set ShowInTaskbar False. That much is right, but it's incomplete:
- Fixed Single sets both buttons False as well.
- None sets ShowInTaskbar False.
- The tool windows set the buttons False too.
- Sizable sets all three back to True, and Fixed Single sets ShowInTaskbar back to True.

So a form that goes to Fixed Dialog and back comes back as it started, and a value changed by hand in between is lost.

### Not measured

- **The Properties window at every step.** Only the MaxButton rows above were looked at; the rest is the saved file.
- **A form that is an MDI child,** or one with a menu.
- **The designer at a DPI other than 96,** which would move the ClientLeft and ClientTop values.
## Where the client origin goes when a form's BorderStyle changes (2026-11-22, #1596, design-time, IDE, SP6)

**The question.** The section above saw ClientLeft and ClientTop change with the style. From the one starting point it
measured, (60, 345), the fixed styles gave -15, 270, None gave -60, -120, and the sizable styles kept 60, 345. Is that a
fixed offset, or something that depends on where the form is?

**The probe.** A project of two forms from hand-written `.frm` files, opened in VB6's designer on the guest's console
(VB6.EXE 6.00.9782, SP6, 96 dpi). Both were Sizable with ClientLeft 3000, ClientTop 2000, ClientWidth 3600 and
ClientHeight 2400. Form1 had `StartUpPosition = 3 'Windows Default` and Form2 `0 'Manual`. Each step clicked the
BorderStyle row's value cell, typed the number, pressed Return and saved with Ctrl+S, and the saved `.frm` was read back.
Before the first step, the designer's position box on the toolbar read 2880, 1530 for the selected form.

**From ClientLeft 3000, ClientTop 2000, one change after another, on Form2.** Form1 gave the same values for each
style it was changed to.

| Changed to | ClientLeft | ClientTop |
|---|---|---|
| 3 Fixed Dialog | 2925 | 1920 |
| 2 Sizable | 3000 | **1995** |
| 0 None | 2880 | 1530 |
| 2 Sizable | 3000 | 1995 |
| 4 Fixed ToolWindow | 2925 | 1920 |
| 5 Sizable ToolWindow | 3000 | 1995 |
| 1 Fixed Single | 2925 | 1920 |
| 2 Sizable | 3000 | 1995 |

The client size never changed. MinButton, MaxButton and ShowInTaskbar followed the section above's table at every
step, for Fixed Single and both tool windows as well.

**One more starting point, to tell rounding from truncation.** Form1 was edited on disk to ClientLeft 3010 and
ClientTop 2008, and the project reopened. The position box read 2895, 1545.

| Changed to | ClientLeft | ClientTop |
|---|---|---|
| 3 Fixed Dialog | 2940 | 1935 |
| 2 Sizable | 3015 | 2010 |

**So VB6 keeps the window's outer corner where it is, in whole pixels, and puts the client area inside the new frame.**
Every row above fits this, and so does the earlier section's start at (60, 345):

1. **The outer corner** is the client origin less the current style's frame, in pixels, rounded to the nearest whole
   pixel. From 3000, 2000 at Sizable, that's (3000 − 120) / 15 = 192 and (2000 − 465) / 15 = 102.33, so 102. That gives
   2880, 1530, which is what the position box showed.
2. **The new client origin** is that corner in twips, plus the new style's frame:

| BorderStyle | Left | Top |
|---|---|---|
| 0 None | 0 | 0 |
| 1 Fixed Single, 3 Fixed Dialog, 4 Fixed ToolWindow | 45 | 390 |
| 2 Sizable, 5 Sizable ToolWindow | 120 | 465 |

These are the twips VB6's designer used at 96 dpi, read from the rows. They aren't a rule for other frames or DPI
settings.

- **The corner is rounded, not truncated.** From 3010, 2008 the corner is 192.67 and 102.87 pixels. Truncating gives
  2880, 1530 and Fixed Dialog at 2925, 1920. VB6 showed 2895, 1545 and saved 2940, 1935, which is rounding.
- **A change and its reverse needn't come back to the start.** 2000 less Sizable's 465 is 102⅓ pixels. The corner
  became 102, so Sizable came back as 1995, and stayed 1995 for every later round trip. A start that is a whole pixel from
  its corner, such as (60, 345), came back exactly.
- **StartUpPosition makes no difference** in the designer. Form1 (Windows Default) and Form2 (Manual) gave the same rows.

**What was overturned.** The section above said a form that goes to Fixed Dialog and back "comes back as it started".
For the three properties in its table that's true. For the client origin it holds only when the start is a whole pixel
from the outer corner, which (60, 345), the start it measured, happens to be.

**What resisted explanation.** The vertical frames: a Sizable form's 465 twips is 31 pixels and a fixed one's 390 is 26.
Neither was matched to a system metric. Sizable ToolWindow takes Sizable's numbers and Fixed ToolWindow the fixed ones,
though a tool window's caption is shorter. These are VB6's designer numbers as read, not an account of them.

### Not measured

- **A corner exactly half a pixel from the grid,** so how a tie rounds.
- **A form with a menu,** or an MDI child.
- **The designer at a DPI other than 96.**
- **Left and Top in the Properties window** after each change. Only the position box's reading before the first change
  was taken, and the saved file holds only the client values.
