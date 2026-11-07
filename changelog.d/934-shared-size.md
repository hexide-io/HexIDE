### Fixed

- **A Font changed through a retained reference keeps VB6's size and stays shared** (#934). Setting an Arial
  font's Size to Integer 14 through `Set f = Label1.Font` reads back Currency 14.25, as vb6.exe gives it, and
  reaches every control sharing that font.
