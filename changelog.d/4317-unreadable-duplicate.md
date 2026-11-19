### Fixed

- **Damaged forms with a refused duplicate after a readable property remain editable** (part of #4317). The loader keeps the readable value and its diagnostic, then requires Save As without changing the original. Values VB6 accepts but HexIDE cannot read, and other unreadable shapes, retain the read-only hold.
