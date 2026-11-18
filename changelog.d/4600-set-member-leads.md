### Fixed

- **`Set` finds object member receivers through With, array elements and bare Property Get or Function names**
  (#4600, #4602, #4607). Leading-dot assignments now parse as Set and use the innermost With target;
  indexed receivers and bare procedure reads reach the existing Property Set and object-field paths.
  Indexed user-defined Type receivers remain explicitly refused pending measurements (#3924).
