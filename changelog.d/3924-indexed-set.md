### Fixed

- **Type fields accept the measured built-in object types and indexed Set targets** (part of #3924).
  StdFont, TextBox, Control, Form and StdPicture fields start as Nothing, enforce their object types and share
  references on record copy; TextBox and font Lets write their default members. Set works through indexed
  records and Variant array fields. Integer, variable-length String, nested-Type and Integer-array-element
  targets report "Object required". ADODB fields and other unmeasured targets remain explicitly refused.
