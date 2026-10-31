# Design

## Context

See [proposal.md](proposal.md) for motivation and scope. The data contracts are
[local diagnostics](specs/operational-diagnostics/spec.md) and
[optional export](specs/operational-diagnostics-export/spec.md), with a separate
[application-log contract](specs/application-log-export/spec.md) for potentially sensitive content.

Existing integration points inspected at the reviewed revision:

- `HexIDE.Core/Conversations` provides bounded protocol records and disclosure/export helpers. Its
  bodies, handshakes and process output can contain source material; the new pipeline does not ingest them.
- `HexIDE/Infrastructure/LoggingSetup.cs` configures a local Serilog file sink and a Microsoft logging
  facade. A host-owned branch can prepare explicitly selected live events without changing the existing
  file sink, threshold or retention. The new Off control stops supported exports, not those local files.
- `ProjectService` coordinates opening and saving; `HexIDE.Lsp` owns client request and connection
  boundaries. These are the first instrumentation sites.
- `IHexIdeHost.Diagnostics` is `IDiagnosticsAccess`: editor/compiler diagnostics with document names
  and messages. It is unsuitable for this content-free stream and remains unchanged.
- `IHexIdeHost` already supports contributed commands/windows, while the add-in registry supplies
  verified manifest identity and package-private dependencies. Those seams support an optional exporter.
- Options is transactional. The core page therefore owns only Off/Local recording; an add-in-owned
  command owns collector configuration. Immediate Stop Export lives in the core diagnostics view.

The budgets and delivery choices below are proposed first-release defaults, made concrete so they can
be reviewed and tested. They are not measurements of current functionality.

## Goals / Non-Goals

**Goals:** useful timings without project contents; no exporter acquisition or telemetry networking in a
standard installation; a stable host subscription usable by independently authored exporters; explicit
install, trust and transmission choices; optional live application logs with default redaction and a
deliberate unredacted override; bounded resources and independently accessible revocation.

**Non-goals:** usage analytics, automatic crash uploads, a hosted collection service, disk spooling,
historical log-file upload, program/external-process output forwarding, statement/keystroke tracing,
protocol content capture, runtime execution instrumentation,
deep parser spans in external processes, or a security sandbox for add-ins. The initial exporter supports
OTLP/HTTP protobuf; gRPC, automatic startup export and persistent credentials are deferred.

## Decisions

### 1. Core records locally; an add-in owns OpenTelemetry export

Instrument selected boundaries with long-lived `ActivitySource` and `Meter` instances from the .NET
diagnostic APIs. Keep source names and operation identifiers stable. Core contracts use framework types
and immutable values only. The desktop host owns the local listener, aggregation, budgets and UI.

The optional exporter lives in its own project/package, proposed as `HexIDE.TelemetryExport`, and owns
the OpenTelemetry SDK, HTTP client and OTLP exporter dependencies. Normal desktop publishing does not
reference or copy that project's outputs. Its separately signed package declares every private dependency
and a `minHexideVersion` matching the release that introduces the host API. Package selection and permissive
licence provenance are recorded during implementation using the repository's normal package checks.

