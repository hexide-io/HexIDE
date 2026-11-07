# `press_key` reported success on a disabled control — **CLOSED** (#2366, 2026-10-27)

**Symptom (2026-10-27, Windows, #623's measurement on main at 759df134).** Options → Language's region combo is
disabled while the language combo shows the system default.

- `inspect_element {"target":"Window/Custom/Custom/ComboBox[#1]","window":"Options"}` gave `"isEnabled": false` and
  `"isKeyboardFocusable": true`.
- `press_key {"target":"Window/Custom/Custom/ComboBox[#1]","key":"Down","window":"Options"}` answered
  `{"success":true,"mechanism":"routed-key","detail":"pressed Down"}`.

`interact`'s `click` and `invoke` refuse the same control.

**Workaround.** Read `isEnabled` with `inspect_element` before pressing.

**Cause.** A disabled control can't take focus, so `PressKey` fell back to raising the key as a routed event on it, which
no person's keyboard could do, and reported that as success. `inspect_element` took `isKeyboardFocusable` from the
automation peer, which answers for `Focusable` alone.

**Fixed (#2366).** `press_key` refuses a target that isn't effectively enabled, with the refusal `interact` gives every
verb since #2223 (`DisabledInteractionRefusal`), which names a container that disables it. A modal dialog over the window was already refused, by `KeysTakenBy`.
`isKeyboardFocusable` is false for a control that isn't effectively enabled. `PressKeyDisabledTests` covers a disabled
combo, a disabled container and the focusable read; each fails with its half of the fix removed.

**Live (2026-10-27, Windows, a Debug build of the fix, headless `claude -p` through the hexide tools).** On the same
combo, `inspect_element` gave `"isEnabled": false` and `"isKeyboardFocusable": false`, and the same `press_key` answered
`{"success":false,"mechanism":"keyboard","error":"'ComboBox' is disabled (\"isEnabled\": false). No person could use
it, so 'press_key' was refused and nothing was done. Wait until it is enabled before trying again; inspect_element
reports its current state"}`.
