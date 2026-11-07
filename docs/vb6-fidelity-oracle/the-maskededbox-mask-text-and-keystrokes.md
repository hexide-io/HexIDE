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

### Defaults, TypeName and the default member

| Read at run time, nothing set | Logged |
|---|---|
| `TypeName` | `MaskEdBox` |
| BackColor, ForeColor | `&H80000005`, `&H80000008` |
| Appearance, BorderStyle, ClipMode | 1, 1, 0 |
| HideSelection, AutoTab, Enabled, TabStop | True, False, True, True |
| MousePointer, OLEDragMode, OLEDropMode | 0, 0, 0 |
| Font | MS Sans Serif, 8.25, not bold |
| Format, MaxLength | "", 64 |
| hWnd | non-zero |

- **The default member is `Text`.** With `Text` `12`, `v = m` read `12`, a String, and `m = "34"` set it.
- **`ClipMode`** takes 0 and 1. 2 is 380. With 1, `SelText` of the whole of `12-34` read `1234`, leaving out the
  literals, and `Text` and `ClipText` read as before.
- **`SelStart = 99` is taken as the end**, 5 for `##-##`. **`SelLength = 99`** from `SelStart` 1 is taken as 4. A
  negative value for either is 380.
- **`Format` shapes only `FormattedText`.** With no mask and `Format` `#,##0.00`, `Text = "1234.5"` read `1234.5` and
  `FormattedText` `1,234.50`, focused or not. Moving the focus in and out changed neither. Typing `98.7` read
  `FormattedText` `98.70`. **Odd:** with `Mask` `####.##` and `Format` `0.00`, typing `12` read `FormattedText`
  `____.12`, prompts and all, where a mask with no `Format` shows prompts as spaces. Recorded, not explained.
- **`AutoTab`:** with True, the keystroke that fills the mask moves the focus to the next control in tab order, raising
  `Validate` and then `LostFocus`. A key typed into a field already full, with AutoTab True, leaves the focus and the text
  where they are. With False, the focus stays.
- `Refresh` raises nothing.

### Every key, exhaustively

Each row starts from a fresh mask, focused, with the caret placed, and reads `Text`, `SelStart` and `SelLength` after.
VE is `ValidationError(InvalidText, StartPosition)`. 116 cases, native and p-code logging the same lines.

**Each mask character against each kind of key**, typed into a one-character mask. ✓ is entered, with a `Change` and the
caret to 1; ✗ is refused with `VE [the key] at 0`, no `Change`, and the caret staying at 0.

| Mask | `5` | `a` | `A` | space | `+` | `_`, the prompt |
|---|---|---|---|---|---|---|
| `#` | ✓ | ✗ | ✗ | ✗ | ✗ | ✗ |
| `9` | ✓ | ✗ | ✗ | ✓ | **✓** | ✗ |
| `?` | ✗ | ✓ | ✓ | ✗ | ✗ | ✗ |
| `A` | ✓ | ✓ | ✓ | ✗ | ✗ | ✗ |
| `a` | ✓ | ✓ | ✓ | ✓ | ✗ | ✗ |
| `&` | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ |
| `C` | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ |

- **`9` took a `+`** where `#` refused one. Recorded as measured. The property probe refused `Text = "1a2"` into `999`,
  and didn't try a sign.
- **The prompt character** is refused everywhere with `AllowPrompt` False. With True, `#` and `&` each took it.
- **`>` and `<`** convert a letter as it is typed: `a` and `A` into `>?` gave `A`, into `<?` gave `a`, and the same into
  `>A`, `<a` and `>&`. A digit goes where the character takes one: into `>?` 5 was refused, into `>A` taken.
- **`>?<?` has two places**, so typing `aBcD` gave `Ab`. `c` and `D` were each `VE [Ab] at 2`.
- **A literal at the caret**, in `\##`: typing `5` there went into the next place (`#5`, caret 2), and typing the literal
  `#` itself stepped over it (`#_`, caret 1, one `Change`).

**Typing inserts.** The key goes in at the caret, or at the next place after a literal, and pushes what follows to the
right, up to the first prompt. If no prompt takes the push, the key is refused, and the VE shows the pushed text cut to
the mask's length, at the mask's length:

