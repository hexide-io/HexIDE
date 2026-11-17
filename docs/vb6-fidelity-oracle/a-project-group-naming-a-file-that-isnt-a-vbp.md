## A project group naming a file that isn't a .vbp (2026-11-16, #4051, design-time, IDE, SP6)

**The question.** HexIDE can hold a project of another type, such as an `.rdproj`, in a group with `.vbp` projects
(#4051). What does vb6.exe make of a `.vbg` that names one, and of a section of HexIDE's own inside the `.vbg`?

**The probe.**
- **The IDE:** VB6.EXE 6.00.8176 SP6, in the guest's console session through the agent ("Reaching the VB6 *IDE*, not
  just the compiler" in the README).
- **The method:** each `.vbg` was opened as the IDE's command-line argument, and what it showed was read from the screen.
  - **The group's lines:** each `.vbg` held `VBGROUP 5.0`, then `StartupProject=P1.vbp`, then the line under test.
  - **`P1.vbp`:** a one-module Standard EXE.

| Row | The line under test | VB6 |
|---|---|---|
| G1 | `Project=Other.rdproj`, an existing file holding XML | Opens with no message and no `.log`. The group holds P1 and a new, unsaved **`Project1`** whose one member is **`Module1 (Other.rdproj)`**: the file opened as a code module, its XML shown as red error lines |
| G2 | `Project=Vbpish.rdproj`, an existing file holding a valid `.vbp`'s text | The same as G1: `Project1` holding `Module1 (Vbpish.rdproj)`. The `.vbp` text inside isn't read as a project |
| G3 | `Project=Missing.rdproj`, no such file | Asks "File not found: '…\Missing.rdproj'--Continue Loading Project Group?" with Yes, No and Help. Yes opens the group with P1 alone |
| G4 | a blank line, then `[HexIDE]` and `Project=Other.rdproj` after the standard lines | Refuses the whole group: "The project group file '…\GSection.vbg' is corrupt, and can't be loaded." Nothing opens |

**Readings:**
- **VB6 decides by the extension, not the contents.** A `Project=` line naming an existing file that isn't a `.vbp` is opened
  the way VB6 opens a lone source file: as a module of a new project. G2's valid `.vbp` text doesn't change that.
  - **The resulting group isn't the one saved.** It has an extra unsaved project, and nothing says so.
  - **A save in VB6 then writes to the other project's file.** The module's path is the `.rdproj` itself, so a save
    would rewrite it as a VB module. That's reasoned from the module's path, not measured: the save wasn't run.
- **A `.vbg` takes no trailing section.** It's unlike a `.vbp`, where `/make` accepts a section it doesn't know (see the
  serialization-round-trip spec's `[HexIDE]` requirement). Anything of HexIDE's own about a group has to live outside the
  `.vbg`.

**Not measured:**
- a non-`.vbp` file named by `StartupProject=`;
- what VB6's own save writes after G1;
- `Project=` naming a `.vbp`-like extension VB6 knows, such as `.vbg` or `.bas`.
