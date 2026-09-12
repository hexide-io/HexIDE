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

It SHALL also allow a connection's record to be discarded without disarming it. A loop that exercises one
thing, reads the record and moves on needs the next reading to contain only the next thing; discarding that
also stopped the recording would make every iteration after the first useless.

A listing SHALL say how many entries matched when it returns fewer, for the same reason a truncated message
body states its true length: a list that quietly stops reads exactly like a complete one.

#### Scenario: A development loop that restarts the IDE
- **WHEN** the IDE is launched with capture requested
- **THEN** capture is armed before the first connection is made, including its initialization exchange

#### Scenario: Moving on to the next thing
- **WHEN** a connection's record is discarded
- **THEN** the record is empty, the connection is still armed, and what follows is recorded

#### Scenario: More matched than were asked for
- **WHEN** a listing is limited
- **THEN** the most recent matches are returned, and the total that matched is stated

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

### Requirement: An automation client SHALL be able to answer a native file dialog
The server SHALL let a client pre-answer the next file dialog the application opens, with a path or with a
cancellation, and SHALL let armed answers be discarded. An armed answer SHALL be consumed by exactly one
dialog. Nothing about the flow below the dialog SHALL be bypassed, and no file SHALL be created by arming
an answer. The facility SHALL be absent from builds the automation server is absent from.

A file picker is a native operating-system dialog. It is outside the control tree, so the tree cannot see
it and no path addresses it, and while a modal one is up the server does not answer at all. Every feature
that ends in Save As, Open, Make EXE, Add File or Export was therefore verifiable only by asking a person
to click, which is the one thing the development loop is not allowed to require.

Single-shot rather than a standing override, because an answer left armed silently redirects the next
unrelated save, and that damage surfaces somewhere other than where it was caused.

#### Scenario: Driving a save
- **WHEN** a client arms a path and then invokes an action that opens a save dialog
- **THEN** the flow proceeds as though a person had chosen that path, and the file is written there

#### Scenario: Driving a cancellation
- **WHEN** a client arms an empty answer and an action opens a file dialog
- **THEN** the flow proceeds as though the dialog had been cancelled

#### Scenario: An answer that was never spent
- **WHEN** a client arms an answer and the expected dialog does not open
- **THEN** the answer remains armed and is reported as such, and discarding it is possible

#### Scenario: A distributed build
- **WHEN** a build excludes the automation server
- **THEN** it contains no way to bypass a file dialog
