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

### Requirement: Inspection SHALL give the text of every item a list holds
`inspect_element` on a list, an open dropdown or a tab control SHALL give `items`, the text of each item as `select`
matches it, including items whose rows are scrolled out of view, and SHALL leave the list where it was. It SHALL
leave out items that do not show, and list at most 200, with `itemsNote` saying what was left out. For a dropdown
that is closed it SHALL give no `items`, and `itemsNote` SHALL say to expand it; it SHALL NOT open it.

#### Scenario: Options' Language dropdown
- **WHEN** the Language dropdown, holding 31 items of which ten have rows, is expanded and inspected
- **THEN** `items` gives all 31 texts, and `select` takes any of them as written

### Requirement: The Immediate window's editor SHALL be found by match and read where it is shown
`dump_visual_tree`'s `match` SHALL find the Immediate window's editor by its class, `TextEditor`, and by the window's
name, under a path that is the same in every language. `inspect_element` on that editor SHALL report the window's text
as its `value`, with `isReadOnly`. An editor whose document is a project file SHALL leave its text to `get_file_content`.

#### Scenario: Reading a program's Debug.Print output
- **WHEN** the Immediate window holds a run's output and `dump_visual_tree` is called with `match: "TextEditor"`
- **THEN** the Immediate window's editor is among the matches, and `inspect_element` on it gives the output as `value`

#### Scenario: A code window's editor
- **WHEN** a code window's editor is inspected
- **THEN** its `value` is absent, and its file is read with `get_file_content`

### Requirement: Inspection SHALL report the mouse pointer a control shows
`inspect_element` SHALL give `pointer` for a control showing a pointer set on it or above it: a running program's
MousePointer by number and name, the pointer drawn, where it is set, and `setBy`, which says whether it is the
control's own value, one set over it as `Screen.MousePointer` sets it, or a style's. A pointer the runtime did not make
SHALL be reported as not recorded, never guessed, and a control with none set SHALL have no `pointer`.

#### Scenario: Screen.MousePointer over a control's own
- **WHEN** a running form's code sets `Screen.MousePointer = 11` over a button whose own MousePointer is 2
- **THEN** the button's `pointer` is 11, Hourglass, set by Override, and after `Screen.MousePointer = 0` it is 2, Cross, its own

### Requirement: Inspection SHALL report what a tree has selected
`inspect_element` on a tree SHALL report in `selectionItems` the rows the tree has selected, each named as its row
reads, and SHALL report an empty list only when nothing is selected. A tree whose peer offers no selection provider
SHALL be read from the tree itself.

#### Scenario: A selected watch row
- **WHEN** a Watches row is selected and the Watches tree is inspected
- **THEN** `selectionItems` names that row

#### Scenario: A cleared tree
- **WHEN** the row is removed from the selection and the tree is inspected again
- **THEN** `selectionItems` is empty

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

### Requirement: The embedded host SHALL leave process signals to the IDE
The embedded web host SHALL use a lifetime that installs no process signal handlers. On Unix, a Debug
IDE started with `--server-port` SHALL handle SIGTERM by dispatching the forced `shutdown_ide` cleanup
and main-window close to the UI thread. Its signal registration SHALL be disposed at desktop exit.

#### Scenario: A supervisor terminates an automation IDE
- **WHEN** the IDE receives SIGTERM on Unix
- **THEN** it stops the running project and closes remaining dialogs before closing the main window
- **AND** the web host does not independently stop and leave a live IDE without automation

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
- **WHEN** `interact` selects a property by text in the categorized Properties list or by its row path and its category is collapsed
- **AND** the category row and its toggle are showing
- **THEN** the refusal SHALL name the category as displayed and tell the caller to `toggle` the Button in that row
- **AND** the selection and category expansion remain unchanged

#### Scenario: A hidden control has showing copies
- **WHEN** `interact` refuses a provider or pointer action on a hidden control
- **AND** the same window contains showing controls of the same type with the same label or non-empty automation ID
- **THEN** the refusal SHALL give the showing copy's addressable path when there is one, or the total count and first three paths when there are several
- **AND** no action is performed; the caller chooses which path to target
- **AND** when no copy shows, the existing guidance to make the control show remains

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

