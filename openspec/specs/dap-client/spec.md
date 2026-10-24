# dap-client Specification

## Purpose
Communicate with an external debug adapter over streams and own explicitly launched adapter processes without
depending on interpreter execution machinery or the language-server protocol. This capability provides the
library boundary, asynchronous launch/inspection sessions, and explicit JSON-configured IDE launches with
run-control routing and remote Locals/Call Stack presentation.
Optional shared capture makes wire traffic and process diagnostics available to inspector and export consumers.

## Requirements

### Requirement: DAP stream framing and envelopes

The client SHALL send and receive UTF-8 DAP messages with Content-Length byte-stream framing. It SHALL
reject malformed envelopes and invalid, duplicate, missing or oversized Content-Length headers, and SHALL
bound the header size. It SHALL retain unknown response and event fields.

#### Scenario: A message arrives in fragments

- **WHEN** a valid header or multibyte JSON body arrives in multiple reads
- **THEN** the client assembles exactly one complete message before interpreting it
- **AND** bytes belonging to a following message remain available to that message

#### Scenario: A new capability appears

- **WHEN** an adapter's initialize response includes an unfamiliar capability object
- **THEN** the response preserves that object for the caller

### Requirement: Independent requests and ordered events

The client SHALL assign increasing sequences to outgoing messages, serialize complete writes, and match
responses to requests using request_seq. Events SHALL be available in received order. An unsuccessful
response SHALL fail its request with the complete response available, without closing an otherwise healthy
connection. Unsupported adapter-initiated requests SHALL receive an unsuccessful DAP response.

#### Scenario: Responses arrive out of order

- **WHEN** an adapter answers a later request before an earlier request
- **THEN** each response completes only the request its request_seq identifies

#### Scenario: An adapter refuses launch

- **WHEN** a launch response reports success false and includes a structured error
- **THEN** the launch caller receives that error and the connection can carry a subsequent request

### Requirement: Bounded connection lifetime and resource use

Each request SHALL have a finite configurable deadline and accept caller cancellation. The client SHALL
bound pending requests and queued events. It SHALL NOT retry timed-out or cancelled requests automatically.
EOF, malformed input, event overflow and failed writes SHALL close the connection and fail pending work.
Disposal SHALL settle pending requests and release owned streams; repeated disposal SHALL be safe.

#### Scenario: A cancelled request receives a late reply

- **WHEN** a caller cancels its wait after a complete request was written
- **THEN** the late reply cannot complete another request
- **AND** subsequent requests can use the connection

#### Scenario: Cancellation interrupts a frame write

- **WHEN** cancellation interrupts writing a request frame
- **THEN** the connection is closed rather than reused with a potentially incomplete frame

#### Scenario: The adapter disappears

- **WHEN** the adapter closes its output with requests pending
- **THEN** every pending request completes with a connection failure
- **AND** the connection's completion reports the closure cause

### Requirement: Explicit adapter process ownership

The client library SHALL support launching one explicitly configured adapter executable directly, preserving
individual argument boundaries, an optional working directory and environment overrides. It SHALL own the
redirected stdio connection and SHALL NOT restart the process or negotiate a debug session automatically.
Invalid configuration and launch failures SHALL be reported to the caller.

#### Scenario: Arguments contain spaces and shell characters

- **WHEN** a caller supplies individual arguments containing spaces, quotes or shell metacharacters
- **THEN** the adapter receives each argument literally without shell expansion

#### Scenario: A command cannot start

- **WHEN** the configured executable does not exist
- **THEN** startup reports the failure without returning a live adapter owner

### Requirement: Bounded adapter diagnostics

The process owner SHALL continuously drain standard error independently of protocol output. It SHALL retain
only a configurable tail and report the number of discarded characters. Its final result SHALL expose the
exit code, connection failure and cleanup failure separately.

#### Scenario: An adapter floods stderr without newlines

- **WHEN** standard error exceeds the retained diagnostic budget without a newline
- **THEN** the pipe continues to drain, the retained tail stays bounded and discarded characters are counted
- **AND** the adapter can continue writing protocol messages to standard output

#### Scenario: The adapter exits unsuccessfully

- **WHEN** the process exits with a nonzero code and diagnostic text
- **THEN** completion retains both the exit code and the diagnostic tail

