### Fixed

- **A code editor's language-server session posts to the UI dispatcher it was built on** (#2530). Its
  debounced change and its diagnostics arrive on pool threads. Each post used to ask `Dispatcher.UIThread`
  there, at the moment of posting. In a headless test run the UI dispatcher is cleared as each test is set
  up. So a session that one test never disposed could post during the next test's setup, make its pool
  thread the UI thread, and hang the Integration run. The session now takes the dispatcher when it's built.
