### Fixed

- **`Dim n As Node`, `Dim li As ListImage` and the other common-controls classes work without their library's name**
  (#2265). They stopped with "User-defined type not defined" unless written as `MSComctlLib.Node`. A bare name binds
  to the first library in the project's `Reference=`/`Object=` order that declares it, as VB6 binds it. A Set of
  another class raises 13 and keeps the earlier object, and `TypeOf` follows the same binding.
