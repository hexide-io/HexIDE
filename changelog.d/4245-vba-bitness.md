### Added

- **VBA7 bitness belongs to the project** (#4245). A trailing `[HexIDE]` section can set
  `VbaBitness=64` to select measured 64-bit VBA7 compiler constants and require `PtrSafe` on active
  declarations. VB6 projects reject active `Declare PtrSafe`; 32-bit VBA7 refuses to run pending
  measurement. Code windows default their conditional preview to the project's target.