The .NET APIs are the supported instrumentation route described in
[Microsoft's observability guidance](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel)
and [OpenTelemetry's .NET instrumentation documentation](https://opentelemetry.io/docs/languages/dotnet/instrumentation/).
The exporter uses the standard SDK pipeline rather than implementing OTLP serialization itself; the
[.NET exporter documentation](https://opentelemetry.io/docs/languages/dotnet/exporters/) describes that boundary.

Alternatives rejected: bundling a disabled exporter still puts its dependencies and configuration in
every installation; letting the add-in subscribe directly to the global log sink bypasses host selection
and consent. An add-in based on the existing editor Diagnostics API would disclose unrelated data.

### 2. A separate, immutable operational subscription on the host facade

Add a separately named `OperationalDiagnostics` access surface to `IHexIdeHost`, with a compatibility
default that reports unavailable on older host implementations. Do not overload `Diagnostics`. The
exporter's minimum host version and runtime capability check both refuse an unsupported host gracefully.
Core adds no dependency on add-in implementation assemblies or exporter packages.

Proposed contract responsibilities, with exact C# signatures settled in phase 3:

- Register an exporter description and obtain a disposable registration scoped to its verified add-in
  instance. The host supplies publisher/build identity; the caller cannot impersonate another package.
- Request a session grant for a destination, selected traces/metrics and disclosure-schema version.
  The host displays its own localized confirmation. This is separate from permission to load the add-in.
- Open a bounded asynchronous read-only stream using that grant. It yields immutable trace records and
  metric measurements, plus a cancellation signal and gap/loss information. No arbitrary callback runs
  on an editor, save or protocol thread, and no internal `Activity`, model or raw message object escapes.
- Report transport health using a fixed state vocabulary and numeric counters, never raw server text.
  Dispose, stop and unload revoke the stream, even if the caller's cleanup throws.

The host binds a grant to the verified manifest hash, loaded instance, exact normalized destination,
signals, schema version and session. At most one operational stream and one separately authorized log
stream exist per registered exporter instance; replacing a stream disposes its predecessor. Registration
and stream caps are explicit, not unbounded add-in maps. No record-history or aggregate-history API is
exposed through either surface. Operational consent cannot be reused as a log grant.

The separate in-flight `2026-10-26-addin-capability-declarations` change must not be treated as implemented.
This stream's explicit host grant is mandatory independently of that work. If capability declarations have
landed by phase 3, use their UI contribution declaration for the configuration command and document this
new grant separately; do not ask for `project.read` or silently redefine it. A future manifest vocabulary
extension is a separately reviewed change, not a new sandbox claim smuggled into this proposal.

### 3. Safe data is constructed at the instrumentation site

Use a closed catalog rather than a free-form property bag. Validate values when building the immutable
record and again before serializing a snapshot or handing it to a subscriber. Defensive filtering of
SDK output remains useful but is not the primary privacy mechanism. Automatic instrumentation and
resource detection are not enabled.

| Field family | First-release choices |
|---|---|
| Resource | Fixed service name, HexIDE build version, OS family and architecture, schema version |
| Operation | Registered open/save stages, LSP request, connection start/exit/restart |
| Outcome | success, partial, refused, cancelled, timeout, failure |
| Reason | Registered codes such as unfaithful-save, encoding-refusal, io-failure, unavailable, other |
| Measurements | Duration in seconds, counts of documents/members/requests, bounded numeric protocol error/exit codes |
| Trace correlation | Random session/operation IDs, safe parent IDs, batch member ordinal, ephemeral connection slot |
| Metrics dimensions | Registered operation/outcome/reason/method categories only; no session, connection or document ID |

For protocol methods, use a fixed reviewed method catalog; map custom names to `other`. Server names
configured by a user are not exported: use bundled/external and, for traces only, an ephemeral connection
slot. Never hash project or user identities as a substitute for excluding them. The exporter uses an
explicit resource allowlist and does not import ambient baggage, trace state, environment-derived resource
attributes, exception events or automatic service-instance identity. Validate the serialized OTLP payload,
including SDK-added fields, in tests. [OpenTelemetry's data-handling guidance](https://opentelemetry.io/docs/security/handling-sensitive-data/)
does not supply application-specific sensitivity classification; that is this catalog's job.

### 4. Record actual work and expose its limits

Start an operation after user interaction has resolved its inputs. Instrument processing segments around
mid-operation prompts so dialog dwell time is excluded; preserve wall-clock timestamps separately from
the summed work duration. Save batches produce member outcomes and an honest aggregate outcome. No
document identifier is needed to distinguish ordinal members within one trace.

Use client-owned request IDs only for local correlation, mapping them to random trace identifiers in the
safe record. One request has one terminal observation; notifications are not requests and do not acquire
invented response latency. Connection restart and loss are separate operations. Do not claim that a
client-side duration identifies time spent in a remote parser. Do not add trace fields to LSP/DAP messages,
inject startup instrumentation or change external-process environments.

Emit metric measurements independently of retained trace samples. The first version records every
eligible coarse operation while capacity permits; it does not sample individual interpreter statements.
Any loss of metric measurements in a subscriber queue is visible as incomplete delivery, not silently
reported as a complete total. A future sampling policy must preserve this distinction.

### 5. Fixed initial resource budgets

| Resource | Proposed initial limit and overflow policy |
|---|---|
| Completed local traces | 10,000 records or 8 MiB accounted payload, whichever comes first; evict oldest |
| Single safe record | 2 KiB; reject oversized records, increment rejection count |
| Pending instrumented operations | 1,024; stop allocating diagnostic state for excess operations and count loss |
| Metric series | 256 per recording/export window; fixed histogram buckets; reject excess series |
| Active exporters/streams | 4 exporters globally; 1 operational and 1 log stream each; refuse excess explicitly |
| Host operational subscriber queues | 1,024 records / 2 MiB each; drop oldest and report gaps |
| Host log subscriber queues | 256 records / 2 MiB each; drop oldest and report gaps |
| All host subscriber queues | 16 MiB global across both stream types |
| Host log preparation | 128 records / 1 MiB per pipeline, at most 5 pipelines including preview; reject excess and expire entries after 30 seconds |
| Single log record | 16 KiB total, 4 KiB per string, 32 fields, nesting depth 4; omit unsupported values, reject oversized records |
| Redaction work | At most 10 ms per record; timed-out/failed records are dropped, never passed through |
| Log preview | One host-owned preview, at most 50 records / 256 KiB, 15 seconds; discard on close or expiry |
| Optional add-in delivery queues | 2,048 records / 4 MiB combined, with at most 1,024 records / 2 MiB for logs; discard oldest on saturation |
| Manual snapshots | One immutable snapshot in progress, 9 MiB including metric/manifest allowance |
| Network batch | At most 256 records and 512 KiB serialized data |
| Collector timing | Five-second batch interval and per-request timeout; five attempts or 30 seconds of age, whichever comes first |
| Stop/exit | Two-second cooperative cancellation/shutdown budget; discard remaining data |

Budget bookkeeping includes records and string storage conservatively; these payload limits are not a
claim about total CLR heap size. Measure actual allocations with bounded-cardinality fixtures and a
stalled consumer. Do not offer knobs to disable the ceilings in the first release. Expose limits and
separate eviction/rejection/subscriber/export loss counts in the view and snapshot metadata.

These limits are proposed operating defaults, not throughput targets. Benchmark Off versus Local on
fixed open/save and LSP workloads: target at most 5% median overhead for operations exceeding 20 ms and
at most 1 ms absolute median overhead for shorter operations on the recorded benchmark machine. Publish
the workload, allocations and timings; investigate a miss before shipping rather than loosening the
claim silently. No network, disk or add-in call may be on the operation's synchronous path. Log callbacks
only make bounded primitive snapshots and attempt non-blocking enqueue; redaction runs on a host worker
before subscriber delivery. Preparation copies remain host-owned and are released on processing, expiry
or revocation; prepared output follows the explicitly authorized redaction mode. Do not allocate this
additional queue without a log grant or explicit
local preview. Existing application log files keep their existing independent retention.

### 6. Collection and export use independent windows

Local collection starts at IDE startup and ends on clear, Off or exit. Clearing increments a generation
so a previously started operation cannot later repopulate the cleared record. An exporter receives a
different generation beginning only after the host grants its destination and scope. The host excludes
operations that began before that grant and forwards no earlier metric measurements.

The add-in creates fresh SDK providers/aggregators for each export window and converts only that window's
authorized records, with an independent provider and window for logs. Reconstructed spans retain original
safe timestamps and durations, not the time spent in
the exporter. Use the SDK's supported timestamp APIs and test the mapping against emitted OTLP. The SDK
generates transport span IDs: version one exports independent root spans with the host's random operation
and parent-operation IDs as correlation attributes. It does not claim to reconstruct a distributed parent
graph or override SDK-generated span IDs. Explicit roots exclude ambient parent context. Metric instruments
receive only newly delivered measurements. Avoid process-global providers shared with the core or other add-ins.

Changing destination or scope closes both the host and add-in queues and cancels old transport work.
Ordinary bounded retries within one active destination stay in the same window. OTLP delivery is best
effort: retries can produce duplicates after an ambiguous response, and acceptance does not prove final
backend persistence. The UI reports collector acceptance, not guaranteed storage.

### 7. Installation, configuration and Start Export are separate actions

The exporter contributes `Tools → Export Diagnostics…`. Its dedicated dialog can remember a validated
endpoint and selected signals in the active profile, but has no startup-enable option in version one.
It starts stopped on every IDE launch. First-party load pre-consent does not bypass export confirmation.
Saving configuration is offline; Start Export invokes the host's confirmation and creates a fresh window.
Changes while active are staged until confirmed; cancelling leaves the original stream active.

Use an explicit HTTP/protobuf base endpoint, deriving the standard trace/metric/log paths consistently.
The first release uses one destination per exporter instance for all selected signals. A destination
change invalidates both grants and queues.
Accept HTTPS with ordinary certificate validation; accept plain HTTP only for literal loopback addresses.
Reject userinfo, query and fragment components and never follow redirects. Authentication headers are
entered separately, masked, memory-only and excluded from settings, resource attributes and error logs.
Persist only that authentication is required; missing credentials prevent Start. Do not include a
maintainer endpoint, connection-test-on-type behavior, fallback endpoint or collector installation step.

Do not let `OTEL_*` environment settings configure these providers implicitly: set destination, resource,
headers, batching and sampling through the validated application configuration and verify precedence
against the chosen SDK version. This policy governs the supported exporter; it cannot constrain a profiler
or independent instrumentation deliberately injected by whoever controls the process.

### 8. Stop remains available without the add-in UI

The core view lists each registered exporter, verified identity, confirmed endpoint, operational/log
subscription states, log selection/severity/redaction, transport-reported health and loss. It owns Stop
Export and Stop All. These synchronously close both host streams and issue cancellation; they do not
depend on an add-in callback completing. The optional
exporter responds by cancelling HTTP, discarding its queue and disposing providers off the UI thread.
An unacknowledged stop is displayed honestly as a closed subscription with unconfirmed transport state.
Stopping only logs leaves independently authorized operational export running; Stop Export for an
exporter stops both. Applying Off also revokes both grants and prevents log-only export until Local is
restored and fresh consent is obtained. Neither action changes existing local log-file settings.

Disablement revokes the grant immediately, even though existing add-in management may defer assembly
unloading until restart. Initialization failures dispose registrations as existing host contributions do.
No reference retained by a stream may prevent unloading after it is disposed. This is cooperative
behavior, not process containment: malicious in-process add-ins can bypass the curated API and use the
network or filesystem directly. Signing identifies their publisher; this design does not sandbox them.

### 9. Local inspection and export do not need an add-in

Add `Tools → Diagnostics…` and the recording-only Diagnostics Options category. Use the existing tree,
localization and accessibility conventions. The diagnostics view can inspect, clear, export a file and
stop subscriptions; Options remains a confirm-or-discard transaction. Cancelling an Off selection does
not revoke anything. Confirming Local after a stop cannot revive a grant.

Manual export previews one immutable snapshot, then writes a single versioned JSON file at a chosen
destination. Include safe traces, local metric summaries, time windows, limits and loss metadata. This
is a documented support format, not a claimed OTLP file. Reuse file-picker, replacement-consent and safe
write patterns; create absent destinations exclusively to avoid races. Never attach a log or protocol
capture automatically. Cancellation and failed writes leave existing destination contents intact.

### 10. Application logs use a separate host subscription

Add a separately named `ApplicationLogExport` access surface, with the same older-host unavailable
fallback as the operational facade. It accepts a separate host-owned log grant; it never exposes local
log files, logger instances, raw event/exception objects or operational history. The initial catalog
contains reviewed IDE application sources/categories only. Offer explicit selections from that catalog,
with minimum severity Warning. Resolve selections to a closed set at confirmation; new categories and
unknown origins do not join an existing grant. Fixed source identities come from host integration,
not arbitrary user-authored category names. This remains cooperative in-process behavior, not an
authenticity boundary against malicious add-ins.

Keep the branch in desktop logging composition using existing logging dependencies. Core only defines
framework-based immutable records and contracts; the OTel SDK remains entirely in the optional package.
Capture a bounded value snapshot at event production and associate it with the active grant generation.
Prepare it asynchronously. Do not enumerate arbitrary objects or invoke user-defined formatters in the
new branch. Unsupported property values are omitted and counted; enforce nesting/size limits before
queueing. With neither a log grant nor an explicit preview there is no additional log retention.

The log schema carries original event time, severity, fixed source/category, event identifier where
available, bounded message text, structured primitive fields and exception details. The disclosure names
all these fields. Log contents may include identities, paths or source fragments, even with best-effort
redaction; the operational schema's content-free promise does not apply to this stream. Resource
metadata remains restricted to the operational resource allowlist, without automatic user/host detection.
Do not scrape files, call `IDiagnosticsAccess`, tap protocol capture, or subscribe to process output.
Existing application messages can themselves contain fragments of such material; the warning accounts
for that possibility. The source restriction is not a promise to classify all free text.

Export only records actually produced by existing logging configuration. Selecting Debug export does
not lower the global threshold or override `HEXIDE_LOG_LEVEL`; explain that effective limitation. A log
grant does not require trace/metric consent and cannot access those streams. Log entries created before
authorization, including entries waiting in an upstream asynchronous buffer, are ineligible. Bind the
window at event production rather than using the time a worker dequeues a record.

### 11. Redaction defaults on and can deliberately be turned off

Redaction is a versioned host policy applied before log records reach the exporter or preview display.
The initial policy removes known sensitive structured fields such as passwords, authorization headers,
cookies, tokens and connection strings, and masks recognized credential, path and identity patterns in
all outgoing strings, including exception messages and stacks. Publish exact rules and fixtures; do not
claim to detect all personal data or secrets. Nested supported fields receive the same treatment. Do not
preserve a raw message template, original exception or hidden attribute alongside the redacted result.
An enabled redactor that fails or times out drops the record and increments a content-free health count.

The host binds the log grant to the policy version and enabled/disabled state as well as selection and
severity. Redaction starts enabled at every IDE launch; a disabled choice is never persisted in profile
or project settings. The same session can intentionally send unredacted logs. In that mode the content
transformation is bypassed, including its masking of recognized credentials in application messages.
The UI must not imply that a universal secret filter remains. Exporter authentication is kept out of
record construction and diagnostic messages in either mode; record limits and other scope/transport
controls still apply. No change to the operational metadata allowlist is permitted by this choice.

Use a prominent, localized warning with accessible text and focus on **Keep redaction enabled**. The
explicit alternative is **Disable redaction for this session**. No preselected acknowledgement, generic
OK action, colour-only warning, or silent restoration from saved settings. Each unredacted export grant
must carry an affirmative warning acknowledgement; combine this with the log-specific Start confirmation
where possible rather than stacking redundant prompts. Acknowledging an unredacted local preview alone
never authorizes network export. Proposed warning copy:

> Export selected application logs without redaction to **[collector]** for this session? Messages,
> fields and exception details may contain file paths, usernames, source fragments, passwords or tokens.
> The collector may retain or forward them. Stopping export cannot remove data already sent.

Both the add-in surface and the core diagnostics view retain a prominent **Redaction OFF** indicator
while unredacted export is active, including when the add-in window is closed. Use ordinary informed
controls without requiring typed phrases or artificial delays.

Adding categories, lowering severity, broadening fields, weakening the policy or disabling redaction
requires fresh host confirmation. Any applied log policy change, including narrowing scope or restoring
redaction, cancels that log provider's requests, discards raw preparation and processed delivery queues,
and creates a new window admitting only subsequent entries. Discard old-generation records even if a
worker completes late. Restoring redaction does not need another risk warning; show it as effective for
new records and disclose any unconfirmed cancellation. Already transmitted raw data cannot be recalled.
Unchanged operational grants/providers can continue when only the log policy changes.

### 12. Preview and OTLP mapping use the prepared records

The host offers **Preview outgoing logs** from the add-in's configuration surface. This is an explicit,
bounded local capture of subsequent matching entries under the proposed selection and redaction policy;
it requires no export grant and performs no destination lookup or connection. The host owns the preview
and never returns its records to the add-in. Show the message, structured attributes, exception details,
fixed resource fields and omissions as they would be sent. An empty sample says so. Explain that future
entries may contain different sensitive information. Require the redaction-off acknowledgement before
displaying an unredacted sample. Close/timeout discards the sample; Start creates a separate empty window.

The add-in uses a dedicated OTel logging provider to map only the host-prepared immutable log records,
preserving original timestamp and severity. Verify the selected SDK's mapping against an independent
OTLP receiver, including emitted message body, attributes, exception fields and resource metadata.
Do not resubscribe to global logging, re-render original templates, import ambient scopes/baggage or
attach raw exception objects. Preserve a host operation correlation identifier only when present; do
not invent a trace/span association with the reconstructed operational roots. Mapping loss is visible.

Keep log providers, batching and cancellation separate from operational providers while sharing the
confirmed destination and combined delivery budget. Prevent exporter/transport/redactor failure logs
from re-entering the exported stream by origin, even when redaction is off; report fixed health codes
and counters instead. Local file logging is unchanged. Collector unavailability or log floods cannot
block editing, saving, either host stream or bounded shutdown.

## Risks / Trade-offs

- **An add-in makes acquisition explicit but cannot sandbox networking.** Describe the host API guarantee
  and cooperative exporter behavior separately; show unconfirmed transport shutdown rather than assurance.
- **Session-only local recording is lost on a crash.** Existing application crash logs remain; persistent
  operational recording requires a separate retention decision.
- **Independent exporter dependencies can conflict through shared statics.** Keep SDK types out of Core,
  resolve the package's private dependencies in its load context, and test two exporters and disposal.
- **Adding metrics or SDK enrichment can broaden disclosure.** Version the safe schema and inspect final
  payloads containing sentinel secrets in every relevant application input and inherited environment.
- **Delivery loss and duplicates reduce analytical certainty.** Expose windows and losses, retain stable
  span identifiers on retries, and avoid claims of exact population counts or guaranteed remote storage.
- **UI, grants and shutdown can race.** Use identity-bound generations and deterministic tests for stop,
  Off, clear, configuration cancellation, package disablement and in-flight completion.
- **Log redaction cannot identify every sensitive value.** Describe its tested coverage, preview its
  output and retain the warning even when enabled. Verify all serialized fields, including templates
  and exception details, rather than just the visible message text.
- **Unredacted export intentionally permits sensitive log contents.** Keep that choice explicit,
  session-scoped and visible, and verify that restoring redaction discards all old raw queues. These
  are supported-product defaults and consent rules, not a claim to control modified builds or add-ins.

## Migration Plan

Ship local recording and inspection first. Existing profiles without a recording preference use Local;
existing explicit Off preferences are preserved. Existing logs, captures and add-in trust behavior stay
unchanged. Add the subscription seam next, without acquiring an exporter on the user's behalf. Publish
the exporter as a separate signed package and separate release asset, with installation and removal docs.
Log export starts disabled for both new and existing exporter profiles. Default redaction is enabled,
and no pre-existing trust decision or operational grant is migrated into log consent.

Rollback is disabling/removing the optional exporter and revoking its subscriptions; local diagnostics
continue. Removing the core feature later leaves only an ignored recording preference and deliberately
created user export files. It does not leave a background service, listener, spool or installed collector.

Before each phase lands, update only the user claims that phase actually implements. No capability is
advertised as available merely because its proposal or package source exists.
