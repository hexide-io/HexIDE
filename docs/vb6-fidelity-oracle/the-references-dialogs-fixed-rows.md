## The References dialog's fixed rows (2026-10-22, #1026, design-time)

**The question.** HexIDE's References dialog headed its list with one fixed row, "Visual Basic For Applications",
carrying OLE Automation's GUID. So the project's own OLE Automation reference never showed, and OK dropped it. The fix
needed to know what VB6's dialog lists, which had only been described from memory.

**The probe.** A Standard EXE whose `.vbp` has two references, OLE Automation then Microsoft Scripting Runtime, opened in
VB6's IDE through the console agent. Project > References was opened, each of the first four rows selected with the
arrow keys, and Space pressed on the first.

**What it shows:**
- **Three fixed rows head the list, checked:** Visual Basic For Applications (`VB98\VBA6.DLL`), Visual Basic runtime
  objects and procedures (`SysWOW64\msvbvm60.dll\3`) and Visual Basic objects and procedures (`VB98\VB6.OLB`). The
  `.vbp` has no line for any of them. Space on the first is refused: "Can't remove control or reference; in use".
- **The project's references follow, checked, in the file's order:** OLE Automation (`SysWOW64\stdole2.tlb`), then
  Microsoft Scripting Runtime. OLE Automation is an ordinary row, not one of the fixed ones. The unchecked libraries
  come after.
- **Priority arrows:** both are disabled on each fixed row. On OLE Automation, the first row below them, Up is disabled
  and Down enabled. So nothing moves above the fixed rows.
- **The dialog opens with the last checked row selected**, Microsoft Scripting Runtime here, not the first.
- **Two details HexIDE does not copy yet** (#1468): the title names the project's file, "References - probe.vbp", where
  HexIDE names the project; and the Language reads "English/Standard" on the fixed rows but "Standard" on OLE
  Automation, where HexIDE shows "English/Standard" for every row.


## The References dialog's Language line follows the library's LCID (2026-11-21, #1468, IDE, SP6)

**The question.** This is #1468's second point. VB6's References dialog reads "English/Standard" on its fixed rows and
"Standard" on OLE Automation, while HexIDE reads "English/Standard" on every row. The guess was that the line follows
the library's LCID, with 0 read as "Standard", but only four rows had been seen, all under LCID 9 or 0.

**The probe.** First, the guest's registry. Every library under `HKEY_CLASSES_ROOT\TypeLib` was counted by the LCID key
under each version: 452 registrations, all under LCID 0, 9 or 0x409 (448, 3 and 1). None is in another language.

Then a saved Standard EXE with no references was opened in VB6's IDE (VB6.EXE 6.0.9782, SP6), and Project > References
was opened. Its title read "References - gen1468.vbp", as part (a) found. Its list was read with `LB_GETTEXT` (325
rows). Four rows were then selected in turn with the arrow keys, and the dialog's static text was read after each.

| Row | Registered LCID | Location shown | Language shown |
|---|---|---|---|
| Visual Basic For Applications | 9 | `C:\Program Files (x86)\Microsoft Visual Studio\VB98\VBA6.DLL` | English/Standard |
| Visual Basic runtime objects and procedures | 9 | `C:\Windows\SysWOW64\msvbvm60.dll\3` | English/Standard |
| OLE Automation | 0 | `C:\Windows\SysWOW64\stdole2.tlb` | Standard |
| Microsoft Windows Installer Object Library | 0x409 | `C:\Windows\System32\msi.dll` | English/United States |

**What it shows:**
- **The Language line comes from the library's registered LCID, not a fixed "English".** The one library registered
  under 0x409 reads "English/United States".
- **All three rows fit "language/sublanguage"**: 9 (English, no sublanguage) reads "English/Standard", 0x409
  (English, United States) reads "English/United States", and 0 (no language) reads "Standard" alone. That is
  consistent with these rows only. It hasn't been tested on any other language.

**For HexIDE (reasoned from #1468, not checked here):** `ReferencesReferenceViewModel.Language` shows
"English/Standard" on every row. On these rows, VB6's follows the LCID.

**Not measured:**
- A library registered under a language other than English. The guest has none, so how VB6 names another language,
  and whether in English or in Windows' display language, is still open.
