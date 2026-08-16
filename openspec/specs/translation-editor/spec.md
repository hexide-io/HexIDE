# translation-editor Specification

## Purpose
Define editing translations inside the IDE: correcting a word in a shipped language, and starting a
translation for one that does not ship yet.

Shipped translations are produced in bulk and are best-effort, so some of them are wrong in ways only a
native speaker will notice — a term that is technically correct and not what anyone in that language calls
that thing. The person who can fix it is looking at it right now, and the gap between noticing and fixing it
should not involve a repository.

## Requirements
### Requirement: A user's own translations SHALL override the shipped ones
The IDE SHALL apply a per-user layer of translations on top of the shipped chain, and that layer SHALL take
priority over the shipped values for the language it targets.

Overriding rather than editing is what keeps the two separable: the shipped pack can be updated without
destroying local corrections, and a correction can be removed to see what shipped. Storing the layer per
user rather than with the project keeps one person's terminology preference out of everyone else's IDE.

#### Scenario: Correcting a term
- **WHEN** the user provides their own value for a key
- **THEN** it is used in place of the shipped value

#### Scenario: Removing a correction
- **WHEN** the user clears their value for a key
- **THEN** the key resolves through the shipped chain again

#### Scenario: An override on a neutral language
- **WHEN** an override targets a neutral language
- **THEN** it applies in every region that resolves through that language

### Requirement: The editor SHALL show every translatable key with a reference translation
The editor SHALL list the full canonical key set, showing each key's value in a chosen reference language
alongside an editable value for the language being translated, and SHALL support finding a key by searching
and by grouping.

Translating from a key name alone is guesswork — the same word is a noun in one place and a verb in
another. A reference column supplies the meaning, and being able to choose which language provides it means
a translator who is stronger in a third language than in English can work from that instead.

#### Scenario: Translating a key
- **WHEN** the translator selects a key
- **THEN** they see the reference language's value beside an editable value for their target

#### Scenario: Finding a key
- **WHEN** the translator searches or filters by area
- **THEN** the list narrows to the matching keys

### Requirement: A reference value that is only an English fallback SHALL be marked
Where the reference language is not English and a key has no value of its own in that language's chain, the
reference value SHALL be visibly marked as a fallback rather than presented as a translation.

Without the marking the column silently lies: the translator sees English text under a column labelled with
another language and reasonably concludes that is what that language says. Marking it turns a trap into
information — it tells the translator this key is untranslated in the reference too, which is often the more
useful fact.

#### Scenario: A key untranslated in the reference language
- **WHEN** the reference language has no value for a key and shows English instead
- **THEN** that row is marked to show it is a fallback

### Requirement: The editor SHALL be able to target a language that does not ship
The translator SHALL be able to select any language recognised by the system as the target, including one
with no shipped pack, and begin translating it.

This is how a new language starts. Requiring a pack to exist before it can be translated means the first
step is a code contribution, which is exactly the barrier that stops a translation happening. Starting from
every row falling back to English gives the translator a complete working list on the first day.

#### Scenario: Starting a new language
- **WHEN** the translator targets a language with no shipped pack
- **THEN** the editor opens with every key showing its English fallback, ready to be translated

### Requirement: Edits SHALL be visible in the IDE as they are made
A value edited in the editor SHALL take effect in the interface without restarting.

Translation is judged in place — whether a term fits depends on the control it sits in and whether it gets
truncated. Seeing the change immediately turns the editor into a fitting session rather than a series of
guesses confirmed on the next launch.

#### Scenario: Editing a menu label
- **WHEN** the translator changes a value that appears in a menu
- **THEN** the menu shows the new text without a restart
