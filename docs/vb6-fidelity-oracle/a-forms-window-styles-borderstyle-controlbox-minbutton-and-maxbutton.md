## A form's window styles: BorderStyle, ControlBox, MinButton and MaxButton (2026-10-23, #1575)

**The question.** A running form was a sizable window with a title bar and every button, whatever its file said. What
window does vb6.exe make for each combination of BorderStyle, ControlBox, Caption, MinButton, MaxButton, ShowInTaskbar
and a menu?

**The probe.** One project of 20 forms built by `/make`, native and p-code, in session 0 on the Windows 10 guest. Sub
Main loads and shows each form in turn and reads:
- `GetWindowLong(hWnd, GWL_STYLE)` and `GWL_EXSTYLE`;
- the form's own MinButton, MaxButton, ControlBox, ShowInTaskbar and BorderStyle;
- `WM_GETICON` for the small and big icons, and the class's icons.

A window's styles are set as it is made, whatever desktop it is on, so session 0 answers this. **Native and p-code
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

