# `interact`'s `click` pressed without moving the pointer there, so a program's MouseMove never ran from one — **CLOSED** (#2407, 2026-10-27)

**Symptom (2026-10-27, hexide-frm's TreeView recheck, on #2353).** `click` raised a press and a release at the point,
and no move first, so a running form's MouseMove never fired from a click. A person's click always arrives with the
pointer having moved onto the control. A live check of a control's event order, MouseMove, MouseDown, MouseUp,
Click, couldn't be made with `click` alone, and the recheck had to `hover` at the same points to get MouseMove at all.

**Fixed (#2407).** `click` raises a `PointerMoved` on the element it presses, at the point, before the press, on its own
pointer. That is the route a running form's MouseMove is raised from. A click where the last click left the pointer in
that window moves nothing first, as a still pointer sends no move; a hover's end takes the pointer out of the window,
so the next click arrives again. A double click's second press already moved nothing. The reply ends `the pointer moved
there first, as a person's does` or `the pointer was already there from the last click, so it did not move first`.
Guarded by `InteractClickTests` and `MouseEventTests`. The oracle's mouse probes logged no MouseMove, so whether VB6
raises others around a click is unmeasured: #2412.

**Live, against a real pointer (2026-10-27, a Debug build of the fix, headless `claude -p` through the hexide tools).**
A twips form logged each mouse event into a label: `cMM`, `cMD`, `cMU` and `cC` for Command1's MouseMove, MouseDown,
MouseUp and Click, and `fMM`, `fMD`, `fMU` and `fC` for the form's.

| Input | Windows, through `click` | Linux under WSLg, through `click` | Linux, real X input from `xdotool` |
|---|---|---|---|
| click on Command1 | `cMM cMD cC cMU` | `cMM cMD cC cMU` | `cMM cMD cC cMU` |
| click on the form at (333, 200) | `fMM fMD fMU fC` | `fMM fMD fMU fC` | `fMM fMD fMU fC` |
| the same click again | `fMD fMU fC` | `fMD fMU fC` | `fMD fMU fC` (and `cMD cC cMU` on Command1) |

- **The real pointer.** `xdotool mousemove` then `click 1`, so the X server's own input reached Avalonia's X11
  backend. With the form not yet active, the first real click on Command1 gave `cMM` alone, as the window manager took
  the press to activate the form; with it active, it gave the row above.
- **Windows.** The real-pointer column couldn't be measured there: the session was locked, and posted mouse messages
  don't reliably reach Avalonia's input on this host, as an earlier entry found. Three posted clicks left one
  `fMM fMD fMU fC`, which says nothing either way.
