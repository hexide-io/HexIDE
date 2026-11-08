### Fixed

- **`CommonDialogFileResultTests` run on the headless session, like every other test that builds a control**
  (#2530). They were plain xunit tests, so they ran on xunit's own threads alongside the Avalonia tests.
  Building a `VBCommonDialog` there gave that thread a dispatcher of its own, and if that happened while the
  session was resetting the UI dispatcher for another test, that thread took the UI thread. The session's
  setup then failed and the Integration run hung. A Linux CI run showed this, with no language server
  involved.
