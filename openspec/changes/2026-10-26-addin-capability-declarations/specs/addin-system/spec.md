## ADDED Requirements

### Requirement: Capability declarations belong to the signed build
A capability-aware manifest SHALL contain a top-level `capabilities` array of unique, recognized,
case-sensitive identifiers, covered by its signature and manifest hash. Invalid declarations SHALL
be refused before code runs. An empty array SHALL declare none. Changed declarations SHALL require
fresh third-party consent keyed by manifest hash, never inherited by add-in identifier.

#### Scenario: Declaration changed without signing
- **WHEN** a package's declared capabilities change without updating its signature
- **THEN** signature verification fails and no package code runs

#### Scenario: A correctly signed update changes capabilities
- **WHEN** a third-party publisher signs a manifest with a changed capability list
- **THEN** the new manifest hash has no inherited consent from the previous build
- **AND** the user must consent before the updated build runs

#### Scenario: Explicitly declaring no capabilities
- **WHEN** a capability-aware manifest contains an empty capabilities array
- **THEN** it receives no declared host capabilities

### Requirement: A manifest that declares nothing gets no host capabilities
A manifest from before capability declarations, which has no `capabilities` field, SHALL still load after
the existing trust and consent checks, and SHALL receive no host capabilities. Each host call that needs a
capability SHALL be refused, naming the capability, until the package is repacked with a declaration. No
consent, signature or add-in identifier SHALL grant such a manifest full trust.

#### Scenario: An add-in written before declarations
- **WHEN** a schema-1 add-in with no capabilities field is allowed and loads
- **THEN** its request to read a project's source through the host is refused, naming `project.read`
- **AND** it was not offered a legacy full-trust option

#### Scenario: Template creation
- **WHEN** an add-in that declares `ui.contribute` but not `project.write` registers a project template
- **AND** the user creates a project from it
- **THEN** the template's files are not written, and the refusal names `project.write`

### Requirement: Host capability checks reflect declared consequences
The host SHALL gate project inspection, modification, execution, UI/navigation and language-server
contribution with the independent declarations `project.read`, `project.write`, `program.run`,
`ui.contribute` and `languageServer.contribute`. For capability-aware manifests, undeclared operations
SHALL be refused before reads or side effects, including older overloads. Cleanup SHALL remain available.
These checks SHALL NOT be described as in-process containment.

#### Scenario: An undeclared editor write
- **WHEN** an add-in with only project.read requests an editor content change
- **THEN** the host refuses it with the missing project.write capability identified
- **AND** no content changes

#### Scenario: Independent add-in authority
- **WHEN** one add-in has consent for project.read and another has no such declaration
- **THEN** only the first can read source through the host

### Requirement: Consent and disclosure preserve declaration provenance
The IDE SHALL make the verified manifest hash and capability declarations available to consent and
information surfaces. It SHALL distinguish a legacy manifest with unknown capabilities from an explicit
empty declaration. Disclosure of observed host-mediated use SHALL distinguish declarations from use
and SHALL NOT imply that unobserved activity did not occur outside the host surface.

#### Scenario: Legacy information shown
- **WHEN** an information surface describes a manifest without a capability declaration
- **THEN** it identifies the declaration as absent rather than claiming the add-in needs no capabilities

#### Scenario: No host-mediated use recorded
- **WHEN** no use is recorded for a declared capability
- **THEN** the disclosure does not claim the add-in could not have exercised it directly
