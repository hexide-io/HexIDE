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

### Requirement: Removing a selected list or tree item SHALL report the resulting selection
`interact` with `remove_from_selection` SHALL clear a selected item in a single-selection list or tree,
report the resulting selection even when empty, and leave an unselected item unchanged with an explanation.
If the control keeps the item selected, it SHALL refuse and suggest `click` with `value: "ctrl"`.
A single-selection list's `add_to_selection` refusal SHALL name that verb and point to `select`.

#### Scenario: Clearing a watch row
- **WHEN** `remove_from_selection` targets the selected Watches tree row
- **THEN** the tree has no selected item and the reply says nothing is selected now

#### Scenario: Removing an unselected row
- **WHEN** the target list or tree row is not selected
- **THEN** the selection remains unchanged and the reply says the target was not selected

### Requirement: Interrupted keyboard typing SHALL distinguish sending a key from applying its edit
When a key sent by keyboard `type_text` opens a dialog, the reply SHALL identify that key's position
and target, describe the dialog and where it opened, warn that the key may not have landed, and state
that no later keys were sent. It SHALL tell the caller to answer the dialog and check the text before
deciding what to send next.

#### Scenario: A key opens the reset prompt
- **WHEN** the first key of `xyz` opens a dialog
- **THEN** the reply identifies key 1 of 3 as sent and possibly unapplied
- **AND** it directs the caller to answer and check the text rather than blindly send `yz`

### Requirement: Shutdown SHALL preserve the reply and a cancelled IDE's server
`shutdown_ide` SHALL close the main window only after its HTTP response completes. Server cancellation
and context disposal SHALL wait for desktop exit, so a refused or cancelled close leaves automation
available. The reply reports a request, not confirmation of process exit.

#### Scenario: Shutdown is requested through a tool call
- **WHEN** `shutdown_ide` returns its result
- **THEN** the main-window close is deferred until the HTTP response completes

#### Scenario: A close is cancelled
- **WHEN** shutdown is requested but the IDE stays open
- **THEN** the automation server and its context remain available

### Requirement: Server startup failure SHALL preserve forced-exit cleanup order
On server startup failure, the IDE SHALL dispose the add-in loader before forcing desktop shutdown,
so closing an unanswered consent dialog cannot persist a decision. It SHALL attempt shutdown with the
reported exit code even if cleanup throws, and SHALL close the log after shutdown returns so window
closing messages can still reach the file.

#### Scenario: The requested port is already taken
- **WHEN** the server cannot bind the requested port
- **THEN** the add-in loader is disposed before desktop shutdown runs with exit code 3
- **AND** a cleanup exception does not prevent that shutdown
- **AND** the log closes after desktop shutdown returns

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

#### Scenario: CI checks the distributed configuration
- **WHEN** the build-ide job runs
- **THEN** Desktop and Standalone compile in Release
- **AND** the Desktop output is checked for server code, MCP dependencies and AspNetCore references

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

#### Scenario: Selecting a property hidden by a collapsed category
- **WHEN** `interact` selects a property by text in the categorized Properties list and its category is collapsed
- **AND** the category row and its toggle are showing
- **THEN** the refusal SHALL name the category as displayed and tell the caller to `toggle` the Button in that row
- **AND** the selection and category expansion remain unchanged

#### Scenario: A grid recycles a row after filtering
- **WHEN** filtering a DataGrid leaves recycled rows in its rows presenter
- **THEN** interface discovery SHALL omit those rows and their stale cells
- **AND** the remaining current rows SHALL each appear once with paths that resolve to them

### Requirement: Icon-only controls SHALL expose localized action names
The IDE SHALL give the dock pane's Menu, Pin, Maximize/Restore and Close buttons and tab scroll arrows
localized automation names describing their actions. The name SHALL belong to the control's automation peer and be
accepted by path matching, as well as being available to screen readers. Document-tab close buttons, designer caption buttons,
dropdown toggles, property color/font picker buttons and all eight scroll bar line/page buttons SHALL likewise
have localized names describing their actions.