| Start | Typed | `Text` after | `SelStart` | Events |
|---|---|---|---|---|
| `#####` `12___`, caret 0 | `9` | **`912__`** | 1 | Change |
| `#####` `1_3__`, caret 0 | `9` | **`913__`**: the push stopped at the prompt | 1 | Change |
| `#####` `12345`, caret 0 | `9` | unchanged | 0 | **VE `91234` at 5** |
| `##-##` `12-34`, caret 2, on the literal | `9` | unchanged | 2 | VE `12-93` at 5 |
| `##-##` `12-34`, caret 2, on the literal | `-` | unchanged | **4** | **Change** |
| `##-##` `1_-__`, caret 1 | `-` | unchanged | 1 | VE `1--__` at 1 |
| `##-##` `12-34`, caret 5, the end | `9` | unchanged | 5 | VE `12-34` at 5 |

The `-` typed on its own literal moved the caret to 4, not 3, and raised a `Change` with nothing changed. Recorded, not
explained.

**Backspace** removes the character before the caret, skipping literals, and pulls the rest of the entry left one place,
across literals. The caret lands where the character was:

| `Text`, caret | After | `SelStart` |
|---|---|---|
| `12-34`, 5 | `12-3_` | 4 |
| `12-34`, 4 | `12-4_` | 3 |
| `12-34`, 3 | **`13-4_`** | 1 |
| `12-34`, 2, on the literal | `13-4_` | 1 |
| `12-34`, 1 | `23-4_` | 0 |
| `12-34`, 0 | unchanged, no `Change` | 0 |
| `12345` in `#####`, 2 | `1345_` | 1 |

**Delete** removes the character at the caret and pulls the rest left the same way:

| `Text`, caret | After | `SelStart` |
|---|---|---|
| `12-34`, 0 | `23-4_` | 0 |
| `12-34`, 1 | `13-4_` | 1 |
| `12-34`, 3 | `12-4_` | 3 |
| `12345` in `#####`, 1 | `1345_` | 1 |
| **`1_-34`, 0** | **`34-__`**: the empty place closed up too | 0 |
| `12-34`, 2, on the literal | **refused**, `VE [123__] at 2` | 2 |
| `a1b2` in `?#?#`, 0 | **refused**, `VE [1___] at 0`: the pull would put a digit in a letter's place | 0 |

**A selection** is removed first, pulled left as by Delete, and then the key is typed:

| `12-34`, selected | Key | After | `SelStart` |
|---|---|---|---|
| 1 to 4 (`2-3`) | `9` | `19-4_` | 3 |
| all | `9` | `9_-__` | 1 |
| all | `a` | **unchanged, still selected**: `VE [a_-__] at 0` | 0, `SelLength` 5 |
| 1 to 4 | Backspace, or Delete | `14-__` | 1 |
| all | Delete | `__-__` | 0 |

**The caret:** from 0, Right twice stopped at 2, on the literal; from 4, Left twice also stopped at 2. End went to 5
with `1_-__`, the mask's end rather than the last character typed. Home went to 0.

**Paste**, Ctrl+V, inserts like typing, but takes each character it can, skips the rest, and cuts the overflow instead
of refusing:

| Start | Clipboard | After | `SelStart` | Events |
|---|---|---|---|---|
| empty `##-##` | `1234` | `12-34` | 5 | Change |
| empty | `12-34` | `12-34`: the clipboard's literal skipped | 5 | Change |
| empty | `12` | `12-__` | 3 | Change |
| empty | `1a34` | **`13-4_`**: the `a` skipped, with no VE | 4 | Change |
| empty | `ab` | unchanged | 0 | **Change, and one VE, `[b_-__] at 0`** |
| empty | `123456` | `12-34` | 5 | Change, VE `12-34` at 5 |
| `12-34`, caret 3 | `9` | **`12-93`**: the `4` pushed off and lost | 4 | Change, VE `12-93` at 5 |
| `12-34`, all selected | `98` | `98-__` | 3 | Change |
| `12-34`, ClipMode 1 | `12-34` | `12-34` | 5 | Change |
| no mask, `abc`, caret 1 | `XY` | `aXYbc` | 3 | Change |

The paste of `ab` raised one VE, for its last character, and a `Change` with nothing changed. Recorded, not explained.

**Copy and cut**, Ctrl+C and Ctrl+X, with the whole entry selected unless stated:

| `Text` | ClipMode | Clipboard | `Text` after |
|---|---|---|---|
| `12-34`, copy | 0 | `12-34` | unchanged |
| `12-34`, copy | 1 | `1234` | unchanged |
| `1_-34`, copy | 0 | `1_-34` | unchanged |
| `1_-34`, copy | 1 | **`134`**: no literals, no prompts | unchanged |
| `12-34`, cut 1 to 4 | 0 | `2-3` | `14-__` |
| `12-34`, cut 1 to 4 | 1 | `23` | `14-__` |

