## A quote inside a .frm string (2026-10-22, #218)

**The question.** How does VB6 read and write a `"` inside a string property of a `.frm`? HexIDE kept both quotes of
a doubled pair on read, and wrote a lone quote unescaped. Its reader and writer agreed with each other, so its own
files round-tripped. They disagreed with VB6 in both directions. The `.vbp` reader already halved doubled quotes, but
a `.vbp` and a `.frm` come from different writers inside VB6, so this was measured, not assumed.

**The probe.** A Standard EXE, built with `vb6.exe /make` as p-code (`CompilationType=-1`), on 6.00.8176, the original release. Its form
gave these string properties a doubled quote:
- the form's `Caption` and `LinkTopic`;
- five Labels' `Caption`s, one with a `Tag`;
- a CommandButton's `ToolTipText`;
- a TextBox's `Text`.

`Form_Load` wrote each value and its `Len` to a file, then ended. A second copy of the form wrote one Label's
`Caption` as HexIDE did, with its quotes single. The first form was then opened in the VB6 IDE, dirtied with a
comment line in its code, and saved with Ctrl+S.

**Read: a doubled quote is one quote, in every string property tried.**

| In the `.frm` | Read back | `Len` |
|---|---|---|
| `Caption = "He said ""hi"""` | `He said "hi"` | 12 |
| `Caption = """"` | `"` | 1 |
| `Caption = """Start"` | `"Start` | 6 |
| `Caption = "End"""` | `End"` | 4 |
| `Caption = """"""` | `""` | 2 |
| form `Caption = "F""orm"` | `F"orm` | 5 |
| form `LinkTopic = "Link""Topic"` | `Link"Topic` | |
| `Tag = "t""g"` | `t"g` | |
| CommandButton `ToolTipText = "tip""s"` | `tip"s` | |
| TextBox `Text = "a""b"` | `a"b` | 3 |

**Write: the IDE doubles every quote.** Its save wrote all ten string lines exactly as the probe had them. The save
did not keep the probe's control order. It moved the CommandButton and the TextBox ahead of the five Labels, giving
the Labels TabIndex 2 to 6. That was not examined further.

**A quote written single breaks the form, and the build fails.**
- `Caption = "He said "hi""`: `/make` exits 1.
- Its `/out` log reads `Errors during load. Refer to '…\Form1.log' for details`, then `'…\Form1.frm' could not be
  loaded.`, then `Build of 'probe.exe' failed.`
- `Form1.log` holds `Line 13: Syntax error: property hi"" in lbl1 was missing an '='.`

So the reader ends the string at the second quote, and takes the rest of the line for another property. The log
names line 13, one past the Caption's line 12.

**Not measured:**
- A font's `Name` with a quote, which names no real face. HexIDE reads and writes it through the same code as every
  other string.
- Strings VB6 keeps in the `.frx`, such as a multi-line TextBox's `Text`, which are stored with a length rather than
  quoted.
- What the VB6 IDE does when opening the malformed form.

HexIDE now halves a doubled quote on read and doubles each quote on write (`VbFrmFormatDeserializer.ParseValue`,
`VbFrmFormatSerializer.WriteSimpleType`), and `FrmStringQuoteTests` holds the table. A side finding: a CommandButton
did not model `ToolTipText` in HexIDE, so its line was kept as unknown text, and the property couldn't be set. #1403
measured which controls have it (next section).

