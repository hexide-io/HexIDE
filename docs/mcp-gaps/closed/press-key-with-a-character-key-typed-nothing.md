# `press_key` with a character key typed nothing — **CLOSED** (#2428, 2026-10-27)

**Symptom (2026-10-27, Windows, #2422's live check).** `press_key {"target":"Window/Edit[Text1]","key":"K",
"modifiers":"Shift","window":"Form1"}` on a running TextBox logged `D16/1 D75/1 U75/1 U16/0`: no KeyPress, and no
text. The oracle puts KeyPress 75 between the KeyDown and KeyUp, and a person's press types K. The reply, `pressed
Shift+K`, does not say nothing was typed. The raw key events carry no text, which a platform sends separately.

**Workaround, until the fix.** `type_text` with `mode: "keyboard"`, which logged `D16/1 D75/1 P75 U75/1 U16/0`.

**Suggested fix.** Type the key's character between its KeyDown and KeyUp when it types one, as `type_text` does; or
say in the reply that nothing was typed and name `type_text`. [#2428](https://github.com/hexide-io/HexIDE/issues/2428).

**Fixed (#2428).** `press_key` sends the key's character after its KeyDown, as text of its own, as a platform sends
a person's press, through the window input and the routed fallback alike. None goes with Ctrl, Alt or Meta held,
whose chords are commands, and none for a control character such as Backspace's, which a running program's KeyPress
takes from the key itself. In a code window the key is judged as typing is, so a protected line refuses it. The
reply ends `it typed "K", as the key does`. Guarded by `PressKeyTypesTests` and `TypeKeysTests`.

**Live (2026-10-27, Windows, a Debug build of the fix on main at batch 86, which has #2418, headless `claude -p` through the
hexide tools).**

| `press_key` | Logged | Text1 | The oracle |
|---|---|---|---|
| K, `modifiers` Shift | `D16/1 D75/1 P75 U75/1 U16/0` | `K` | KeyCode 75, KeyAscii 75 |
| K | `D75/0 P107 U75/0` | `k` | KeyCode 75, KeyAscii 107 |
| A, `modifiers` Ctrl | `D17/2 D65/2 P1 U65/2 U17/0` | | KeyAscii 1, once |
| X, `modifiers` Alt | `D18/4 D88/4 U88/4 U18/0` | | no KeyPress |
| Back | `D8/0 P8 U8/0` | | 8 in all three |
