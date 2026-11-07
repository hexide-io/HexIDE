## Where and how VB6 writes a project's description (2026-10-22, #42)

**The question.** Project Properties sets a project's description, and HexIDE wrote it nowhere. Which key does VB6 write
it under, where among the other keys, and how? No `.vbp` in `corpus/` or `demo/` carries one, and the position matters as
much as the name: `ProjectSerializer` places the lines it does not model by counting the keys it does, in VB6's order.

**The probe.** Two copies of one project: a form, a reference to `stdole2.tlb`, and the keys `Startup`, `Command32`,
`Name="probe"`, `HelpContextID="0"`, `CompatibleMode="0"` and the three version numbers. `none.vbp` has no description.
`quoted.vbp` adds `Description="He said ""hi"""` after `HelpContextID`. The VB6 IDE (6.00.8176, the original release) opened each, and
File > Save Project As saved it to a new name. The saved files were read back.

| Read | Written by Save Project As |
|---|---|
| `none.vbp`, no `Description` line | `Name="probe"`, `HelpContextID="0"`, `CompatibleMode="0"`: no `Description` line |
| `quoted.vbp`, `Description="He said ""hi"""` | `Name="probe"`, `HelpContextID="0"`, `Description="He said ""hi"""`, `CompatibleMode="0"` |

**What it shows:**
- **The key is `Description`,** written quoted, with a quote inside doubled, as `Name` is.
- **It sits after `HelpContextID` and before `CompatibleMode`.** The VB98 Template agrees: `Addin.vbp` and
  `DHTML Application.vbp` carry it there, after `Name` and `HelpContextID`.
- **No description, no line.** VB6 does not write an empty `Description=""`. The Template's four other projects
  have no line.

HexIDE now reads `Description` into the project and writes it there. A file read without one has room made for the line
after its `HelpContextID`, so a description typed in later lands where VB6 puts it. One case is inferred, not measured:
a project HexIDE created has no `HelpContextID` line, so HexIDE writes `Description` straight after `Name`. That is
VB6's order with the missing line left out. What VB6 itself writes when it saves such a file, presumably a
`HelpContextID="0"` of its own, was not probed.

