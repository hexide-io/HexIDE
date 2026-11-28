# Signed publisher requests (#5321)

## Why

Requesting a publisher record meant sending a public key, an id and a display name by chat. Nothing checked their form,
and nothing proved the sender held the private key ([#5321](https://github.com/hexide-io/HexIDE/issues/5321)). This is the
first part of the request flow decided on the Team Desk on 2026-11-22. #5322 (offline issuance) and #5323 (the request
repository and its check) build on it.

## What changes

- **`request-publisher`** makes, or takes, the publisher's key, and writes a canonical, signed `request.json`.
- **`verify-request`** checks a request as hostile input:
  - its form, byte for byte;
  - each field, by the rules `sign-publisher` uses;
  - its signatures, which cover a domain prefix;
  - with a ledger, that no other publisher holds its id, key or namespaces.
- **A rotation** is signed by the new key and the old one. Checked without a ledger, it exits 3, which no automation
  takes as a pass.
- **The supervisor agreed** to `--previous-key`, `verify-request --github` and exit code 3 on 2026-11-25.
