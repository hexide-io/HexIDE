### Fixed

- **Indent-guide tests no longer depend on Avalonia's dispatcher thread** (#1995). Guide calculations use plain text lines, so their unit tests run independently of UI thread ownership.
