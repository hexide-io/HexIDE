### Changed

- **VB6's projects open through a project type of their own** (#4053), registered by HexIDE at start-up the way an
  add-in registers one, which reads every `.vbp` and `.vbg`. A project file named on the command line can now be any file:
  the project type that reads it opens it, and one no loaded type reads is refused with a message saying so, where before
  anything but a `.vbp`, `.vbg` or `.twinproj` was skipped in silence.
