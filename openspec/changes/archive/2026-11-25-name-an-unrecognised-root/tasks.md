## 1. Verifier and messages

- [x] 1.1 `root.pub` is an envelope file; a chain that verifies only under it is refused with its fingerprint.
- [x] 1.2 Options → Add-Ins and the developer-mode consent line name the root, in every shipped pack, as trust verdicts profiles can't change.

## 2. Packer

- [x] 2.1 `pack` writes the trust folder's `hexide-root.pub` as `root.pub`, and refuses one that didn't sign the intermediate.
- [x] 2.2 hexide-pack's trust folder check, TRUST.md and the packer README name the root key.
