## The MaskedEdBox: its mask, its Text and its keystrokes (2026-10-28, #1098)

**The question.** What do a MaskedEdBox's `Text`, `ClipText` and `FormattedText` read for a given mask, which assignments
does it refuse, and what does it do with a keystroke the mask doesn't allow?

**The probe.** MSMASK32.OCX 6.00.8418 (SP6) on a Standard EXE form, built native and p-code, which logged the same lines in
every case. The guest's locale is English (United Kingdom).
- **Properties** were set from code in `Form_Load`. Each assignment was followed by a log of the error it raised, if any,
  and the five readings. Every `Change` and `ValidationError` was logged as it fired.
- **Keystrokes** were real ones: `keybd_event` from a Timer, with the form in the foreground of the console session,
  logging `KeyDown`, `KeyPress`, `Change` and `ValidationError` as they fired.

**At design time**, with nothing set: `Mask` "", `Text` "", `MaxLength` 64, `PromptChar` "_", `PromptInclude` True,
`AllowPrompt` False, `Format` "", `AutoTab` False.

### The readings

With `Mask = "##-##"`:

| | `Text` | `ClipText` | `FormattedText` |
|---|---|---|---|
| just after the Mask is set | `__-__` | (empty) | `  -  ` (spaces for the prompts) |
| `Text = "12-34"` | `12-34` | `1234` | `12-34` |
| `Text = "1_-3_"` | `1_-3_` | `13` | `1 -3 ` |
| `PromptInclude = False`, then `Text = "12-34"` | **`1234`** | `1234` | `12-34` |
| `PromptInclude = False`, `Text = "13"` | `13` | `13` | `13-  ` |

- **`ClipText` is the characters typed, without literals or prompts.**
- **`FormattedText` is `Text` with each prompt shown as a space.**
- **With `PromptInclude` False, `Text` reads as `ClipText` does.** It also takes an assignment without the literals: `"13"`
  and `"1-3"` both gave `13`, filled from the left, and `"1_-3_"` gave `13` with `FormattedText` `1 -3 `.
- Setting `Mask` fills `Text` with prompts and literals, and drops what `Text` held (`12-34`, then `Mask = "###"`, gave
  `___`).
- `MaxLength` reads the mask's length (5 for `##-##`, 14 for `(###) ###-####`).

### The mask characters

Each row is `Mask`, then the assignment, then what `Text` and `ClipText` read.

| Mask | `Text =` | Result |
|---|---|---|
| `#` | a digit only | `"1a-34"` and `"12/34"` into `##-##`: **380** |
| `9` | a digit or a space | `"1 2"` into `999`: `1 2`, `ClipText` `1 2`; `"1a2"`: 380 |
| `?` | a letter | `"a1"` into `??`: 380 |
| `A` | a letter or digit | `"a1B"` into `AAA`: `a1B`; `"a b"`: 380 |
| `a` | a letter, digit or space | `"a b"` into `aaa`: `a b` |
| `&` | any character | `"+ !"` into `&&&`: `+ !` |
| `C` | any character or a space | `"+ !"` into `CCC`: `+ !` |
| `>` | what follows in capitals | `"ab"` into `>??`: **`AB`** |
| `<` | what follows in lower case | `"AB"` into `<??`: **`ab`** |
| `\` | the next character literally | `"#5"` into `\##`: `#5`, `ClipText` `5` |
| `(`, `)`, space, `-` | literals | `"(555) 123-4567"` into `(###) ###-####`: `ClipText` `5551234567` |
| `.`, `:`, `/`, `,` | literals here, under this locale | `12.34`, `12:34`, `01/02/2003` and `1,234` each kept their separator out of `ClipText` |

**Refused with 380, "Invalid property value", leaving `Text` as it was:**
- `Text` with the literals missing, too short or too long (`"1234"`, `"12-3"`, `"12-345"` into `##-##`);
- a character the mask doesn't allow at its position;
- `Text = ""` while `PromptInclude` is True. With it False, `""` empties `Text`;
- `PromptChar = ""` and `PromptChar = "ab"`;
- `MaxLength = 0`.

A `Text` made of prompts in the mask's shape is taken: `"__-__"`, and `"1_-3_"`.

### `MaxLength` without a mask, and two odd rows

