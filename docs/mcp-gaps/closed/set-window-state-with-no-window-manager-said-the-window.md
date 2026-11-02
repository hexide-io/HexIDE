# `set_window_state` with no window manager said the window manager hadn't confirmed, and to re-read — **CLOSED** (#2185, 2026-10-27)

**Symptom (2026-10-26, openSUSE Leap 16 VM, Xvfb `:99` with no window manager, #1755's live check).**
`set_window_state {"state":"Minimized"}` answered `"error":"The window manager has not confirmed the requested change."`
with a note to re-read with `get_window_state`, and `Maximized` the same. Nothing would ever confirm either, and
re-reading showed `Normal` every time. Nothing said that minimize and maximize couldn't work on that display while a
restore with a position and size could.

**Fixed (#2185).** When a minimize or maximize goes unconfirmed, HexIDE reads the X11 root window's
`_NET_SUPPORTING_WM_CHECK`, which a window manager sets (`X11WindowManager`, libX11 through `LibraryImport`, in the
DEBUG-only server). With none there, the reply says `No window manager is running on this display, so nothing will
apply Minimized.`, explains why, and says Normal with x, y, width and height still works. A window manager that is
there but slow, a display that can't be read, another platform, and an unconfirmed restore all keep the old text.
Guarded by `WindowStateUnconfirmedTests`.

**Live (2026-10-27, a Debug build of the fix under WSL, Ubuntu 26.04, headless `claude -p` through the hexide tools).**

- On Xvfb `:99`, where `xprop -root _NET_SUPPORTING_WM_CHECK` finds no such atom, Minimized and Maximized each
  answered with the new text, and `get_window_state` agreed with the reply's `window` field. A Normal with
  `x 40, y 30, 900×640` was confirmed.
- On WSLg's `:0`, whose root window carries `_NET_SUPPORTING_WM_CHECK`, the same build kept the old text for an
  unconfirmed Minimized, and confirmed Maximized and the same Normal.
