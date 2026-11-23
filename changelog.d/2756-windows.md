### Changed

- **Shared code windows follow VB6's open and close prompts** (#2756). Opening another project's copy
  warns and activates the existing code window. Closing a shared copy with unsaved changes asks whether
  to save it, then offers the usual shared-file update after a save. IDE-driven closes keep their own
  unsaved-changes flow.