#### Scenario: Discovering an icon-only dock button
- **WHEN** a caller discovers a dock pane's close button
- **THEN** its name describes closing the pane in the active language, rather than naming the glyph's .NET type

#### Scenario: Discovering scroll bar buttons after a language change
- **WHEN** a caller inspects a vertical or horizontal scroll bar after changing the IDE language
- **THEN** its line and page buttons expose action names in the active language, including on existing controls
- **AND** path matching accepts those names

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

The IDE and the real-HTTP test server SHALL install the same loopback middleware. Tests SHALL exercise
foreign `Host` and `Origin` headers on `/mcp`, a foreign `Host` on `/health`, and a permitted loopback request.

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
- **AND** a poll, which asks for what changed after a sequence, returns the earliest changes instead, with a sequence
  to continue from, and keeps requests ended together together even past the limit, saying it was cut short only
  when something matched was left out

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

### Requirement: A tool that names a document SHALL name its project, and count lines from the top of the file
A tool that accepts or reports a form, module or class SHALL identify it by its project and its name, SHALL
find it in any loaded project, and SHALL match both without regard to case. Where a reply also carries the name
a language server knows the document by, it SHALL say so. Every line number a tool accepts or reports SHALL be
counted from the top of the file, header included. Reading a document's content SHALL return its whole file
whether or not it is open. Writing content SHALL NOT be refused for being in the shape reading returned — the
whole file with its header unchanged, or the code alone — which leaves the existing refusal of a document the
IDE will not save in force for its own reason. A refused whole replacement SHALL report a changed header first,
then each member whose Attribute lines are changed, added or removed, in the same reply.

A caller that has never seen the IDE cannot know which of a document's several names a bare `uri` field holds,
and a name that is only unique within a project is ambiguous the moment a group is open. A read whose result
the matching write refuses breaks the most basic loop an automation client runs.

Run-control pause notes SHALL qualify the module with the running project. Locals and watch replies SHALL
identify the running project in which expressions are evaluated. Active-designer replies SHALL name the
designer's document, including a UserControl's or PropertyPage's owning module. Tab listings SHALL report
project and document for source editors and designers. Tab activation and closing SHALL accept a document
name with an optional project through the document lookup, alongside title addressing for tool tabs. A match
that names more than one tab SHALL be refused; a caller MAY select code or designer by tab type.

#### Scenario: Choosing between code and designer tabs
- **WHEN** both tabs for Second/Form1 are open and a caller addresses Form1 in Second without a tab type
- **THEN** the call refuses the ambiguity and explains how to select code or designer
- **WHEN** the caller also supplies the designer type
- **THEN** only Second/Form1's designer is activated or closed, and the reply names Second and Form1

#### Scenario: Setting a breakpoint by name in the wrong case
- **WHEN** a client sets a breakpoint in `form1` of a project containing `Form1`
- **THEN** it is shown in `Form1`'s gutter, pushed to the next run, and saved with the project

#### Scenario: Reading and writing back a form
- **WHEN** a client reads the content of a form the IDE will save, and writes the same text back
- **THEN** the write succeeds and the form is unchanged, but for a member's attribute line naming another name than
  its member's, which the save writes with its member's name, as the form's own Save does

#### Scenario: A replacement changes both the header and a member's Attribute lines
- **WHEN** a client submits a whole file with both its header and a member's Attribute lines changed
- **THEN** the write is refused without changing the document, and the one reply names the header first and the member next

### Requirement: Automation saves explain rewritten member qualifiers
The successful replies from `set_file_content`, `set_control_property` and `add_control` SHALL report in their
`note` any member Attribute qualifiers their save rewrote, naming the old and new names and explaining that
VB6's own Save writes the member's name.

#### Scenario: A save rewrites a stale qualifier
- **WHEN** any of those tools saves a document with `Attribute Gone.VB_Description` under `Public Function Total()`
- **THEN** its successful reply explains that the save wrote Total's Attribute lines with its name, where they said Gone

