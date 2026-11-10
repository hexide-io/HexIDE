### Fixed

- **Carried-file saves and profile edits preserve previous disk contents** (#2504). Settings,
  layouts, recent projects, translation and keymap overrides, keymap exports, open-document sessions,
  project sidecars, add-in consent/state and chat history keep unique recovery siblings before
  replacement or removal. Close the IDE and copy a recovery file back to its original name to
  restore it; recovery files are never automatically pruned.
