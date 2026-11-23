## The comments VB6 writes after enumerated values (2026-11-21, #21, IDE, SP6)

**The question.** VB6 writes a comment naming an enumerated value after it in a `.frm`, as in `BorderStyle = 2
'Dash`. #21's findings list asks, as Q12, for the names of values its corpus didn't show:
- a form's `Appearance` and `MousePointer = 5`;
- a CheckBox's `Value = 2`;
- a Shape's `BorderStyle` 2 to 5 and `FillStyle` 2 to 7.

**The probe.** VB6.EXE 6.00.9782 (SP6) in a clone of the oracle VM, driven on its console. A hand-written form gave each
value with no comment:
- **the form:** `Appearance = 0`, `DrawStyle = 2`, `FillStyle = 3` and `MousePointer = 5`;
- **a CheckBox:** `Value = 2`;
- **six Shapes:** `F2` to `F7`, `FillStyle` 2 to 7, and `BorderStyle` 2 to 6, then 0 on `F7`.

VB6 loaded the form with no message and no log. A Replace All of a comment in the form's code dirtied it, and Ctrl+S
saved it.

**What VB6 wrote:**

| Line | Saved as |
|---|---|
| form `Appearance` | `0  'Flat` |
| form `DrawStyle` | `2  'Dot` |
| form `MousePointer` | `5  'Size` |
| CheckBox `Value` | `2  'Grayed` |
| `FillStyle` 2 to 7 | `2  'Horizontal Line`, `3  'Vertical Line`, `4  'Upward Diagonal`, `5  'Downward Diagonal`, `6  'Cross`, `7  'Diagonal Cross` |
| Shape `BorderStyle` 0, 2 to 6 | `0  'Transparent`, `2  'Dash`, `3  'Dot`, `4  'Dash-Dot`, `5  'Dash-Dot-Dot`, `6  'Inside Solid` |

The form's `FillStyle = 3` was written `3  'Vertical Line`, as the Shape's was.

- **Each comment names the value it follows.** That isn't true of every property: VB6 writes `DrawMode`'s name one
  value behind ([What VB6's designer saves for a Line](the-line-control.md#what-vb6s-designer-saves-for-a-line-2026-11-21-163-sp6)).
  `DrawStyle`, `FillStyle`, `BorderStyle`, `MousePointer` and `Value` don't do that.
- **HexIDE writes these the same.** The form VB6 saved was run through the corpus round-trip,
  `Forms_and_controls_round_trip_byte_for_byte` at main ef65c5d96, and came back `OK`, byte for byte.

### Not measured

- **`DrawStyle` 1 and 3 to 6,** and `MousePointer` values other than 5, whose names are in
  [MousePointer's names and pointers](mousepointers-names-and-pointers.md).
- **The release build.**
