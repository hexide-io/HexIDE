## ADDED Requirements

### Requirement: Operational diagnostics are local by default
The desktop IDE SHALL offer Off and Local, defaulting to Local in a fresh profile. Local SHALL use
bounded session memory without automatic persistence, telemetry networking or listener ports. The standard
distribution SHALL NOT bundle or acquire an exporter. Off SHALL stop recording, revoke operational/log grants and
clear retained observations. These controls SHALL NOT claim to disable existing logs, protocol captures,
external processes or independently acting add-ins.

#### Scenario: A fresh profile runs offline
- **WHEN** the desktop IDE starts with a fresh profile and opens and saves a project
- **THEN** its operational record is available locally without telemetry network activity or automatic persistence
- **AND** no collector, exporter add-in or additional listener port is started or downloaded

#### Scenario: Recording is switched off
- **WHEN** Off is committed
- **THEN** new operational observations are not retained or delivered to subscribers and the prior record is discarded
- **AND** supported application-log export is revoked without changing existing local logging
- **AND** returning to Local does not restore revoked subscriptions without fresh authorization

### Requirement: Observations use a closed operational data schema
Records SHALL use a versioned field/value allowlist covering operation, duration, outcome, stable reason,
bounded counts and build/platform metadata. Unknown methods, error text and external names SHALL map to
fixed categories or be omitted. Correlation identifiers SHALL be random and session-scoped, never derived
from users, machines or projects, and SHALL NOT be metric dimensions. Subscription values SHALL contain
no references to internal mutable models or source-bearing objects.

#### Scenario: A failure message contains project material
- **WHEN** a save fails with an exception containing a project path and a source-code fragment
- **THEN** only the registered failure category and safe metadata are recorded
- **AND** neither the original text nor a hash of it appears in the record, snapshot or subscription

#### Scenario: An external server invents names and methods
- **WHEN** a server or custom protocol method has a user-authored name
- **THEN** metadata uses registered categories such as external and other rather than that name
- **AND** metric dimensions do not grow with the number of distinct user-authored values

### Requirement: Detailed captures and application logs remain separate
Operational diagnostics SHALL NOT automatically read, attach, forward or arm protocol bodies, handshake
captures, server output, existing application logs or diagnostic files. An operational subscription SHALL
NOT expand what another feature records. Sharing a detailed capture SHALL retain that feature's own
disclosure workflow. Existing compiler/editor diagnostics containing paths and messages SHALL NOT be
exposed through the operational subscription.

#### Scenario: A protocol connection is armed
- **WHEN** source-bearing protocol capture is armed while an exporter is subscribed
- **THEN** no captured body, handshake or server output enters the operational stream

### Requirement: Supported operations expose meaningful timings and outcomes
The IDE SHALL observe project opening, saving, client language-server requests and connection lifecycle.
It SHALL distinguish success, partial completion, refusal, cancellation, timeout and failure as applicable.
Refusals SHALL carry stable reasons. Work duration SHALL exclude user-interaction waits. Batch saves SHALL
expose member outcomes. Request timings SHALL be labelled client-observed elapsed time, not server CPU
or parser time.

#### Scenario: Save is refused
- **WHEN** a form cannot be saved faithfully
- **THEN** its observation reports refusal and a stable reason without its name or contents

#### Scenario: A batch is only partially saved
- **WHEN** one member is saved and another is refused or fails
- **THEN** the batch outcome is partial and member outcomes remain distinguishable

#### Scenario: The user leaves a file picker open
- **WHEN** a save waits for a path decision before writing
- **THEN** that wait is not included in the reported save-work duration

#### Scenario: A server cancels or never answers
- **WHEN** a client request is cancelled or times out
- **THEN** that outcome and elapsed time are recorded without the payload

### Requirement: Retention is bounded and loss is visible
Local traces SHALL have finite count/byte budgets, evict oldest completed records on overflow and keep
recording. Pending operations, metric series, subscriber queues and snapshots SHALL have finite
per-consumer and global limits. Inspection and manual export SHALL show windows and separate counts
for sampling, eviction, rejection and delivery loss. Sampled trace counts SHALL NOT be presented as
complete operation totals.

#### Scenario: A long session exceeds its record budget
- **WHEN** either local-buffer limit is reached
- **THEN** old completed records are evicted, recording continues and the visible eviction count increases
- **AND** retained data and pending work stay within their documented bounds

#### Scenario: Concurrent requests never finish
- **WHEN** pending operations reach their limit
- **THEN** additional diagnostic state is bounded and its loss is counted without rejecting application requests

### Requirement: Inspection and file export are user-controlled
The developer SHALL be able to inspect and clear observations, stop exporters, and preview an immutable
snapshot before saving it. Preview SHALL identify fields, time range and omissions. Cancellation SHALL
write nothing. Existing-file replacement SHALL require explicit consent; a file appearing at a previously
absent destination SHALL NOT be silently overwritten. Manual export SHALL neither enable subscriptions
nor transmit the snapshot.

#### Scenario: A manual export is cancelled
- **WHEN** the snapshot preview or file picker is dismissed
- **THEN** no file is written and recording and exporter settings remain unchanged

#### Scenario: A destination appears during export
- **WHEN** a file arrives at a previously absent chosen destination before creation
- **THEN** export reports failure rather than overwriting it without replacement consent

#### Scenario: The local record is cleared
- **WHEN** Clear is invoked
- **THEN** the local record and aggregates restart at an empty window
- **AND** prior in-flight observations do not repopulate it while active subscriptions remain unchanged
- **AND** the view explains that clearing local history does not recall already shared data

