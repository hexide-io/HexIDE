# Revoke by key fingerprint (#5315)

## Why

The revocation list could name a build, a publisher id or an intermediate id, but not a key. So the only way to revoke a
publisher's stolen key was to revoke their publisher id, which also revoked the record re-issued for their new key
([#5315](https://github.com/hexide-io/HexIDE/issues/5315)). A leaked first-party key could only be replaced in a new
HexIDE build. And `issue-publisher` couldn't refuse a request for a key the list revokes.

## What changes

- **`revoked.keyFingerprints`,** a new set in the list: SHA-256 key fingerprints, with case and spaces ignored.
- **A revoked publisher key** refuses every package it signed, first-party included. **A revoked intermediate key**
  refuses the packages under it except first-party ones, as an intermediate id does. This was decided by the maintainer
  on 2026-11-26.
- **Older builds skip the new field.** Measured on v0.7.0: unknown fields are skipped, `schema` is never read, and the
  list's other revocations still apply. `schema` stays 1, and new builds go on skipping unknown fields.
- **`verify-request` and `issue-publisher`** refuse a request whose key is revoked, leave a rotation away from a revoked
  key to a person (exit 3), and issue nothing under an intermediate whose key is revoked.
- **`sign-revocations`** refuses a list HexIDE would ignore, wholly or in part, before reading the root key, and
  **`verify-revocations`** fails one.
