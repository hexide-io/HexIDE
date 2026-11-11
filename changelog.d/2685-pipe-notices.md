### Fixed

- **A launched pipe server's immediate exit cannot beat its exit handler** (#2685). Late subscribers
  receive unexpected closure already recorded for that connection attempt; the first notice subscriber
  also receives startup notices, after which no notice history is retained. Callbacks run in order
  outside the process lock, so closure can dispose the transport without blocking its readers. The
  process-notice ordering test waits for closure before starting teardown, and its timeout lists
  which signals arrived.
