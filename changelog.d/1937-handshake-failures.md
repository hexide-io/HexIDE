### Fixed

- **An initialize handshake that fails early logs a giving-up warning** (#1937), including the
  reason, and tears down the connection. Error replies and closed streams now follow the same
  cleanup and reconnect policy as an unanswered handshake; caller cancellation and client shutdown are logged at Debug.
