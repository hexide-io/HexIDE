### Fixed

- **Enum values can name in-box numeric constants** (#4594), bare or qualified by library or container,
  including `VBA.vbBinaryCompare`, `vbObjectError Or 127` and `vbRed`. Project declarations take precedence:
  unsupported project constant resolution is explicitly refused pending #4614 instead of silently using
  the library's value.
