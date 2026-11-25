## ADDED Requirements

### Requirement: A refusal SHALL name a root the chain verifies under that this build doesn't carry
A package MAY carry its root's public key as `root.pub`. When the intermediate's signature fails under the embedded
root, the verifier SHALL check the chain under that key, and SHALL name the key's SHA-256 fingerprint only when the
whole chain verifies under it. Such a package SHALL be refused like any other unverified package.

#### Scenario: A package signed under a newer root
- **WHEN** a package's whole chain verifies under the root it carries, and that root isn't this build's
- **THEN** the package is refused, and Options → Add-Ins names that root's fingerprint and asks the user to compare it
  with a newer release's TRUST.md

#### Scenario: A real root's key copied into a forged package
- **WHEN** a package carries a genuine root's public key, but its chain wasn't signed under it
- **THEN** the package is refused as an invalid signature, and no fingerprint is named

#### Scenario: A broken package under its own root
- **WHEN** a package's chain leads to the root it carries, but a file's hash doesn't match
- **THEN** the refusal names the hash mismatch, not the root

### Requirement: A root key a package carries SHALL never be trusted
The verifier SHALL NOT trust, remember or decide anything by a root key a package carries. Nothing SHALL offer to trust
it, and no message SHALL say such an add-in is genuine, because a newer HexIDE's root and a forger's look alike.

#### Scenario: A package signed under a root this build doesn't carry
- **WHEN** such a package is installed
- **THEN** it is never shown as verified, and no control offers to trust its root

### Requirement: The packer SHALL write the chain's root into each package
`pack` SHALL write the trust folder's `hexide-root.pub` into each package as `root.pub`. It SHALL refuse, before
writing anything, a trust folder without `hexide-root.pub` or one whose root didn't sign its intermediate.

#### Scenario: A trust folder whose root is from another chain
- **WHEN** the trust folder's `hexide-root.pub` didn't sign its `intermediate.json`
- **THEN** `pack` refuses and writes nothing
