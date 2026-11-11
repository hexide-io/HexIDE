### Added

- **References for add-in project types whose writer carries them** (#3698). Changes are saved through
  the type's project-file writer. Save Project As is available only when a loaded project can move its
  file; in a mixed save, add-in projects are saved where opened, and one message names only those
  whose project files were written.
