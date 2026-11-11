### Added

- **An add-in project can edit its name and description** (#3698) when its project-file writer carries
  properties. Project Type and startup object stay disabled and unchanged; the next save writes the
  properties through that writer.

### Changed

- **An add-in project settings refusal names only the refused settings** (#3698): properties, references
  or components, rather than claiming all three are read-only.
