## MODIFIED Requirements

### Requirement: Unreproducible binary content SHALL hold a file read-only
Where a file cites binary content the IDE cannot reproduce — because the property carrying the citation is
one it does not model, or because the cited content did not reach the model at all — the IDE SHALL treat
that file as one it cannot reproduce.

The reference is what is lost, not the bytes: the companion file is left alone by a separate guard, so the
image survives on disk while the property pointing at it does not. That is a save which looks successful and
silently strips a control's picture, which is the same class of failure as flattening. Recognising it widens
the refusal before the container work narrows it — the count of refused files gets worse before it gets
better, and that is the correct direction, because a refusal is recoverable and a silent strip is not.

Whether the citation was honoured SHALL be judged against **what the file itself cites**, not against what
the companion yielded. A count taken from the companion cannot see a companion that is absent — there is
nothing to count — nor one that is truncated, where the unreachable offset is dropped from both sides at
once and they agree at a number lower than the file actually cites. The citations are in the file being
opened, so they can be counted whatever became of the file beside it.

#### Scenario: A property the IDE does not model referencing a companion blob
- **WHEN** such a file is loaded
- **THEN** it is presented read-only rather than saved with the reference dropped

#### Scenario: A modelled property referencing a companion blob
- **WHEN** the referencing property is one the IDE does model and the cited content reached the model
- **THEN** the file is not held read-only on that account

#### Scenario: A citation that cannot be honoured
- **WHEN** a file cites companion content that did not reach the model, because the companion is absent or
  too short to hold the cited offset
- **THEN** the file is presented read-only, however ordinary the property carrying the citation

#### Scenario: Two properties citing one offset
- **WHEN** more than one property cites the same companion offset
- **THEN** it counts once, so sharing content is not mistaken for a shortfall
