### Added

- **An add-in can contribute a project type.** `IHexIdeHost.RegisterProjectType` names the files a type reads, such as
  an RDCore `.rdproj`, and what reads them. Open Project, the recent list and a project group then open one as a
  project: its modules, named by their `VB_Name`, its references and its folders, an empty folder included, with code
  editing. The project file stays the add-in's program's to write: saving writes edited modules only, and HexIDE
  neither adds nor removes members, edits the project's properties or references, makes it nor runs it. The add-in
  reports a member added, removed or moved, or new references, in order, and the project follows as a .vbp project
  would.
