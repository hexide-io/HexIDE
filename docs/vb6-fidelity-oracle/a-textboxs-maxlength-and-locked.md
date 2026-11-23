## A TextBox's MaxLength and Locked (2026-11-22, #5300, SP6)

**The question.** HexIDE's running TextBox had neither property: writing either raised 461. What do they do in VB6, from code, from the keyboard and from the clipboard?

**The probe.**
- **The form:**
  - `Text1`, a plain TextBox;
  - `Text2`, saved with `Locked = -1`, `MaxLength = 4` and `Text = "abcdefg"`.
- **The run:** `Sub Main` showed the form and set `Text1` up fresh for each row. It cleared `MaxLength`, set `Text`, then set `MaxLength` and `Locked` as the row needs, gave the box the focus with the caret at the end, and zeroed a count of `Change` events.
- **Keys** were sent with `keybd_event`: letters, Backspace, Delete, and Ctrl+V, Ctrl+C and Ctrl+X.
- **The build:** `/make` on vb6.exe SP6 as native code and as p-code, run on the guest's console. **All 28 rows logged the same in both.**

**Reading and writing the properties:**

| Statement | VB6 |
|---|---|
| a new TextBox | `MaxLength` 0, `Locked` False |
| `Text2`, as saved | `MaxLength` 4, `Locked` True, `Text` `abcdefg`, all seven characters |
| `MaxLength = 5`, `Locked = True` | no error; they read back 5 and True |
| `MaxLength = -1`, `MaxLength = 70000` | **380**, and `MaxLength` kept its value |
| `MaxLength = 2.6` | 3 |
| `MaxLength = "3"` | 3 |
| `MaxLength = "x"` | 13 |
| `Locked = 5` | True |

**`MaxLength`**, with `Locked` False:

| Row | Before | Action | Result | `Change` events |
|---|---|---|---|---|
| B1 | empty, `MaxLength` 5 | type `abcdefgh` | `abcde` | 5 |
| C1 | empty, `MaxLength` 5 | `Text = "abcdefgh"` | **`abcde`**, no error | 1 |
| C2 | `abcdefgh` (set before `MaxLength = 5`) | type `x` at the end | `abcdefgh`, unchanged | 0 |
| D1 | `abcdefgh`, `MaxLength` 0 | `MaxLength = 3` | `abcdefgh`, unchanged | 0 |
| E1 | `abc`, `MaxLength` 5, caret at 3 | `SelText = "defgh"` | **`abcde`**, no error | 1 |
| E2 | `abc`, `MaxLength` 5, `b` selected | type `xyz` | `axyzc` | 3 |
| F1 | empty, `MaxLength` 0 | type ten letters | all ten | 10 |
| F2 | `ab`, `MaxLength` 5, clipboard `PQRSTUV` | Ctrl+V | `abPQR` | 1 |
| H2 | `Text2` unlocked, `abcdefg`, `MaxLength` 4 | type `x` at the end | `abcdefg`, unchanged | |

**`Locked`**, with `MaxLength` 0, starting from `abc` with the caret at the end:

| Row | Action | Result | `Change` events |
|---|---|---|---|
| G1 | type `xyz` | `abc` | 0 |
| G2 | Backspace | `abc` | 0 |
| G3 | Ctrl+V | `abc` | 0 |
| G8 | Delete | `abc` | 0 |
| G4 | `SelText = "Q"` from code | **`abcQ`** | 1 |
| G5 | `Text = "zz"` from code | **`zz`** | 1 |
| G6 | select all, Ctrl+C | clipboard `abc`, text `abc` | 0 |
| G7 | select all, Ctrl+X | **clipboard `abc`, text `abc`**: copied, not removed | 0 |
| H1 | `Text2`, saved `Locked`, type `x` | `abcdefg` | |

**What it shows:**
- **`MaxLength` limits what code puts in as well as what's typed.** `Text =` and `SelText =` are cut to fit, without an error. Typing and pasting stop at the limit, and a paste keeps what fits.
- **But text already past the limit stays.** Lowering `MaxLength` below the text's length cuts nothing, nor does loading a `.frm` whose `Text` is longer than its `MaxLength`. Typing into such a box added nothing.
- **0 means no limit.** -1 and 70000 are 380, and a fraction rounds. Where between them 380 starts wasn't searched.
- **`Locked` stops only the user.** Typing, Backspace, Delete and paste change nothing and raise no `Change`. `Text =` and `SelText =` from code still work and raise `Change`.
- **Cut in a `Locked` box copies and keeps the text,** where an unlocked box would remove it (reasoned, not checked: Ctrl+X on an unlocked box wasn't run here).

**The obvious guess,** from a Windows edit control, whose limit applies only to typing, was that text set from code isn't cut. VB6 cuts it, for `Text =` and `SelText =` alike.

**HexIDE,** run on `upstream/main` `d15cca9d6` and not built yet: writing either property raises 461, and reading either is a compile error (#5300).

**Not measured:**
- a `MultiLine` box, where line breaks count towards the limit;
- a Unicode or double-byte character against the limit;
- `MaxLength` between 32768 and 69999;
- the right-click menu on a `Locked` box;
- whether a typed character refused by `MaxLength` raises `KeyPress`.

### MaxLength's range (2026-11-22, #5300, SP6)

**The question.** The rows above took `-1` and `70000` as out of range and `2.6` as 3. Where exactly does the range
end, how does a half round, and does a limit at the top of the range still let typing through?

**The probe.** The same form and build as above, `/make` on vb6.exe SP6 as native code and as p-code, run on the
guest's console. Under `On Error Resume Next`, for each value `v` in `Array(32767, 32768, 65535, 65536, 2147483647,
1.5, 2.5, -0.4, True)`, `Sub Main` set `Text1.MaxLength = 7`, then `Text1.MaxLength = v`. It logged `Err.Number`,
the `MaxLength` read back, and its `TypeName`. Then it logged `TypeName(Text1.Locked)`. Last, with `MaxLength` 65535 and
the box empty and focused, it typed `abc` with `keybd_event`. The two builds logged the same.

| `MaxLength = v` | `Err` | `MaxLength` reads back |
|---|---|---|
| 32767, 32768, 65535 | 0 | the value, a Long |
| 65536, 2147483647 | **380** | 7: the old value kept |
| `True` | **380** | 7 |
| 1.5, 2.5 | 0 | 2 and 2: **half to even** |
| -0.4 | 0 | 0 |

- **`Locked` reads back as a Boolean.**
- **Typing `abc` into a box with `MaxLength` 65535** put all three letters in.

**What it shows.** **`MaxLength` takes a Long from 0 to 65535,** rounding a fraction half to even, as a Long store
does. Anything outside that range raises 380 and changes nothing, and so does `True`, which is -1. The top of the range
is a working limit, not a sentinel: 65535 still lets typing through.

**Not measured:** `MaxLength` on a `MultiLine` box, and whether a limit above 32767 behaves differently with text
that long.
