### Fixed

- **Redirected command-line output starts with the text, not a byte-order mark.** `--help` or a refusal
  written to a file could begin with a UTF-8 BOM whenever something in the process had set the console to
  `Encoding.UTF8`, which also made a ConsoleOutputTests case pass or fail by test order.
