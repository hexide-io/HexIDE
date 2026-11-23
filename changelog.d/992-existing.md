### Fixed

- **SavePicture replaces existing plain files in place** (#992), truncating longer files while keeping their creation time. Read-only files and Windows hidden files raise error 75 with the path as passed and remain unchanged, matching the measured VB6 SP6 behaviour.
