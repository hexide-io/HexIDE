## MODIFIED Requirements

### Requirement: The ledger SHALL hold every id and key ever issued
Each issued record SHALL be added to the ledger with the request it was issued from. A rotation SHALL move the old record
to `retired/<n>/`, numbered without gaps. No id or key in the ledger, live or retired, SHALL be issued again, and with a
root every record and stored request SHALL be verified before the ledger is trusted. `issue-publisher` SHALL write the
ledger entry before the publisher's `--out` folder, and SHALL refuse an `--out` inside the ledger, so that every record it
signs is one the ledger holds.

#### Scenario: A retired key requested under a new id
- **WHEN** a request names a key the ledger holds in a retired record
- **THEN** it is refused

#### Scenario: A tampered ledger
- **WHEN** a ledger record or its stored request doesn't verify under the root
- **THEN** the check refuses rather than trusting the ledger

#### Scenario: The publisher's folder can't be written
- **WHEN** `issue-publisher` signs a record and then can't write `--out`
- **THEN** the ledger already holds the record, and the error says where to copy it from

#### Scenario: The publisher's folder inside the ledger
- **WHEN** `--out` is the ledger or a folder inside it
- **THEN** it is refused, and nothing is signed or written
