### Fixed

- **Enum members and module-level Consts report VB6 compile errors for invalid in-box names** (#4745). String constants in Enum values raise "Type mismatch"; missing container members and invalid library qualifiers report the measured "Method or data member not found" or "Invalid qualifier" error.
