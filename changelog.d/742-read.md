### Fixed

- **A name read through its type character is read, as vb6.exe reads it** (#742). `Debug.Print s$`, `i% + 1`, `arr%(1)`,
  `UBound(a%)` and `Bump i%` stopped the run with "Type hint is not supported". The character is now checked against the
  name's type: the declared one, a Const's value's, an Enum member's Long, or an array's element type. A mismatch, or any
  character on a Variant, an object, a Byte, a Boolean or a Date, is VB6's compile error. A name never declared is declared
  by the read with the character's type, ahead of a DefType, and a ByRef parameter given `i%` writes into `i`.
