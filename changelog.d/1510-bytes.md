### Added

- **`LenB`, `AscB` and `InStrB` run, and `MidB`, `LeftB` and `RightB` where their result is whole characters**
  (#1510). Each counts bytes, as measured on vb6.exe: `LenB("abc")` is 6, `InStrB` searches by byte, and `LeftB("abc", 4)`
  is "ab". A result of an odd number of bytes, and so `ChrB`, is refused, where all seven stopped the run as undefined.
