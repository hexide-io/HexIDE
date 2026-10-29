# No tool could see a running form's frame — **CLOSED** (#1595, #2182, 2026-10-27)

**Symptom (2026-10-23, #1575, Windows, Debug).** A change to a running form's window, its border, caption bar and
buttons, could not be checked through the tools. `take_snapshot(window: "B3x1")` on a running Fixed Dialog returned
a 252 × 150 image at scale 1.5: the form's 168 × 100 client area, with no caption bar, border or buttons.
`inspect_element(target: "Window", window: "B3x1")` returned `boundingRect [0,0,168,100]`, `providers []` and
`dataContextMembers []`, and nothing about whether the window can be resized, its decorations, its buttons or whether
it is a tool window.

**Workaround.** From outside the IDE: `GetWindowLong` with `GWL_STYLE` and `GWL_EXSTYLE` through a PowerShell
P/Invoke on the windows HexIDE's process owns, and a screen capture of their rectangles. Both bypass the surface a
real caller uses.

**Implemented; Windows live verification pending (#1595).** `inspect_element` on a Window reports the
Avalonia frame and now reads `nativeStyle` / `nativeExStyle` as unsigned 32-bit Win32 bit masks from its HWND.
`nativeStyleNote` explains when those reads are unavailable, including on headless and non-Windows hosts.
`take_snapshot(window: "B3x1", includeFrame: true)` captures the window with its frame, cropped to the DWM frame
rectangle, with client-origin offsets for mapping inspected bounds. Since #2182 Windows renders it (`PrintWindow` with
`PW_RENDERFULLCONTENT`) rather than the screen being copied, so a window covering it is not in the image, and open
popups are drawn over it as the default capture draws them. Should Windows render nothing, or only black, the frame
rectangle is copied from the screen, and the note says how much of it other windows cover. It refuses a window with
no HWND, or hidden or minimized, rather than silently returning a client-only image; a copy from the screen is also
refused for a window partly off the desktop. The default client/popup render is unchanged.

**Required live check.** On Windows at 150% scaling, inspect a running Fixed Dialog and tool window, compare
both style masks with `GetWindowLong`, and capture each with `includeFrame: true` (also with `inline: true`).
Confirm caption, border and buttons, client-origin coordinate mapping, an overlapping window, and the explicit
refusal for a minimized or partly off-screen window. Keep this entry open until those checks pass.
[#1595](https://github.com/hexide-io/HexIDE/issues/1595).

**Measured (2026-10-26, Windows 11 at 150%, a Debug build of this branch merged with main at c208b2d7, headless
`claude -p` through the hexide tools).** A running form with BorderStyle 3 (Fixed Dialog) and the caption "Fixed":

- `inspect_element {"target":"Window","window":"Fixed"}` gave `"frame":{"canResize":false,"windowDecorations":"Full",
  "canMinimize":false,"canMaximize":false,"showInTaskbar":true,"windowState":"Normal","nativeStyle":382206080,
  "nativeExStyle":2359553}`. Both masks equal `GetWindowLongPtr`'s `GWL_STYLE` and `GWL_EXSTYLE`, read on the same
  window: caption, system menu, no sizing frame and no minimize or maximize box, with a modal dialog frame.
- `take_snapshot {"window":"Fixed","includeFrame":true}` gave `"width":454,"height":347,"scale":1.5,"windowOffsetX":2,
  "windowOffsetY":45` and the note. With the form in front, the image is the form: a caption bar reading "Fixed",
  the border, a close button only, and the client area at (2, 45), so `boundingRect` [0,0,300,200] maps to
  (2, 45)-(452, 345) as the description says. With `"inline":true`, the inline copy is the same, at 454×347 and
  `inlineScale` 1.5. Without `includeFrame` the image is the 450×300 client area, with offsets 0.
- **With another window over the form, the image is that window.** The IDE was started in the background, so the
  running form opened behind another application's window, and the same call returned that window's pixels. The reply
  was identical to the unobscured one, note included, so nothing in it says the capture missed. No tool brings a
  running form to the front, so the note's advice can't be followed through the tools. Filed as #2182.
- **Not checked:** a tool window, and the refusal for a minimized or partly off-screen window. `set_window_state`
  acts on the IDE's main window only, and nothing else minimizes or moves a running form.

**Measured again with #2182's fix (2026-10-27, Windows at 150%, a Debug build of this branch merged with main at
27e5bd06, headless `claude -p` through the hexide tools).** The running form was a Fixed Dialog captioned "Fixed" with
a Toolbar. A test harness outside the IDE covered it with a topmost magenta window; `WindowFromPoint` at the form's
centre returned the harness window.

- `inspect_element {"target":"Window","window":"Form1"}` gave the same `nativeStyle` 382206080 and `nativeExStyle`
  2359553 as before, equal to `GetWindowLong`'s `GWL_STYLE` and `GWL_EXSTYLE` read by the harness.
- `take_snapshot {"window":"Form1","includeFrame":true}` with the form covered gave `"width":604,"height":497,
  "scale":1.5,"windowOffsetX":2,"windowOffsetY":45` and the frame `captureArea`. The image is the form: its caption bar
  reading "Fixed", the close button, the Toolbar and the client area, with no magenta pixel in it.
- With the Toolbar's dropdown open, the same call drew its menu, item First, over the form, still covered.
- Without `includeFrame` the image is the 600×450 client area, offsets 0, also with no magenta.
- With the harness then moving the form to x 1800 on a desktop 1920 wide, so all but 120 pixels of it were off the
  right edge, the call returned the whole 604×497 window: no part black or missing.
- **One divergence from the screen.** The 1-pixel border down the sides and along the bottom comes out black, about
  3,000 pixels of the 604×497 image, where the screen draws it in the border colour. Windows 11 draws that border
  outside the window's own pixels.

**The last two checks (2026-10-27, Windows at 150%, a Debug build of this branch merged with main at 8048301e,
headless `claude -p` through the hexide tools).**

- **Tool windows.** Two running forms, Form1 a Fixed ToolWindow (BorderStyle 4) and Form2 a Sizable ToolWindow (5).
  - `inspect_element {"target":"Window","window":"Form1"}` gave `"nativeStyle":382205952,"nativeExStyle":2097536`, and
    Form2's gave `382664704` and `2097536`. A harness's `GetWindowLongPtr` read the same four values, with
    `WS_EX_TOOLWINDOW` set on both.
  - `take_snapshot {"window":"Form1","includeFrame":true}` gave a 304×197 image with `"windowOffsetX":2,"windowOffsetY":45`.
    It shows the caption reading "Tool fixed", a close button only, the border, and the client area. Form2's is the same
    with its own caption. With `"inline":true` the inline copies are the same size, at `inlineScale` 1.5.
  - Windows' own metrics agree, read without the screen: the client origin sits at (2, 45) inside the DWM frame, and at
    144 dpi `SM_CYCAPTION` and `SM_CYSMCAPTION` are both 34, so a tool window's 45-pixel caption band is
    34 + `SM_CYFRAME` 5 + `SM_CXPADDEDBORDER` 6. The session was locked, so no screen copy could be compared; that the
    frame captures still rendered shows #2182's route needs no unlocked screen.
- **A minimized window.** `set_window_state {"state":"Minimized","window":"Form1"}`, which reaches a running form since
  #2357, then `take_snapshot {"window":"Form1","includeFrame":true}`, was refused: `The window is hidden or minimized.
  Show or restore it, then retry includeFrame true.` That reply also carried the client `captureArea`, describing an
  image it never took. Both are fixed here: a refusal carries no `captureArea`, and the refusal names the way back: `The
  window is minimized, so Windows draws no frame for it. Restore it with set_window_state, state Normal and its name as
  'window', then retry includeFrame true.` A hidden window gets a refusal of its own. `FrameCoverTests` pins both.
- **Partly off the desktop, through the tools.** `set_window_state {"state":"Normal","window":"Form1","x":1800,"y":200}`
  on a 1920-wide desktop, then the frame capture, returned the whole 454×347 window, nothing black or missing, with no
  note. That is the harness check above, now made through the surface.

**Found on the way.** The default capture of a minimized window returned the last frame it drew before minimizing,
with the reply of a fresh capture: #2416, in the live document.
