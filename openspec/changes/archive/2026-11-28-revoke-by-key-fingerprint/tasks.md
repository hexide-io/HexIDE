## 1. The list and the check

- [x] 1.1 `revoked.keyFingerprints`, checked against a package's publisher and intermediate keys, at load, mid-session and before a language server launches.
- [x] 1.2 A revoked publisher key reaches first-party; a revoked intermediate key exempts it.

## 2. The packer

- [x] 2.1 `verify-request` and `issue-publisher` refuse a revoked key, and leave a rotation away from one to a person.
- [x] 2.2 `sign-revocations` refuses, and `verify-revocations` fails, a list HexIDE would ignore.
