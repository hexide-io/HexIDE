## ADDED Requirements

### Requirement: The captured conversation SHALL be readable through the automation surface, in two tiers
The server SHALL expose the recorded conversation to an automation client: a filterable listing of message
envelopes, and retrieval of one message's content by its identifier. It SHALL also allow a connection to be
armed and disarmed, and the IDE SHALL accept an argument at launch that arms capture before any connection
is made.

An agent verifying a language feature has the same problem a person does, and a sharper version of it: it
cannot tell "the request was never sent" from "the answer came back empty" from "the answer was fine and the
panel rendered it wrong". Only the last is a user-interface defect. A capture that only a human can read
leaves the loop that does most of the verifying here unable to use it.

The two tiers are not an optimisation. A conversation is measured in megabytes per minute of typing, so a
tool that returned one whole would be unusable; and the split already exists in the record, because
envelopes are retained always and content only when armed. Listing is cheap and answers most questions;
fetching one body answers the rest.

The launch argument exists because the documented development loop restarts the IDE on every iteration,
while arming is deliberately session-scoped. Without it, every iteration would begin by arming again.

#### Scenario: Working out why a feature did nothing
- **WHEN** an agent lists the envelopes for a connection after exercising a feature
- **THEN** it can see whether the request was sent, what came back, and how long it took

#### Scenario: Reading one message
- **WHEN** an agent asks for a specific message's content
- **THEN** it receives that message and not the conversation around it

#### Scenario: A development loop that restarts the IDE
- **WHEN** the IDE is launched with capture requested
- **THEN** capture is armed before the first connection is made, including its initialization exchange

### Requirement: The capture SHALL remain present in builds the automation server is absent from
The recording machinery SHALL be part of the shipped application rather than of the automation server, so
that removing the server from a distributed build does not remove the ability to record.

The automation server is a development tool and is compiled out of distributed builds. The inspector is not:
its audience is somebody writing a language server against a HexIDE they downloaded. Placing the recording
beside the tools that read it would tie the shipped feature to the unshipped one, and the failure would be
silent, appearing only in a configuration nothing in CI currently builds.

#### Scenario: A distributed build
- **WHEN** a build that excludes the automation server is asked to record a conversation
- **THEN** it records it, and only the automation tools are absent
