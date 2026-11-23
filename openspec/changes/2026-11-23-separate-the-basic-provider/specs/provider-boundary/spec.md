## Purpose
Keep the boundary between the IDE and the Basic provider visible and shrinking while the provider moves out
of the tree (#3411).

## ADDED Requirements

### Requirement: Product references into the provider are listed
The IDE's product assemblies SHALL reference the Basic provider's assemblies only where a committed list in
the repository names the reference. The list SHALL name each referenced provider type, each assembly
reference, each project reference and each internals grant that crosses the boundary. The repository's tests
SHALL fail when a product assembly has a reference into the provider that the list doesn't name, and the
failure SHALL name the reference.

#### Scenario: A new reference into the provider
- **WHEN** a change makes a product assembly reference a provider type that the list doesn't name
- **THEN** the tests fail, naming the product assembly and the provider type
- **AND** the failure says to move the code into the provider or behind a shared contract

#### Scenario: A project added to the tree
- **WHEN** a project file is added that the guard doesn't classify
- **THEN** the tests fail, naming the project

### Requirement: The list can only shrink
The list SHALL name only references that exist. The repository's tests SHALL fail when the list names a
reference that no product assembly makes any more, and the failure SHALL name the row to delete. The tests
SHALL NOT write the list themselves.

#### Scenario: A slice removes a reference
- **WHEN** a change moves code so that a listed reference no longer exists
- **THEN** the tests fail until the change also deletes that row from the list

### Requirement: Provider registrations only add
A provider SHALL contribute to the IDE only through registrations that add. A registration naming a key,
resource, command, component or project type that the IDE or another provider already holds SHALL be refused,
and the refusal SHALL be logged, naming both owners.

#### Scenario: A second registration of an existing key
- **WHEN** a provider registers a resource whose key the IDE already holds
- **THEN** the registration is refused and logged, naming the IDE and the provider
- **AND** the IDE's own value is unchanged

### Requirement: A provider's strings come only from its own pack area
A provider's resource dictionaries SHALL NOT supply a localization string key or a theme key. Its strings SHALL
come only through its own language pack area, which SHALL be refused any key the IDE or another provider holds.
Nothing a provider registers SHALL replace a resource that the add-in consent and trust dialogs read.

#### Scenario: A provider's strings name a consent dialog's string
- **WHEN** a provider's language pack area or resources include a key that a consent or trust dialog reads
- **THEN** that key is refused
- **AND** the dialog shows the IDE's own text

#### Scenario: A provider's own strings
- **WHEN** a provider's language pack area supplies keys that no one else holds
- **THEN** the IDE shows the provider's text for them

### Requirement: A bundled provider registers as any add-in does
A provider's registrations SHALL go through the same host interface that an add-in receives once it has passed
consent. No registration point SHALL exist outside that interface. A first-party or bundled provider SHALL use the
same calls, and meet the same checks, as a third-party one.

#### Scenario: The bundled Basic provider starts
- **WHEN** the IDE starts the bundled Basic provider
- **THEN** the provider registers its contributions through the host interface an add-in receives
- **AND** each registration meets the same checks a third-party add-in's would

### Requirement: A registration beyond a plain control needs the provider's grant
The IDE SHALL keep, in its own source, a grant naming the owners that may register resources, a language pack area, or a
component whose role is not `Control` or that is a container. A registration of any of these from an owner the grant
doesn't name SHALL be refused, and the refusal SHALL be logged, naming the owner and what it tried to register. While the
provider is composed from `HexIDE.Desktop`, the grant SHALL name that composition and nothing else.

#### Scenario: An add-in registers a form component
- **WHEN** an add-in the grant doesn't name registers a component whose role is `Form`
- **THEN** the registration is refused and logged, naming the add-in and the component
- **AND** the designer offers no such component

#### Scenario: An add-in registers a container
- **WHEN** an add-in the grant doesn't name registers a component that is a container
- **THEN** the registration is refused and logged

#### Scenario: The bundled provider registers its form component
- **WHEN** the provider, composed from `HexIDE.Desktop`, registers a component whose role is `Form`
- **THEN** the registration succeeds

### Requirement: A granted registration still only adds
A registration from an owner the grant names SHALL still only add: it SHALL be refused, and logged naming both
owners, where any key or component it carries is already held by the IDE or another owner, and no grant SHALL
cover a key the consent or trust views read. The detailed rules arrive with the add-in system's delta (#5415) and
the localization delta (#5453).

#### Scenario: The bundled provider registers a key the IDE holds
- **WHEN** the provider, composed from `HexIDE.Desktop`, registers a resource key the IDE already holds
- **THEN** the registration is refused and logged, naming the IDE as the holder
