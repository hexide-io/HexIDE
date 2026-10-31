## ADDED Requirements

### Requirement: Application-log export requires separate authorization
Log export SHALL default to disabled and require its own host-mediated session grant. Installation,
load trust and operational-signal consent SHALL NOT authorize logs. The grant SHALL bind the verified
exporter build/instance, destination, sources/categories, minimum severity, record schema and redaction
policy. The host SHALL refuse access without that grant or while diagnostics is Off. Logs SHALL NOT
enter the content-free operational stream or its snapshots.

#### Scenario: Only traces and metrics are authorized
- **WHEN** an exporter has operational-signal consent but no log grant
- **THEN** it receives no application-log records through either host subscription

#### Scenario: Logs are the only selected signal
- **WHEN** diagnostics is Local and the developer confirms only application-log export
- **THEN** the exporter receives eligible logs without receiving traces or metric history

### Requirement: Log selection is explicit and does not expand recording
The developer SHALL select supported application-log sources/categories and minimum severity, initially
Warning. The confirmed selection SHALL exclude subsequently added categories. Export SHALL NOT lower
the application's logging threshold, read log files, attach protocol captures, subscribe to program or
external-process output, or obtain editor diagnostics. Messages, structured fields and exception details
within selected logs SHALL be disclosed as potentially content-bearing.

#### Scenario: A new source becomes available
- **WHEN** a new category or add-in log source appears after authorization
- **THEN** it remains excluded until selected and freshly authorized

#### Scenario: A lower export threshold is selected
- **WHEN** Debug export is authorized but application logging produces only Information and above
- **THEN** export includes only eligible records actually produced and explains the effective limit
- **AND** it does not enable Debug logging elsewhere

### Requirement: Log disclosure names the destination and content risks
Before transmission, host confirmation SHALL name the destination, selection, severity and redaction
state, including for loopback. It SHALL explain that logs may contain paths, identities, source fragments,
exception details and secrets; redaction is best effort; future records may differ from the preview;
and collectors may retain or forward data. Cancelling SHALL preserve the existing authorized state
without contacting a proposed destination or widening disclosure.

#### Scenario: Log export is added to an active exporter
- **WHEN** the developer enables logs alongside authorized metrics
- **THEN** a separate log disclosure is confirmed before any log delivery
- **AND** cancelling leaves the existing metrics-only authorization unchanged

### Requirement: Log redaction is enabled by default
Local redaction SHALL default to enabled at each IDE launch. Known sensitive fields and credential
patterns SHALL be removed or masked across message text, structured fields and exception details before
subscriber delivery or preview display when enabled. The policy SHALL document coverage and limitations
without promising anonymity or complete secret detection. Redaction failure SHALL drop the record and
report bounded loss, never pass it through unredacted.

#### Scenario: Sensitive values occur in several fields
- **WHEN** a selected record contains credentials recognized by the policy in its message, attributes and exception
- **THEN** every recognized occurrence is removed or masked in the outgoing record and local preview

#### Scenario: Redaction fails
- **WHEN** the enabled redactor throws or exceeds its processing limit
- **THEN** that record is dropped, loss is visible and raw content is not delivered or displayed

### Requirement: Disabling redaction is a deliberate session choice
The developer SHALL be able to disable content redaction for selected logs after a prominent,
accessible warning explicitly naming unredacted content and possible secrets. The default action SHALL
keep redaction enabled; proceeding SHALL require an explicit labelled confirmation. Each unredacted
export grant SHALL include that acknowledgement. The override SHALL NOT persist across IDE launches
or be activated by environment variables, project settings or add-in load trust.

#### Scenario: The developer accepts unredacted export
- **WHEN** the warning is explicitly accepted and the destination and log scope are confirmed
- **THEN** eligible new logs may be exported without the content-redaction transformation
- **AND** scope, record limits, transport validation and lifecycle controls still apply

#### Scenario: The warning is dismissed
- **WHEN** the developer cancels the redaction-off warning during a redacted export
- **THEN** redaction stays enabled and the current authorized export continues unchanged

#### Scenario: The IDE restarts after unredacted export
- **WHEN** a new IDE session starts
- **THEN** export is stopped, redaction is enabled and no unredacted grant survives

### Requirement: Log policy changes establish a fresh delivery window
Adding sources/categories, lowering minimum severity, expanding fields, weakening redaction or disabling
it SHALL require fresh host confirmation. Applying any changed log policy SHALL close the previous log
window, discard queued records, cancel its requests and admit only newly eligible entries. Re-enabling
redaction SHALL NOT require another risk acknowledgement. Cancelling an edit SHALL leave the old policy
effective. Already transmitted records SHALL be described as unretractable.

