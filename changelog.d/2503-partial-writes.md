### Fixed

- **Partial-write tests cover project/group saves, package encoding and carried-file retries** (#2503).
  The tests check that a refused write preserves the destination and removes its staging file;
  carried-file saves retain their dirty state until a successful retry.
