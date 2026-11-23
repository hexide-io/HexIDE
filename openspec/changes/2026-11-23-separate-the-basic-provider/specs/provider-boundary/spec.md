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
