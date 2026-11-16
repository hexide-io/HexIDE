### Added

- **A file opened from a twinBASIC project archive now reaches language servers** (#1118). A server that claims
  the file's extension is sent its text when the read-only tab opens, and its diagnostics are underlined there. The
  file is named by an `archive-entry:` URI holding the archive's path and the file's path inside it. Reloading the
  archive refreshes the file, and closing the tab tells the server. `get_project_info` lists each entry's URI, and
  exported protocol captures hide the names.
