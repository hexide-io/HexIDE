## ADDED Requirements

### Requirement: A server may declare a workspace artefact it requires

A server entry SHALL be able to declare that it requires a file at a path relative to the workspace root,
naming a provider that produces the file's content. HexIDE SHALL materialise that file before the server is
started, so that a server which reads a project descriptor at startup finds one.

The declaration SHALL name the provider rather than carry the content, so that no descriptor format is
described in configuration and the set of producible formats is open.

#### Scenario: A declared artefact exists before the server starts

- **GIVEN** a server entry declaring an artefact at a relative path and a provider that produces content
- **WHEN** the server is started for a workspace
- **THEN** the file SHALL exist at that path, holding that content, before the server process is created

#### Scenario: A server declaring no artefact is unaffected

- **GIVEN** a server entry with no artefact declaration
- **WHEN** the server is started
- **THEN** HexIDE SHALL write nothing and start the server exactly as before

#### Scenario: A provider may decline

- **GIVEN** a provider that returns no content for the current project
- **WHEN** the server is started
- **THEN** no file SHALL be written, no error SHALL be reported, and the server SHALL start

### Requirement: A provider receives a project snapshot and returns content

A provider SHALL receive a read-only snapshot of the project — its name, the path of the file defining it,
its files, and its references — and SHALL return content. A provider SHALL NOT write to disk.

The snapshot SHALL give each file both the path as the project spells it and the resolved host path,
because a descriptor records the former while reading a file requires the latter, and they differ on hosts
whose separator is not the one the native project format uses.

#### Scenario: A provider cannot bypass validation or consent

- **GIVEN** any provider
- **WHEN** it produces an artefact
- **THEN** the path check, the consent decision and the write SHALL be performed by HexIDE rather than by
  the provider

### Requirement: An artefact is written only inside the workspace, and only with consent

HexIDE SHALL refuse to write an artefact whose resolved path lies outside the workspace root. Writing into
a workspace SHALL require the user's consent, recorded per server, and that consent SHALL be revocable.

#### Scenario: A path escaping the workspace is refused

- **GIVEN** an artefact declaration whose path resolves outside the workspace root
- **WHEN** the configuration is read
- **THEN** the declaration SHALL be refused with the entry named, and the server SHALL still start without
  an artefact

#### Scenario: Consent is asked once and remembered

- **GIVEN** a server whose artefact has not been consented to
- **WHEN** it is first about to be written
- **THEN** the user SHALL be asked, naming the server and the file, and a granted consent SHALL be
  remembered for that server and revocable afterwards

#### Scenario: A denied write disables the server, not the IDE

- **GIVEN** a user who denies the write
- **WHEN** the server would have started
- **THEN** no file SHALL be written, the server SHALL NOT be started, and the reason SHALL be recorded
  against the connection

### Requirement: A failed artefact degrades to the server's own behaviour

A malformed artefact declaration SHALL be reported and ignored rather than preventing the server from
starting, because a server that reads no descriptor may still be useful, while a server that does not start
is certainly not.

A provider that throws, or a write that fails, SHALL disable that server alone and SHALL record the reason
where the user can read it.

#### Scenario: A declaration naming an unknown provider

- **GIVEN** an artefact declaration naming a provider that is not registered
- **WHEN** the configuration is read
- **THEN** the fault SHALL be reported with the entry named, and the server SHALL start with no artefact

### Requirement: A changed project invalidates a generated artefact

When the project model changes in a way that alters an artefact's content, HexIDE SHALL regenerate it and
SHALL notify servers that registered an interest in that file.

HexIDE SHALL NOT rewrite a file whose content has not changed, so that a server watching it is not woken
without cause.

#### Scenario: A material change regenerates and notifies

- **GIVEN** a running server with a generated artefact
- **WHEN** the project gains or loses a reference or a file
- **THEN** the artefact SHALL be regenerated, and a server that registered an interest in that path SHALL
  be notified of the change

#### Scenario: An immaterial change writes nothing

- **GIVEN** a running server with a generated artefact
- **WHEN** the project changes in a way that does not alter the artefact's content
- **THEN** the file SHALL NOT be rewritten and no notification SHALL be sent
