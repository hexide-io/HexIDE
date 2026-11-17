## The Object Browser's library list: its order (2026-11-16, #4113, design-time, IDE, SP6)

Measured on vb6.exe SP6's IDE in the oracle guest's console session.

**The setup:** a Standard EXE project, `ObProbe`, whose `.vbp` names its libraries in an order that is not alphabetical:
- `Reference=` lines in this order: OLE Automation (`stdole`), Microsoft Scripting Runtime (`Scripting`), Microsoft Windows Common Controls 6.0 (`MSComctlLib`);
- then `Object=` lines: `TABCTL32.OCX` (`TabDlg`), `comdlg32.ocx` (`MSComDlg`), `MSCOMCTL.OCX` (`MSComctlLib` again) and `RICHTX32.OCX` (`RichTextLib`).

The project loaded with nothing missing. I opened the Object Browser with F2 and read its library dropdown's rows through Win32 (`CB_GETLBTEXT`).

| Row | Library |
|---|---|
| 0 | `<All Libraries>`, selected |
| 1 | `MSComctlLib` |
| 2 | `MSComDlg` |
| 3 | `ObProbe`, the project itself |
| 4 | `RichTextLib` |
| 5 | `Scripting` |
| 6 | `stdole` |
| 7 | `TabDlg` |
| 8 | `VB` |
| 9 | `VBA` |
| 10 | `VBRUN` |

**The readings:**
- **`<All Libraries>`, then every library in case-insensitive alphabetical order,** whatever order the `.vbp` names them in.
  - Two rows tell this apart from an ordinal sort: `MSComctlLib` comes before `MSComDlg`, and `stdole` sits between `Scripting` and `TabDlg`.
  - Two rows tell it apart from file order: `stdole` was the first `Reference=`, and `TabDlg` the first `Object=`.
- **The project's own library is sorted in with the rest** (`ObProbe`, row 3). It isn't placed first.
- **A library named on both kinds of line appears once:** `MSComctlLib`.
- **The combo has no `CBS_SORT` style** (`0x50000203`), so the IDE inserts in this order itself.

**HexIDE differs in one respect.** `ObjectBrowserToolViewModel.ReorderLibraries` puts `<All Libraries>`, then the focused project, then the rest alphabetically, ignoring case. VB6 sorts the project in with the rest. That order came with the original import and has no recorded decision behind it, so I've filed a fix as #4202. The rest matches: the case-insensitive sort, and one row for a library named twice.

**Not measured:** a project group, where each project would add a library of its own.
