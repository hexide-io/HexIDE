## Mouse events (2026-10-14, #974)

Which controls raise MouseDown, MouseUp and MouseMove, in what order against Click, and with what Button, Shift, X and
Y? The list of controls is read from VB6.OLB's event interfaces with `scripts/dump-typelib-members.ps1`:

| Class | VB6 |
|---|---|
| CommandButton, CheckBox, DirListBox | Click, MouseDown, MouseUp, MouseMove |
| Label, TextBox, PictureBox, Frame, OptionButton, Image, ListBox, FileListBox, Form | Click, DblClick, MouseDown, MouseUp, MouseMove |
| ComboBox | Click, DblClick |
| HScrollBar, VScrollBar, Shape, Line, Timer, DriveListBox | none of the five |

The rest was measured by sending each window synthetic messages with `SendMessage`, from a Timer, in compiled
projects run headless: four projects, each native and p-code, all agreeing. Form1 is a twips form unless the row
says otherwise, and holds Command1 (Left 240, Top 240), Label1, Text1, Check1 and a Label array `lblCell(0)`. Each
handler logs its arguments in the order `Button Shift X Y`.

| Sent | VB6 |
|---|---|
| `WM_LBUTTONDOWN`, `WM_LBUTTONUP` to Command1 at client (10, 5) | MouseDown `1 0 150 75`, **Click**, MouseUp `1 0 150 75` |
| the same with the right button, and the middle | MouseDown and MouseUp with Button `2`, and `4`; no Click |
| the left pair with `MK_SHIFT` in wParam, and with `MK_CONTROL` | Shift `0` both times, and Click as for a plain press |
| `WM_MOUSEMOVE` to Command1, wParam 0 at (12, 6), then `MK_LBUTTON` at (13, 7) | MouseMove `0 0 180 90`, then `0 0 195 105` |
| the left pair to Text1 at client (10, 5) | MouseDown, MouseUp, **then** Click |
| the left pair to Check1 at client (10, 5) | MouseDown, Click, MouseUp; `Value` is then 1 |
| the left pair to the form at (21, 83), over the windowless Label1 (Left 240, Top 1200) | Label1 MouseDown and MouseUp `1 0 75 45`; no Click, and nothing on the form |
| the left pair to the form at (300, 10), over no control | Form MouseDown and MouseUp `1 0 4500 150`; no Click |
| the left pair over `lblCell(0)` | lblCell MouseDown and MouseUp with Index `0` first, then `1 0 75 45` |
| down, up, `WM_LBUTTONDBLCLK`, up, over Label1 | MouseDown, MouseUp, DblClick, MouseUp: the second press raises no MouseDown |
| all of the above on a form whose .frm says `ScaleMode = 3` | the same twips for every control (Command1 still `150 75`); the form's own `300 10` |

Where each control's client area starts inside its window, which is the origin of its X and Y, read with
`GetWindowRect`, `GetClientRect` and `ClientToScreen` after `Show`:

| Control | VB6 |
|---|---|
| TextBox, 3-D and bordered (the default) | window 81x33, client 77x29, origin +2,+2 |
| TextBox, flat; TextBox with no border | client = window, origin +0,+0 |
| ListBox, 3-D | window 81x43, client 77x39, origin +2,+2 |
| PictureBox, 3-D | window 81x49, client 77x45, origin +2,+2 |
| CommandButton, CheckBox, OptionButton, Frame | client = window, origin +0,+0 |

And whether a control with no handlers passes the event to what contains it:

| Sent | VB6 |
|---|---|
| the left pair to the form at (94, 74), near the far corner of an Image with no picture (Left 240, Top 240, 1200 x 900) | Image1 MouseDown and MouseUp `1 1170 870`; no Click |
| the left pair over Label2, which has no handlers | nothing, on the Label or the form |
| the left pair to Frame1 over Image2, which has no handlers, inside it | nothing, on the Image or the Frame |
| the left pair to Frame1 at client (140, 60), over no control | Frame1 MouseDown and MouseUp `1 2100 900` |

What these show:
- **A CommandButton and a CheckBox raise Click between MouseDown and MouseUp.** The obvious order, and the one #974
  suggested, is MouseDown, MouseUp, then Click. That is what a TextBox does, not a button.
- **A control's X and Y are twips from its client origin, whatever the ScaleMode around it.** That differs from its
  Left and Top, which follow the container's ScaleMode (*A control's position at run time*). Only an object with a
  ScaleMode of its own, the form here, reports in it.
- **Button is 1, 2 or 4.** On MouseMove it is the buttons held, but VB6 reads those from the mouse, not the message:
  `MK_LBUTTON` in wParam gave 0. Shift is read from the keyboard the same way, so `MK_SHIFT` gave 0, and the mask itself
  (VB6.OLB's `vbShiftMask` 1, `vbCtrlMask` 2, `vbAltMask` 4) could not be measured from synthetic messages.
- **An Image with no picture takes the mouse across its whole rectangle.**
- **An event a control does not handle is not passed to its container**, for the form and for a Frame alike.

What resisted explanation: synthetic messages raise no Click on a windowless Label, an Image, a PictureBox or the
form, though each has one, while a TextBox's Click comes. That repeats the PictureBox row under *A form-based game's
runtime surface*. It is most likely how VB6 synthesises Click for those, from the real cursor or capture, so where
Click falls among their mouse events is still to be measured with real input on the interactive desktop. Measured
since: *A click with real input* below (#1364).

### HexIDE

Before #974, no control raised MouseDown, MouseUp or MouseMove, and the code window did not offer them.

Since #974, the controls HexIDE has that VB6 gives the events raise them: CommandButton, Label, TextBox, PictureBox,
Frame, CheckBox, OptionButton, Image, ListBox, and the form from its client area. Their order against Click is set
by registration: MouseUp follows the Click a Button raises on release, and comes before the Click HexIDE raises on
release for the rest. Under the IDE's debugger that order holds only because the second of two events from one
release waits for the first (*One handler at a time*, #766); before that, the first yielded its time slice and the
second overtook it. A move that waits behind a running handler is coalesced into a move at the back of the queue. X and Y are the whole pixel under the pointer, from the client origin (2 pixels in for a 3-D
TextBox or ListBox, and a PictureBox's inside its border), in twips, or in the form's or PictureBox's own ScaleMode;
one HexIDE cannot convert (User, centimetres, millimetres) is refused as the run's error, naming #990. An Image is
hit-tested across its whole rectangle, and gains Click too. A handler that does not exist is skipped before
dispatch, so MouseMove costs nothing where nobody handles it. Pinned in `MouseEventTests`, with real headless pointer
input, from every row above that HexIDE can reach.

What it still does not do:
- **DblClick** is raised by nothing (#1031). Since #1031 it is: *A click with real input* below.
- **The form's own Click** is raised by nothing, as before. Since #1364 it is: *A click with real input* below.
- **Shift's mask and MouseMove's buttons** are taken from the pointer event, which is where they live in Avalonia;
  VB6 takes them from the device, which is the same thing for real input.
- **A press on a 3-D TextBox's or ListBox's 2-pixel border** reaches the control in HexIDE with a negative X or Y,
  where VB6 sends the border no mouse message at all.

