## ADDED Requirements

### Requirement: A granted owner SHALL be able to add resources from its own assembly
`IHexIdeHost.RegisterResources` SHALL add an Avalonia resource dictionary named by an `avares://` address in the calling assembly, only for
an owner that HexIDE's own grant table names, and only with keys in the families its grant names. The IDE's own host SHALL refuse the call;
an add-in's owned host SHALL pass its origin and its calling assembly.

#### Scenario: A provider adds its controls' themes
- **WHEN** the Basic provider registers its control themes at startup, through the owned host the IDE composes for it
- **THEN** its controls draw with those themes in the designer and when the program runs
- **AND** `App.axaml` names none of the provider's resources

#### Scenario: An owner with no grant
- **WHEN** an add-in without a grant registers a dictionary
- **THEN** it is refused and nothing is merged

### Requirement: A registered dictionary SHALL NOT change what the IDE shows
Every key, in merged and theme dictionaries too, SHALL be read before anything merges, and one refused key SHALL refuse the whole dictionary.
A key SHALL be refused if it is `Str.*`, is held by the IDE or another owner, is a type outside the grant or the calling assembly, or is
neither a string nor a type. A root that isn't a resource dictionary SHALL be refused. Each refusal SHALL be logged naming the key and both
owners.

#### Scenario: A key another owner holds
- **WHEN** an owner registers a dictionary holding a key the IDE or another owner already holds
- **THEN** the whole dictionary is refused, the log names the key and both owners, and the key keeps its value

#### Scenario: A consent dialog's text
- **WHEN** a dictionary holds a `Str.*` key, at its top or in a merged dictionary, even one its owner's grant would cover
- **THEN** the dictionary is refused and the IDE's text is unchanged

### Requirement: Grants SHALL be held only by the owners HexIDE names
A grant for an owner the IDE composes SHALL match only an origin marked as composed, which nothing that reads a package's manifest marks.
Any other grant SHALL match only a package signed by the first-party key. No grant SHALL name a key the trust and consent views read. When
phase 3 of #3411 loads the Basic provider as a bundled add-in, its grant SHALL move to that first-party package with its id, and the
composed origin SHALL go.

#### Scenario: A package named after the provider
- **WHEN** a package whose manifest names it `hexide.basic`, signed or not, registers resources
- **THEN** it holds no grant and is refused

#### Scenario: The trust and consent views
- **WHEN** the grant table is read against the resource keys the trust and consent views read
- **THEN** no grant's families reach any of them
