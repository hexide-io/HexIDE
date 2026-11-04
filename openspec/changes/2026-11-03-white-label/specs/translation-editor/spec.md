## MODIFIED Requirements

### Requirement: A user's own translations SHALL override the shipped ones
The IDE SHALL apply a per-user layer of translations on top of the shipped chain, and that layer SHALL take
priority over the shipped values for the language it targets, except the protected upstream-credit
block. Overrides of that block MUST be ignored with source attribution; its localization SHALL use
shipped translations and fallback only.

Overriding rather than editing is what keeps the two separable: the shipped pack can be updated without
destroying local corrections, and a correction can be removed to see what shipped. Storing the layer per
user rather than with the project keeps one person's terminology preference out of everyone else's IDE.

#### Scenario: Correcting a term
- **WHEN** the user provides their own value for an overridable key
- **THEN** it is used in place of the shipped value

#### Scenario: Removing a correction
- **WHEN** the user clears their value for a key
- **THEN** the key resolves through the shipped chain again

#### Scenario: An override on a neutral language
- **WHEN** an override targets a neutral language
- **THEN** it applies in every region that resolves through that language

### Requirement: The editor SHALL show every translatable key with a reference translation
The editor SHALL list the full canonical key set, showing each key's value in a chosen reference language
alongside a target-language value, and SHALL support finding a key by searching and by grouping.
Overridable keys SHALL be editable; protected upstream-credit keys MUST be read-only and explain
that upstream credits cannot be replaced through translation overrides.

Translating from a key name alone is guesswork — the same word is a noun in one place and a verb in
another. A reference column supplies the meaning, and being able to choose which language provides it means
a translator who is stronger in a third language than in English can work from that instead.

#### Scenario: Translating a key
- **WHEN** the translator selects an overridable key
- **THEN** they see the reference language's value beside an editable value for their target

#### Scenario: Finding a key
- **WHEN** the translator searches or filters by area
- **THEN** the list narrows to the matching keys

#### Scenario: Inspecting protected credits in the editor
- **WHEN** the translator selects a protected upstream-credit key
- **THEN** its shipped translation is shown read-only with the protection explained
- **AND** other keys remain editable
