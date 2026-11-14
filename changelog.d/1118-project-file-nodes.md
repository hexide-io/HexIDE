### Added

- **Open the project's own file from Project Explorer** (#1118). A saved project's file is its first
  child, including contributed project types and a group's `.vbg`. It opens as read-only text and
  refreshes after Save Project; `open_file` also opens it by filename. Projects without a chosen home
  have no file node. Editing project-file text remains in #3915.
