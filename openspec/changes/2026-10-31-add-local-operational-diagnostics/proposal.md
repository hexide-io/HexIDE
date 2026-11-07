# Local operational diagnostics and an optional exporter add-in

Design-record issue: [#2823](https://github.com/hexide-io/HexIDE/issues/2823).

## Why

Reports of slow project opening, refused saves and unresponsive language features need timings and
outcomes that users can inspect and share without disclosing their projects. HexIDE already records
protocol conversations and application logs; a small, content-free operational record makes those
reports easier to investigate and can feed a collector when the user explicitly chooses one.

## What Changes

- Add Off and Local recording modes. Local is the default: a bounded session record in memory,
  with no automatic network export or persistence of the new observations.
- Record project-open and save operations, client-observed language-server requests and connection
  lifecycle, using a documented, allowlisted metadata schema.
- Provide a diagnostics view with recording/export state, limits, loss counts, inspection, clearing
  and previewed manual file export. Existing logs and protocol captures retain their own controls.
- Expose a host-owned, bounded live subscription for content-free observations through the add-in API.
- Deliver OTLP traces and metrics through a separately installed exporter add-in, not a built-in
  Options setting. The standard distribution neither bundles nor automatically installs the add-in.
- The add-in contributes its own configuration command. Installation and load consent do not start
  export: a separate host-mediated confirmation names the destination and disclosure scope.
- Changing the destination, exporter build or exported scope requires fresh confirmation. Users can
  stop delivery from the core diagnostics view even if the add-in's UI fails. No local history is replayed.
- Keep project contents, identities, protocol bodies and arbitrary log/exception text out of this
  operational pipeline. Collector failures cannot block editing, saving or bounded shutdown.

The maintainer requested local-by-default instrumentation with discretionary collector export, then
suggested an add-in to make optionality explicit; this proposal adopts that boundary. The
specific budgets, transport and first-release scope below are proposed defaults for review, not a
claim that those details were previously agreed. This change records a design; it implements nothing.

## Capabilities

### New Capabilities

- `operational-diagnostics`: content-free observations, local retention and inspection, manual export,
  data boundaries and the bounded live subscription exposed to add-ins.
- `operational-diagnostics-export`: an explicitly installed exporter add-in, destination-specific
  authorization, OTLP delivery, revocation and failure isolation for operational signals.

### Modified Capabilities

- `tools-options-dialog`: a Diagnostics category with transactional Off/Local settings and a clear
  explanation that collector export is supplied by separately installed add-ins.

The existing `protocol-inspector` requirements are unchanged. Its raw captures, handshake exceptions,
redaction and disclosure flow are not an implicit source of collector data.

## Impact

Future implementation touches the desktop composition root, settings and localized
UI, project orchestration, the LSP client and the curated add-in host API. Instrumentation uses the .NET diagnostic
APIs; the OpenTelemetry SDK and exporter belong exclusively to the optional add-in's private dependencies.
Core remains free of third-party dependencies and the standard desktop distribution has no OTLP exporter.
Package versions and licences are verified and recorded when dependencies are introduced; none are
added by this proposal. No VB6 files, runtime semantics or language protocols change.

The first release observes external servers from the client. It neither injects instrumentation into
them nor modifies protocol messages to propagate trace context. Server-side parser spans, interpreter
statement tracing, runtime execution diagnostics, persistent flight recording, automatic log export,
historical log-file upload, external-process/program-output forwarding, maintainer-operated collection
and usage analytics are deferred. Live application-log export is decided separately in
[#2861](https://github.com/hexide-io/HexIDE/issues/2861); existing local log levels and retention remain independently controlled.

## Delivery phases

1. **Safe local observations:** define the schema, budgets and .NET hooks; record a bounded local
   session with tests for excluded content, loss and disabled operation.
2. **User controls and first useful operations:** add the localized view and Options mode, instrument
   open/save/LSP operations, and deliver inspection, clearing and previewed file export.
3. **Host subscription boundary:** add identity-bound export grants and bounded live subscriptions,
   with revocation independent of the exporter UI and no access to historical data.
4. **Optional collector add-in:** separately package the SDK/exporter, configuration command,
   trace/metric delivery, disclosure UI and bounded transport; installation alone sends nothing.
5. **Release acceptance:** verify the complete transitions, exported bytes, offline behavior,
   resource bounds and platform UI in distributed builds.

Each phase carries its own tests and user documentation in [tasks.md](tasks.md). Keep the change
unarchived until implementation and verification are complete. Current capability claims remain unchanged.

## Alternatives considered

- A network exporter configured solely by ambient environment variables is conventional in services,
  but does not establish an IDE user's intent. Explicit application configuration owns transmission.
- Persistent local traces would improve post-crash investigation but create a new retention promise.
  Begin with bounded session memory and deliberate file export; retain existing crash logs separately.
- Broad automatic instrumentation increases volume and disclosure scope before it answers a concrete
  support question. Start with a small catalog of manual operation boundaries.
- A built-in Collector mode would be convenient but puts network-export machinery in every installation.
  An explicitly installed add-in makes acquisition, dependencies and removal visible. It is an
  extensibility boundary, not a sandbox: add-ins execute in-process with the user's privileges.
