# `press_key` reports Alt+letter success without activating an access key — **CLOSED**

> **Fixed (#1523, 2026-10-23).** Keys now go through the window's platform input callback, with the key symbol
> Avalonia's access-key handler requires. The reply names `window-input`, or `routed-key` when focus could not
> be taken or the platform callback could not be reached. Guarded by `PressKeyWindowInputTests`, including a
> running form's Click handler and a menu bar opening.

**Symptom (2026-10-23, #1390 live check, #1523).** With `cmdWide` captioned `One &Wide` and its Click setting
`lblResult`, both calls reported success with mechanism `keyboard` while the Label stayed `waiting`:

```text
press_key {"target":"Window/Button[cmdTom]","key":"W","modifiers":"Alt","window":"Form1"}
press_key {"target":"Window","key":"W","modifiers":"Alt","window":"Form1"}
```

On the IDE menu bar, `press_key {"target":"Window","key":"F","modifiers":"Alt","window":"ide"}` highlighted
File without opening it. The form also failed on the build before #1390. A headless raw Alt+W activated the
button, proving the product path worked.

**Workaround.** `interact click` on the button runs its handler; it cannot verify its access key.

**Fix.** Keep target resolution and focus, then deliver raw KeyDown/KeyUp through the window input callback,
with physical key and symbol metadata. Retain routed delivery when focus or platform input is unavailable,
and name the path in the reply. No TextInput is sent; `type_text` remains the way to enter text.
