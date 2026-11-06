## MousePointer's names and pointers (2026-10-22, #120)

**Names.** A form gave seventeen Labels one MousePointer value each, 0 to 15 and 99, written bare (`MousePointer = 4`
with no comment). The VB6 IDE opened the project in the guest's console session. Driven by keys, it opened the form's
code window, added a comment line so the form was changed, and saved it with Ctrl+S. The file it wrote:

| Value | Written | Value | Written |
|---|---|---|---|
| 0 | not written: the default | 9 | `9  'Size W E` |
| 1 | `1  'Arrow` | 10 | `10  'Up Arrow` |
| 2 | `2  'Cross` | 11 | `11  'Hourglass` |
| 3 | `3  'I-Beam` | 12 | `12  'No Drop` |
| **4** | **`4  'Icon`** | **13** | **`13  'Arrow and Hourglass`** |
| **5** | **`5  'Size`** | **14** | **`14  'Arrow and Question`** |
| 6 | `6  'Size NE SW` | 15 | `15  'Size All` |
| 7 | `7  'Size N S` | 99 | `99  'Custom` |
| 8 | `8  'Size NW SE` | | |

The four in bold had no member in HexIDE (#120). The other thirteen match the names it already had, which were
mined from VB98's Template tree or carried without a measurement until now.

**Pointers.** The same form, built as p-code and run in the console session. The pointer was moved to the middle of
each Label, and `GetCursorInfo`'s handle was compared with the shared handles `LoadCursor` gives for Windows' standard
cursors:

| Value | Cursor | Value | Cursor |
|---|---|---|---|
| 0 Default | `IDC_ARROW` | 9 | `IDC_SIZEWE` |
| 1 | `IDC_ARROW` | 10 | `IDC_UPARROW` |
| 2 | `IDC_CROSS` | 11 | `IDC_WAIT` |
| 3 | `IDC_IBEAM` | 12 | `IDC_NO` |
| **4 Icon** | **`IDC_ARROW`** | **13** | **`IDC_APPSTARTING`** |
| **5 Size** | **`IDC_SIZEALL`** | **14** | **`IDC_HELP`** |
| 6 | `IDC_SIZENESW` | 15 | `IDC_SIZEALL` |
| 7 | `IDC_SIZENS` | 99, no MouseIcon | `IDC_ARROW` |
| 8 | `IDC_SIZENWSE` | | |

On this Windows 10, `LoadCursor` returns nothing for `IDC_ICON` and `IDC_SIZE`. Both are obsolete. So Icon draws the
plain arrow, and Size draws the same pointer as Size All. Another Windows version may differ; that is not measured.

### The default on the form and every intrinsic control (2026-10-27, #1970)

**The probe.** One form with one control of each intrinsic type that has a MousePointer, none with a MousePointer
line: CommandButton, Label, TextBox, Frame, CheckBox, OptionButton, ComboBox, ListBox, HScrollBar, VScrollBar,
PictureBox, Image, DriveListBox, DirListBox, FileListBox and Data. Three more controls say it outright: a CommandButton
and a TextBox `MousePointer = 0 'Default`, and a Label `MousePointer = 1 'Arrow`. Built native and p-code, the program
read each control's MousePointer, and the form's. It then added one control of each type with `Controls.Add` and read
that too. The same form was opened in the VB6 IDE in the guest's console session, as above, given a comment line in its
code window, and saved with Ctrl+S.

| Read or written | vb6.exe |
|---|---|
| The form, with no MousePointer line | 0 |
| Each of the sixteen controls, with no line | 0 |
| The CommandButton and TextBox that say 0 | 0 |
| The Label that says 1 | 1 |
| Each of the sixteen types, added with `Controls.Add` | 0 |
| A Shape, a Line or a Timer, added with `Controls.Add` | 438: none of them has a MousePointer |
| The IDE's save: the two `MousePointer = 0 'Default` lines | **removed** |
| The IDE's save: the Label's `MousePointer = 1 'Arrow` | kept |

So the default is 0, "Default", on the form and every control, at design time and at run time. Native and p-code
agree, and the IDE never writes a 0. Among the IDE's other rewrites on that save:
- it added a `TabIndex` to every control that takes focus;
- it moved the windowless Labels and the Image after the windowed controls;
- it wrote out the Data control's defaults in full;
- it ended each `Begin` line with a space.
