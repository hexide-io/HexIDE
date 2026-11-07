### Added

- **Real-server close and shutdown coverage** (#491). The foreign-server suite and bundled-server
  tests read diagnostics from recorded wire frames after closing a document and pin the process exit
  code observed after normal HexIDE teardown.
