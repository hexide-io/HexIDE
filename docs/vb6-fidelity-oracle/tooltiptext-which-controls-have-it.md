## ToolTipText: which controls have it (2026-10-23, #1403)

**The question.** HexIDE declared ToolTipText on five controls: CheckBox, Image, Label, OptionButton and PictureBox.
Which of VB6's built-in controls have it, and how does it take a value?

**The probe.** One form holding one of each intrinsic control, a menu included, built with `/make` native and p-code
and run. Form_Load passed each control, and the form itself, to a procedure taking `o As Object`, so the member was
reached late-bound and a control without it answered rather than failing the build. The procedure logged:
- what `o.ToolTipText` read, and its `TypeName`;
- the value after `= "tip"`, `= 5`, `= Null` and `= ""`, with each error.

A second form, Form2, gave a CommandButton, a TextBox and a Frame a ToolTipText in the file, and read them back. Native
and p-code gave the same logs.

| Control | `o.ToolTipText` |
|---|---|
| CommandButton, TextBox, ComboBox, ListBox, CheckBox, OptionButton, Frame, PictureBox, Label, Image | a String, `""` |
| DriveListBox, DirListBox, FileListBox, Data | a String, `""` |
| HScrollBar, VScrollBar, Shape, Line, Timer, a Menu, the Form | **438** "Object doesn't support this property or method" |

On every control that has it, alike:

| Statement | VB6 |
|---|---|
| `= "tip"` | reads `tip` |
| `= 5` | reads `5`, a **String** |
| `= Null` | **13**, Type mismatch; the `5` stays |
| `= ""` | reads `""` |
| Form2's, from the file | `button tip`, `text tip`, `frame tip` |

**Two more readings from building it.** The first build named `Form2.HScroll1.ToolTipText` directly, and failed to
compile: "Method or data member not found". Form2 also gave its HScrollBar a ToolTipText line at first, and vb6.exe
would not load the form: "The property name ToolTipText in HScroll1 is invalid".

What these show:
- **The scroll bars have no ToolTipText.** #1403 had guessed they would; vb6.exe says not. The graphical Shape and Line,
  the Timer, a menu and a form have none either.
- **It is a String:** a number is stored as its text.

**What resisted explanation.** `ToolTipText = Null` is 13, where `Text1.Text = Null`, `Me.Caption = Null` and
`Label1.Tag = Null` are 94 (above, "Assigning to a control property", #768). A ComboBox's `Text = Null` is 13 too (#926).
No rule here says which String properties take Null as 13.

**In HexIDE (#1403):**
- ToolTipText is declared on the five it was missing from, the CommandButton, TextBox, ComboBox, ListBox and Frame. So
  the Properties window lists it, and a .frm line is read as the property rather than kept as text. It is written back
  where VB6 writes it. The DriveListBox, DirListBox, FileListBox and Data control are not implemented (#164).
- At run time it reads and takes values on those ten as measured, Null as 13 included. The tip shown is the value, and
  none where it is `""`.
- A control without it is "Method or data member not found" when named directly, as in vb6.exe's build. Reached
  late-bound, HexIDE stops with the same compile error where vb6.exe raises 438 at run time
  ([#1433](https://github.com/hexide-io/HexIDE/issues/1433)).
- An HScrollBar's ToolTipText line is kept as text, as any line HexIDE does not model, where vb6.exe refuses the form.

