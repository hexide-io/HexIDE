### Added

- **DAP client protocol coverage is generated and guarded** (#3302). The table in `docs/dap-client.md`
  lists every request, event and reverse request from a commit-pinned, SHA-256-checked DAP schema, with
  support read from the client code. Offline runs skip visibly; CI requires the schema.