### Requirement: An IDE started for an agent session SHALL run tool calls from that session only
When the IDE is started with the name of an agent session, the server SHALL run a tool call only when the client names
that same session. It SHALL answer any other tool call, from a client naming another session or none, with an error
that names the session the IDE serves and identifies its process, and SHALL NOT run it. An IDE started without a
session SHALL serve a client that names no session, and SHALL refuse a client that names one in the same way.

#### Scenario: The session the IDE serves
- **WHEN** a client naming the session the IDE was started for calls a tool
- **THEN** the tool runs and its reply is returned

#### Scenario: Another session's client
- **WHEN** a client naming a different session, or none, calls a tool on an IDE started for a session
- **THEN** the call is answered with an error naming the IDE's session and process, and the tool does not run

#### Scenario: An IDE started without a session
- **WHEN** a client naming no session calls a tool on an IDE started without a session
- **THEN** the tool runs, as it would for a single developer with nothing configured

#### Scenario: A named session reaches an IDE started without one
- **WHEN** a client naming a session calls a tool on an IDE started without a session
- **THEN** the call is answered with an error saying the IDE serves no session, and the tool does not run

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


### Requirement: Snapshots identify the title and capture area

The snapshot description and reply SHALL explain that the image captures the window's client area,
without its title bar or frame, with open popups included unless the reply's note says otherwise.
The reply SHALL report the captured window's actual title in `title`, including for the IDE's own
window and an empty caption. It SHALL retain `activeDialog` with its previous meaning for compatibility.

#### Scenario: Confirming a running form's caption

- **WHEN** a caller captures a running form titled Hello
- **THEN** the reply reports `title: "Hello"`
- **AND** `captureArea` explains that the image omits the title bar and frame
- **AND** the compatibility field `activeDialog` still reports Hello

### Requirement: Window mutations await platform confirmation
`set_window_state` SHALL wait at most one second per phase for the requested state and for requested geometry
notifications, return observed window values, and report an unconfirmed request with `success: false` and a
note directing the caller to `get_window_state`. Geometry SHALL follow a confirmed restore, and off-screen
validation SHALL precede any mutation. Omitted coordinates SHALL retain the restored position.

#### Scenario: A window manager applies changes asynchronously
- **WHEN** the platform confirms a state or geometry change after a dispatcher layout pass
- **THEN** the reply waits for confirmation within the bound and reports observed values

#### Scenario: No window manager applies the request
- **WHEN** the requested state remains unconfirmed at the deadline
- **THEN** the reply reports failure, the observed window, and how to re-read its state

### Requirement: Snapshot coordinates include popup overhang

A snapshot SHALL include the window and its open native popups at their relative screen positions,
including popups extending left of or above the window. The reply SHALL report the window origin in
full-image pixels as `windowOffsetX` and `windowOffsetY`, zero when no left or top overhang exists.
Tool descriptions SHALL explain how to map inspected window-relative bounds into both returned images.

#### Scenario: A menu extends left of the window

- **WHEN** an open native popup extends left of the captured window
- **THEN** the window is shifted right in the image to leave room for the overhang
- **AND** the reply reports that shift as `windowOffsetX`
- **AND** a window-relative x coordinate maps to `x * scale + windowOffsetX` in the full image
- **AND** in the inline copy it maps to `(x + windowOffsetX / scale) * inlineScale`

### Requirement: Automation SHALL be able to start with a pathless project
The `--newproject-unsaved` launch flag SHALL create a Standard EXE and skip the startup dialog without saving
its project or component files. It SHALL take precedence over `--newproject` and a positional project path.
The existing `--newproject` flag SHALL continue saving to a unique temporary directory. `add_file` SHALL
leave new documents pathless and explain the null path and how to save them in its reply.

#### Scenario: Exercising an untitled document from launch
- **WHEN** the IDE starts with `--newproject-unsaved`
- **THEN** its initial form and project have no file paths
- **AND** opening the form's code uses its `untitled:` URI until it is saved

### Requirement: Named controls SHALL expose their visible labels
The control tree and single-control inspection SHALL retain x:Name as `name` when present and report the
peer's meaningful label as `label` when it differs. Paths SHALL retain their existing stable discriminators
and accept a label as an alternative discriminator. Discovery searches SHALL include the label, and interaction
replies SHALL use the meaningful label when available.