### Requirement: Adapter process cleanup

Process exit, connection failure and local disposal SHALL trigger cleanup. Local disposal SHALL settle
pending requests and close the connection. After a bounded grace period, the owner SHALL attempt to terminate
the still-running adapter's process tree and wait for its exit. Each asynchronous cleanup wait SHALL be
bounded, and failures SHALL be reported. Repeated or concurrent disposal SHALL join the same cleanup.

#### Scenario: The adapter ignores closed input

- **WHEN** local disposal closes the connection but the adapter remains running
- **THEN** cleanup forcibly terminates its process tree after the grace period
- **AND** completion reports forced termination and the observed exit status

#### Scenario: A process exits immediately after replying

- **WHEN** the adapter writes a complete response and exits
- **THEN** the client allows buffered protocol input to drain before closing its read stream
- **AND** the waiting request can receive that response

#### Scenario: Invalid protocol output leaves a process alive

- **WHEN** malformed output closes the DAP connection but the adapter remains running
- **THEN** process cleanup runs without requiring another disposal request

### Requirement: Optional shared protocol capture

The DAP library SHALL accept optional per-session capture into the shared conversation log. It SHALL record
requests, successful and unsuccessful responses, events, reverse requests, connection lifecycle and owned
process diagnostics. Capture SHALL NOT wait for a consumer or propagate diagnostic failures into protocol
operation. Existing capture retention limits, arming and drop accounting SHALL apply.

#### Scenario: Request ids overlap across directions

- **WHEN** the client and adapter both send a request with the same sequence number
- **THEN** each response is paired with the request from the opposite direction using request_seq
- **AND** unsuccessful responses mark only the corresponding exchange as failed

#### Scenario: Capture cannot keep up

- **WHEN** the capture queue is full
- **THEN** protocol traffic continues without waiting for the capture consumer
- **AND** the log reports lost capture entries

### Requirement: Honest captured DAP evidence

Retained DAP bodies SHALL preserve the original JSON bytes independently of parsed fields. Outbound records
SHALL identify write attempts rather than claim delivery. Failed writes SHALL produce connection diagnostics.
Local cancellation and closure SHALL end unanswered captured requests, subject to reported capture loss.
Late responses SHALL NOT replace a locally completed request's outcome.

#### Scenario: An adapter writes invalid JSON

- **WHEN** a fully framed body is not valid JSON
- **THEN** capture retains the invalid body according to its retention policy
- **AND** connection closure records the protocol failure

#### Scenario: A caller cancels before the reply

- **WHEN** a sent request is locally cancelled and the adapter later replies
- **THEN** the capture retains the cancelled request outcome and records the late reply separately

### Requirement: DAP path pseudonymisation at export

Pseudonymised exports SHALL rewrite complete JSON path string fields using the existing local-path policy.
Local inspection SHALL retain the original body, and opting out of pseudonymisation SHALL retain the path
in exported bodies. Document URI paths SHALL use the URI policy without a second transformation.

#### Scenario: A source has a filesystem path

- **WHEN** a captured DAP message contains a plain source path
- **THEN** the pseudonymised export replaces its path segments
- **AND** the original raw message remains available for local inspection

### Requirement: Asynchronous launch configuration
The client SHALL provide a single-launch session that initializes an external adapter, waits for initialized,
configures source breakpoints and advertised default exception filters, and sends configurationDone only
when supported. It SHALL permit launch responses to follow configuration and release ownership on startup
failure or cancellation within a bounded startup deadline.

#### Scenario: Deferred launch response
- **WHEN** the adapter sends initialized but defers its launch response until configurationDone
- **THEN** the client configures breakpoints and completes the handshake without waiting for launch first
- **AND** breakpoint verification responses remain available to the caller

### Requirement: Stop-scoped asynchronous inspection
The session SHALL preserve remote thread/frame IDs, source information and variable references and SHALL
reject inspection results belonging to a different session or expired stop. Consumers SHALL be able to
check a stop identity again when applying a result after UI dispatch.

#### Scenario: Delayed locals response
- **WHEN** a variable response arrives after resume and a new stop, even with reused remote IDs
- **THEN** the old inspection fails instead of being offered as current locals

