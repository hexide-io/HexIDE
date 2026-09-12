# hexide-mcp-server Specification

## Purpose
Define the automation server: an endpoint that lets an external agent inspect and drive a running IDE.

It exists because this is a visual tool, and a visual tool cannot be verified by its test suite alone. A
headless test proves a view model computed the right value; it does not prove the control rendered, that the
menu item is enabled, or that the panel is where anyone can reach it. The server closes that gap — the same
agent that wrote the code can open the IDE, drive it, and look at the result.

> **Currently a development tool.** The server is excluded from released builds, so a distributed binary
> opens no port. Making it a supported opt-in feature for end users is a decided direction rather than
> current behaviour, and the requirements below describe what ships today.

## Requirements

### Requirement: The server SHALL be inactive unless explicitly requested
The IDE SHALL start no server, open no port and create no listener unless a port is supplied at launch.

The IDE's normal job does not involve accepting connections, and a port that opens because the application
started is a port nobody decided to open. Requiring it to be asked for means the default posture costs
nothing and exposes nothing, and the presence of a listener is always attributable to a deliberate act.

#### Scenario: Starting the IDE normally
- **WHEN** the IDE is launched without a port
- **THEN** no server starts and nothing listens

#### Scenario: Starting with automation enabled
- **WHEN** the IDE is launched with a port
- **THEN** the server starts on it and the chosen port is recorded in the log so tooling can find it

### Requirement: The server SHALL be absent from distributed builds
The server and its supporting dependencies SHALL be excluded from release builds, so that a distributed
binary cannot serve automation requests under any argument.

Being off by default protects a user who does nothing; being absent protects one who is talked into
something. It also keeps a web-server dependency out of shipped binaries, which is worth having for its own
sake — the smallest attack surface is the code that is not there.

#### Scenario: A released build asked to serve
- **WHEN** a distributed build is launched with a port argument
- **THEN** the argument has no effect and nothing listens

### Requirement: The server SHALL report readiness separately from serving automation
The server SHALL offer a health check that succeeds once the process is running, distinct from the
automation endpoint.

Automation tooling needs to know when the IDE has finished starting before it sends anything, and the
alternative — retrying a real request until it stops failing — cannot distinguish "not ready yet" from
"broken". A dedicated check makes the wait loop trivial and unambiguous.

#### Scenario: Waiting for the IDE to be ready
- **WHEN** tooling polls the health check after launching the IDE
- **THEN** it succeeds once the IDE is running, and automation calls can begin

### Requirement: The tool surface SHALL cover inspection, navigation, editing and execution
The server SHALL expose tools to inspect project and editor state, open and activate documents, read
diagnostics, manipulate the form designer, control execution and debugging, and observe and drive the
interface itself.

The purpose is to let an agent do what a developer sitting at the IDE could do. A surface that only reads
proves rendering but cannot set up the state worth looking at; one that only writes cannot check its own
work. Both halves are needed for the loop to close without a human in it.

#### Scenario: Verifying a change to a visual surface
- **WHEN** an agent needs to confirm a change to the interface
- **THEN** it can set up the required state, act on the interface, and observe the result without human help

### Requirement: Interface automation SHALL be generic rather than per-surface
The server SHALL provide a general means of discovering a control, acting on it, and inspecting the result,
sufficient to reach interface surfaces without a purpose-built tool for each one.

The alternative is a tool per interaction, and that surface grows without limit — every dialog and panel
eventually gets its own verb, the list stops fitting in an agent's context, and each addition is a small
maintenance burden forever. A generic discover-act-inspect trio means new interface work is automatable the
day it lands, with nothing to add.

#### Scenario: Driving a newly added dialog
- **WHEN** a dialog is added to the IDE
- **THEN** an agent can find its controls, act on them and check the outcome, with no new tool

