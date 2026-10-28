# `set_window_state` replies with the window's state from before the change on Linux — **IMPLEMENTED, live verification pending** (#946)

**Symptom.** Each call followed by `get_window_state {}` (Linux, openSUSE Leap 16 under XWayland, Debug build of
main at b15c7cf, 2026-10-13):

```
set_window_state {"state":"Normal"}
→ {"window":{"state":"Maximized","x":0,"y":32,"width":1920,"height":1011}}
get_window_state {}
→ {"state":"Normal","x":240,"y":171,"width":1440,"height":733}

set_window_state {"state":"Normal","x":100,"y":100,"width":1280,"height":720}
→ position updated, width and height still 1440x733
get_window_state {}
→ 1280x720

set_window_state {"state":"Maximized"}
→ {"window":{"state":"Normal", ...}}
get_window_state {}
→ Maximized, 0,32 1920x1011
```

The reply says `success: true` and carries the old value in the field described as the resulting state. #602's
re-read waits only for HexIDE's own layout, and on Avalonia 12.0.4's X11 backend, which every Linux session uses,
XWayland included, state and geometry change only when the window manager answers. Linux-specific by code
reading: the other backends, Headless included, take the requested value at once, so tests cannot reproduce it.
Read in code and not run, from Maximized: a restore with a size writes the maximised origin back as the position,
and a refused off-screen position cannot undo a restore already sent while the reply says "Nothing was changed."
The archived #602 entry's "read after layout, so a new size is not reported as the old one" holds only where the
platform applies a change at once.

**Workaround.** Ignore the reply's `window` and re-read with `get_window_state {}`.

**Suggested fix.** Wait, bounded at about a second, until the window reports the target state and, when geometry
was passed, a move or resize has arrived; on timeout, reply with what the window reports and a note to re-read.
Check the frame against the screens before any state change, so a refusal never has to undo a restore.
[#946](https://github.com/hexide-io/HexIDE/issues/946).


**Implementation.** State confirmation is bounded at 1 s; geometry follows a confirmed restore and waits up to
a further second for move/resize notifications (no exact-size requirement, since a window manager may clamp).
Unconfirmed requests return `success: false`, observed `window`, and a `note` directing the caller to
`get_window_state`. Geometry is validated before restoring; omitted coordinates are not written back. The no-
window-manager case is reported as unconfirmed too. Headless tests cover delayed confirmation and timeout;
X11/XWayland verification remains for the supervising session.