### Requirement: Race-safe execution state
The session SHALL invalidate stopped inspections before sending continue or a step command and SHALL NOT
overwrite a newer stopped event when that request completes. A definitive refusal SHALL establish a fresh
stop; an uncertain resume cancellation or timeout SHALL fault and release the session.

#### Scenario: Immediate second breakpoint
- **WHEN** a stopped event arrives before a successful continue response
- **THEN** the session remains at that new stop after the response

### Requirement: Observable session lifetime
The session SHALL expose bounded ordered updates and raw events, including output, and SHALL fault and close
on update overflow. Program termination SHALL remain distinct from adapter process completion. Disconnect
SHALL request debuggee termination and release ownership even on failure, with process diagnostics retained.

#### Scenario: Adapter remains alive after program termination
- **WHEN** the program terminates but the adapter still awaits disconnect
- **THEN** the session reports the program ended and can explicitly disconnect and await owned cleanup

### Requirement: Explicit IDE debug launch
The IDE SHALL offer an explicit JSON-file launch command and SHALL NOT execute launch files merely because
a project opens. Executable, working-directory and source paths SHALL resolve from the selected file's
directory; command arguments and adapter launch fields SHALL pass unchanged without shell interpretation.

#### Scenario: Launch an existing workspace
- **WHEN** the user selects a valid launch file from the Run menu
- **THEN** the IDE owns one external session and prevents concurrent interpreter runs
- **AND** F5, Break, End and stepping route to that session according to its state

### Requirement: Remote debugger presentation
The IDE SHALL populate Call Stack and Locals asynchronously from the current stop, reject stale inspection
results, and fetch aggregate children without blocking the UI. It SHALL expose output and failures and SHALL
refuse unsupported remote operations rather than applying them to the interpreter.

#### Scenario: Resume during inspection
- **WHEN** the user continues while a locals request is pending
- **THEN** the old locals are cleared and the late response does not repaint them

### Requirement: External session visibility and cleanup
The IDE SHALL expose its current or most recent external debug connection in the shared servers view and
capture its traffic using the shared conversation log. Program completion, End and IDE closure SHALL release
the owned adapter and return run controls to their idle route.

#### Scenario: End while starting
- **WHEN** the user ends a session while its adapter is initializing
- **THEN** startup is cancelled and the adapter is cleaned up without starting an interpreter run

### Requirement: Selected external frame inspection
The IDE SHALL select the top frame at every new external stop and SHALL allow selection of another frame
from that stop in Call Stack. Locals SHALL clear immediately and asynchronously display the selected frame's
non-expensive scopes. Late results from an earlier selection or expired stop SHALL NOT replace current
locals. Unrelated session notifications SHALL preserve selection.

#### Scenario: Switch frames before locals arrive
- **WHEN** a caller's variables request is pending and the user selects the top frame again
- **THEN** only the top frame's variables may populate Locals, even if the caller's response arrives last

#### Scenario: Select a caller without moving execution
- **WHEN** the user selects a caller in the external Call Stack
- **THEN** its loaded source is revealed and its locals are inspected
- **AND** the execution highlight and Show Next Statement still identify the actual top frame

#### Scenario: Resume and stop again
- **WHEN** a selected caller's inspection overlaps a resume and a new stop
- **THEN** the old selection and inspection expire and the new stop selects its top frame

### Requirement: External Immediate evaluation
While an external session owns the run, the IDE SHALL evaluate Immediate lines through that adapter,
using the selected stopped frame and DAP REPL context. It SHALL preserve adapter-specific syntax,
display successful result text and adapter errors, and never fall back to the interpreter.
The IDE SHALL permit only one pending Immediate evaluation and SHALL refresh selected-frame locals
after a completed evaluation while the original stop remains current. It SHALL NOT automatically retry
evaluation, which can have side effects.

#### Scenario: Evaluate in the caller
- **WHEN** a stopped external caller is selected and the user submits an Immediate line
- **THEN** the adapter receives the line with that caller's frame ID and REPL context
- **AND** the response is displayed and locals are refreshed without moving execution

#### Scenario: Inspection changes during evaluation
- **WHEN** the selection, stop or external session changes before an evaluation completes
- **THEN** the old response is replaced by an expired-context diagnostic
- **AND** adapter output events remain visible independently

