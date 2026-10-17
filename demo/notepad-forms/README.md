# Notepad on forms

Windows' Notepad, as it was in Windows 98 and XP, written the way a VB6 developer would write it:
- a multi-line TextBox fills the window and follows it as it resizes;
- **Word Wrap** swaps two TextBoxes, because MultiLine and ScrollBars can't be changed at run time: `txtEdit(0)` has
  a horizontal scroll bar, so it never wraps, and `txtEdit(1)` has none, so it always does;
- File, Edit, Format and Help menus, with Notepad's shortcuts: Ctrl+N, Ctrl+O, Ctrl+S, Ctrl+X, Ctrl+C, Ctrl+V, Del,
  Ctrl+F, F3, Ctrl+A and F5 for the time and date;
- a CommonDialog for Open, Save As and Font, with `CancelError` to tell Cancel from OK;
- files read with `Open ... For Input`, `Line Input #` and `EOF`, and written with `Open ... For Output` and
  `Print #`, each on a number from `FreeFile`;
- a modeless Find dialog owned by the main window, with Match case and a direction, and F3 to find again;
- Cut, Copy and Paste through the `Clipboard` object;
- a "save the changes?" prompt in `QueryUnload`, which closing, New and Open all go through;
- the file named on the command line, opened as Notepad opens it.

## Files

| File | What it is |
|---|---|
| `NotepadForms.vbp` | The project. It starts at `frmNotepad`, and references the CommonDialog control, `COMDLG32.OCX`. |
| `frmNotepad.frm` | The editor: the two TextBoxes, the menus, the dialogs, and the file I/O. |
| `frmFind.frm` | The Find dialog. |

## Running it

- **In VB6:** `VB6.EXE /make NotepadForms.vbp`. It builds cleanly, native and p-code. The p-code build opens a file
  from its command line, finds a word with the Find dialog, wraps a long line, asks to save when it is closed, and
  saves without adding a line ending the text didn't have.
- **In HexIDE:** not yet. The form holds a CommonDialog, which HexIDE can't create yet (#1084), so the form is refused
  rather than run without it. The file statements and the `Clipboard` object are also still missing.

## Left out

**Undo.** Notepad's Undo is the edit control's own, and a VB6 program reaches it by sending the TextBox an
`EM_UNDO` message through a declared Win32 function, `SendMessage`. HexIDE doesn't call the Windows API, by design,
because it runs where there is no Windows API to call. An Undo written in VB6 itself would need the program to keep
its own history of the text, which no VB6 Notepad clone does, so the menu leaves it out instead.

**Print and Page Setup.** They need the `Printer` object, which HexIDE doesn't have.
