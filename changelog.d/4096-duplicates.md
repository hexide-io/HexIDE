### Fixed

- **Damaged form properties no longer change between saves** (#4096). Readable duplicate properties use
  the last value, regardless of case, and save as one line. Unreadable duplicates and measurements VB6
  cannot set leave the form read-only, so Save preserves the damaged original.