#### Scenario: Output arrives during evaluation
- **WHEN** the adapter emits output while an Immediate request is pending
- **THEN** output and the eventual response are appended without using a stale editor line

#### Scenario: Adapter refuses a line
- **WHEN** the adapter rejects a current-frame evaluation
- **THEN** Immediate displays its error and permits another submission

### Requirement: Session-only external breakpoint editing
The IDE SHALL initialize external source breakpoints from launch JSON and SHALL route gutter/F9 and Clear All
to the active external session once launch configuration is complete. Changes SHALL NOT modify the launch file
or saved interpreter marks. Per-source replacements SHALL include the complete desired set, including an empty
set for removal, and SHALL be allowed while running or stopped. Late replies SHALL NOT replace newer intent
or affect another session.

#### Scenario: Rapid edits to one source
- **WHEN** the user changes a source's breakpoints while its replacement request is pending
- **THEN** the newest complete set is sent after that request and only its reply supplies final verification

#### Scenario: Verification and relocation
- **WHEN** an adapter verifies, relocates or rejects a requested breakpoint
- **THEN** the gutter distinguishes that state at the requested line and exposes the requested line, adapter line,
  verification and adapter message on hover

#### Scenario: Clear refused
- **WHEN** the adapter refuses a removal or its outcome is uncertain
- **THEN** the affected markers remain visible as unverified and the failure is reported
- **AND** the IDE does not claim the backend has cleared them

#### Scenario: Adapter changes verification
- **WHEN** a breakpoint event names the ID of an owned non-pending breakpoint
- **THEN** its verification and resolved line update without changing the requested line

#### Scenario: Another launch
- **WHEN** an external session ends and another is launched
- **THEN** session edits are discarded and the launch file seeds the new set

### Requirement: External expression watches
The IDE SHALL keep a separate external expression-watch list for the IDE session and SHALL evaluate it
in the selected stopped external frame using DAP context `watch`. It SHALL preserve result text, type and
expandable variable references, display refusals per expression, and discard replies from obsolete stops,
frames or watch definitions. Resume and End SHALL clear remote values. Stop, frame selection, watch edits
and Immediate completion SHALL refresh values. External watch definitions SHALL NOT alter interpreter watches.

#### Scenario: Select another frame
- **WHEN** the user selects another external call-stack frame
- **THEN** old values clear and expressions are evaluated in that frame, including frame ID zero

#### Scenario: Adapter rejects an expression
- **WHEN** an adapter refuses one watch expression
- **THEN** its row displays the error while other expressions remain evaluable

#### Scenario: Expand a watch
- **WHEN** a watch result has a nonzero variables reference and the user expands it
- **THEN** children are fetched asynchronously for the same stop

#### Scenario: Break-condition types
- **WHEN** an external watch is added or edited
- **THEN** only expression watches are offered; interpreter break-condition watches remain unchanged

### Requirement: External hover data tips
While an external session is active, the editor SHALL evaluate hovered identifiers only against the selected stopped external frame, using evaluate context hover, and only when the adapter advertises supportsEvaluateForHovers as true. It SHALL NOT evaluate these tips through the interpreter. An unsuccessful evaluation SHALL display no value tip. External data tips SHALL take precedence only while a stop and selected frame exist; otherwise the editor SHALL retain language-server quick info.

#### Scenario: Running external session
- **WHEN** the external program is running without a stop
- **THEN** hovering an identifier requests language-server quick info

#### Scenario: Selected caller value
- **WHEN** the user selects a caller frame and dwells over an identifier with a hover-capable adapter
- **THEN** the editor displays the adapter result for that identifier in the selected caller frame

#### Scenario: Unsupported or unresolved hover
- **WHEN** the adapter does not advertise hover support or refuses the identifier
- **THEN** the editor displays no external value tip

### Requirement: External hover inspection lifetime
The editor MUST discard pending and displayed external tips when the pointer moves away, the editor detaches, or the external inspection changes, including frame selection, Immediate evaluation, stepping, resume, End and relaunch.

#### Scenario: Late reply after frame selection
- **WHEN** a hover reply arrives after the selected frame changes
- **THEN** the old result is not displayed

#### Scenario: Visible tip after resume
- **WHEN** execution resumes with a data tip visible
- **THEN** the tip closes and its attached text is cleared