### Requirement: A reply field SHALL explain itself where it appears
A reply field that needs explaining SHALL carry its explanation in the reply, beside the field, when the field
appears. A tool's description SHALL name the field and the vocabulary it uses, and SHALL keep to what a caller needs
before the first call. A tool description SHALL stay within 1900 characters, so that a merge adding both sides' text
cannot take it past the 2048 a client delivers (#638, #2511).

#### Scenario: A new reply field
- **WHEN** a tool's reply gains a field whose meaning is not plain from its name and value
- **THEN** the reply says what it means, and the description names the field without explaining it

#### Scenario: A description past the budget
- **WHEN** a change takes a tool or parameter description past 1900 characters
- **THEN** the build fails, naming the tool, unless the tool is still listed as not yet brought within it

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

### Requirement: One armed answer SHALL be able to name several files, for an Open that allows them
The server SHALL let a client arm one answer that names several files, in order, as a person selects them in an
Open dialog that allows several. Only such a dialog SHALL take all of them. A save dialog, or an Open that takes one
file, SHALL answer it as cancelled rather than with its first file, which no person could have chosen there, and
a reply SHALL say so: the reply of the save that took it, or else the next reply that arms or discards an answer.

#### Scenario: Adding two files at once
- **WHEN** a client arms two module paths as one answer and invokes Add File
- **THEN** both modules join the project

#### Scenario: Several files reach a save
- **WHEN** a client arms two paths as one answer and a document without a file is saved
- **THEN** nothing is written, and the save's reply fails, saying its picker answered as cancelled and why

### Requirement: open_file SHALL put the caret at a line and column in the editor it opens
Given a line, and a column if wanted, `open_file` SHALL put the caret there in the editor it opens, a code window's or
a carried file's, numbering both from 1 as the editor does, against the text `get_file_content` returns. A place off
the text SHALL be refused before anything opens. A fold over the caret SHALL be opened, and the note SHALL read the
caret's line, column and offset back from the editor.

#### Scenario: A carried file
- **WHEN** `open_file` opens a carried file at line 3, column 5
- **THEN** its editor's caret is there, and the note says so

#### Scenario: A place off the text
- **WHEN** `open_file` names a line past the file's last
- **THEN** nothing opens, and the reply gives the lines it has

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

### Requirement: Native window frames are observable

Window inspection SHALL report `nativeStyle` and `nativeExStyle` as unsigned 32-bit masks read from the
Windows HWND, or a `nativeStyleNote` explaining why they cannot be read. These SHALL remain distinct from
the Avalonia frame properties. Other controls SHALL have no frame section.

#### Scenario: Inspecting a running form's window

- **WHEN** a caller inspects the `Window` of a running form on Windows
- **THEN** `nativeStyle` and `nativeExStyle` equal the window's `GWL_STYLE` and `GWL_EXSTYLE`

### Requirement: A frame snapshot SHALL show the window whatever covers it

The snapshot tool SHALL accept optional `includeFrame`, default false. When true on Windows, it SHALL
capture the window as Windows renders it, frame included, even where another window covers it, draw its
open popups over it, and report client-origin offsets in image pixels. Where Windows renders nothing, it SHALL
copy the frame rectangle from the screen instead and say how much of it other windows cover. Unavailable
captures SHALL return an actionable error without a client-only fallback.

#### Scenario: Capturing a running form with its caption

- **WHEN** a caller requests `includeFrame: true` for a visible Windows form fully on screen
- **THEN** the PNG includes the frame and the reply reports the client origin within it
- **AND** the same offsets and scale map inspected bounds into the full and inline images

#### Scenario: Another window covers the form

- **WHEN** another application's window covers a running form and a caller requests `includeFrame: true`
- **THEN** the PNG shows the form and its frame, not the covering window

#### Scenario: The host cannot capture the frame

- **WHEN** the window has no Windows HWND, or is hidden or minimized
- **THEN** frame capture returns an error explaining the limitation and what to do next

### Requirement: The status bar SHALL show that the automation server is listening
While the automation server is listening, the main window's status bar SHALL show a section naming its port, followed
by the session when the IDE was started for one, as `MCP :5140 · docs-pass`, and SHALL NOT show it before the port is
bound. The section SHALL carry that text as its automation name, its strings SHALL be localised, and a build without the
server SHALL contain no such section.

#### Scenario: Telling sessions' IDEs apart
- **WHEN** an IDE is started with `--server-port 5140 --session docs-pass`
- **THEN** its status bar reads `MCP :5140 · docs-pass`, and one started without `--session` reads `MCP :5140`


### Requirement: The status bar's automation section SHALL say what is driving the IDE
Hovering over the section SHALL show the endpoint, whom it serves, the process ID and the build directory with the user's
profile replaced, that the server answers this machine only with no authentication, the running call if any, the last
call and its time, and the count of calls refused from other sessions with the session the last one named. A dot before
the text SHALL be filled with the theme's highlight while a call runs, and SHALL stay filled across a burst of calls.

#### Scenario: A call from another session
- **WHEN** a call from another session is refused
- **THEN** hovering over the section counts it and names the session it named, and it is not shown as the last call


### Requirement: A snapshot SHALL NOT present a minimized window's last frame as current
The default capture SHALL refuse a minimized window, as the frame capture does, naming the window and how to restore
it with `set_window_state`, and SHALL capture nothing in its place.

#### Scenario: A form minimized after its Resize changed its label
- **WHEN** a running form is minimized with `set_window_state` and `take_snapshot` is asked for it
- **THEN** it is refused, naming the form and the `set_window_state` call that restores it, and no image is written

### Requirement: Snapshots identify the title and capture area

The snapshot description and reply SHALL explain that the image captures the window's client area,
without its title bar or frame unless `includeFrame` asks for them, with open popups included unless the
reply's note says otherwise.
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
note directing the caller to `get_window_state`. On an X11 display whose root window has no
`_NET_SUPPORTING_WM_CHECK`, an unconfirmed Minimized or Maximized SHALL instead say that no window manager is
running, so nothing will apply it, and that Normal with a position and size still works. Geometry SHALL follow a
confirmed restore, and off-screen validation SHALL precede any mutation. Omitted coordinates SHALL retain the
restored position.

#### Scenario: A window manager applies changes asynchronously
- **WHEN** the platform confirms a state or geometry change after a dispatcher layout pass
- **THEN** the reply waits for confirmation within the bound and reports observed values

#### Scenario: No window manager applies the request
- **WHEN** the requested state remains unconfirmed at the deadline
- **THEN** the reply reports failure, the observed window, and how to re-read its state

#### Scenario: An X11 display with no window manager
- **WHEN** a Minimized or Maximized request goes unconfirmed on an X11 display with no window manager
- **THEN** the reply says no window manager is running, so nothing will apply it, and does not direct the caller to re-read

### Requirement: The window-state tools SHALL reach any window
`get_window_state` and `set_window_state` SHALL take an optional `window`, resolved as the tree tools resolve it, and
SHALL act on the IDE's own window when it is omitted. The reply SHALL name the window by what reaches it again, and an
unknown name's refusal SHALL say that the IDE's window is the default here.

#### Scenario: No window named
- **WHEN** `get_window_state` is called with no `window`
- **THEN** it reports the IDE's own window, named `ide`

### Requirement: The window-state reply SHALL say what its numbers measure
The window-state reply SHALL give `scale`, and the window's `outer` rectangle, its `frame` as drawn and its `client`
area, each on the screen in device-independent pixels, beside `x`, `y`, `width` and `height`, which it SHALL keep as
`set_window_state` takes them. Every reply SHALL carry `units`, which says the unit of each and which edge each
measures. A window that is hidden or minimized SHALL have no rectangles, and `outer` and `frame` SHALL be left out
where the platform reports no frame.

#### Scenario: A form moved to 720, 360 screen pixels at 150%
- **WHEN** `set_window_state` moves `window: "Form1"` to `x` 720, `y` 360 on a 150% display
- **THEN** the reply's `outer` starts at 480, 240 and its `frame` and `client` inside it, with `scale` 1.5

### Requirement: Another window SHALL change as a person changes it
`set_window_state` SHALL change a window other than the IDE's own as its minimize, maximize or restore box or its frame
would, so a running form's own events run, and SHALL wait for the program as `interact`'s does. It SHALL refuse what a
person couldn't do, with nothing changed: a state the window has no box for, a size for a fixed border, and any change
to a disabled window or one under a modal dialog.

#### Scenario: Minimizing a running form
- **WHEN** `set_window_state` minimizes `window: "Form1"` and then restores it
- **THEN** exactly one Resize runs for each, with the new state and settled client size, as for its caption buttons
- **AND** Paint follows restoring from minimized, with no Activate, Deactivate, GotFocus or LostFocus
- **AND** the reply names `Form1`

#### Scenario: Maximizing and restoring a running form
- **WHEN** `set_window_state` maximizes `window: "Form1"` and then restores it
- **THEN** each change raises exactly one Resize with the final state and client size
- **AND** Paint follows maximizing, but not restoring from maximized

#### Scenario: Resizing a running form
- **WHEN** a person drags a running form's border or corner with window contents shown while dragging
- **THEN** each move raises one Resize, followed by Paint when the client grows
- **AND** releasing the border raises neither event

#### Scenario: A Fixed Dialog
- **WHEN** `set_window_state` asks a Fixed Dialog form to minimize, or to take a new width
- **THEN** it is refused, naming the missing minimize box or the fixed border, and a move alone is applied

#### Scenario: A form under a MsgBox
- **WHEN** the program's MsgBox is open over its form, and `set_window_state` asks to minimize the form
- **THEN** it is refused, naming the MsgBox, and the form is unchanged

### Requirement: An open popup a control owns SHALL be in the dump and the snapshot
An open context menu or flyout SHALL appear under the control that owns it in `dump_visual_tree`, with its items
addressable, and SHALL be drawn in `take_snapshot`, however it is attached: a ContextMenu, a Button's Flyout, a
ContextFlyout, or a flyout attached with `FlyoutBase.SetAttachedFlyout`, to a Button as to any other control.

#### Scenario: A running Toolbar's dropdown
- **WHEN** a running form's Toolbar opens a dropdown's menu, a flyout attached to its button
- **THEN** the dump lists the menu's items under that button, and the snapshot shows the menu

### Requirement: invoke_menu_item SHALL reach what a click on the menu reaches
`invoke_menu_item` SHALL raise each submenu's SubmenuOpened along its path before reading that submenu's items, as
opening it does, so a submenu filled then holds its items. It SHALL match an item whose header is a control by its
automation name, or else its text, and SHALL list that name, never a type name, when a segment is missed.

#### Scenario: A submenu nothing has opened yet
- **WHEN** a path runs through a submenu that a SubmenuOpened handler fills, in a session where nothing has opened it
- **THEN** the handler runs, and the path reaches the item it added

#### Scenario: An item with a TextBlock header
- **WHEN** a path names an item whose header is a TextBlock and whose automation name is its text
- **THEN** the item is reached, and a miss in that menu lists it by that name

### Requirement: hover SHALL report a tip by the text it shows
`hover` SHALL report a tooltip by its text: a string as written, and a tip that is a control by its visible text
blocks in order. A tip with no text to read SHALL be reported as such, never by a type's name, and an open tip whose
text has not appeared yet SHALL be read again until it does or the dwell ends.

#### Scenario: A carried control's box in the designer
- **WHEN** `hover` reaches the designer's box for a carried control, whose tip is a StackPanel of three text blocks
- **THEN** the reply's tip reads as the control's name, its class and the sentence, joined with ` / `

### Requirement: hover SHALL report where an opened tip is
When a tip opens during `hover`, the reply SHALL give its bounds in the window, in the units of `inspect_element`'s
`boundingRect`, and SHALL say how far its top-left is from the point hovered, since the tip closes when the hover ends.

#### Scenario: A toolbar button's tip
- **WHEN** `hover` opens the Add Form button's tip
- **THEN** the reply's `tipBounds` places the tip in the window, and its detail says where that is from the point hovered

### Requirement: Hiding a tool window SHALL close that window alone
`set_tool_window_visible` with `visible: false` SHALL close the named tool window alone, as its tab's close button does,
leaving open any other tool window tabbed in the same dock. Its reply SHALL say what the window is now, and SHALL name
any other tool window that opened or closed with it.

#### Scenario: Two tools tabbed in one dock
- **WHEN** the Toolbox and the Properties window are tabs of one dock and a caller hides Properties
- **THEN** only Properties closes, the Toolbox stays open, and the reply is "Properties is now hidden."

### Requirement: press_key SHALL refuse a disabled control
`press_key` SHALL refuse a target that is not effectively enabled with the refusal `interact` gives a disabled target,
and SHALL NOT raise the key on it by any route. `inspect_element` SHALL
report `isKeyboardFocusable` false for such a control.

#### Scenario: The disabled region combo in Options
- **WHEN** `press_key` presses Down on Options → Language's region combo while it is disabled
- **THEN** the reply is a refusal saying the combo is disabled and nothing was done, and no key reaches it

### Requirement: hover SHALL say what took the tip when input it did not send moved the pointer
When no tip opens during `hover`'s watch and pointer input it did not send reached the window meanwhile, the reply SHALL
say so as the cause: the real mouse, or another hover running at the same time, with where the pointer went and the control
it is over. It SHALL say to move the real mouse off the window, or to hover one control at a time. It SHALL NOT offer either
cause when no such input came, and SHALL NOT count routed pointer events a tool raises on a control, which cannot move a tip.

#### Scenario: The real mouse resting over the window
- **WHEN** the real mouse moves the pointer onto another control during the watch, and no tip opens
- **THEN** the reply names the real mouse, where the pointer went and what it is over, and says to move the mouse off the window

#### Scenario: Two hovers at once
- **WHEN** another hover moves the pointer elsewhere during the watch, and no tip opens
- **THEN** the reply names the other hover, not the real mouse, and says to hover one control at a time

### Requirement: Snapshot coordinates include popup overhang

By default, a snapshot SHALL include the client area and its open native popups at their relative screen positions,
including popups extending left of or above the window. The reply SHALL report the window origin in
full-image pixels as `windowOffsetX` and `windowOffsetY`, zero when no left or top overhang exists.
Tool descriptions SHALL explain how to map inspected window-relative bounds into both returned images.

#### Scenario: A menu extends left of the window

- **WHEN** an open native popup extends left of the captured window
- **THEN** the window is shifted right in the image to leave room for the overhang
- **AND** the reply reports that shift as `windowOffsetX`
- **AND** a window-relative x coordinate maps to `x * scale + windowOffsetX` in the full image
- **AND** in the inline copy it maps to `(x + windowOffsetX / scale) * inlineScale`

### Requirement: Automation SHALL be able to set the lock-key state a running program reads
In a DEBUG build, `set_lock_key` SHALL set a lock key on or off as running programs read it, without changing the
machine's keyboard, or return it to the keyboard's toggle. A StatusBar's key panels SHALL prefer a set state and
repaint when it changes. Every reply SHALL list all four keys with their state and its source, and a refused call SHALL
change nothing.

#### Scenario: Checking a Caps panel's on state
- **WHEN** a running form's StatusBar has a Caps panel and `set_lock_key` sets CapsLock On
- **THEN** the panel draws its text black, and the reply lists CapsLock as on with source Set

#### Scenario: Returning a key to the keyboard
- **WHEN** `set_lock_key` sets CapsLock to Keyboard
- **THEN** the panel follows the keyboard's own toggle again, and the reply gives that key's source as Keyboard on Windows

### Requirement: Snapshots show what the screen shows at any scaling

A snapshot SHALL draw the window and each popup as the compositor draws them on screen, so that text a control draws
after popping a clip appears at every display scaling. A window in no frame yet SHALL be waited for. If the compositor
gives no picture in time, the snapshot SHALL still be taken another way, and its `note` SHALL say so and that such text
may be missing.

#### Scenario: Clipped text at 150%

- **WHEN** a control draws text in a clip, pops it, and draws more text in another, and the display is at 150%
- **THEN** both texts are in the snapshot, where the screen draws them

#### Scenario: No picture from the compositor

- **WHEN** the compositor gives no picture of the window in time
- **THEN** the snapshot is still taken, and its `note` says it was drawn another way and may lack such text

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

### Requirement: An array or object that arrives as text SHALL be refused naming a stale schema
When a string argument holds, as JSON, the array or object its parameter declares, the server SHALL refuse it, and
the refusal SHALL say that a client holding an older schema of the tool sends that, that retrying in the same session
sends it again, and that a new session loads the current schema. It SHALL NOT point the caller at the schema. A long
string in any refusal SHALL be shown by its length and its start.

#### Scenario: A parameter added after the session loaded the tool
- **WHEN** a session that loaded `answer_next_file_dialog` before it took `paths` sends `paths` as an array
- **THEN** the refusal names the stale schema and says a new session is needed, and nothing is armed

### Requirement: Integral argument overflow explains the storage limit
The server SHALL refuse an integral argument outside its parameter's storage range with the argument
name, supplied value, and whether it is larger or smaller than any whole number the argument can hold,
including the exceeded storage limit. It SHALL NOT present the storage range as the parameter's accepted range.
It SHALL include the parameter's declared guidance where available, and SHALL NOT expose serializer
locations or .NET type names for that overflow. Nothing in the tool SHALL run for the refused call.

#### Scenario: Hover dwell exceeds the integer range
- **WHEN** hover receives dwellMs 3000000000
- **THEN** the refusal names dwellMs and says it is larger than any whole number the argument can hold (2147483647)
- **AND** includes the declared 0 to 10000 ms dwell guidance

#### Scenario: A long integer is below its minimum
- **WHEN** an argument for a long integer parameter is -9223372036854775809
- **THEN** the refusal says it is smaller than any whole number the argument can hold (-9223372036854775808)

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

### Requirement: Run-control replies SHALL say what the program printed
A successful reply from `run_project`, `stop_project`, `continue_program`, `break_program`, a step or `run_to_cursor`
SHALL give in `printed` what the program wrote with `Debug.Print` since its run began or a reply last reported it, and
its note SHALL say how much, or that it wrote nothing. Output past 4000 characters SHALL keep its end, with the note
saying how much was left out.

#### Scenario: A program that prints as it loads
- **WHEN** `run_project` starts a program whose `Form_Load` prints two lines
- **THEN** `printed` holds the two lines, and the note says Debug.Print wrote 2 lines

#### Scenario: Nothing printed
- **WHEN** a program prints nothing before `stop_project`
- **THEN** the reply has no `printed`, and its note says Debug.Print wrote nothing

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
capture routed the release. A drag that moves a tool or document SHALL also say what Dock moved, and
where. A drag pressed on one of Dock's drag areas, a tab or a tool window's title strip, that moves nothing
SHALL say the layout is unchanged; any other drag, such as one in the form designer, SHALL say nothing about
Dock, since it could not have docked anything. Combining `double` with `to` SHALL be refused before
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

- **WHEN** a drag pressed on a tool window's title strip releases where no drop target is
- **THEN** the reply says the layout is unchanged, so nothing was docked

### Requirement: Disabled interaction targets explain their refusal
The server SHALL refuse `interact` provider and pointer actions on an effectively disabled target before
acting, naming the control, `"isEnabled": false`, and the refused action, and stating that nothing was
done. The refusal SHALL name the disabling container when enablement is inherited. When a modal dialog
blocks the window, it SHALL name the dialog and direct the caller to find and answer it first. Reflection
actions `invoke_command` and `set_property` SHALL continue to act on the view model.

#### Scenario: Invoking a disabled toolbar button
- **WHEN** `interact` invokes a showing button disabled directly or through its container
- **THEN** the reply explains that it is disabled and nothing was done, without reporting an exception

#### Scenario: A disabled Frame contains an enabled button
- **WHEN** a caller acts on a button whose Frame disables it
- **THEN** the refusal names the Frame and the action, and nothing is done

#### Scenario: A reset prompt disables the running form
- **WHEN** a caller acts on a control while a modal reset prompt disables its window
- **THEN** the refusal names the blocking dialog and directs the caller to answer it
- **AND** nothing is done

### Requirement: A click SHALL move the pointer there before it presses
`interact`'s `click` SHALL raise a pointer move at the point on the element it presses before the press, as a person's
pointer arrives first, so a running form's MouseMove comes before its MouseDown. A click where the last click left the
pointer, in the same window, SHALL move nothing first, as a still pointer sends no move. The reply SHALL say which.

#### Scenario: A click on a running form's button
- **WHEN** `click` presses Command1, which logs its mouse events
- **THEN** they run MouseMove, MouseDown, Click, MouseUp, as a real pointer's click on it runs them

#### Scenario: A second click at the same point
- **WHEN** `click` presses again where the last click left the pointer
- **THEN** no move comes before the press, and the reply says the pointer was already there

### Requirement: A double click's second press SHALL land where the first did
The second press of `click` with `double` SHALL go to whatever takes the pointer at the first press's point when it is
sent, with a click count of 2, even when the first click replaced what it landed on or the addressed control. It SHALL be
refused only when the window is hidden or closed, or nothing takes the pointer there. When the second press reached a
different element from the first, the reply SHALL say so and name it.

#### Scenario: A control that rebuilds itself on a click
- **WHEN** `click` with `double` targets a Toolbar that replaces its buttons on each release
- **THEN** the second press reaches the new button at that point, with a click count of 2
- **AND** the reply says the first click replaced what it landed on, and names where the second press went

#### Scenario: A drag that could not have docked

- **WHEN** a drag in Dock's layout is pressed off Dock's drag areas, as a form designer drag is
- **THEN** the reply says nothing about Dock

### Requirement: A drag's button can be held between calls

A `click` drag with `hold` SHALL stop after its held moves with the button still down at the `to` point. While it is
held, every tool's reply SHALL carry a `heldButton` note saying where and what has the capture, and `click`,
`double_click` and `hover` SHALL be refused without pressing or moving anything. `release` SHALL let it go, after held
moves to any point it is given, with an unheld drag's reply, and SHALL be refused when nothing is held.

#### Scenario: Hold a draw to see its preview

- **WHEN** a toolbox control is selected and `interact` clicks the designer canvas with `20,20 to 100,60 hold`
- **THEN** the drawing phantom shows at the drawn rectangle, nothing is placed, and the reply says the button is still held
- **AND** `release` then places the control an unheld drag to the same point would

#### Scenario: Release at a further point

- **WHEN** `release` is given `140,90` while a draw is held at `100,60`
- **THEN** held moves go to `140,90` before the release, and the control is drawn to it

#### Scenario: A second press while the button is held

- **WHEN** `click` or `double_click` targets any control, or `hover` is called, while a button is held
- **THEN** the call is refused, nothing is pressed or moved, and the refusal says to `release` first

### Requirement: An idle or orphaned hold ends without a stray drop

After 120 seconds with no tool call, a held button SHALL be abandoned, not released where it is held: Escape SHALL go
to the focused control in its window, then held moves SHALL return to the press point and the button SHALL be released
there. The next reply's `heldButton` SHALL say so, and, for a press in the form designer, whether the designer recorded
an undo step anyway. A hold whose window closes SHALL be dropped without a release, and the next reply SHALL say why.

#### Scenario: An idle hold is abandoned

- **WHEN** no tool is called for 120 seconds while a button is held
- **THEN** Escape is sent, the pointer moves back to the press, and the button is released there
- **AND** a Dock drag docks nothing and a designer move ends where it began, while a resize snaps to the grid and a draw
  still places a control, and the next reply's `heldButton` names any undo step the designer recorded

#### Scenario: The held window closes

- **WHEN** the window a button is held in closes
- **THEN** the hold is dropped without a release, and the next reply's `heldButton` says the window closed

### Requirement: Properties category expansion

Categorized Properties rows SHALL expose an expand/collapse provider driven by the category's open state. Their toggle SHALL use the localized category header as its accessible name. `inspect_element` SHALL report `expandCollapseState` from an available expand/collapse provider.

#### Scenario: Opening and closing a category row
- **WHEN** `interact` expands or collapses a showing category row
- **THEN** its open state and property visibility follow the requested state
- **AND** repeating the action succeeds without toggling the state
- **AND** inspection reports the current expansion state, including after using the category toggle

### Requirement: A key pressed with modifiers SHALL press each modifier's own key around it
`press_key` with modifiers, and `type_text` in keyboard mode typing a capital, SHALL send each modifier's own key
down before the key and up after it, in reverse, as a person's chord does. Each event SHALL carry the modifiers held
at that moment, so a modifier key's own down reports itself held and its up does not. The reply SHALL name them.

#### Scenario: Shift+K on a running TextBox
- **WHEN** `press_key` presses K with Shift on a TextBox that logs its key events
- **THEN** they run KeyDown 16 with mask 1, KeyDown 75 with mask 1, KeyUp 75 with mask 1, KeyUp 16 with mask 0

#### Scenario: A capital typed as keys
- **WHEN** `type_text` types K in keyboard mode
- **THEN** Shift's own KeyDown comes before K's and its KeyUp after K's, and the reply says so

### Requirement: press_key SHALL type the character a key types
`press_key` SHALL send the character a key types, as text of its own between the key's KeyDown and KeyUp, as a
platform sends a person's press. It SHALL send none with Ctrl, Alt or Meta held, and none for a control character,
which no platform sends as text. In a code window the key SHALL be judged as typing is. The reply SHALL say what was typed.

#### Scenario: Shift+K on a running TextBox
- **WHEN** `press_key` presses K with Shift on a TextBox that logs its key events
- **THEN** they run KeyDown 16, KeyDown 75, KeyPress 75, KeyUp 75, KeyUp 16, and the TextBox holds K

#### Scenario: A command chord
- **WHEN** `press_key` presses A with Ctrl
- **THEN** no text is sent, and a running TextBox's KeyPress is the 1 its runtime makes from the KeyDown

### Requirement: type_text SHALL send keys to a control that takes typing itself
In keyboard mode, `type_text` SHALL send its keys to the addressed control when nothing under it is a text box and it
takes focus, as a person's keyboard sends them, and SHALL judge it by what it did with them, not by its class. When it
handled the text of none of the keys that type a character, the reply SHALL say so, and that their KeyDown and KeyUp
were raised. A control that does not take focus SHALL be refused, saying that a person's keys could not reach it.

#### Scenario: A running form's RichTextBox
- **WHEN** `type_text` types `abc` in keyboard mode into a running RichTextBox, whose KeyPress and Change log
- **THEN** each key runs KeyPress and the edit runs Change, and the RichTextBox holds `abc`

#### Scenario: Document mode
- **WHEN** `type_text` in its default mode addresses a control that takes typing itself
- **THEN** it is refused as having no text surface, and the refusal names keyboard mode

### Requirement: press_key reads a chord written as one key
`press_key` SHALL read a `key` written as a chord, such as `Ctrl+H`, as its last part, with the parts before it
as modifiers added to any `modifiers` given, and its reply SHALL say how it read the chord. A chord whose leading
parts are not all modifiers SHALL be refused before anything is pressed, naming the part that is not one.

#### Scenario: A chord written as one key

- **WHEN** `press_key` is called with `key` `Ctrl+H` and no `modifiers`
- **THEN** it presses H with Control
- **AND** the reply says `'Ctrl+H' was read as key 'H' with modifiers 'Ctrl'`

#### Scenario: A chord with a part that is not a modifier

- **WHEN** `press_key` is called with `key` `A+B`
- **THEN** nothing is pressed, and the refusal says `A` is not a modifier

### Requirement: Adorners are addressed under the element they adorn
The control view SHALL list an adorner under the element it adorns, after that element's own children, and not where
its adorner layer sits, so its paths read as the screen does and round-trip through `Resolve` and `PathWithin`. An
adorner SHALL be folded away as any wrapper is unless it, or something in it, is meaningful. The designer's draw
preview, its rubber band, a selected control's resize adorner and the adorner's eight grab handles SHALL carry
AutomationIds, so the dump lists them and `inspect_element` reads their bounds.

#### Scenario: A selected control's grab handles

- **WHEN** a control is selected in the form designer
- **THEN** `…/ListItem[Command1]/None[ResizeAdorner]` and its handles, such as `None[PART_BottomRight]`, are in the dump
- **AND** a `click` drag on a handle resizes the control

#### Scenario: The draw preview

- **WHEN** a toolbox tool is selected
- **THEN** `NewControlSpawner` and its `NewControlSpawnerPhantom` are in the dump, the preview marked hidden until a draw shows it

#### Scenario: An adorner with nothing meaningful in it

- **WHEN** an element's adorner has no AutomationId and holds nothing meaningful
- **THEN** the dump adds nothing for it

### Requirement: Control identity in interaction replies

The shared control description used by interaction replies SHALL append the control's Avalonia name
in brackets to its concrete type, falling back to its automation id when the name is empty, and to
the bare type when neither identifier exists. This includes click press/release descriptions and
disabled-window refusals.

#### Scenario: Running form control identified in click and refusal

- **WHEN** a caller clicks a running form's `Button[cmdMark]`, whose concrete type is `VBCommandButton`
- **THEN** the click reply describes it as `VBCommandButton[cmdMark]`
- **AND** a click refused because a modal dialog disables its window uses the same description

#### Scenario: Toolbar button identified without an Avalonia name

- **WHEN** a caller clicks an unnamed button whose automation id is `Standard.OpenProject`
- **THEN** the reply describes it as `Button[Standard.OpenProject]`
