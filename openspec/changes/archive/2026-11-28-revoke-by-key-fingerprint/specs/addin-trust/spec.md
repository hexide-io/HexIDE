## MODIFIED Requirements

### Requirement: The IDE SHALL be able to revoke a build, a publisher, or an intermediate signer
The IDE SHALL consult a signed revocation list before the consent decision, and SHALL refuse to load
anything it revokes. Revocation SHALL be expressible against a specific build, a publisher, or an
intermediate signer, and against a signing key by its SHA-256 fingerprint. A revoked publisher key SHALL refuse every
package it signed, first-party included. A revoked intermediate key SHALL refuse the packages chained through it except
key-pinned first-party ones, as a revoked intermediate id does.

A signature says a package is authentic, not that it is safe — a publisher can turn out to be malicious, or
have their key stolen. Checking before consent matters because a revoked add-in must never be presented to
the user as a decision they can get wrong. The three granularities correspond to the three things that
actually go wrong: a bad release, a bad publisher, and a compromised signer. A key is finer than an id: revoking a
publisher's stolen key leaves the record re-issued for their new key standing.

#### Scenario: A revoked build
- **WHEN** an add-in's build is revoked
- **THEN** it does not load and the user is not prompted about it

#### Scenario: A compromised intermediate signer
- **WHEN** an intermediate signer is revoked
- **THEN** every add-in whose identity chains through it refuses to load

#### Scenario: Revoking a bad first-party release
- **WHEN** a specific first-party build is revoked
- **THEN** that build does not load, even though it is first-party

#### Scenario: A compromised signer that also signed first-party add-ins
- **WHEN** a signer is revoked at the identity level and first-party add-ins chain through it
- **THEN** the key-pinned first-party add-ins still load, so the IDE's own features survive the revocation

#### Scenario: A stolen publisher key
- **WHEN** the list names a publisher's key by its fingerprint
- **THEN** packages signed with that key refuse to load, and packages signed with the publisher's re-issued key still load

#### Scenario: A leaked first-party key
- **WHEN** the list names the first-party key by its fingerprint
- **THEN** first-party add-ins signed with it refuse to load

#### Scenario: A stolen intermediate key that also signed first-party add-ins
- **WHEN** the list names an intermediate's key and first-party add-ins chain through it
- **THEN** the key-pinned first-party add-ins still load

## ADDED Requirements

### Requirement: A revocation list SHALL stay readable by older builds
The IDE SHALL skip a revocation list's unknown fields and SHALL NOT refuse a list for its `schema` number, so a list that
adds a field still updates every older install. A build from before key revocation applies the rest of such a list and
ignores its key fingerprints, so a malicious publisher's id SHALL be revoked as well as its key.

#### Scenario: A list with a field this build doesn't know
- **WHEN** a validly signed list carries a field this build doesn't know
- **THEN** the list becomes active and its known revocations apply

### Requirement: A revoked key SHALL never be issued
`verify-request` and `issue-publisher` SHALL refuse a request whose key the revocation list names. A rotation away from
a key the list names SHALL be left to a person (exit 3), because that key's signature on the request proves nothing once
it is stolen. `issue-publisher` SHALL issue nothing under an intermediate whose key the list names.

#### Scenario: A request for a revoked key
- **WHEN** the list names the request's key
- **THEN** it is refused, and nothing is signed

#### Scenario: A rotation away from a stolen key
- **WHEN** the list names the key a rotation replaces
- **THEN** it exits 3, and a person checks the account and the published fingerprint

### Requirement: sign-revocations SHALL refuse a list HexIDE would ignore
`sign-revocations` SHALL refuse, before reading the root key, a list that doesn't parse, has a null set, has an issue
time HexIDE can't read, or names a key fingerprint that isn't 64 hex digits. `verify-revocations` SHALL fail such a list
even when its signature verifies.

#### Scenario: A mistyped fingerprint
- **WHEN** a list's key fingerprint is a digit short
- **THEN** `sign-revocations` refuses it, and nothing is signed
