## 1. Issuance

- [x] 1.1 `issue-publisher` signs only an approved request that verifies with nothing left unchecked, once its id is typed back.
- [x] 1.2 It writes the trust folder and the ledger entry, retiring the old record on a rotation.

## 2. The ledger

- [x] 2.1 With a root, every ledger record and stored request is verified, and retired records run 1, 2, 3 without gaps.
- [x] 2.2 A rotation is bound to its stored request's handle and address, and refused for a revoked id.
