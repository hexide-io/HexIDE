# Issue a publisher record from the approved request (#5322)

## Why

The maintainer issued a publisher record by retyping the request into `sign-publisher`. Nothing recorded what had been
issued, so nothing stopped an id, key or namespace being issued twice
([#5322](https://github.com/hexide-io/HexIDE/issues/5322)).

## What changes

- **`issue-publisher`** signs only the approved request (its SHA-256), after every `verify-request` check runs again
  against the ledger, the root and the revocation list, and after the publisher id is typed back at a terminal.
- **It writes the publisher's trust folder and adds the record to the ledger.** A rotation moves the old record to
  `retired/<n>/`, where its key stays held.
- **`verify-request --root` chain-verifies the ledger,** each record's stored request included. A rotation from another
  handle or address, from a ledger entry with no stored request, or checked against a list over 7 days old, is left to a
  person.
- **Agreed with the supervisor on 2026-11-25;** the issue records it.
