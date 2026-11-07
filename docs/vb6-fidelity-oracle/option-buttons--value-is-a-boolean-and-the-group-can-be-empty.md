## Option buttons — `Value` is a Boolean, and the group can be empty (2026-08-22, #95)

Verified with a real form probe (`/make` an EXE, run it, `Form_Load` writes results under `On Error Resume
Next`). Form has `Option1`, `Option2`, `Option3` flat on the form — one group, no Frame — with `Option2`
carrying the designer's `Value = -1  'True`, plus a `Check1` for comparison. Each control's `Click` appends to
a log string so dispatch is observable.

| Probe | Result | Rule |
|---|---|---|
| `TypeName(Option1.Value)` | `Boolean` | **not** the check box's Integer |
| `TypeName(Check1.Value)` | `Integer` | the two controls are not the same shape |
| `Option2.Value` at load | `True` | the designer's `-1  'True` survives, and fires **no** Click |
| `Option1.Value = True` | `Err 0`, Option1 `True`, Option2 `False` | selecting one clears the group |
| ...its Click log | `C1;` | the **newly selected** one fires; the deselected sibling fires **nothing** |
| `Option2.Value = True` when already `True` | Click log empty | Click is a *transition*, not an assignment |
| `Option1.Value = False` on the selected one | `Err 0`, o1/o2/o3 all `False` | **honoured, not refused — the group is left with nothing selected**, and nothing is promoted in its place |
| ...its Click log | empty | deselection raises no event |
| `Option1.Value = False` when already `False` | `Err 0`, selection untouched | a no-op |

The two questions #95 left open both answer against the intuitive guess: a programmatic select **does** raise
`Click` (so the event cannot live in an `OnClick` override, which only a user reaches), and clearing the
selected member **is** allowed (so a group with nothing selected is a reachable state — from code only; a user
cannot click their way to it).

### Boolean coercion at the property boundary is the ordinary language rule

Probed separately on `Command1.Enabled`/`.Visible`, because nothing about it is option-button-specific:

| Probe | Result | Rule |
|---|---|---|
| `Enabled = 0` / `= 1` / `= 7` | `False` / `True` / `True` | **any non-zero** is True |
| `Enabled = 0.4` | `True` | non-zero, **not** "rounds to 0" |
| `Enabled = 100000` | `True` | no Integer range check |
| `Enabled = "False"` / `= "1"` | `False` / `True` | strings parse, as a Boolean literal or as a number |
| `Enabled = "banana"` | **Err 13**, property **unchanged** | trappable type mismatch |
| `Enabled = Empty` | `False` | Empty is zero |
| `Enabled = Null` | **Err 94** | Invalid use of Null |
| `Command1.Left = True` | `-1` (`Single`) | the reverse crossing: True widens to −1 |

These are exactly `CBool`'s rules, which the interpreter had already pinned — so `AvaloniaInteroperability`
calls `CBool` rather than growing a second opinion about them. Before #95 this whole row set threw a bare CLR
exception, not a VB6 error, for **every** boolean property on every control: `On Error Resume Next` could not
catch it.

### `Appearance` is readable AND settable at run time

| Probe | Result |
|---|---|
| `Option1.Appearance` (read) | `1` (`Integer`) |
| `Option1.Appearance = 0` | `Err 0`, reads back `0` |
| `Check1.Appearance` | same on both counts |

Worth pinning because the plausible assumption is the opposite one — several VB6 appearance-ish properties
*are* design-time only and raise Err 382/383 — and `OptionButton.Appearance` was registered alongside `Value`
in #95 on the strength of this measurement rather than on that assumption.

### `Check1.Value = True` is an error

`Err 380` (Invalid property value), and the value is left unchanged. A check box's `Value` is 0..2 and −1 is
outside it. This is the sharpest evidence that the two controls' `Value` properties cannot share a type: the
literal that *selects* an option button is *refused* by a check box.


