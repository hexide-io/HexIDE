### Fixed

- **Slider stores take VB6's measured conversions** (#1091): numeric, Boolean and Text properties coerce before validation, Null preserves the previous value with error 94 (13 through Object), and MousePointer accepts 0–15 and 99 and updates the visible cursor.
