# A control's Font object (#934)

Provenance: the measurement comment on hexide-io/HexIDE#934, reported as vb6.exe SP6,
with native and p-code agreeing, committed on PR #2279 at `87a0a2be`. Transcribed from
the issue brief; this run did not repeat the Windows probe.

| Probe | Observed result |
|---|---|
| `TypeName(X.Font)` for Form, Label, TextBox, CommandButton, CheckBox, OptionButton, ComboBox, ListBox, Frame, PictureBox, DriveListBox, DirListBox, FileListBox and Data | `Font` |
| No Font block | Name `MS Sans Serif`, Size Currency 8.25, Bold False, Italic False, Weight 400, Charset 0; FontSize Single 8.25 |
| Font on HScrollBar, VScrollBar, Timer, Shape, Line or Image | Error 438 |
| `v = Label1.Font` and `CStr(Label1.Font)` | String `MS Sans Serif`; Name is the default member |
| `Label1.Font = "Arial"` | Name becomes Arial |
| `Label1.Font Is Label1.Font` | True |
| `Label1.Font.Size = 12`, then `Label1.FontBold = True` | Both size accessors read 12; Font.Bold True and Font.Weight 700 |
| `Set Text1.Font = Label1.Font`, then `Label1.Font.Size = 9` | Text1.FontSize 9; both Font references are identical |
| `Set Command1.Font = sf` for `sf As New StdFont`, then `sf.Size = 20` | Command1.FontSize 20.25; Command1.Font and sf are identical |
| `Me.Font Is Label1.Font` | False |
| `Set Label1.Font = Nothing` | Error 380; original font retained |

The comment does not record a new StdFont's initial member values, object-member conversion
errors, native handles or font methods. The measured StdFont sharing case remains work for
the next slice; a constructor must not invent its initial values.
