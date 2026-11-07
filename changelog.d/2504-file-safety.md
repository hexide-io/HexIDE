### Fixed

- **Add-in signing outputs preserve existing files** (#2504). Key/trust-chain generation and
  revocation signing refuse to replace any existing output, including private keys. Failed signing
  and parity-harness writes remove only the output files created by that invocation.
