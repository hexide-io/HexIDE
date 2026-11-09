### Fixed

- **Windows that react to a language server post to the UI dispatcher they were built on** (#2530). The Language
  Servers, Diagnostics and Object Browser windows, the code editors' language-server state, code lenses and a
  server's own requests are all called on a client's background threads. Each used to ask `Dispatcher.UIThread`
  there, at that moment. In a headless test run the UI dispatcher is cleared as each test is set up, so one of
  these arriving late could make its thread the UI thread and hang the Integration run. The IDE itself behaves
  as before.