#### Scenario: Discovering a named menu
- **WHEN** a menu item has x:Name `ToolsMenuItem` and visible header `_Tools`
- **THEN** its node and inspection report `name: "ToolsMenuItem"` and `label: "Tools"`
- **AND** its emitted path ends in `MenuItem[ToolsMenuItem]`, while `MenuItem[Tools]` also resolves
- **AND** expanding it names `Tools` in the reply

### Requirement: Attribute window waits when settling running-form actions

An automation reply for a running form SHALL distinguish a handler waiting on a window from one suspended at a
time slice, breakpoint or asynchronous control call. Starting a window wait SHALL clear that handler's earlier
DoEvents loop mark, so a loop resumed by answering the window is waited for as work the action sets going.
A window already open before the action SHALL be considered by the program wait only when the program was
waiting on a window before the action.
Only a running form or a box marked by the program's own MsgBox or InputBox call SHALL opt into the
program wait. An IDE dialog SHALL use the IDE settling path even when a running form owns it.

#### Scenario: An IDE dialog over a running form
- **WHEN** interact toggles a checkbox in an IDE dialog owned by a running form
- **THEN** it reads the dialog's settled state through the IDE path
- **AND** it does not attribute that dialog's state to the program continuing

#### Scenario: Answer a window after an earlier DoEvents
- **WHEN** a handler calls DoEvents, waits at a MsgBox or modal form, then runs a finite DoEvents loop
- **AND** an automation action answers or closes that window
- **THEN** the reply waits for the loop to finish, up to the usual program wait limit

#### Scenario: An unrelated old dialog during a busy loop
- **WHEN** a watch finds the program between two DoEvents, with an unrelated dialog already open
- **AND** the action queues a handler that opens no window
- **THEN** the old dialog does not end the program wait or get named as holding the handler

### Requirement: Discovery and legacy clients share a stateless transport
The automation server SHALL support discovery for the 2026-07-28 protocol revision and SHALL keep both discovery
and legacy initialize clients stateless, without issuing an MCP session ID or offering a standalone GET stream.

#### Scenario: Discovery without initialization
- **WHEN** a client sends `server/discover` without first sending `initialize`
- **THEN** the server advertises `2026-07-28` among its supported versions and issues no MCP session ID

#### Scenario: Rebuild with an attached client
- **WHEN** the server restarts on the same endpoint after a client has connected
- **THEN** that client's next tool call is answered without a stale transport session

### Requirement: Integral argument overflow explains the accepted range
The server SHALL refuse an integral argument outside its parameter's storage range with the argument
name, supplied value, whether it is too large or too small, and the accepted whole-number range.
It SHALL include the parameter's declared guidance where available, and SHALL NOT expose serializer
locations or .NET type names for that overflow. Nothing in the tool SHALL run for the refused call.

#### Scenario: Hover dwell exceeds the integer range
- **WHEN** hover receives dwellMs 3000000000
- **THEN** the refusal names dwellMs and the whole-number range -2147483648 to 2147483647
- **AND** includes the declared 0 to 10000 ms dwell guidance

#### Scenario: A long integer is below its minimum
- **WHEN** an argument for a long integer parameter is -9223372036854775809
- **THEN** the refusal says it is too small and gives -9223372036854775808 to 9223372036854775807

### Requirement: Visual-tree match replies SHALL explain unselected contents
A `dump_visual_tree` match reply SHALL name unselected document tabs when searching the IDE window,
and unselected Options pages when searching the Options dialog, and explain how to select them.
This note SHALL accompany both empty and nonempty matches, alongside any depth-limit warning.

#### Scenario: An editor is behind a designer
- **WHEN** a match search runs with a code tab open but unselected
- **THEN** the note names that tab, says its contents are not in the tree, and directs the caller to `activate_document_tab`

#### Scenario: A setting is on another Options page
- **WHEN** a match search runs in Options with other pages unselected
- **THEN** the note names their category and page and directs the caller to expand the category and select the page with `interact`

### Requirement: Program wait replies distinguish running and queued handlers

