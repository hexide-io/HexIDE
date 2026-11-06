# `get_window_state` mixed screen and device-independent pixels, and didn't say which edge it measured — **CLOSED** (#2454, 2026-10-28)

**Symptom (2026-10-28, hexide-int, Windows at 150%, #2448's live check).** The IDE, maximized, read
`{"x":-11,"y":-11,"width":1280,"height":649}`. A running form placed at Left 7200, Top 3600 twips read
`{"x":721,"y":325,"width":304,"height":200}`. `x` and `y` were the platform's position in screen pixels, which on
Windows includes the invisible resize border (the -11). `width` and `height` were the client area in device-independent
pixels. Nothing in the reply said either, so a caller could not tell whether a form's `y` of 325 against the 360 its
Top implies was a placement defect or only what the number measured.

**Fixed (#2454).** The four numbers are kept, as `set_window_state` takes them, and every reply now also carries:
- `scale`;
- `outer`, the window's whole rectangle, its invisible resize border included, which is where `x` and `y` are;
- `frame`, the window as drawn;
- `client`, the area inside;
- `units`, which says all of this.

The three rectangles are on the screen in device-independent pixels, twips divided by 15. Positions stay in screen
pixels for `x` and `y` because they are what the platform and `set_window_state` use, and the only coordinates that
hold across monitors of different scale. A hidden or minimized window has no rectangles. Guarded by
`WindowStateUnitsTests`; Windows' own rectangles are read from the HWND, which a headless test has none of, so those
were checked live.

**Live (2026-10-28, Windows at 150%, headless `claude -p` through the hexide tools).** A form whose `Form_Load` runs
`Me.Move 7200, 3600, 4800, 3600`:

```
get_window_state {"window":"Form1"}
→ {"state":"Normal","x":4,"y":23,"width":304,"height":200,"name":"Form1","scale":1.5,
   "outer":{"x":2.7,"y":15.3,"width":318.7,"height":238},"frame":{"x":8.7,"y":15.3,"width":306.7,"height":232},
   "client":{"x":10,"y":45.3,"width":304,"height":200.7},"units":"…"}
set_window_state {"state":"Normal","window":"Form1","x":720,"y":360}
→ "outer":{"x":480,"y":240,…},"frame":{"x":486,"y":240,…},"client":{"x":487.3,"y":270,…}
```

The first reply shows the form at Windows' default place, not at 480, 240, while the program's own `Me.Left` and
`Me.Top` read back 7200 and 3600. That is #852 (a form's Left and Top don't move the window at run time), whose
oracle rows say a form's Left and Top are the outer window's position, so `outer` is the rectangle to compare with
them. The reply now shows that defect instead of hiding it. The maximized IDE reads `outer` -7.3, -7.3 (the -11 in
screen pixels) and `frame` 0, 0.
