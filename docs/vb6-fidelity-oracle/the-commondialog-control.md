## The CommonDialog control (2026-10-17, #1084)

**The probes.** Found by the Notepad demo (`demo/notepad-forms`), which opens, saves and picks its font through a
CommonDialog. A form holding one `MSComDlg.CommonDialog`, named `dlg`, whose `Form_Load` logs each step to a file under
`On Error Resume Next` and then `End`s. The probes were built as p-code and run in the guest's console session on
Windows 10. A script in that session answered each dialog with `SendKeys`, finding it by its `DialogTitle` and taking
a screenshot of the font dialog before answering it. Native code was not run: the dialogs are COMDLG32's own, and
nothing in this probe is arithmetic.

**Types and defaults, before any dialog:**

| Read | Logged |
|---|---|
| `TypeName` of the control, FileName, Flags, FilterIndex, CancelError, FontSize, Color, FontName | `CommonDialog String Long Integer Boolean Single Long String` |
| FileName, FileTitle, Filter, FilterIndex, Flags, CancelError, DialogTitle, DefaultExt, InitDir, FontName, FontSize, FontBold, Color | empty, empty, empty, 0, 0, False, empty, empty, empty, empty, 8, False, 0 |

**Open and Save As:**

| Step | Answer | Logged |
|---|---|---|
| `CancelError = True`, `FileName = "keep.txt"`, ShowOpen | Esc | `32755 [Cancel was selected.]`, FileName `keep.txt`, FileTitle empty |
| `CancelError = False`, `FileName = "keep2.txt"`, ShowOpen | Esc | no error, FileName `keep2.txt` |
| Filter "Text Documents (*.txt)\|*.txt\|All Files (*.*)\|*.*", FilterIndex 1, DefaultExt `txt`, InitDir `App.Path`, Flags 0, ShowSave | types `abc`, Enter | FileName `C:\hexoracle-cd2\abc.txt`, FileTitle `abc.txt`, FilterIndex 1, Flags 0 |
| Flags `cdlOFNFileMustExist Or cdlOFNHideReadOnly` (4100), ShowOpen | types `sample.txt`, Enter | the full path, FileTitle `sample.txt`, Flags **6148** |
| FilterIndex 2 (All Files), Flags 0, ShowSave | types `noext`, Enter | FileName `...\noext.txt`, FilterIndex 2 |

So Cancel leaves every property as it was, with or without the error. 6148 is 4100 plus `cdlOFNPathMustExist` (2048):
Windows adds it to Flags for a FileMustExist Open. And DefaultExt is added to a name typed without an extension even
under the "All Files (*.*)" filter.

**Font and Color:**

| Step | Answer | Logged |
|---|---|---|
| Flags 0, ShowFont | Esc, after a screenshot | `32755`: on Windows 10 the font dialog shows with no Flags. It does not raise `cdlNoFonts` (24574) |
| Flags `cdlCFScreenFonts`, FontName Arial, FontSize 12, CancelError True, ShowFont | Esc | `32755`, FontName Arial, FontSize 12 |
| the same | Enter | no error, Arial, 12 (a Single), FontBold False, FontItalic False, Flags 1 |
| Flags `cdlCFBoth`, Courier New 10, ShowFont | Enter | Courier New, 10, Flags 3 |
| `Color = vbRed`, ShowColor | Esc | `32755`, Color 255 |
| Flags `cdlCCRGBInit`, `Color = vbRed`, ShowColor | Enter | Color 255, Flags 1 |

**The constants.** COMDLG32.OCX's type library holds 102 constants in seven enums. They were dumped with
`scripts/dump-typelib-constants.ps1` into `IDE/HexIDE.Runtime/Interpreter/Data/comdlg32-constants.json`. Three
were confirmed by the probe: `cdlCancel` is 32755, `cdlCFBoth` is 3, and `cdlOFNFileMustExist Or cdlOFNHideReadOnly`
is 4100.

**In HexIDE (#1084):**
- **Runs:**
  - ShowOpen, ShowSave and ShowFont, through the IDE's own pickers and font dialog. An automation client answers the
    pickers with `answer_next_file_dialog`.
  - Every row above for those three, apart from what follows.
  - The constants, in a project that references the control.
- **The CommonDialog lives only at run time.** In the designer and in the file it stays the block the form was read
  with, so a form holding one saves byte for byte.
- **The divergences:**
  - Avalonia's Open picker doesn't say which filter the user chose. After an Open dialog that offered more than one,
    reading FilterIndex is refused as not implemented, rather than reporting the one the program set. A Save picker
    reports it, outside the IDE.
  - The IDE's font dialog has no underline or strikeout, and FontUnderline and FontStrikethru are refused.
  - Windows sets `cdlOFNExtensionDifferent` when the extension differs from DefaultExt. That is not measured here and
    not set.
- **Not implemented:**
  - ShowColor, ShowPrinter and ShowHelp;
  - a CommonDialog control array;
  - the constants qualified by library or enum.
