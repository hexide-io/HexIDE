## ADDED Requirements

### Requirement: Collector export is an explicitly installed add-in
Collector delivery SHALL use a separately distributed add-in and authorized host streams.
Standard IDE distributions SHALL NOT include or automatically install it. Its package SHALL follow
existing signing, verification, consent and dependency-isolation rules. Installing, trusting, loading or
updating it SHALL NOT start delivery. Core local diagnostics SHALL work without the add-in or its
exporter dependencies.

#### Scenario: The standard IDE is installed
- **WHEN** the developer installs or upgrades the normal IDE distribution
- **THEN** no collector exporter is installed, loaded or offered as an enabled default
- **AND** local inspection and manual snapshot export work without it

#### Scenario: The developer installs the exporter
- **WHEN** the exporter package passes verification and the developer allows it to load
- **THEN** its configuration command becomes available but no telemetry connection is attempted

### Requirement: The add-in owns a dedicated configuration surface
The exporter SHALL contribute a localized, accessible command under Tools or Add-Ins, with its own
endpoint, trace/metric/log selections, log policy, masked session authentication and delivery state. It SHALL NOT add
collector controls to core Options. Editing or saving configuration SHALL NOT resolve endpoints,
test connections or transmit data. Cancel SHALL preserve the prior active configuration.

#### Scenario: Configuration is explored and cancelled
- **WHEN** an inactive exporter is configured with an endpoint and then cancelled
- **THEN** no connection has occurred and no export grant exists

#### Scenario: An active configuration is edited
- **WHEN** an active exporter's endpoint is edited and the edit is cancelled
- **THEN** its previously confirmed destination remains active and the proposed destination is never contacted

### Requirement: Starting delivery requires a separate explicit action
Start Export SHALL obtain host confirmation of the build, endpoint, signals and data categories,
explaining possible forwarding by the collector. This SHALL also apply to loopback. Invalid/incomplete
configuration SHALL prevent starting. Grants SHALL expire at IDE exit; remembered settings SHALL NOT
restart delivery. Inherited telemetry environment variables SHALL NOT enable, redirect or enrich export.

#### Scenario: A loopback collector is selected
- **WHEN** Start Export is chosen for a loopback endpoint
- **THEN** host confirmation names it and the forwarding possibility before any connection

#### Scenario: The IDE is restarted
- **WHEN** the exporter loads with remembered endpoint and signal settings
- **THEN** it remains stopped until Start Export and fresh host confirmation

#### Scenario: The process inherits another endpoint
- **WHEN** exporter endpoint, resource or header environment variables are inherited
- **THEN** they neither enable delivery nor override the confirmed configuration or data allowlist

#### Scenario: Configuration cannot start delivery
- **WHEN** the endpoint or transport is invalid, required session credentials are absent, or no signals are selected
- **THEN** Start Export explains the missing or invalid value and performs no network operation

### Requirement: Destination and disclosure changes invalidate the old grant
A changed endpoint, exporter build, enabled signal set that widens disclosure, or data-category expansion
SHALL require fresh host confirmation. Applying a changed active destination SHALL stop the previous
delivery, discard its queues and start a fresh authorized window. Declining confirmation SHALL NOT send
anything to the proposed destination. Records queued for one destination SHALL NOT be redirected to another.

#### Scenario: An endpoint changes while data is waiting
- **WHEN** the developer confirms a different endpoint
- **THEN** old pending records are discarded rather than sent to the new destination
- **AND** only newly eligible observations enter the new stream

### Requirement: Operational export preserves its content-free contract
For traces and metrics the exporter SHALL use only the authorized operational stream and its allowlist.
It SHALL NOT import logs/captures, use editor diagnostics or unrestricted instrumentation, or add
identity, environment data or arbitrary context. Names, resource metadata, attributes, events and links
SHALL obey that allowlist. Metric totals SHALL start at authorization. A separate log grant SHALL NOT
widen operational payloads or supply their attributes.

#### Scenario: A local record already contains activity
- **WHEN** the exporter is started after a period of local recording
- **THEN** neither old traces nor old cumulative metric values enter OTLP output

#### Scenario: Raw captures and application logs exist
- **WHEN** the exporter is running alongside a source-bearing protocol capture
- **THEN** captured content and application logs remain absent from its operational OTLP payloads
- **AND** log payloads remain absent unless a separate application-log grant authorizes them

