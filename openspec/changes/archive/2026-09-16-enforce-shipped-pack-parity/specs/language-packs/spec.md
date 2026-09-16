## MODIFIED Requirements

### Requirement: A pack SHALL declare only what it translates
A pack that has not been declared in the manifest, and a regional variant of a language already shipped,
SHALL contain only the keys it provides values for, and SHALL NOT be required to enumerate the full key
set.

Requiring completeness of these would make every new canonical key a breaking change for work in progress,
and would block a contribution until it was finished. Since anything absent resolves to English, a pack
that grows one section at a time is valid at every point along the way.

A **region** is a country or territory variant of a language that already ships — `fr-CA`, `pt-BR`,
`en-GB` — and overriding only what differs from its neutral is the whole of its job: `en-GB` carries
eighteen keys of seven hundred and twenty-seven, deliberately. The word is easy to misread as *dialect* or
*minority language*, and that reading is wrong: Basque is not a region of Spanish, it is a language, and it
would ship as the neutral pack `eu`. Anything that is a language rather than a variant of one is a neutral,
and the requirement below governs it once declared.

#### Scenario: A pack covering part of the interface
- **WHEN** an undeclared pack defines values for some keys only
- **THEN** it is valid, and may be completed over time

#### Scenario: A regional variant
- **WHEN** a region pack defines only the keys that differ from its neutral
- **THEN** it is valid and ships, with the remainder resolving through its neutral

## ADDED Requirements

### Requirement: A declared language SHALL be a complete translation
A pack listed in the manifest of offered languages SHALL define a value for every key in the canonical
pack, and the build SHALL fail when one does not.

Declaring a language is the moment the project tells a user it is available, and the guarantee attached to
that offer is that the interface is in their language rather than partly in English. Nothing else about the
system is worth its cost without it: every pack is a permanent tax on every new key, since one key added to
the canonical pack is one translation owed by each of them, and the only thing that makes the tax worth
paying is that they are all actually complete. It is also the stated reason for refusing further languages,
which is an argument that only holds while it is true.

Inheritance SHALL remain the behaviour for a key that is nonetheless absent, so that a gap renders English
rather than a blank control. That is a safety net and SHALL NOT be read as permission to leave the gap: a
missing key is invisible to a reviewer, indistinguishable from a deliberate choice, and reaches a release
unless something fails.

A value identical to the canonical one SHALL still be stated explicitly. This is not duplication: an
explicit entry records a decision — that a product term stays as it is, or that the word is already correct
in this language — and distinguishes it from a key nobody has examined. Omission collapses those two into
one, which is exactly the drift this requirement exists to prevent.

Completeness SHALL be measured against the declared set rather than against the directory, and the two
SHALL agree in both directions. A declared language with no pack silently renders wholly in English while
appearing in the menu; an undeclared full translation on disk is kept current by every translation pass
without anyone having decided it ships, which is how two of them came to be complete before that decision
was taken.

#### Scenario: A declared language missing a key
- **WHEN** a key exists in the canonical pack and a declared language does not define it
- **THEN** the build fails, naming the language and the missing keys

#### Scenario: A key whose translation is the English word
- **WHEN** the correct value in a declared language is identical to the canonical one
- **THEN** it is stated explicitly rather than inherited

#### Scenario: A declared language with no pack file
- **WHEN** the manifest offers a language for which no pack exists
- **THEN** the build fails

#### Scenario: An undeclared full translation
- **WHEN** a pack file is a full translation and the manifest does not list it
- **THEN** the build fails, because whether it ships is a decision rather than an accident
