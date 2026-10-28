# `take_snapshot` of a minimized window returned its last frame as if current — **CLOSED** (#2416, 2026-10-28)

**Symptom (2026-10-27, Windows, #1925's minimized check).** After `set_window_state {"state":"Minimized","window":"Form1"}`
had run the form's Resize, `take_snapshot {"window":"Form1"}` returned a 450×300 image, with the usual client
`captureArea`, whose label read `resize 1, activate 1, state 0`: the form before it minimized. Restored, it read
`resize 3`. A minimized window draws no new frames, and the compositor capture (#2306) returns the last one.

**Workaround.** Restore the window with `set_window_state` before capturing it, and treat a capture of a minimized
window as stale.

**Suggested fix.** Refuse a minimized window in the default capture, as the frame capture does, naming
`set_window_state`; or render it fresh and say so. Never present the last frame as current.

**Fixed (#2416).** The default capture refuses a minimized window, as the frame capture does, and says how to
restore it. A person can't see a minimized window's contents either, so nothing is rendered in its place. Guarded by
`FrameCoverTests`, which pins the wording; headless can't minimize a window, so the refusal itself was checked live.

**Live (2026-10-28, Windows at 150%, headless `claude -p` through the hexide tools).** A form whose Resize writes
`resize N, state S` into a label:

```
set_window_state {"state":"Minimized","window":"Form1"}      → label: resize 2, state 1
take_snapshot {"window":"Form1"}
→ 'Form1' is minimized, so nothing of it is on the screen, and the last frame it drew before it was minimized would
  read as current; nothing was captured. Restore it with set_window_state {"state":"Normal","window":"Form1"},
  then capture it.
set_window_state {"state":"Normal","window":"Form1"}         → label: resize 3, state 0
take_snapshot {"window":"Form1"}                               → a 450×300 image reading "resize 3, state 0"
```
