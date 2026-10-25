## The fonts a form names (2026-10-17, #1132, #1133)

**The probe.** A form with four Labels at Size 12, built native and run on the oracle guest's console session, and
screenshotted:
- "PMJKLN" in Wingdings, which the .frm records as Charset 2, SYMBOL_CHARSET;
- "abrtu" in Marlett, Charset 2;
- "Courier iiii" in Courier New;
- "Times iiii" in Times New Roman.

| Label | vb6.exe draws |
|---|---|
| Wingdings `PMJKLN` | a flag, a bomb, a smiling, a neutral and a frowning face, and a skull |
| Marlett `abrtu` | two ticks, a cross, an up arrow and a down arrow |
| Courier New | monospaced: each `i` takes a full cell |
| Times New Roman | serif |

So each font draws in its own family, and a symbol font's letters are its symbols. GDI does that by mapping each
character of a SYMBOL_CHARSET font into the font's private-use range: `P` is U+F050.

**In HexIDE:**
- **The family:** fixed (#1132). Every font used to draw in one sans serif, because the family was looked up only in the
  bundled collection, which holds no fonts. Now it is looked up among the installed fonts first.
- **The symbols:** the same as vb6.exe for both Labels, where the font is installed (#1133). A caption in a font whose
  Charset is 2 is drawn at U+F000 plus each character's ANSI byte, as GDI maps it, and still reads back as the letters
  the program wrote. That covers a Label and the captions of a CommandButton, CheckBox and OptionButton, in the
  designer and at run time. Not yet: a Frame's caption, list items, a TextBox's text, and `Print` on a form.
- **Two things stood in the way, neither of them VB6's.** Avalonia reads a symbol font's family name a byte at a time,
  so Wingdings came back as `\0W\0i\0n...` and never matched the name asked for. And Marlett, named with the fallback
  families after it, was passed over for the platform's default face, which has none of its glyphs, and drew boxes;
  named alone it resolves. So an installed family is now named alone.
- **On a machine without the font**, as Linux and macOS are for Wingdings and Marlett, the letters are drawn as before:
  the mapped characters would draw as empty boxes. What to draw there is the open half of #1133.

**Setting the font at run time.** A second probe, native and p-code agreeing: 18-point Labels changed in `Form_Load`,
read back, then shown and screenshotted.

| Probe | Logged | Drawn |
|---|---|---|
| Arial Label, read `FontName`, `TypeName`, `Font.Charset` | `Arial String 0` | |
| `FontName = "Wingdings"` on it | `Wingdings`, `Font.Name` Wingdings, **Charset 2** | the flag, the bomb and the face |
| Wingdings Label (Charset 2), `FontName = "Arial"` | `Arial`, **Charset 0** | `PMJ` |
| `FontName = "Wingdings"`, then `"Arial"` | `Arial`, Charset 0 | `PMJ` |
| `Font.Name = "Wingdings"` on an Arial Label | **`Arial`**, Charset 0, no error | `PMJ` |
| `FontName = "Wingdings"`, then `Font.Charset = 0` | **`Arial`**, Charset 0 | `PMJ` |
| `FontName = "NoSuchFont123"` | **380**, "Invalid property value"; FontName stays `Arial` | |
| `FontName = ""` | 380; FontName stays `Arial` | |

FontName picks the font's own charset, 2 for Wingdings and 0 for Arial. `Font.Name` doesn't. Wingdings at Charset 0
isn't Wingdings at all: GDI picks another face, and the name then reads back as that face. Two obvious guesses are
wrong here: that `Font.Name` and FontName are the same assignment, and that an assignment raising no error took
effect.

**Which names FontName takes.** A third probe set FontName on an Arial Label at 12 point to each name in turn, from
Arial each time, and read back FontName, `Font.Charset` and FontSize. Native and p-code agree.

| Names | Logged |
|---|---|
| MS Sans Serif, MS Serif | taken, Charset 0, Size 12 |
| System; Courier | taken, Charset 0, **Size 9.75** |
| Terminal | taken, **Charset 255**, Size 9 |
| Fixedsys | taken, Charset 0, Size 9 |
| Small Fonts | taken, Charset 0, Size 8.25 |
| Modern, Roman, Script | taken, **Charset 255** |
| MS Shell Dlg, MS Shell Dlg 2, Microsoft Sans Serif, Segoe UI, Tahoma, Courier New, MS Gothic | taken, Charset 0 |
| Helvetica, Times | taken, Charset 0, and read back as written |
| `arial`; `WINGDINGS` | read back **`Arial`**; **`Wingdings`**, Charset 2 |
| Webdings, Symbol, Marlett | taken, Charset 2 |
| `5`; `"  Arial  "` | 380; 380, with no trimming |
| Null | 94 |
| `FontBold = True`, then FontName | the bold stays |
| `Command1.FontName = "Wingdings"` | taken, Charset 2 |
| `Me.FontName = "Courier New"` | taken; a Label on the form keeps MS Sans Serif |

**The size is snapped to one a bitmap font has.** The 12 points became 9.75 under System and Courier, 9 under Terminal
and 8.25 under Small Fonts, and setting Arial back afterwards didn't restore them: every later row read 8.25. That is
why the Size column stops at Small Fonts. FontName returns the family's own spelling, except for the names Windows
substitutes, Helvetica and Times, which read back as written.

**In HexIDE (#1141):**
- FontName reads and sets on every control whose font HexIDE holds, and on the form, which leaves its controls'
  fonts alone. The charset comes with it: 2 for a symbol font, found by its glyphs being at U+F061 rather than at `a`;
  255 for the four OEM names above; otherwise 0. The size and weight stay. `""`, padding, `5` and a missing family are
  380, and Null is 94.
- **Which families count as the machine's** is a decision, because HexIDE runs where Windows' fonts mostly aren't. A
  family installed here by its exact name counts, and so does any text family from the table above, drawn in a
  substitute where it isn't installed, as a form naming it at design time is. A symbol font has no substitute, since
  letters aren't its glyphs, so one that isn't installed is 380: VB6's own answer on a machine without it, which a
  program can trap. Wingdings by name is therefore 380 on Linux.
- Not modelled: the size snapping for a bitmap font. HexIDE can't read a size yet, so nothing reads wrong. FontSize,
  FontBold and the other font members are still 461.

