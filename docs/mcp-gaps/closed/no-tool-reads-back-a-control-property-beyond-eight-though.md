# No tool reads back a control property beyond eight, though `set_control_property` sends callers to `get_form_controls` for them

**Symptom.** (Linux, openSUSE Leap 16, Debug build of main at b15c7cf, 2026-10-13.)

```
get_form_controls {"formName":"Form1"}
→ Label0: name, type VB.Label, left 120, top 10, width 100, height 20, caption "Centred &label", visible, enabled
get_file_content {"name":"Form1"}
→ the code section only, not the form's property block
```

No Alignment, AutoSize, WordWrap, BorderStyle, UseMnemonic or Font, so none of them can be read back after
`set_control_property` sets them; the label's Alignment had to be inferred from its caption. `get_locals {}` while
paused listed Label0 without them too. `get_form_controls` describes itself as returning "key design-time
properties", which is accurate; what is not is `set_control_property`'s "Use get_form_controls to see available
controls and properties".

**Since #273 task 3.18 the `get_file_content` line reads differently, and the gap is narrower.** That run was on
main, where `get_file_content` returned a form's code alone. With #273 it returns the form's whole file, its
designer block included, and the block follows the designer: #273's live check on 2026-10-13 saw
`move_control {"formName":"Dialog","controlName":"OKButton","left":300}` reach the open code window's header as
`Left = 4500`, and after a save, which `set_control_property` makes, the block is the one written. So a property
the `.frm` records can now be read back, in the file's own spelling: twips for a position, a Font as its
`BeginProperty Font` block, a `Byte[]` property as a reference into the `.frx`. That is not the Properties
window's text, which is what `set_control_property` takes, and it is only what the file records.

**Workaround.** With #273, read the control's `Begin` block in `get_file_content {"name":"Form1"}`, as the file
spells it. For the Properties window's own spelling, not exercised in that run: select the control in the designer
and read the Properties window's rows with `dump_visual_tree` and `interactiveOnly: false`, or `inspect_element` on
a row.

**Suggested fix.** An optional `controlName` on `get_form_controls` that returns every property the Properties
window shows for that control, under its name there and in the text form `set_control_property` accepts, and a
corrected `set_control_property` description. [#923](https://github.com/hexide-io/HexIDE/issues/923).

> **Fixed.** `get_form_controls` takes `controlName`, a control's name, the form's own, or a control array's element
> as `name(index)`. The reply lists that control alone, with `properties`: every property the Properties window
> shows for it, by the name there, with its value as text (an enum as `1 - Opaque`, a colour as its literal, a
> number with `.` in any culture; a font as its face, size, weight and style, a picture as its size or `(None)`)
> and `settable`, whether `set_control_property` takes that text back. Every row marked settable, on every built-in
> control, the Line and the Menu, sets the value it shows; a font and a picture are set through their dialogs.
> What the window's rows alone would hide is in the reply too, each not settable and with a `note`: a value the
> file holds that HexIDE could not read (a palette colour, #786), given as the file's text rather than the default
> the window shows; a property HexIDE does not model (a TextBox's MultiLine, a control array's Index), as a row of
> its own; and a Timer's Width, Height and Visible, which the file does not keep. A control array named without an
> index answers with its first element, and the reply's `note` says so. A Menu no longer has Left, Top, Width or
> Height, which a caller could set and the save wrote under names VB6 gives no menu. `set_control_property`'s
> description now sends callers to `controlName`. Guarded by `PropertyTextTests`.
