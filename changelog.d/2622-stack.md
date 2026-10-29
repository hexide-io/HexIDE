### Fixed

- **Pointer identities no longer enlarge every interpreter value** (#2622): String buffer identities
  share the value's existing payload slot, restoring the stack footprint used before `StrPtr` and
  `VarPtr` were added while preserving their identity relations.
