### Fixed

- **Make with VB6 asks before replacing an existing exe**, with No the default (#4504). Run with VB6
  asks too, unless the output's filename and SHA-256 match its last successful Run build, recorded in
  the project's user sidecar. No builds and writes nothing; a Run asks at most once about replacement.
