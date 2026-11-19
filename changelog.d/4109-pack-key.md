### Fixed

- **The add-in packer refuses a signing key that its publisher record doesn't name** (#4109). `pack` signed with whatever
  `firstparty.key` its trust folder held, so a key from another signing chain made packages HexIDE refuses, and nothing
  failed. It now stops before writing anything, gives both keys' fingerprints, and says to remove a leftover key or get
  the current one.
