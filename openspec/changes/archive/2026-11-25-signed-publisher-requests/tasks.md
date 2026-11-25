## 1. Requests

- [x] 1.1 `request-publisher` makes or takes the key and writes a canonical request signed with it, behind a domain prefix.
- [x] 1.2 A rotation is also signed by the key it replaces.

## 2. Checking

- [x] 2.1 `verify-request` checks form, fields, signatures, size and links, and returns refusals rather than throwing.
- [x] 2.2 With a ledger it refuses a held id, key or overlapping namespace; a rotation must replace the ledger's key.
- [x] 2.3 Exit codes 0, 2, 3 and 1; both commands are in hexide-pack.
