### Fixed

- **VB6 form properties use stricter numeric parsing** (part of #4988). The reader preserves refused
  values as raw lines, reads short hex and octal values on enums, and writes accepted short hex and
  decimal colours in VB6's eight-digit form.
