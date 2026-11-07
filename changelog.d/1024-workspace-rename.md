### Fixed

- **Rename can update several modules together** (#1024). Every edited member opens in a code window,
  with one undo step per module and a status message naming the module count. The whole rename is
  refused if any target is unresolved, read-only, protected or stale; edits against disk also require
  no unsaved changes. Versioned document edits are now declared and accepted from language servers.
