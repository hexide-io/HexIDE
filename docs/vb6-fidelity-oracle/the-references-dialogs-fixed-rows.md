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

