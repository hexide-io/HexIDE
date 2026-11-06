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
- What Build writes, ticked or unticked "Use Default Build Options", and whether it saves anything first (#1515).
- What VB6 does to a group when a project is removed from it. HexIDE drops its group at one project.