### Requirement: Application logs have their own subscription and policy
The same optional exporter MAY deliver OTLP logs through a separately granted host log stream under
the application-log-export contract. Log export SHALL default to disabled and redaction to enabled.
Disabling redaction SHALL require that contract's prominent warning and session acknowledgement.
The exporter SHALL use only records prepared under the authorized policy, without file tailing or a
global logging subscription. Operational-only consent SHALL NOT authorize any logs.

#### Scenario: Unredacted logs are explicitly authorized
- **WHEN** the developer confirms the log scope, destination and redaction-off warning
- **THEN** the same add-in may deliver newly eligible unredacted log records over OTLP
- **AND** any accompanying traces and metrics retain their restricted metadata schema

### Requirement: Delivery uses a verified standard transport
The exporter SHALL support OTLP traces, metrics and authorized logs with verified TLS server identity for remote
connections. Plain HTTP SHALL be limited to literal loopback IPs. URL credentials, redirects, certificate
bypass and fallback destinations SHALL be refused. Separate authentication SHALL go only to the confirmed
destination. Success SHALL indicate collector acceptance, not guaranteed downstream storage or analysis.

#### Scenario: A collector redirects to another host
- **WHEN** an export receives a redirect
- **THEN** no observations or credentials are forwarded and delivery health reports the refusal

#### Scenario: TLS verification fails
- **WHEN** the collector's certificate cannot be verified
- **THEN** export fails visibly without weakening verification or interrupting IDE work

### Requirement: Export queues and retries are finite
Delivery SHALL batch asynchronously with documented finite queue count/byte, record-size, retry-age and
shutdown limits and no disk spool. Saturation or terminal failure SHALL cause visible loss without delaying
IDE operations. Host-visible health SHALL use fixed states and counts, excluding raw remote error bodies
and credentials. Exporter failures SHALL NOT recursively produce exported failures.

#### Scenario: The collector is unavailable
- **WHEN** a collector stays unreachable while the developer works
- **THEN** IDE operations proceed normally, queues remain bounded and delivery loss/health is visible
- **AND** retries expire at the documented limit without a disk spool

### Requirement: Revocation stops delivery and discards pending data
Stop, grant revocation, Off, disablement, initialization failure and disposal SHALL stop new batches and
retries, discard queues and cancel in-flight requests. Stop SHALL require neither an IDE restart nor a
working add-in UI. Already transmitted data SHALL be described as unretractable. Re-enabling SHALL need
a fresh grant without backlog replay. Local recording SHALL continue unless Off was separately chosen.

#### Scenario: The core view revokes a running exporter
- **WHEN** Stop Export is invoked from the host view while a request is in flight
- **THEN** the add-in cancels that request, stops new batches and retries and discards its queue
- **AND** the host reports any bounded shutdown timeout without claiming to recall transmitted data

#### Scenario: Add-in disablement is scheduled for next startup
- **WHEN** the developer disables the exporter in add-in management
- **THEN** its export grant is revoked immediately even if unloading the assembly requires a restart

### Requirement: Exporter configuration respects profile and secret boundaries
Non-secret exporter settings SHALL belong to the active user profile, not project files or sidecars.
Authentication secrets SHALL be retained only in memory for the current session and SHALL be excluded
from configuration files, logs, health reports and manual diagnostic exports. Closing the IDE SHALL erase
the export grant and in-memory authentication. Missing session authentication SHALL keep delivery stopped.

#### Scenario: Another user profile loads the exporter
- **WHEN** a fresh alternate user-data directory is used
- **THEN** the exporter has no inherited endpoint, credentials or export grant

### Requirement: The optionality boundary is described accurately
The add-in's installation and export disclosures SHALL explain that add-ins execute in-process with the
user's privileges. Host subscription checks SHALL NOT be presented as a network sandbox or a guarantee
against malicious code. The supported exporter SHALL obey the authorization and cancellation contract;
the host SHALL report stopped subscription delivery separately from exporter-reported transport state
when it cannot verify that state.

#### Scenario: The exporter does not acknowledge cancellation
- **WHEN** the host closes a subscription but receives no transport-stop acknowledgement in time
- **THEN** the view states that subscription delivery stopped and exporter transport state is unconfirmed
- **AND** it does not claim that all networking by that add-in is forcibly blocked

## Purpose

Define the explicitly installed add-in that delivers authorized operational observations and separately
consented application logs to a user-selected collector. Acquisition, configuration and transmission
remain separate choices. Operational metadata retains its content-free contract while log delivery
has its own scope and redaction policy; both use host-controlled subscription boundaries.
