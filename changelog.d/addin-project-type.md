### Added

- **An add-in can contribute a project type.** `IHexIdeHost.RegisterProjectType` names the files a type reads, such as
  an RDCore `.rdproj`, and what reads them. Open Project then offers those files and opens one as a project: its
  modules, named by their `VB_Name`, its references and its folders, an empty folder included, with code editing.
  The project file stays the add-in's program's to write: saving writes edited modules only, and members are not added
  or removed from the IDE. The add-in reports a member added, removed or moved, or new references, and the project
  follows as a .vbp project would.