Program wait replies SHALL name only events with handlers in the program and SHALL describe queued handlers
as waiting to run separately from handlers already started.

#### Scenario: Events arrive while a MsgBox holds a handler

- **WHEN** a handler is suspended at a MsgBox and other control events arrive
- **THEN** the reply names the suspended handler as still running
- **AND** events with handlers queued behind it are named as waiting to run
- **AND** events without handlers are omitted

### Requirement: Realized items SHALL be listed in item order
The control-view collector shared by `dump_visual_tree` and path resolution SHALL order an items
panel's own containers by their owning `ItemsControl` index. Children without an item index SHALL
follow those containers, retaining their relative visual-child order.

#### Scenario: Selection scrolls a virtualized list
- **WHEN** `interact` selects a row out of view and the list realizes rows around it
- **THEN** the dump lists those rows in item order, each once, and their paths resolve to those containers

#### Scenario: A selected row is visible near the top
- **WHEN** a selected row is the third row on screen
- **THEN** it follows the first two rows in the dump rather than appearing first because it was created first

### Requirement: Large discovery trees prioritize showing controls

A `dump_visual_tree` tree exceeding the 40,000-character tree budget SHALL first omit children below
hidden nodes, retaining the hidden nodes and marking omitted children with `truncated`. The requested
root SHALL retain its immediate children so a caller can retrieve more by rooting a subsequent dump at
an omitted branch. If necessary, the server SHALL then cut other branches to the greatest depth
that fits, retaining at least one level and preserving the active document subtree (to the requested
maxDepth) and its ancestors. If the active document alone prevents fitting, the note SHALL explain
that it remains over budget and advise scoping with `root`. The reply note SHALL explain each kind of omission and how to
retrieve the rest. A tree that fits SHALL remain unchanged; `match` searches SHALL search the unpruned
tree to the requested depth.

#### Scenario: Hidden toolbars compete with an open document
- **WHEN** a dump exceeds the budget and omitting hidden toolbar children brings it within budget
- **THEN** the visible document retains its full requested depth
- **AND** hidden toolbars retain addressable paths and `truncated` markers
- **AND** the note explains that hidden children were omitted and can be retrieved using `root`

#### Scenario: Visible chrome still competes with the designer
- **WHEN** a whole-IDE dump with a designer open remains over budget after hidden children are omitted
- **THEN** the active designer's subtree retains its controls to the requested depth
- **AND** other visible branches lose depth before the designer
- **AND** the note identifies the preserved active document and explains how to retrieve omitted branches

### Requirement: Pointer drags through interact

`interact` SHALL accept `to x,y` or `to <path>:x,y` after a `click` value's press point, button and
modifier keys. Release coordinates SHALL be relative to the target, or to the control at the pasted
`dump_visual_tree` path. The press SHALL establish implicit capture, allow press work to run, then send
several moves with the button held before releasing to the pointer capturer or the hit-tested control.
Each held move SHALL be input of its own, with the dispatcher's layout run before the next, as a real
pointer's moves are. The reply SHALL name the press landing, release point and recipient, and whether
capture routed the release. A drag pressed in Dock's layout SHALL also say which tools or documents Dock
moved, and where, or that the layout is unchanged. Combining `double` with `to` SHALL be refused before
pressing.

#### Scenario: Draw a toolbox control

- **WHEN** a toolbox control is selected and `interact` clicks the designer canvas with `20,20 to 100,60`
- **THEN** held moves show the drawing phantom and release places the control using the drawn rectangle

#### Scenario: Release over a different control

- **WHEN** a drag names another control's path as its release destination
- **THEN** the destination coordinates are translated into window coordinates and release follows pointer capture

#### Scenario: Dock a tool window by dragging its title strip

- **WHEN** a drag presses a tool window's title strip and releases over the centre of another dock
- **THEN** Dock's drop target, added on the first held move, has been laid out by the release, and the tool joins that dock
- **AND** the reply says Dock moved it, from where to where

#### Scenario: A drag that docks nothing

- **WHEN** a drag in Dock's layout releases where no drop target is
- **THEN** the reply says the layout is unchanged, so nothing was docked
