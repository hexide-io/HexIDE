### Fixed

- **CommonDialog loads through its MSComDlg library and registered component** (#1084, #1962), using the same
  mechanism as the other in-box controls. Its saved scalar properties remain carried verbatim. Color reads now
  retain the measured unsigned Automation Long value and VarType 19, and assigning -1 raises overflow; unmeasured
  numeric conversions are refused as not implemented.
