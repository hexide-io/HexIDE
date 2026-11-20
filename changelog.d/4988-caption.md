### Fixed

- **An unquoted Caption in a `.frm` is refused** (part of #4988). The reader logs the invalid value,
  leaves the caption unset and preserves the original line on save. A refused duplicate keeps the earlier
  readable caption and retains the save hold.
