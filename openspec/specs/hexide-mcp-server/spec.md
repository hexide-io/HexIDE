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
