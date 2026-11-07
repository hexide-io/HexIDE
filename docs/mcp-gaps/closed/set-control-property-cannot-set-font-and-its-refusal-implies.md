# `set_control_property` cannot set Font, and its refusal implies a text form that does not exist

**Symptom.** (Linux, openSUSE Leap 16 under XWayland, Debug build of main at b15c7cf, 2026-10-13.)

```
set_control_property {"formName":"Form1","controlName":"Label0","property":"Font","value":"DejaVu Sans"}
→ {"success":false,"error":"Cannot set 'Font' to 'DejaVu Sans': it takes a value the Properties window accepts for a VBFont"}
```

The Properties window's Font row read "DejaVu Sans", the string refused, and that row is read-only beside a "..."
button, so the set of values it accepts is empty. The description promises that a refused value's reply lists
what the property takes, and here it does not. By code reading every `byte[]` property (Picture, Icon,
MouseIcon, DragIcon, OleObjectBlob, List, ItemData) falls through the same way, refused as "...accepts for a
Byte[]". Cross-platform by code reading.

**Workaround.** For Font, the Properties window: select the control, `interact` `invoke` on the Font row's
`PART_OpenFontWindowButton`, then drive the Font dialog, which the run did successfully. For the `byte[]`
properties none is known: the grid gives them only a generic text box, and what it does with a typed value was
not checked.

**Suggested fix.** Accept Font as the grid shows it (a family, optionally a size and style words), merged with the
current font so what is not given is kept. Never let a refusal promise a text form the parser refuses: name the
Font dialog for Font, and say that no automation route exists yet for the `byte[]` properties.
[#952](https://github.com/hexide-io/HexIDE/issues/952).

> **Fixed.** `set_control_property` sets Font from the text `get_form_controls` shows for it: parts separated by
> commas, the face first, such as `Tahoma, 9.75 pt, bold, italic, charset 0`, with `regular`, `underline`,
> `strikethrough` and `weight N` too, each at most once. A part not given keeps the current font's, so `12 pt`
> changes the size alone; a new face without a charset takes charset 0, as the Font dialog gives it, so `DejaVu Sans`,
> as the run sent it, is that face in the ANSI charset; and when any style word is given, the words given are the
> whole style, so `regular` clears it. The text `get_form_controls` shows names every part, so it sets another
> control to exactly that font, and its row is `settable` only when it does. Anything that does not say one font is
> refused rather than read as some font: a part given twice (`9,75 pt` is two sizes), a size or a style mistyped as
> the face (`Arial 12`, `bold italic`), a size past VB6's 2160 points or finer than four decimals, a face longer than
> 31 characters or holding a quote. The reply's note gives the whole font, not its face. A refusal names this form
> for a Font, and for a `byte[]` property says it has no text form and no automation route yet. A control with no
> font of its own merges with HexIDE's default, which the designer draws it in, not its form's: HexIDE has no ambient
> font. Guarded by `PropertyTextTests`.
>
> **Two corrections, later.** A new face's charset of 0 was wrong for a symbol or OEM face: `Wingdings` saved
> Charset 0, where VB6 gives 2, and the control drew letters. A new face given without a charset now takes the one
> VB6's FontName gives it, and a face equal to the current one but for case keeps its charset
> ([#1310](https://github.com/hexide-io/HexIDE/issues/1310)). And the guess against a mistyped size or style also
> refused real faces, `Wingdings 2` and `MS Shell Dlg 2` among them, so a control in one showed a font that could not
> be set back. The control's own face, and one FontName takes here, are now faces whatever their names end in, and a
> refusal of the guess says why ([#1311](https://github.com/hexide-io/HexIDE/issues/1311)).
>
> **Live (#1330, 2026-10-25).** Windows, a Debug build of main at beada72, from a Claude Code session's hexide tools,
> with two Labels added to a new project's Form1:
>
> ```
> set_control_property {"formName":"Form1","controlName":"Label1","property":"Font","value":"Tahoma, 12 pt, bold"}
> → Label1.Font is now Tahoma, 12 pt, bold, charset 0, and the file is saved.
> get_form_controls {"formName":"Form1","controlName":"Label1"}
> → {"name":"Font","value":"Tahoma, 12 pt, bold, charset 0","settable":true}
> set_control_property {… "controlName":"Label2","property":"Font","value":"Tahoma, 12 pt, bold, charset 0"}
> → Label2.Font is now Tahoma, 12 pt, bold, charset 0, and the file is saved.
> ```
>
> `take_snapshot` drew Label1 in it: the ink of its text is 76 pixels wide at 150% scaling, where Tahoma's ink for
> `Label1` at that size is 65 regular and 76 bold. The saved `Form1.frm` gives both Labels `Name = "Tahoma"`, `Size = 12`, `Charset = 0` and
> `Weight = 700`.
