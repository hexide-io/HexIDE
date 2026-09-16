# language-packs Specification

## Purpose
Define what a language pack is and the conventions every one follows, so that adding a language is a
contained piece of work rather than a project.

The localization mechanism is a separate capability; this is the content that rides on it. The bar being set
here is deliberately low in effort and high in consistency — a pack should be one file that anyone who
speaks the language can contribute to, and every pack should behave the same way once it is in.

## Requirements

### Requirement: A language SHALL be one declarative file plus one manifest entry
A pack SHALL be a single data file declaring the language's display name, its text direction, and its
key-to-string map, and SHALL become available by being listed in the manifest of offered languages.

One file is what makes a translation a contribution rather than a development task: it needs no code, no
build step, and no understanding of the IDE's internals. Requiring a manifest entry rather than discovering
files at startup keeps the offered list explicit and predictable, and avoids scanning behaviour that would
have to be trusted.

#### Scenario: Adding a language
- **WHEN** a new language is contributed
- **THEN** it consists of one pack file and one manifest entry, with no code change

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

### Requirement: A region SHALL ship a file only if it changes something
A regional variant SHALL be offered through the manifest without requiring a pack file, and SHALL have one
only where it carries genuine overrides.

Most regions of most languages differ in nothing that appears in an IDE. Shipping a file per region anyway
would mean dozens of near-empty files that have to be maintained and reviewed and that say nothing — while
listing the region in the manifest already communicates that it is supported.

#### Scenario: A region with real differences
- **WHEN** a region's usage genuinely differs on some terms
- **THEN** it ships a pack containing exactly those terms

#### Scenario: A region with no differences
- **WHEN** a region differs in nothing
- **THEN** it is offered with no pack file and resolves through its neutral language

### Requirement: Access-key markers SHALL follow the script, not the language
A pack in a Latin-script language SHALL carry one access-key marker per value where the English value has
one; a pack in a non-Latin script SHALL omit access-key markers entirely.

Access keys work by showing an underlined Latin letter the user presses. In a script with no such letters
the marker either renders as a stray character or silently does nothing, so carrying one is worse than
omitting it. Tying the rule to script rather than to individual languages makes it decidable by whoever
writes the pack, without a per-language ruling.

#### Scenario: Translating into a Latin-script language
- **WHEN** a value's English original carries an access-key marker
- **THEN** the translation carries one, placed on a letter that suits the translated word

#### Scenario: Translating into a non-Latin script
- **WHEN** a pack is written in a non-Latin script
- **THEN** it carries no access-key markers

### Requirement: Chinese SHALL be split by script rather than by region
Chinese SHALL ship as separate Simplified and Traditional packs, and a Chinese-speaking region SHALL resolve
to the pack matching the script it uses.

The difference that matters is the writing system, not the country — a reader of Traditional characters
cannot use a Simplified pack whichever region they select. Treating region as the axis would either produce
several duplicate packs or route readers to the wrong script.

#### Scenario: Selecting a Chinese-speaking region
- **WHEN** a region using Traditional characters is selected
- **THEN** the Traditional pack is used, not a region-specific or Simplified one

### Requirement: Packs SHALL be constrained to translating, not reformatting
A pack SHALL provide text only, and SHALL NOT influence how numbers, dates or currency are parsed or
formatted.

Formatting is the interpreter's concern and is deliberately fixed, because a VB6 program's behaviour depends
on it. Keeping packs to strings means a translator cannot accidentally change what a program computes, and
means translating is a task requiring no knowledge of the runtime at all.

#### Scenario: Translating a pack
- **WHEN** a translator provides values for a language
- **THEN** nothing they can write affects numeric or date handling

### Requirement: Pack coverage SHALL be enforced automatically
The build SHALL fail when a key used by the interface is absent from the canonical pack, and when a pack
defines a key the canonical pack does not.

The two checks catch opposite mistakes. A key used but not canonical renders as its own raw name, which
reaches a user as gibberish. A key in a pack but not canonical is dead weight — usually a typo, or a
leftover from a renamed key, and it will silently never be used. Both are invisible in review and trivial
for a machine.

#### Scenario: Interface text with no canonical value
- **WHEN** interface text references a key the canonical pack does not define
- **THEN** the build fails

#### Scenario: A pack with an unrecognised key
- **WHEN** a pack defines a key that is not canonical
- **THEN** the build fails

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
