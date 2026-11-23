### Fixed

- **FlatScrollBar accepts VB6's measured property conversions** (#1097). Its Integer properties and
  Enabled convert before validation, MousePointer updates the cursor, hidden `[_Value]` reads and
  writes Value, and Refresh redraws without an error.