#### Scenario: Redaction is disabled during delivery
- **WHEN** an active redacted exporter requests redaction off
- **THEN** it receives no unredacted entries before fresh warning acknowledgement and host confirmation
- **AND** acceptance discards the old log queues and starts a new authorized window

#### Scenario: Redaction is restored with raw records queued
- **WHEN** the developer applies redaction on while unredacted records await delivery
- **THEN** pending raw records are discarded, old requests are cancelled and future entries are redacted
- **AND** retries cannot send records from the old policy window

#### Scenario: A source is removed
- **WHEN** the developer narrows the confirmed category selection
- **THEN** queued records from the old selection are discarded before the new window begins

### Requirement: Preview is local and represents the proposed policy
An explicit preview action SHALL capture a bounded sample of newly produced selected logs locally,
using the proposed schema and redaction policy, without transmission or an exporter grant. It SHALL
show message, fields and exception details that would be sent. Unredacted preview SHALL require the
redaction-off acknowledgement. Samples SHALL expire on close or timeout and SHALL NOT be replayed
into export. An empty sample SHALL be reported honestly.

#### Scenario: The developer inspects a sample without exporting
- **WHEN** Preview is requested for a proposed destination
- **THEN** newly produced eligible records are shown locally under the selected redaction policy
- **AND** no destination lookup or connection occurs and no exporter receives the sample

#### Scenario: No matching logs arrive
- **WHEN** the bounded preview period ends without an eligible entry
- **THEN** the preview reports no matching records without reading older logs or inventing a sample

### Requirement: Log subscriptions are live and bounded
Log subscriptions SHALL deliver only entries produced after their grant and SHALL expose no file or
history access. Records, preparation work and queues SHALL have finite size/count and processing limits.
Delivery SHALL be asynchronous without waiting for redaction, network or add-in code on application
paths. Saturation, unsupported values and processing failures SHALL cause visible loss or omission.
Exporter failures SHALL NOT recursively generate exported logs. No disk spool SHALL be created.

#### Scenario: Logs predate consent
- **WHEN** log export starts after a failure and after a local preview
- **THEN** earlier application logs and preview samples are excluded from delivery

#### Scenario: A collector stalls while large exceptions are logged
- **WHEN** log production exceeds preparation or delivery budgets
- **THEN** bounded loss is reported and application work continues without unbounded memory or disk growth

### Requirement: Log export state remains visible and revocable
The add-in and core diagnostics view SHALL show active log export, destination, selection, severity,
redaction state and loss without echoing log contents or credentials. Unredacted operation SHALL carry
a persistent prominent Redaction OFF indicator that does not rely on colour alone. Stop/Stop All, Off,
disablement, unload, initialization failure and exit SHALL revoke the log grant, discard queues and
cancel transport. Restarting export SHALL require fresh consent.

#### Scenario: The add-in window is closed
- **WHEN** unredacted export continues with its configuration window closed
- **THEN** the core diagnostics view still identifies unredacted export and offers Stop

#### Scenario: Diagnostics is switched Off
- **WHEN** Off is confirmed while logs alone are being exported
- **THEN** the log grant is revoked and supported log export stops
- **AND** existing local application logging retains its own settings and retention

### Requirement: Content redaction does not control other export safeguards
Disabling log redaction SHALL affect only the selected log contents. It SHALL NOT widen operational
metadata, enable automatic identity/resource enrichment, include unselected sources, or weaken verified
transport, queue limits or consent. Exporter authentication SHALL remain separate from recorded data
and SHALL NOT be intentionally added to logs, previews, health, settings or payloads. The UI SHALL NOT
promise removal of secrets already present in unredacted application messages.

#### Scenario: An application message contains a credential
- **WHEN** an authorized unredacted log contains a credential supplied by the application
- **THEN** its content may be exported as the warning describes
- **AND** exporter configuration credentials are not added as attributes or diagnostic messages

#### Scenario: Operational signals accompany unredacted logs
- **WHEN** traces and metrics are exported alongside an authorized unredacted log stream
- **THEN** operational records and snapshots still obey their original content-free allowlist

## Purpose

Define voluntary live application-log export through the optional collector add-in, with separate
scope-bound consent, default local redaction and a deliberate session-only unredacted mode. This
content-bearing stream is distinct from operational metadata. It preserves existing local logging
controls and does not authorize historical files, protocol captures or external-process output.
