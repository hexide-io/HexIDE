### Added

- **`LSet` copies between user-defined types** (#2518), using VB6's measured field alignment and
  in-memory encodings, including fixed-length Strings, fixed arrays and nested types. Short sources
  preserve the target's remaining bytes, and copied padding survives subsequent copies.
  Types containing pointers raise error 13; noncanonical Boolean bits remain explicitly not implemented.
