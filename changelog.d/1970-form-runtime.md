### Fixed

- **A form keeps its saved MousePointer at run time** (#1970). `Me.MousePointer` reads the value loaded from
  the `.frm`, including `1 - Arrow` and `11 - Hourglass`, and the window shows that cursor, as assigning the
  property in code does.
