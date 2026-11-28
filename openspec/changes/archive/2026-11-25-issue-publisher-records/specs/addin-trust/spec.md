## ADDED Requirements

### Requirement: issue-publisher SHALL sign only the approved request
The packer's `issue-publisher` command SHALL sign a publisher record only from a request whose SHA-256 equals the approved
one and whose `verify-request` check, against the ledger, the root and, for a rotation, the revocation list, passes with
nothing refused or left unchecked. It SHALL ask for the publisher id to be typed back at a terminal, and refuse input that
isn't one. It SHALL write nothing when it refuses.

#### Scenario: A request edited after approval
- **WHEN** the request's SHA-256 is not the approved one
- **THEN** nothing is signed or written

#### Scenario: A rotation from another account
- **WHEN** a rotation's github handle or fingerprintUrl differs from the request its id was issued from
- **THEN** it is left to a person, and nothing is signed

### Requirement: The ledger SHALL hold every id and key ever issued
Each issued record SHALL be added to the ledger with the request it was issued from. A rotation SHALL move the old record
to `retired/<n>/`, numbered without gaps. No id or key in the ledger, live or retired, SHALL be issued again, and with a
root every record and stored request SHALL be verified before the ledger is trusted.

#### Scenario: A retired key requested under a new id
- **WHEN** a request names a key the ledger holds in a retired record
- **THEN** it is refused

#### Scenario: A tampered ledger
- **WHEN** a ledger record or its stored request doesn't verify under the root
- **THEN** the check refuses rather than trusting the ledger