- With `Mask` "", `MaxLength = 5` was taken. Then `Text = "abcdefgh"` was taken whole, and **`MaxLength` then read 8**.
- **Odd:** after `Text = "12-345"` into `##-##` was refused with 380, `MaxLength` read **6**, not 5, and kept reading 6
  until the mask was set again. Recorded, not explained.
- With a mask, `MaxLength = 3` was taken and read back 3, and `Text` stayed `__-__`.

### `SelText`

`Text` `12-34`, then `SelStart = 1` and `SelLength = 3`: `SelText` read `2-3`. Then **`SelText = "99"` gave `19-94`**:
the two characters went into the selection's placeholders, past the literal, and the third placeholder kept its `4`.

### `AllowPrompt`

With `AllowPrompt` True, `Text = "1_-34"` gave `ClipText` `134`: the prompt still isn't a character.

### Keystrokes

With the caret at the start of an empty `##-##`:

| Typed | Events, in order | `Text` after | `SelStart` |
|---|---|---|---|
| `1a2` | KeyDown 49, KeyPress 49, Change `1_-__`; KeyDown 65, KeyPress 97, **ValidationError `1a-__` at 1**; KeyDown 50, KeyPress 50, Change `12-__` | `12-__` | 3 |
| `12345` | four Changes, to `12-34`; then KeyDown 53, KeyPress 53, **ValidationError `12-34` at 5** | `12-34` | 5 |
| `12-34` | after `12` the caret is past the literal, so the `-` is **ValidationError `12--_` at 3**; then `3`, `4` | `12-34` | 5 |
| `1234`, Backspace | KeyDown 8, KeyPress 8, Change `12-3_` | `12-3_` | 4 |
| a second Backspace | KeyDown 8, KeyPress 8, Change `12-__` (the caret skips back over the literal) | `12-__` | 3 |
| `1234`, Home, Delete | KeyDown 36; then **Change `23-4_` before KeyDown 46** | **`23-4_`** | 0 |
| `ab` into `>??` | KeyPress 97, Change `A_`; KeyPress 98, Change `AB` | `AB` | 2 |
| `_1` into `##`, `AllowPrompt` False | the `_`: KeyDown 16, KeyDown 189, KeyPress 95, **ValidationError `__` at 0**; then Change `1_` | `1_` | 1 |
| `_1` into `##`, `AllowPrompt` True | the `_`: KeyPress 95, **Change `__`**; then Change `_1` | `_1` | 2 |
| `1 2` into `999` | Change `1__`, Change `1 _`, Change `1 2` | `1 2` | 3 |
| `abcdef`, no mask, `MaxLength` 3 | three Changes, to `abc`; then `d`, `e`, `f` each KeyDown and KeyPress, and **no Change and no ValidationError** | `abc` | 3 |

So:
- **A typed character the mask refuses is not entered.** It raises `ValidationError(InvalidText, StartPosition)`, whose
  `InvalidText` is the text as it would have been with the character placed there, and no `Change`. A character typed
  at the end of a full mask raises it too, with the text unchanged.
- **The caret steps over literals**, typing forwards and with Backspace. Typing the literal itself where a placeholder
  comes next is refused.
- **Delete shifts the rest of the entry left across the literal:** `12-34` became `23-4_`. Its `Change` fired before its
  `KeyDown` event. Recorded, not explained.
- **`KeyPress` fires for a refused character** as for an accepted one.
- Typing past `MaxLength` with no mask is silent: no `Change` and no `ValidationError`.

### `Change`

`Change` fired on every `Text` assignment, **including one refused with 380**: `Text = "1234"` into `12-34` fired `Change`
with `12-34`, unchanged. Setting `Mask` fired it, and so did setting `PromptChar`, with `Text` unchanged. Setting `PromptInclude` did not. Setting `Mask`
from a non-empty `Text` fired it more than once, the first time with the old `Text` (`(555) 123-4567`, then `__`). The
whole property probe fired 69 `Change` events. Its log is the reference for any count.

**Not measured yet:** what `Format` does. With `Mask` `##.##` and `Format` `0.00`, `Text = "12.30"` read `12.30`, and
nothing showed an effect. Nor `AutoTab`, `HideSelection`, a `PromptChar` other than `_` typed with `AllowPrompt` True, or
a paste.
