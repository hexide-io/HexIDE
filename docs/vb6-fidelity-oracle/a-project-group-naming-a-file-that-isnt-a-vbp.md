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

### Which lines a .vbg and a .vbp can hold, through /make (2026-11-21, #2757, `/make`, SP6)

**The question.** G4 above showed a `.vbg` refusing a `[HexIDE]` section, when opened in the IDE. #2757 asked whether a
`.vbg` could carry such a section as a `.vbp` does, before or after the `Project=` lines. Is it the section, or any
line VB6 doesn't know? And does a `.vbp` really take a section it doesn't know?

**The probe:**
- **The software:** VB6 SP6's `/make`, in the Win10 oracle guest.
- **The method:** a `.vbg` naming one Standard EXE, `P1.vbp`, was built with `/make G.vbg`. Each row changed the
  group's text only. The `.vbp` rows built `P1.vbp` directly.
- **Re-runs:** the no-final-newline row was run three times and the all-LF row twice, each with the same answer.

| Row | The `.vbg` | `/make` |
|---|---|---|
| H0 | `VBGROUP 5.0`, `StartupProject=P1.vbp`, each line ending CRLF | builds |
| H1 | H0, then a blank line | builds |
| H2 | H0, then `[HexIDE]` with two keys | "The project group file '…\G.vbg' is corrupt, and can't be loaded." |
| H3 | `[HexIDE]` and a key between `VBGROUP 5.0` and `StartupProject=` | the same "corrupt" refusal |
| H4 | as H3, with a second section, `[Other]`, after it | the same |
| H5 | H0, then an empty `[HexIDE]` header | the same |
| H6 | H0, then `Foo=bar`, with no section | the same |
| H7 | `Foo=bar` between `VBGROUP 5.0` and `StartupProject=` | the same |
| H8 | a comment line, `; note`, after `VBGROUP 5.0` | the same |
| H9 | H0 without the final CRLF | the same |
| H10 | H0 with its last line ending LF alone | builds |
| H11 | H0 with both lines ending LF alone | "Compile Error in File '…\G.vbg', Line 0 : Invalid outside procedure", and "Build of 'Project1.exe' failed." |

| Row | The `.vbp` (Type, Form, Startup, ExeName32, Name) | `/make` |
|---|---|---|
| V0 | as written, ending CRLF | builds |
| V1 | without the final CRLF | builds |
| V2 | then `[HexIDE]` alone | builds |
| V3 | then `[HexIDE]` and `Form1.frm=65001`, the shape of HexIDE's encoding records | builds |
| V4 | then `[HexIDE]` and `Targets=win32` | builds |
| V5 | then `[HexIDE]` and `Note=trailer` | builds |
| V6 | then `Note=trailer`, with no section | "The project file '…\P1.vbp' contains invalid key 'Note'." |
| V7 | then `[MS Transaction Server]` and `AutoRefresh=1` | builds |

**What it says:**
- **A `.vbg` takes nothing VB6 doesn't know.** A key, a comment, a section or an empty section header, before or after
  the `Project=` lines, makes the whole group "corrupt". Only a blank line is let through.
  - So #2757's hypothesis is overturned under `/make`, as G4 overturned it in the IDE. Anything of HexIDE's own about a
    group has to live outside the `.vbg`.
- **A `.vbg`'s last line must end in a newline.** CRLF or LF alone both work, and none at all is "corrupt". A `.vbp`
  without a final newline builds.
- **A `.vbg` in LF alone isn't read as a group at all.** `/make` compiled it as a project called `Project1`, failing
  on line 0. That looks like the file was taken as one line and opened as a source file, as G1 above opens a non-`.vbp`.
  It's reasoned from the message, not checked.
- **A `.vbp` takes any section,** with any keys in it. A key VB6 doesn't know is refused only outside a section.
  - So HexIDE's `[HexIDE]` trailer is safe for `/make` whatever keys it holds, provided they stay inside the section.

**An unrepeated failure.** One run of V5 failed with "Path/File access error: '…'". The VM was being exported live at
the time, with a production checkpoint and a VSS freeze. A re-run built. I count it as the freeze rather than VB6, but
that's reasoned, not checked.

**Not measured:**
- what the IDE's Save Project and Save Project Group write back, for a `.vbp` with a `[HexIDE]` section (#2757's third
  question);
- a `.vbg` with other line endings inside the file, such as CR alone;
- a `.vbp`'s unknown key ahead of the standard keys.

**HexIDE's side.** `GroupSerializer` ends every line with CRLF, the last included (`IDE/HexIDE.Runtime/Serialization/GroupSerializer.cs`,
`StringWriter` with `NewLine = "\r\n"` and `WriteLine`), so a group HexIDE writes meets H9's rule. It keeps lines it
doesn't recognise and writes them back, which VB6 would then refuse. That's read from the code, not run.
