# `press_key` with Alt and a letter never reaches a control's access key

**Symptom (2026-10-23, #1390, Windows).** On a running form whose `cmdWide` is captioned `One &Wide`,
`press_key {"target":"Window/Button[cmdTom]","key":"W","modifiers":"Alt","window":"Form1"}` answered
`{"success":true,"mechanism":"keyboard","detail":"pressed Alt+W"}`, and `press_key {"target":"Window","key":"W",
"modifiers":"Alt","window":"Form1"}` answered `pressed Alt+W on VBCommandButton, at Window/Button[cmdWide]`. Neither ran
`cmdWide_Click`. On the IDE, `press_key {"target":"Window","key":"F","modifiers":"Alt","window":"ide"}` highlighted
File without opening it. `press_key` raises routed `KeyDown` and `KeyUp` on the target, which Avalonia's access-key
handling doesn't act on.

**Workaround.** None through the surface. A headless test sending the chord as raw input
(`window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.Alt)`) with a button focused clicked `cmdWide`, so the product
works.

**Suggested fix.** Deliver the keys through the window's input path, as real keystrokes, the way `interact click`
does for the pointer, or offer that as a mode and say in the reply which path was used. Until then, say in the
description that an access key can't be reached. [#1523](https://github.com/hexide-io/HexIDE/issues/1523).
