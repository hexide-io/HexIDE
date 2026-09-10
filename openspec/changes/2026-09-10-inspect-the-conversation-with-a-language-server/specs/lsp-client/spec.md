## ADDED Requirements

### Requirement: The client SHALL ask a server to describe its own work, and SHALL be able to change its mind
The client SHALL declare a trace level when it initializes a connection, SHALL take that level from
per-server configuration, and SHALL be able to change it on a running connection. It SHALL NOT report a
level as having been refused, because no such signal exists.

A wire trace shows what was said. A server's own trace shows why it said it, which is the half a server
author cannot reconstruct from the outside. The two answer different questions and an author chasing a
failure wants both interleaved.

The initial level travels in the initialization request and so must be known before the process exists,
which is why it belongs in configuration; changing it afterwards is what the protocol's own notification is
for. Reporting a refusal is impossible rather than merely unimplemented: that notification carries no reply,
and the protocol defines no capability by which a client can discover whether a server honours tracing at
all. Measured against five servers, every one accepted the request and four then said nothing. The only
honest report is that the server was asked and has not answered.

#### Scenario: Starting a server with tracing on
- **WHEN** a server is configured to start with tracing enabled
- **THEN** the level is declared in its initialization request

#### Scenario: Turning tracing up on a running server
- **WHEN** the developer raises the level on a connection that is already up
- **THEN** the server is told, without being restarted

#### Scenario: A server that ignores it
- **WHEN** a server accepts the request and emits nothing
- **THEN** the IDE reports that it was asked and nothing has arrived, rather than reporting a refusal

### Requirement: What a server says about itself SHALL reach somewhere a person can see
The client SHALL surface a server's own trace output and its log messages rather than discarding them, and
SHALL rank their severities as the protocol defines them, including severity values introduced after the
version this client targets.

A server that starts, connects and then serves nothing explains itself over exactly these channels, and they
are the only account it can give. Discarding them leaves a misconfigured server indistinguishable from a
broken IDE, which is the failure this client's connection diagnosability was built to end and did not
finish.

Severity has to be right for a reason beyond tidiness. An unknown value mapped to a middle rank makes a
newer protocol version's *noisiest* channel the loudest thing in the record, so a client that guesses
upward is worse than one that does not recognise the value at all.

#### Scenario: A server explaining a misconfiguration
- **WHEN** a server reports a problem with its own setup
- **THEN** that report reaches a surface the developer can read, rather than only a discarded log level

#### Scenario: A severity this client does not know
- **WHEN** a server sends a severity introduced after the version this client targets
- **THEN** it is not ranked above severities the client does know
