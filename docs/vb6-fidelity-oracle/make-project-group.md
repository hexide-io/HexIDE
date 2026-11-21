## Make Project Group (2026-10-23, #746, design-time, IDE, release build)

**The question.** With one project loaded, HexIDE's File > Make Project Group threw on the missing group, and nothing
said so. The issue asked whether VB6 offers the command with one project. Reading the code raised a second question:
what the command does. HexIDE's saves every project and the group file.

**The probe.** Two projects, each with an empty form, plus `group.vbg` naming both and `single.vbg` naming only the
first. Each was opened in VB6's IDE, and I read the File menu. Where the command was offered, I chose it, then pressed
Cancel. The folder's file times were read before and after.

| Opened | Make Project Group... | Choosing it |
|---|---|---|
| `probe1.vbp` alone | **greyed** | |
| `group.vbg` (two projects) | enabled | a dialog: "Build the following projects:", listing `probe1.vbp` and `probe2.vbp`, both selected; "Use Default Build Options", ticked; Build, Cancel, Help |
| `single.vbg` (one project) | **enabled** | the same dialog, listing `probe1.vbp` alone |

**What it shows:**
- **The command needs a group open, not two projects.** A group of one project offers it; one project without a group
  doesn't. The File menu reflects the same state: Save Project Group and Save Project Group As replace Save Project
  and Save Project As whenever a group is open, one project or two.
- **It builds.** It's the group's counterpart of Make *project*.exe, not a save. Opening the dialog and cancelling
  wrote nothing: every file kept its time.

**In HexIDE (#746):**
- The command is offered only with a group open, or with two projects loaded. HexIDE's implicit Group1 can lag
  behind a second project, so two projects count as a group. With one project and no group it is greyed out.
- With a group, it says the feature isn't implemented yet and writes nothing, because HexIDE can't build a group
  (#1515). It used to save every project and the group, which is Save Project Group's job, under a Make label.

**Not measured:**
- What Build writes, ticked or unticked "Use Default Build Options", and whether it saves anything first (#1515). Measured 2026-11-21, in the next section.
- What VB6 does to a group when a project is removed from it. HexIDE drops its group at one project.


## What Make Project Group's Build writes (2026-11-21, #1515, IDE, SP6)

**The question.** #1515 asks HexIDE to make Make Project Group a build. Three answers were owed: what Build writes with
"Use Default Build Options" ticked, what it does unticked, and whether it saves anything first. Two more came up while
measuring: what Cancel partway through leaves, and what a compile error in one project does to the rest.

**The probe.** Five copies of one group, each in its own folder:
- a `.vbg` naming two Standard EXE projects, the first as `StartupProject=` and the second as `Project=`;
- each `.vbp` holding `Type=Exe`, one module with an empty-bodied `Sub Main`, `ExeName32="<name>.exe"`, and no `Path32`.

Each copy was opened in VB6's IDE (VB6.EXE 6.0.9782, SP6) through the console agent:
1. File > Make Project Group... was chosen with the keys.
2. The dialog's check box and buttons were pressed with `BM_CLICK`, since its controls carry no UI Automation patterns.
3. Each dialog that followed was recorded (title and control text) and screenshot before it was answered.
4. The folder's files, with sizes and times, were listed before opening, at Build, and after.

| Case | After Build | Files written |
|---|---|---|
| Ticked, the default | no dialog | `QA.exe` and `QB.exe` (16384 bytes each), each beside its `.vbp`, both within a second of Build; nothing else changed |
| Unticked | "Make Project" for the first project: Save in its folder, File name `RA`, with OK, Cancel, Help and Options... After OK, the same for the second | `RA.exe`, then `RB.exe`, each where its dialog said |
| Unticked, Esc on the first project's "Make Project" | nothing more, and no dialog for the second project | nothing |
| Ticked, a comment line added to the first project's module in its code window, unsaved | no dialog | both `.exe` files; the edited `.bas` kept its size and time |
| Ticked, an unsaved `Call NoSuchSub` in the first project's `Sub Main` | "Compile error: Sub or Function not defined" (OK, Help); after OK, nothing more | nothing, not even the second project's `.exe` |

**What it shows:**
- **Ticked, Build asks nothing.** Each project is made under its own `ExeName32`, here beside its `.vbp`.
- **Unticked, it asks where each program goes:** one "Make Project" dialog per project, in the group's order, each
  starting in that project's folder with the project's name.
- **Build saves nothing and prompts for nothing.** Not the projects, not the group, and not an edited module.
- **It compiles what is in the code window, not what is on disk.** The unsaved `Call NoSuchSub` was reported.
- **A failure ends the whole build.** After Cancel on the first project's dialog, or a compile error in the first
  project, the second project isn't built either.

**For #1515 (reasoned, not checked):** the issue's proposed design is a list of the group's projects and a Make of each
through the single-project path. That matches the unticked case. The ticked case asks nothing, and HexIDE's
single-project path asks where the program goes.

**Not measured:**
- A project with `Path32` set, ticked.
- A project deselected in the list.
- A failure in the second project: whether the first project's `.exe` stays.
- The Options... button.
- Which kind of code the `.exe` files hold. The `.vbp` files had no `CompilationType` line.
