# `press_key` refused a chord written as one key as an unknown key — **CLOSED** (#2189, 2026-10-27)

**Symptom (2026-10-26, Windows, #2030's live check, Sonnet).**
`press_key {"target":"…/Custom[Root]/None[TextEditor]","key":"Ctrl+H","window":"ide"}` answered
`unknown key 'Ctrl+H' — use an Avalonia Key name (Enter, Tab, Back, Escape, Down, S, …)`. `H` was never the problem:
modifiers go in their own parameter, and the refusal didn't say so. The caller gave up on the keyboard and opened
Replace from the Edit menu.

**Fixed (#2189).** A `key` with a `+` between two parts is read as its last part, with the parts before it as
modifiers added to any `modifiers` given, and the reply says so: `pressed Control+H …; 'Ctrl+H' was read as key 'H'
with modifiers 'Ctrl'`. A leading part that is not a modifier is refused naming it, and an unknown last part is named
in the chord. Combined modifiers now read `Control+Shift+End`, not `Control, Shift+End`. Guarded by
`UiAutomationDriverTests`.

**Live (2026-10-27, Windows, a Debug build of the fix, headless `claude -p` through the hexide tools).** `Ctrl+H` in
the code window opened Replace, as `H` with `Ctrl` does. `A+B` and `Ctrl+Nope` were refused naming the wrong part, and
`Shift+End` with `Ctrl` replied `pressed Control+Shift+End …, added to 'Ctrl'`.
