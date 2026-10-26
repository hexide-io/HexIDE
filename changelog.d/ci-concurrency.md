### Changed

- **CI cancels a pull request's superseded run when a newer push arrives.** Each push used to start a full run while the
  older one kept going, and superseded runs held the few macOS runners the whole queue shares. Pushes to a branch are
  never cancelled, so every commit on main is still built.
