## ADDED Requirements

### Requirement: An IDE started for an agent session SHALL run tool calls from that session only
When the IDE is started with the name of an agent session, the server SHALL run a tool call only when the client names
that same session. It SHALL answer any other tool call, from a client naming another session or none, with an error
that names the session the IDE serves and identifies its process, and SHALL NOT run it. An IDE started without a
session SHALL serve every caller.

#### Scenario: The session the IDE serves
- **WHEN** a client naming the session the IDE was started for calls a tool
- **THEN** the tool runs and its reply is returned

#### Scenario: Another session's client
- **WHEN** a client naming a different session, or none, calls a tool on an IDE started for a session
- **THEN** the call is answered with an error naming the IDE's session and process, and the tool does not run

#### Scenario: An IDE started without a session
- **WHEN** any client calls a tool on an IDE started without a session
- **THEN** the tool runs, as it would for a single developer with nothing configured

### Requirement: A refused tool call SHALL leave the client connected and told why first
A tool call refused because it came from another session SHALL be answered as an error result rather than as a refused
request, so that the client stays connected and can read the reason however many calls it makes. The refusal SHALL be
given before any other check on the call, such as of its arguments, and requests other than tool calls, such as
listing the tools, SHALL be served.

#### Scenario: Repeated calls from the wrong session
- **WHEN** a client naming another session makes several tool calls in a row
- **THEN** each is answered with the refusal, and the client can still list the tools

#### Scenario: A wrong session with wrong arguments
- **WHEN** a client naming another session calls a tool with arguments the tool would refuse
- **THEN** the reply is the session refusal, not a description of the arguments

### Requirement: The health check SHALL name the session the IDE serves
The health check SHALL report the agent session the IDE was started for, beside the process ID it already reports, and
SHALL report none for an IDE started without one.

#### Scenario: Readiness names the session
- **WHEN** tooling polls the health check of an IDE started for a session
- **THEN** the reply names that session beside the process ID
