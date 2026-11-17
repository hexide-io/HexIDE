### Fixed

- **Crash recovery keeps unsaved VB6 project-file tab edits** (#4078). Restoring opens the `.vbp` text as a pending edit, without writing the file or applying project settings. Copies already held on disk are not offered.