### Requirement: Observations are session-local and settings are profile-local
Operational observations SHALL be discarded on IDE exit and SHALL NOT be automatically persisted or
recovered. Manual export SHALL be the only file containing this capability's recorded observations.
Recording settings SHALL belong to the active user profile and SHALL NOT be written into projects or
sidecars. An explicit Off choice SHALL persist. A fresh alternate profile SHALL default to Local.

#### Scenario: A session closes
- **WHEN** the IDE exits with observations in memory
- **THEN** no automatic observation file is created and the next session has no prior record

#### Scenario: Another profile is opened
- **WHEN** a fresh alternate user-data directory is used
- **THEN** recording uses Local without inheriting another profile's settings

### Requirement: Add-ins receive a bounded authorized live subscription
The host SHALL expose a read-only operational stream separately from editor diagnostics, requiring a
host-mediated grant naming the verified build, destination and signal/data scope. Loading or trusting
any add-in, including first-party code, SHALL NOT grant access. Grants SHALL be instance-bound and
session-scoped. Changing build, destination or widening scope SHALL require a new grant. Off SHALL
refuse subscriptions.

#### Scenario: A loaded add-in subscribes without a grant
- **WHEN** an add-in requests the operational stream without the user's export confirmation
- **THEN** the host refuses it without disclosing any observations

#### Scenario: The developer authorizes one exporter
- **WHEN** the developer confirms the identified build, destination and requested data categories
- **THEN** only that exporter instance receives the authorized live stream
- **AND** no other instance can reuse its grant

### Requirement: Subscribers cannot replay local history
A subscription SHALL begin with an empty observation window and deliver only operations begun and
measurements recorded after its grant. It SHALL expose neither local history nor prior aggregate totals.
The host SHALL revoke delivery synchronously when Stop Export is invoked, discard its pending subscriber
queue and signal cancellation to the add-in. A slow, failed or disposed subscriber SHALL NOT block the
producer or another subscriber. Disposing a subscription SHALL release its resources.

#### Scenario: Local history exists at authorization
- **WHEN** a subscription is granted after ten projects have been opened
- **THEN** those operations and their metric totals are absent from the stream
- **AND** operations already in progress at the grant are excluded

#### Scenario: The exporter UI is unavailable
- **WHEN** Stop Export is invoked in the core diagnostics view
- **THEN** that subscriber receives no further records and its cancellation signal is raised
- **AND** stopping does not depend on the add-in's own window responding

#### Scenario: A subscriber stops consuming
- **WHEN** its queue fills or its consumer fails
- **THEN** bounded loss is reported for that subscriber and the IDE and other subscriptions keep working

### Requirement: Instrumentation preserves application behavior
Recording and subscription delivery SHALL NOT wait for disk, network or add-in code on editing, saving
or protocol paths. Diagnostic errors SHALL produce bounded loss and a visible state, not application
failure. Diagnostic shutdown SHALL have a documented finite timeout. Observation failures SHALL NOT
recursively generate observations. Instrumentation SHALL NOT modify language-protocol frames or write
telemetry to a protocol process's standard output.

#### Scenario: Diagnostics fail during Save
- **WHEN** the operational recorder fails or its queue is full
- **THEN** Save follows its normal persistence and error behavior independently

#### Scenario: A language server uses standard input and output
- **WHEN** operational diagnostics are enabled
- **THEN** its protocol frames remain unchanged and no telemetry is written to its standard output

### Requirement: Project content and identities are excluded from operational data
Operational metadata SHALL exclude source text, project/document names, paths and URIs, variable values,
keystrokes, clipboard data, protocol bodies, free-form logs, stack traces, arbitrary exception messages,
command lines, environment contents, credentials and persistent user/machine identities. This SHALL cover
names, attributes, events, links, resource metadata and propagated context. Hashing an excluded value
SHALL NOT make it eligible for collection.

#### Scenario: Identifying data appears in enrichment
- **WHEN** an ambient context or resource attribute contains a user identity, secret or project path
- **THEN** that value and any hash of it are absent from local records, snapshots and subscriber output

### Requirement: The core view reports effective state
The IDE SHALL provide a localized, accessible diagnostics view showing active recording mode, observation
window, resource limits and separate loss counts. Registered exporters SHALL show effective destinations
without credentials, operational/log subscription states and independently reported transport health. The view SHALL
distinguish a stopped subscription from unconfirmed exporter transport state.

#### Scenario: Exporter transport cannot be confirmed stopped
- **WHEN** a revoked exporter fails to acknowledge transport cancellation
- **THEN** the host view shows stopped subscription delivery and unconfirmed transport state
- **AND** inspection and stopping other subscriptions remain available

### Requirement: Exporter lifecycle revokes its host grant
An exporter's host grant SHALL expire on IDE exit, disablement, unload, initialization failure or explicit
revocation. Disablement SHALL revoke delivery immediately even if assembly unloading is deferred until
restart. Revocation SHALL discard the host's pending queue and signal cancellation without waiting for
add-in code. Re-enablement SHALL require a fresh grant.

#### Scenario: A loaded exporter is disabled
- **WHEN** the developer disables its package for the next startup
- **THEN** its current grant is revoked immediately and it receives no further operational records

## Purpose

Define the content-free operational record for investigating IDE performance and reliability locally,
and the host-owned, consented live subscription used by optional exporter add-ins. Detailed protocol
capture and application logging remain separate capabilities with their own controls. This curated
API is not a security sandbox for code running in the IDE's process.
