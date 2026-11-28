## ADDED Requirements

### Requirement: A publisher request SHALL be signed by the key it asks to certify
The packer's `request-publisher` command SHALL write `request.json` in one canonical ASCII form and sign it with the
publisher's key, and a rotation also with the key it replaces. Every signature SHALL cover a fixed domain prefix
followed by the request's bytes, so that it can't pass for any other signature HexIDE checks.

#### Scenario: Making a request
- **WHEN** a publisher runs `request-publisher` with a new key
- **THEN** the key is written on its own, never into the request folder, and the request verifies

#### Scenario: A signature over the bare bytes
- **WHEN** a request's signature covers its bytes without the prefix
- **THEN** `verify-request` refuses it

### Requirement: verify-request SHALL treat a request as hostile input
`verify-request` SHALL refuse a request that is oversized, a link, not in canonical form, has an unknown or invalid
field, or whose signatures don't verify. With a ledger it SHALL refuse an id, key or overlapping namespace another
publisher holds. It SHALL exit 0 only when every check passed, and 3 for a rotation it couldn't check against a ledger.

#### Scenario: A request for a held id
- **WHEN** the ledger already holds the request's id, key or an overlapping namespace
- **THEN** the request is refused

#### Scenario: A rotation checked without a ledger
- **WHEN** a rotation is checked with no ledger
- **THEN** it exits 3 and says its previous key was not checked
