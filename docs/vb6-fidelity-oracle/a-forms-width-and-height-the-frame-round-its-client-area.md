## A form's Width and Height: the frame round its client area (2026-10-23, #1072)

**The question.** A form's Width and Height at run time are its outer size ("Move, ScaleWidth and ScaleHeight"
measured 90 and 375 twips more than the client for one Fixed Single form). How big is the frame for each BorderStyle,
with a menu bar and with no caption bar? And what does Width read back after an assignment, since the window is whole
pixels?

**The probe.** Built EXEs, native and p-code, on the SP6 runtime (#1524). Each was run twice: in session 0, through
PowerShell Direct, and in the guest's console, through the console agent, where Windows 10 draws the themed frame. Every
form has a 2520 by 2520 twip client area and `StartUpPosition` Manual. `Sub Main`, for each form:
- `Load` it and log `Width`, `Height`, `ScaleWidth`, `ScaleHeight` and the differences;
- `Show` it, `DoEvents` and log again;
- set `Width = 4000` and `Height = 3000`, `DoEvents`, log, and `Unload` it.

Three rounds, the second and third for the rows the first left open. Native and p-code agreed on every line, and so did
each form's loaded and shown rows: the frame is there before the form is shown. `Screen.TwipsPerPixelX` is 15.

**Width - ScaleWidth by Height - ScaleHeight, in twips:**

| Form | Session 0 | Console |
|---|---|---|
| 0 None | 0 × 0 | 0 × 0 |
| 1 Fixed Single | 90 × 375 | 90 × 435 |
| 2 Sizable | 120 × 405 | 240 × 585 |
| 3 Fixed Dialog | 90 × 375 | 90 × 435 |
| 4 Fixed ToolWindow | 90 × 330 | 90 × 435 |
| 5 Sizable ToolWindow | 120 × 360 | 240 × 585 |
| 0 None, with a menu | 90 × 660 | 90 × 735 |
| 1 Fixed Single, with a menu | 90 × 660 | 90 × 735 |
| 2 Sizable, with a menu | 120 × 690 | 240 × 885 |
| 3 Fixed Dialog, with a menu | 90 × 660 | 90 × 735 |
| 4 Fixed ToolWindow, with a menu | 90 × 615 | 90 × 735 |
| 1 Fixed Single, `ControlBox = False`, `Caption = ""` | 30 × 30 | 30 × 30 |
| 2 Sizable, the same | 120 × 120 | 240 × 240 |
| 2 Sizable, the same but `MinButton` and `MaxButton` left True | 120 × 120 | 240 × 240 |
| 3 Fixed Dialog, the same | 90 × 90 | 90 × 90 |
| 4 Fixed ToolWindow, the same | 30 × 30 | 30 × 30 |
| 5 Sizable ToolWindow, the same | 120 × 120 | 240 × 240 |
| 1 Fixed Single, `ControlBox = False`, a Caption | 90 × 375 | 90 × 435 |
| 1 Fixed Single, `Caption = ""`, the control box | 90 × 375 | 90 × 435 |
| 2 Sizable, `ControlBox = False`, a Caption | 120 × 405 | 240 × 585 |
| 2 Sizable, `Caption = ""`, the control box | 120 × 405 | 240 × 585 |

**After `Width = 4000` and `Height = 3000`**, console:

| Form | Width, Height | ScaleWidth, ScaleHeight |
|---|---|---|
| 0 None | 4000, 3000 | 4005, 3000 |
| 1 Fixed Single | 4000, 3000 | 3915, 2565 |
| 2 Sizable | 4000, 3000 | 3765, 2415 |
| 1 Fixed Single, with a menu | 4000, 3000 | 3915, 2265 |
| 2 Sizable, with a menu | 4000, 3000 | 3765, 2115 |
| 1 Fixed Single, no caption bar | 4000, 3000 | 3975, 2970 |

What these show:
- **The console's frame is one rule.** A border, across and down: 240 for the sizable styles, 90 for the fixed ones,
  and none for None. A caption bar adds 345 down, and a menu bar 300 more.
- **There is no caption bar only when ControlBox is False and the Caption is empty.** Either alone keeps it, and
  MinButton and MaxButton don't matter. Without one, Fixed Single and Fixed ToolWindow have a border of 30, a pixel
  each side, and Fixed Dialog keeps 90.
- **A borderless form with a menu** has Fixed Single's border and caption bar.
- **A tool window's frame is the ordinary one** on the themed desktop. Only session 0 draws a smaller caption.
- **Width and Height read back as assigned, but the window is whole pixels.** 4000 twips is 266.67 pixels. The window is
  267, so the client area is 267 less the frame: 261 pixels, 3915 twips. A borderless form's client is all 267, 4005.
- **Session 0's frame is smaller**, being unthemed: 120 across a sizable form, 375 down a fixed one, and a menu bar of
  285. "Move, ScaleWidth and ScaleHeight" and "Menus at run time, and the menu bar" were measured there.

### In HexIDE (#1072)

`FormFrame` gives a form's window the console's frame, worked out once from its file's BorderStyle, ControlBox, Caption
and menu bar. A program then reads Width and Height as the client area plus that frame, or as the value it last
assigned, while the window keeps the size that value gave it. An assignment rounds the outer size to whole pixels and
gives the window the client area left inside the frame. Read and write use the same frame, so `Width - ScaleWidth` is
the frame and the fit-to-content idiom lands where it did.

**The frame is VB6's, not the window's.** HexIDE drew every form as a sizable window with a full title bar until #1575,
which gives the window vb6.exe's own styles ("A form's window styles", below). A frame read from the window would still
be unknown until the form is shown, and so change across Show, and would differ between hosts: the frame Windows draws
for those styles depends on its version, theme and dpi. The table is stable from Load onwards and the same everywhere.
What that costs is screen arithmetic, such as `(Screen.Width - Width) / 2`, which is off wherever the host draws a frame
other than the themed Windows 10 one the table was measured on. A form's Left and Top don't work at run time yet anyway
(#852).

**The console's values, not session 0's,** because the themed frame is what a user's desktop draws. A classic-theme
profile would supply session 0's to `FormFrame` instead.

**Not followed or not measured:**
- The single-view host's forms, which are not windows, still read their client size.
- A frame that changes at run time, as when a program empties the Caption of a form whose ControlBox is False. HexIDE
  keeps the frame the form's file gave it. Unmeasured.
- What a user's resize does to the value read back. HexIDE reads the new size, since the window no longer has the size
  the assignment gave it.
- A Width or Height smaller than the frame: HexIDE gives the window no client area. Unmeasured.
- Other dpi settings: HexIDE counts 15 twips to a pixel, as at 96 dpi.

