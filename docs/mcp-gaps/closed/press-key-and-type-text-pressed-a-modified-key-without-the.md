# `press_key` and `type_text` pressed a modified key without the modifier's own KeyDown and KeyUp — **CLOSED** (#2422, 2026-10-27)

**Symptom (2026-10-27, hexide-frm's live check of #2418, the TextBox key events).** `press_key` with `modifiers`, and
`type_text` in keyboard mode, sent the key with its modifiers as flags and never the modifier's own key. A person's
Shift+K is Shift down, K down, K up, Shift up. The oracle's TextBox key table measures that pair, Shift 16, Ctrl 17
and Alt 18 with mask 1, 2 or 4 while held and mask 0 on their own release, and none of it could be driven:
`press_key(key: "A", modifiers: "Ctrl")`, `press_key(key: "X", modifiers: "Alt")` and
`type_text(text: "K", mode: "keyboard")` each raised the main key's events alone. The keyboard twin of #2407.

**Fixed (#2422).** Each modifier's own key goes down before the key, in the order Ctrl, Shift, Alt, Meta, holding
what is down so far, and up after it in reverse, holding what is still down. The window input, the routed fallback
and `type_text`'s keyboard mode all send the same chord. A modifier pressed alone reports itself held going down and
not coming up, as Windows reports it. The replies name the keys: `pressed Shift+K; LeftShift went down before K and
up after it, as a person holds it`, and `type_text`'s `LeftShift went down before the capital and up after it`.
Guarded by `ModifierKeyChordTests`. A press of a key with nothing typed, such as Shift+K through `press_key`, is
#2428.

**Live (2026-10-27, Windows, a Debug build of this branch merged locally with #2418, headless `claude -p` through the
hexide tools).** Text1's KeyDown, KeyPress and KeyUp each logged a token: `D<KeyCode>/<Shift>`, `P<KeyAscii>`,
`U<KeyCode>/<Shift>`.

| Call | Logged | The oracle |
|---|---|---|
| `press_key` K, `modifiers` Shift | `D16/1 D75/1 U75/1 U16/0` | the same, with KeyPress 75 before the KeyUp: #2428 |
| `press_key` A, `modifiers` Ctrl | `D17/2 D65/2 P1 U65/2 U17/0` | KeyCode 65, KeyAscii 1 |
| `press_key` X, `modifiers` Alt | `D18/4 D88/4 U88/4 U18/0` | KeyCode 88, no KeyPress |
| `press_key` LeftShift | `D16/1 U16/0` | Shift 16, mask 1 held and 0 on release |
| `type_text` "Kk", keyboard mode | `D16/1 D75/1 P75 U75/1 U16/0 D75/0 P107 U75/0` | K and k both 75, KeyAscii 75 and 107 |
