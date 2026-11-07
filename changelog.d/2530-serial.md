### Fixed

- **The Integration suite runs its test classes one at a time** (#2530). Avalonia's headless session resets
  the UI dispatcher as each Avalonia test is set up. Any plain test that built an Avalonia object on another
  thread at that moment took the UI thread, the session's setup failed, and the run hung. With one class at
  a time, no other test runs during a setup. Full runs on Windows and WSL took no longer.