**`SelText =`** inserts at the selection as typing would, but **cuts the overflow with no error and no VE, drops a
character the place refuses, and leaves `SelStart` at 0**:

| `12-34`, selection | `SelText =` | After |
|---|---|---|
| caret 0 | `9` | **`91-23`**: the `4` lost |
| caret 3 | `99` | `12-99` |
| 1 to 4 | `a` | `14-__`: the selection removed, the `a` dropped |
| all | `9876` | `98-76` |
| all | `98-76` | `98-76` |
| all | "" | `__-__` |
| 1 to 4 | `123456` | `11-23` |

**With no mask** it is a text box that inserts: `abc` with the caret at 1, typing `x`, gave `axbc`; Backspace gave `bc`.

### The saved files

VB6's IDE re-saved a hand-written form with four MaskEdBoxes, after a comment typed into its code made it dirty. Both are
in `corpus/inbox-controls/`: VB6's in `pending/maskedbox/`, the input in `input/maskedbox/`.
- The form's line is `Object = "{C932BA88-4374-101B-A56C-00AA003668DC}#1.1#0"; "MSMASK32.OCX"`. The block is
  `Begin MSMask.MaskEdBox`, with `_Version` 393216. There is no `.frx`.
- **Read without extents**, a block was given Height 1125 and Width 3000, `_ExtentX` 5292 and `_ExtentY` 1984: 200 × 75
  pixels.
- **`PromptChar` is always written**, `"_"` included. A block with nothing else set saved only that, after `_Version`.
- **The control's own lines, in VB6's order** (from a block that had them all off their defaults, written reversed):
  BorderStyle, ClipMode, MousePointer, Appearance, BackColor, ForeColor, PromptInclude, AllowPrompt, AutoTab,
  HideSelection, Enabled, MaxLength, OLEDragMode, OLEDropMode, the `Font` block, Format, Mask, PromptChar, and then
  **OLEDragMode and OLEDropMode again**. Each was written twice, with the same value. A default is not written.
- `MaxLength` was written when set, with a mask (5 for `##-##`) and without one (10).
- **`Text` is not saved.** VB6 dropped the `Text` line from every block, with a mask and without.
- **The Properties window has no `Text` row**, so it can't be set at design time. Its 41 rows, alphabetic: (About),
  (Custom), (Name), AllowPrompt, Appearance, AutoTab, BackColor, BorderStyle, CausesValidation, ClipMode, DataBindings,
  DataField, DataFormat, DataMember, DataSource, DragIcon, DragMode, Enabled, Font, ForeColor, Format, Height,
  HelpContextID, Index, Left, Mask, MaxLength, MouseIcon, MousePointer, OLEDragMode, OLEDropMode, PromptChar,
  PromptInclude, TabIndex, TabStop, Tag, ToolTipText, Top, Visible, WhatsThisHelpID, Width. **HideSelection isn't among
  them**, though a block that sets it saves it.

### The type library

`corpus/inbox-controls/typelib/MSMASK32-*.jsonl`, dumped on the oracle VM by `scripts/dump-typelib-*.ps1`. The class is
`MaskEdBox`, default interface `IMSMask`, events `MaskEdBoxEvents`.
- **33 properties:** ClipMode, PromptInclude, AllowPrompt, AutoTab, MousePointer, HideSelection, MaxLength, Format, Mask,
  FormattedText, SelLength, SelStart, SelText, ClipText, PromptChar, Text, MouseIcon, Appearance, BackColor, Font,
  ForeColor, Enabled, hWnd, BorderStyle, OLEDragMode and OLEDropMode. **Hidden:** FontBold, FontItalic, FontName,
  FontSize, FontStrikethru, FontUnderline, and `defaultText`, the default member.
- **Methods:** Refresh, OLEDrag, and AboutBox (hidden).
- **Events:** Change, ValidationError(InvalidText, StartPosition), KeyDown, KeyPress, KeyUp, and the six OLE drag events.
  There is no Click and no mouse event.
- **The constants** (`MSMASK32-constants.jsonl`): ClipModeConstants mskIncludeLiterals 0, mskExcludeLiterals 1; the
  mouse pointers msk… 0 to 15 and mskCustom 99; mskNone/mskFixedSingle, mskFlat/mskThreeD; the OLE drag and drop
  constants; the error numbers mskInvalidPropertyValue 380 and the like; and the Format picker's 12, from
  mskDefaultFormat 0 to mskhhmm 11.

**Not measured:** `HideSelection`'s effect, a `PromptChar` other than `_` typed with `AllowPrompt` True, drag and drop, and
the drawing beyond the designer's.
