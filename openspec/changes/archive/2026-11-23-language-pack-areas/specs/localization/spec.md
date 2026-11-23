## ADDED Requirements

### Requirement: Language-pack areas join each layer the IDE reads
The IDE SHALL read registered language-pack areas beside its own packs. An area is a folder of `{id}.json` files in
the IDE's pack format. Each area's English SHALL join the canonical fallback. Each area's file for a neutral or a
region SHALL merge into that layer, after the IDE's own pack and before the profile and user tiers.

#### Scenario: A region over its neutral
- **WHEN** an area has a French file and a Canadian French file, and the IDE is in Canadian French
- **THEN** a key in the region's file reads from it, and a key only in the neutral's file reads from that

### Requirement: An area decides neither language nor direction
An area SHALL NOT make an unknown language resolve, and SHALL NOT set the reading direction, which stays the IDE
pack's. Pseudo text and the Translation Editor's "secretly English" rule SHALL treat an area's keys as the IDE's own.

#### Scenario: A file declaring right to left
- **WHEN** an area's German file declares a right-to-left direction
- **THEN** German still reads left to right

### Requirement: The active language follows areas as they come and go
When an area is registered or taken out, the IDE SHALL re-apply the active language on the UI thread, so the area's
text shows or goes without another language change.

#### Scenario: Registering while a language is active
- **WHEN** an area registers while the IDE is in German
- **THEN** its German text shows, and goes again when the area is taken out

### Requirement: Only a grant in the IDE's source admits an area
The add-in host SHALL refuse a language-pack area unless a grant in the IDE's own source names its owner. A manifest
SHALL NOT grant anything. A grant SHALL name the key families its owner may supply, and SHALL NOT name a family the
consent or trust dialogs read. An area SHALL be read only from the registering code's own assembly. In #3411's phase
2 the Basic provider's grant names the Desktop composition's origin; phase 3 switches it to a first-party signature
with the id `hexide.basic`.

#### Scenario: An owner with no grant
- **WHEN** an add-in no grant names registers an area
- **THEN** it is refused, and the IDE's text is unchanged

### Requirement: An area is refused whole
Before merging anything, the IDE SHALL refuse the whole area for any key outside its grant, not starting `Str.`, held
by the IDE's canonical pack or by another area, or with blank English. It SHALL also refuse it for a language-file key
its own `en.json` lacks, and for a translation whose placeholders differ from its English. Each refusal SHALL be logged
naming both owners.

#### Scenario: An area that names an IDE key
- **WHEN** an area's English holds `Str.Menu.File` beside keys of its own
- **THEN** none of its keys is added, and the log names the area's owner and the IDE

### Requirement: The user's translations keep what no area holds now
The user's translations SHALL keep an override whose key no area holds when they load, and SHALL save it with the rest
of the language's overrides without applying it. They SHALL place it once an area holding its key registers, and SHALL
keep an override of an area's key again when its area is taken out.

#### Scenario: Saving while the provider is absent
- **WHEN** the user's German file translates a provider key, no area holds it, and the user changes another German string
- **THEN** the saved file still holds the provider key's translation

#### Scenario: The provider registers
- **WHEN** the area that holds that key registers
- **THEN** the user's translation shows in place of the area's

### Requirement: Clearing and resetting leave kept translations on disk
Clearing a language's last applied override SHALL NOT delete a file that still holds kept ones. Resetting a language
SHALL remove what the Translation Editor shows and SHALL leave the kept overrides, which it does not show.

#### Scenario: Resetting German
- **WHEN** the user resets German while its file holds a kept translation
- **THEN** the file holds only that translation afterwards
