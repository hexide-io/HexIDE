# ide-personalities Specification

## Purpose
Define personalities: named profiles that decide how much of the IDE is on show.

The IDE reproduces a family of products, not one. Standalone VB6 and the editor embedded in Office share a
language and most of a window, and differ in what makes sense to offer — an embedded editor has no
standalone project to create, and no executable to build. A personality is how one build presents itself as
whichever of those the developer is working in.

## Requirements
### Requirement: A personality SHALL determine which surfaces are offered
A personality SHALL control which menu entries, toolbar buttons and project types are presented.

The differences between these products are almost entirely about what is offered rather than how anything
works. Expressing them as visibility keeps one implementation behind every surface, which is what stops the
variants diverging into separate products that have to be maintained in parallel.

#### Scenario: Working in a reduced personality
- **WHEN** a personality that excludes standalone projects is active
- **THEN** the commands and project types for creating them are not offered

### Requirement: A personality SHALL be a declarative profile, not a code path
A personality SHALL be expressed as a configuration value describing which features are surfaced, and
feature availability SHALL be decided by consulting it rather than by branching on the personality's
identity.

Branching on a name means every new personality edits every site that cares, and the set of differences
exists only as scattered conditionals nobody can read as a whole. A profile makes the differences a single
readable object, and adding a personality becomes describing one rather than finding every place that must
change.

#### Scenario: Adding a personality
- **WHEN** a new personality is introduced
- **THEN** it is described as a profile, without adding branches at the surfaces it affects

#### Scenario: Deciding whether to show a surface
- **WHEN** the IDE decides whether to offer a feature
- **THEN** it consults the active profile rather than testing which personality is active

### Requirement: A personality SHALL change what is offered, never what works
Functionality SHALL remain present and correct regardless of the active personality; a personality SHALL
only affect what is surfaced.

Two reasons. A project created under one personality must still open under another — a file is not
personality-specific and the IDE must not become unable to load its own work. And keeping one code path
behind every feature means a personality cannot introduce a bug that exists only in one of them, which is
the failure mode that makes variant builds expensive.

#### Scenario: Opening a project created under a different personality
- **WHEN** a project created under a fuller personality is opened under a reduced one
- **THEN** it loads and works, even where the commands that created it are not offered

### Requirement: The full VB6 surface SHALL be the default
The personality presenting the complete standalone VB6 surface SHALL be the one in force unless another is
chosen.

It is what the product is for, and it is the least surprising default: a reduced surface would leave a
developer hunting for a command that is present, working, and hidden — which reads as a missing feature
rather than as a setting.

#### Scenario: A fresh installation
- **WHEN** the IDE runs with no personality chosen
- **THEN** the full VB6 surface is offered
