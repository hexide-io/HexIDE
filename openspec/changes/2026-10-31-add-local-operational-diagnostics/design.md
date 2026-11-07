# Design

## Context

See [proposal.md](proposal.md) for motivation and scope. The data contracts are
[local diagnostics](specs/operational-diagnostics/spec.md) and
[optional export](specs/operational-diagnostics-export/spec.md).

Existing integration points inspected at the reviewed revision:

- `HexIDE.Core/Conversations` provides bounded protocol records and disclosure/export helpers. Its
  bodies, handshakes and process output can contain source material; the new pipeline does not ingest them.
- `HexIDE/Infrastructure/LoggingSetup.cs` configures a local Serilog file sink and a Microsoft logging
  facade. The new Off control stops supported exports, not those local files.
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
install, trust and transmission choices; bounded resources and independently accessible revocation.

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
every installation. An add-in based on the existing editor Diagnostics API would disclose unrelated data.

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
signals, schema version and session. At most one operational stream exists per registered exporter
instance; replacing a stream disposes its predecessor. Registration and stream caps are explicit, not
unbounded add-in maps. No record-history or aggregate-history API is exposed.

#2853 landed schema-2 capability validation and verified-build declarations from
`2026-10-26-addin-capability-declarations`; that wider change remains in flight. The exporter declares
`ui.contribute` for its configuration command. The landed vocabulary is `project.read`, `project.write`,
`program.run`, `ui.contribute` and `languageServer.contribute`; none expresses destination-, signal-,
schema- and session-bound transmission consent. Declarations are not grants. Keep this stream's explicit
host confirmation separate, without requesting or redefining `project.read` or inventing a capability.

### 3. Safe data is constructed at the instrumentation site

Use a closed catalog rather than a free-form property bag. Validate values when building the immutable
record and again before serializing a snapshot or handing it to a subscriber. Defensive filtering of
SDK output remains useful but is not the primary privacy mechanism. Automatic instrumentation and
resource detection are not enabled.

| Field family | First-release choices |
|---|---|
| Resource | Build-carried product name as service name, build version, OS family and architecture, schema version |
| Operation | Registered open/save stages, LSP request, connection start/exit/restart |
| Outcome | success, partial, refused, cancelled, timeout, failure |
| Reason | Registered codes such as unfaithful-save, encoding-refusal, io-failure, unavailable, other |
| Measurements | Duration in seconds, counts of documents/members/requests, bounded numeric protocol error/exit codes |
| Trace correlation | Random session/operation IDs, safe parent IDs, batch member ordinal, ephemeral connection slot |
| Metrics dimensions | Registered operation/outcome/reason/method categories only; no session, connection or document ID |

The service name is the product name carried by the build, including a rebranded build, rather than a
literal `HexIDE`. #2042's branding configuration supplies that name once it lands.

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
| Active exporters/streams | 4 exporters globally; 1 operational stream each; refuse excess explicitly |
| Host operational subscriber queues | 1,024 records / 2 MiB each; drop oldest and report gaps |
| All host subscriber queues | 16 MiB global |
| Optional add-in delivery queues | 2,048 records / 4 MiB; discard oldest on saturation |
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
claim silently. No network, disk or add-in call may be on the operation's synchronous path.
Existing application log files keep their existing independent retention.

### 6. Collection and export use independent windows

Local collection starts at IDE startup and ends on clear, Off or exit. Clearing increments a generation
so a previously started operation cannot later repopulate the cleared record. An exporter receives a
different generation beginning only after the host grants its destination and scope. The host excludes
operations that began before that grant and forwards no earlier metric measurements.

The add-in creates fresh SDK providers/aggregators for each export window and converts only that window's
authorized records. Reconstructed spans retain original safe timestamps and durations, not the time spent
in the exporter. Use the SDK's supported timestamp APIs and test the mapping against emitted OTLP. The SDK
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

Use an explicit HTTP/protobuf base endpoint, deriving the standard trace/metric paths consistently.
The first release uses one destination per exporter instance for all selected signals. A destination
change invalidates the grant and queues.
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

The core view lists each registered exporter, verified identity, confirmed endpoint, operational
subscription state, transport-reported health and loss. It owns Stop Export and Stop All. These
synchronously close host streams and issue cancellation; they do not depend on an add-in callback
completing. The optional exporter responds by cancelling HTTP, discarding its queue and disposing
providers off the UI thread. An unacknowledged stop is displayed honestly as a closed subscription with
unconfirmed transport state. Applying Off also revokes grants until Local is restored and fresh consent
is obtained. Neither action changes existing local log-file settings.

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

## Migration Plan

Ship local recording and inspection first. Existing profiles without a recording preference use Local;
existing explicit Off preferences are preserved. Existing logs, captures and add-in trust behavior stay
unchanged. Add the subscription seam next, without acquiring an exporter on the user's behalf. Publish
the exporter as a separate signed package and separate release asset, with installation and removal docs.

Rollback is disabling/removing the optional exporter and revoking its subscriptions; local diagnostics
continue. Removing the core feature later leaves only an ignored recording preference and deliberately
created user export files. It does not leave a background service, listener, spool or installed collector.

Before each phase lands, update only the user claims that phase actually implements. No capability is
advertised as available merely because its proposal or package source exists.
