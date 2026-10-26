### Changed

- **Two PRs that each add a line to the fidelity oracle's index now merge without a conflict.** The index takes one line
  per subject at its foot, so any two PRs that both measured something collided there, though keeping both lines was
  always the answer. `.gitattributes` now merges that one file with git's built-in `union` driver.
