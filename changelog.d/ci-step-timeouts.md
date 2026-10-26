### Changed

- **A hung test in CI fails its step after 30 minutes, and keeps its log.** Every test step in `build.yml` now
  has a timeout. A hang used to hold its job for GitHub's six-hour default, and cancelling the job to free the
  merge queue threw its log away, so nothing said which test hung. `WorkflowActionTests` checks that every
  test step keeps one.
