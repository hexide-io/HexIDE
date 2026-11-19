## A project's reference to another project of its group (2026-11-19, #4899, design-time and run, IDE, SP6)

**The question.** In a group, a Standard EXE can reference an ActiveX DLL project of the same group (#4899). What line
does VB6 write for that reference? How does the referencing project name the other project's classes? What happens
when the referencing project is opened without its group?

**The probe.**
- **The IDE:** VB6.EXE 6.00.9782 SP6, in the guest's console session through the agent ("Reaching the VB6 *IDE*, not
  just the compiler" in the README).
- **The group:** `Group1.vbg` holding `Project=Lib\Lib.vbp`, then `StartupProject=Client\Client.vbp`.
  - **`Lib.vbp`:** an ActiveX DLL (`Type=OleDll`) named `Lib`, never compiled. Its one class, `LibClass`, is `MultiUse`,
    with `Public Function Hello() As String` returning `"from Lib"`.
  - **`Client.vbp`:** a Standard EXE named `Client`, with no references and one module, whose `Sub Main` writes to a file.
- **The reference** was added through Project > References in Client, by checking the group project's entry. It was saved
  through the IDE's own exit prompt, "Save changes to the following files?", answered Yes.
- **What was read:** the References list with `LB_GETTEXT`, the saved files, the run's output file and the dialogs' text.

| Row | What was done | VB6 |
|---|---|---|
| R1 | Project > References in Client, with the group open | The list shows the group project as **`Lib`**, its project name, at index 3: after the three Visual Basic entries, before every type library |
| R2 | R1's entry checked, then the exit prompt's Yes | `Client.vbp` gains **`Reference=*\A..\Lib\Lib.vbp`**: the path relative to `Client.vbp`'s folder, with nothing after it. It's written right after `Type=`, before `Module=`. The `.vbg` is unchanged |
| R3 | F5 on the group. `Sub Main` declares `Dim bare As LibClass` with `Set bare = New LibClass`, and `Dim qualified As Lib.LibClass` with `Set qualified = New Lib.LibClass` | Runs. Both `Hello` calls return `from Lib`, and `TypeName(bare)` is `LibClass`. The class is reached **bare**, and **qualified by the project's name**, `Lib` |
| R4 | `Client.vbp` with R2's line, opened alone, with `Lib.vbp` still on disk at that path | Opens with no message. References lists **`MISSING: Lib.vbp`**, checked, with its location `…\Lib\Lib.vbp` |
| R5 | F5 on R4 | "Compile error: Can't find project or library". Nothing runs |

**Readings:**
- **A project reference names a `.vbp` by its path, relative to the referencing `.vbp`.** It carries no GUID, version or
  name. The name code uses is the referenced project's `Name=`, from that `.vbp`.
- **It resolves only to a project open in the same group.** Opened alone, the reference is MISSING although the file
  exists, and the program doesn't compile. VB6 doesn't load the referenced `.vbp` on its own account.
- **The referenced project's classes are reached bare, or qualified by its project name,** as a type library's are.

**Not measured:**
- a referenced project outside the referencing one's folder tree, or on another drive: whether the path stays relative;
- a referenced project that has been compiled and registered, with the referencing `.vbp` opened alone: whether VB6 then
  uses the DLL;
- a clash between a name in the referenced project and one in another reference, and how the References order settles it;
- where a project reference sits once other references are added.
