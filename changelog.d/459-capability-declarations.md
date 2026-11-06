### Added

- **Signed add-in capability declarations** (#459): schema 2 manifests carry a validated
  `capabilities` array; the packer accepts repeated `--capability` arguments and emits a stable,
  explicit declaration. Bundled add-ins declare their current host use. Host capability enforcement
  and consent presentation follow in later tasks.

- **Add-in declaration verification coverage** (#459): signed-package regression tests cover
  declaration tampering and reordering, exact-byte hashes, re-signing, invalid signed declarations
  and declaration-free legacy package compatibility. Host denial remains pending security review.

- **Add-in consent isolation coverage** (#459): signed-package tests verify that adding, removing or
  reordering declarations, or repacking a legacy package, requests fresh consent even when the add-in
  id, publisher and version are unchanged. Earlier build decisions remain independent.

- **Verified add-in declaration metadata** (#459): successful verification and registry discovery expose
  an immutable capability declaration tied to the signed build hash, with legacy absence distinguished
  from an explicit empty list. This is declaration data for consent and disclosure, not a capability
  grant or a record of host use.