### Requirement: A purpose-built tool SHALL be justified against the generic mechanism
A new dedicated tool SHALL be added only where the generic mechanism genuinely cannot reach the surface, and
where the tool reads state the interface does not expose, performs a transaction such as persisting or
recording an undoable step, or is materially more reliable than addressing a control by path.

Without a stated bar, "add a tool" is always the easiest answer and the surface sprawls by default. With
one, the question has a decidable answer — and the three exceptions are real: some state exists only in a
model with no visual representation, some actions must be atomic, and addressing a control by its position
in a tree is fragile in a way a direct call is not.

#### Scenario: Proposing a new tool
- **WHEN** a new tool is proposed
- **THEN** it is accepted only if the generic mechanism cannot reach the surface and one of the exceptions applies

#### Scenario: A tool that duplicates the generic mechanism
- **WHEN** a proposed tool does something the generic mechanism already does
- **THEN** it is declined in favour of the generic mechanism

### Requirement: The server SHALL serve only requests that originated on this machine
The server SHALL refuse any request whose `Host` header does not name a loopback address on the port it
bound, and SHALL refuse any request carrying an `Origin` that is not itself loopback on that port. The check
SHALL apply to every endpoint, including the health check. A request with no `Origin` SHALL be served, and a
refusal SHALL be distinguishable from the endpoint not existing.

Binding to loopback is not by itself the control it appears to be. It prevents a request arriving from the
network. It does not prevent a browser already running on this machine, and a browser is the only client that
reaches a local port without anyone choosing to.

The vector is DNS rebinding, and it defeats same-origin protections rather than being caught by them: a page
from another site is same-origin with itself, its name is re-resolved to a loopback address, and the request
arrives with no preflight because as far as the browser can tell nothing changed. The one thing the trick
cannot hide is the `Host` header, which still names the site the browser was asked for.

The absence of an `Origin` is not suspicious and must not be refused, because an automation client is not a
web page and sends none. Refusing it would turn a security control into an outage.

This addresses the browser vector only. Anything that can already open a socket to the port may put whatever
it likes in a header, and the control against that is authentication, which this requirement does not claim.

#### Scenario: A request naming this machine
- **WHEN** a request arrives whose `Host` is a loopback address on the bound port
- **THEN** it is served

#### Scenario: A rebinding attempt
- **WHEN** a request arrives on the loopback interface whose `Host` names some other site
- **THEN** it is refused before reaching any endpoint

#### Scenario: An ordinary automation client
- **WHEN** a request arrives with a loopback `Host` and no `Origin` header
- **THEN** it is served

#### Scenario: A page that reached the right host
- **WHEN** a request arrives with a loopback `Host` but an `Origin` naming another site
- **THEN** it is refused

#### Scenario: The health check
- **WHEN** a request whose `Host` is not loopback asks for the health check
- **THEN** it is refused, rather than being told the project name and language-service state

### Requirement: A tool that writes a file SHALL report whether it was written
Where a tool asks the IDE to write a file, it SHALL report the outcome the IDE actually reached. A write
the IDE refused SHALL NOT be reported as a success, and the report SHALL say the file on disk is unchanged.

An automation client has no dialog to read. Everything a developer would learn from a warning — that the
file was left alone, and why — reaches an agent only through the tool's own answer, so an answer that says
"written" when nothing was written is not a cosmetic inaccuracy: it is the only signal there was, and it
was wrong. The agent then proceeds on the belief that its change is on disk, and every step after that is
built on it.

This matters most where the IDE is behaving correctly. A refusal is the IDE working as designed, protecting
a file it cannot reproduce; reporting it as success converts a safe outcome into a misleading one at the
surface, which is the one place the protection cannot be seen.

#### Scenario: A write the IDE refuses
- **WHEN** a tool asks the IDE to write a file the IDE will not reproduce
- **THEN** the tool reports that it was not written, and that the copy on disk is unchanged

#### Scenario: A write that succeeds
- **WHEN** a tool asks the IDE to write a file it can reproduce
- **THEN** the tool reports success, as before

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
