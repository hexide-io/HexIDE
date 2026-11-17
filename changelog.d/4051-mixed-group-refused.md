### Fixed

- **Mixed project groups refuse saving before any file is written** (#4051). The warning names projects whose files are not `.vbp`: VB6 would open them as code modules. Save Project As checks the new path too. Standalone contributed projects keep their existing save behavior.
