# No way to turn a lock key on for a running program, so a StatusBar key panel's on state couldn't be checked — **CLOSED** (#2296, 2026-10-27)

**Symptom (2026-10-27, Windows, #2254's live check).** A StatusBar panel of Style 1 to 4 draws its Text black while
its key's toggle is on and grey while it's off. A live check could see the off state but not the on state. On Windows
the StatusBar reads the toggle the operating system keeps, and `press_key` raises Avalonia's key events without
changing it. On the test form, `press_key` targeting `Window` was also refused, since nothing there takes keyboard
focus.

**Workaround.** None live. The headless tests reached both states through an injected key reader.

**Fixed (#2296).** A new DEBUG-only tool, `set_lock_key {key, state}`. It sets CapsLock, NumLock, Insert or ScrollLock
`On` or `Off` as programs running in HexIDE read it, and `Keyboard` returns the key to the keyboard's own toggle. The
state lives in `LockKeys` in the runtime, beside the keyboard read it overrides. A StatusBar prefers it, and repaints
when it changes. Every reply lists all four keys, each with `on` and a `source` (`Set`, `Keyboard`, or `Window` where a
host has no toggle to read). The machine's keyboard is untouched. A new tool rather than a change to `press_key`: a
press can't set the OS toggle, and teaching the StatusBar to count automation's presses on top of the OS reading
would double-count a real one. Guarded by `SetLockKeyTests` and by
`StatusBarDrawingAndEventsTests.A_key_panel_follows_a_lock_key_set_by_automation`, whose four cases fail with the
override ignored.

**Live (2026-10-27, a Debug build of the fix on Windows at 150%, headless `claude -p` through the hexide tools).** The
form was the StatusBar corpus form with its third panel made an enabled Caps panel reading `CAPS`.

- With no arguments, the reply listed all four keys off, with source `Keyboard`, and changed nothing.
- The running form's `CAPS` was grey. After `{"key":"CapsLock","state":"On"}` it was black, with no other pixel
  of the window changed: 69 dark pixels in the text against none.
- After `Off` it was grey again, as it was after `Keyboard`. Both pictures were identical to the first.
- `Shift` and a key with no state were refused, each naming the accepted keys and states.
