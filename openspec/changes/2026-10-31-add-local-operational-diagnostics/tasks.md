# Implementation tasks

This proposal is documentation only. Every task below remains implementation work. Follow the repository's
one-phase-at-a-time rule: land a phase with its tests and docs, report the result, then wait for the next
phase to be requested. Create/claim the implementing issues before beginning their code changes.

## 1. Safe local observations

- [ ] 1.1 Define immutable observation/measurement contracts, stable operation/reason catalogs and schema version in Core; verify the project keeps zero third-party dependencies and serialization rejects unknown fields/values.
- [ ] 1.2 Add .NET instrumentation sources and the host's bounded local recorder; verify count/byte limits, pending-operation limits, oversized-record rejection, metric-series limits and continuing recording after eviction.
- [ ] 1.3 Implement Off/Local lifecycle and recording generations; verify Off allocates no retained diagnostic state, clear excludes earlier in-flight completions, and exit creates no observation files.
- [ ] 1.4 Test the data allowlist with source/path/credential sentinels, arbitrary methods, exception messages and ambient context; verify local snapshots exclude every sentinel and its derived identifier.
- [ ] 1.5 Document the local schema, budget accounting and distinction from logs/protocol captures in `docs/operational-diagnostics.md`; verify examples against serialized fixtures and the tree-hygiene check.

## 2. Useful operations and local user controls

- [ ] 2.1 Instrument project-open and save processing segments; verify success/refusal/failure/cancellation/partial-batch outcomes and prove simulated picker dwell does not enter work duration.
- [ ] 2.2 Instrument client LSP requests and connection lifecycle; verify terminal events occur once, notifications do not get response latency, custom names map to fixed categories and wire frames stay unchanged.
- [ ] 2.3 Add the Off/Local Diagnostics Options category using active-profile settings; verify confirm/cancel/defaults, alternate-profile isolation and absence of built-in endpoint/authentication/export controls. Explain that Off revokes supported exports while existing local logs retain their own controls.
- [ ] 2.4 Add the core diagnostics view with inspection, windows, resource limits and separate loss counters; verify keyboard/accessibility behavior, bounded snapshots and clear-versus-in-flight races.
- [ ] 2.5 Implement previewed single-file operational export; verify immutable preview/output agreement, cancellation, existing-file consent, destination-creation races and failed-write preservation.
- [ ] 2.6 Add localized keys and translations for this phase and inspect the running IDE through its supported automation surface; verify English/pseudo/non-English rendering and accessible names, then update the user guide with only working local controls.
- [ ] 2.7 Benchmark Off versus Local on the documented workloads; record timing/allocation evidence against design budgets and verify recorder failures cannot change Save or request results.

## 3. Host-controlled add-in subscriptions

- [ ] 3.1 Add the separate operational access seam to the curated host facade with older-implementation compatibility; verify existing add-in diagnostics APIs are unchanged and unsupported hosts report the new seam unavailable.
- [ ] 3.2 Add exporter registration and host-owned session confirmation bound to verified package hash, instance, destination, signals and schema; test refusal without consent, first-party treatment, impersonation attempts and grant invalidation.
- [ ] 3.3 Implement bounded asynchronous streams with per-consumer/global caps and fresh windows; verify no historical records or metric totals escape, pre-grant operations are excluded, and stalled consumers cannot block other streams or IDE operations.
- [ ] 3.4 Wire grant revocation to Stop, Off, desired add-in disablement, unload and initialization failure; verify immediate host delivery stop, disposal despite throwing cleanup, and no grant revival on returning to Local.
- [ ] 3.5 Extend the core view with exporter identity/destination, operational/log subscription states, log scope/severity/redaction, health and Stop/Stop All; verify a persistent accessible Redaction OFF indicator, stopping without responsive add-in UI and honest transport non-acknowledgement.
- [ ] 3.6 Exercise the API with a test add-in and unload tests; verify stream disposal removes references preventing unload, two consumers are isolated and exporter data contains no internal model references.
- [ ] 3.7 Document both host APIs, independent grants, schema compatibility and non-sandbox guarantee; reconcile the state of capability-declarations work without inventing permissions, and verify localized grant/status/warning UI live.
- [ ] 3.8 Add the separate immutable log facade and bounded host logging branch with a closed source/category catalog; verify no retention without a grant or explicit preview, production-time windows, log-only grants, category exclusion, lower severity not changing global logging, omitted unsupported values and absence of file/protocol/editor-diagnostic access.
- [ ] 3.9 Implement versioned local redaction across message text, structured/nested fields and exception details; document exact rules and test recognized credentials/paths/identities, raw templates, oversized values, processing timeouts and fail-closed behavior. Verify prepared records contain no hidden raw originals and unchanged local file logging keeps its own policy.
- [ ] 3.10 Implement redaction-on defaults and the explicit session-only override; test warning acceptance/cancellation, no unredacted delivery before consent, intentional transmission of recognized application-message secrets when off, reset on restart, and unchanged operational allowlists and transport protections.
- [ ] 3.11 Bind log grants to category set, severity, schema and redaction policy; test scope expansion, lower severity, redaction weakening, return to redaction, category removal, raw queued data, late worker completions, cancellation and no old-generation retries. Verify log-only changes leave independently authorized operational delivery intact.
- [ ] 3.12 Implement host-owned outgoing-log preview; verify finite capture/sample limits, local-only preparation, unredacted-preview acknowledgement, all outgoing fields visible, empty-sample reporting, expiration, no add-in sample access, no endpoint lookup and no sample replay into export.

## 4. Separately installed collector exporter

- [ ] 4.1 Create the optional exporter project and select supported SDK/exporter package versions; record exact NuGet licence provenance, run licence checks, and verify no package reference or publish-copy edge adds them to the standard desktop distribution.
- [ ] 4.2 Implement the contributed configuration command, offline staged settings, profile isolation and masked session-only authentication; verify install/load/configure/cancel and IDE restart make no collector requests or name lookups.
- [ ] 4.3 Wire Start Export to host confirmation and independently granted operational/log subscriptions; verify no pre-consent traces/counters/logs, operational-only consent never enables logs, endpoint changes discard both backlogs, and inherited endpoint/header/resource environment variables cannot override the authorized configuration.
- [ ] 4.4 Convert immutable records to SDK traces and metrics with explicit resource metadata; inspect serialized OTLP using an independent receiver and verify original timestamps, durations, safe parent IDs, allowlisted fields and window-local metric totals.
- [ ] 4.5 Implement HTTP/protobuf delivery with endpoint validation, verified TLS, loopback-only plaintext and redirects disabled; test refusal paths, credentials confined to the intended endpoint and collector acceptance distinguished from downstream storage.
- [ ] 4.6 Enforce queue, byte, batch, retry-age and shutdown budgets; test a stalled/rejecting collector, ambiguous delivery and retries, overflow accounting, immediate stop/disablement and cancellation acknowledgement without recursive telemetry.
- [ ] 4.7 Package the exporter separately using existing signing/verification rules and a suitable minimum host version; verify install, upgrade, disable, removal and package dependency isolation, plus the absence of the exporter in normal installer/portable contents.
- [ ] 4.8 Document optional installation, dedicated configuration, log risks, redaction limitations/override, preview, session-only Start, secrets, loss and removal; complete translations and live UI verification, and run the guide against a local test collector without any maintainer endpoint.
- [ ] 4.9 Add dedicated OTLP log mapping/providers and verify payloads with an independent receiver: original timestamps/severity, selected fields, exception details, resource allowlist, redacted versus deliberately unredacted sentinels, no hidden raw templates or automatic ambient enrichment, and no invented trace association.
- [ ] 4.10 Wire log selection, prominent warning, preview and persistent redaction status into the add-in command; test first install, upgrade from operational-only configuration, cancel, log-only Start, Start without a preview sample, stop/restart and IDE relaunch. Verify disabled log export and enabled redaction defaults cannot be changed by environment or project settings.
- [ ] 4.11 Enforce independent log cancellation within combined delivery budgets; test raw backlog disposal on redaction restore, stale retries after policy/destination changes, exporter-origin recursion exclusion even with redaction off, stalled collectors, log floods and uninterrupted operational delivery/application work.

## 5. Release acceptance

- [ ] 5.1 Verify standard Release builds on Windows, Linux and macOS: local diagnostics work with no exporter assemblies, telemetry connections or listener ports; record platform evidence without claiming an unchecked platform passed.
- [ ] 5.2 Exercise the full lifecycle with the separately installed exporter: confirm, cancel, stop, clear, Off/Local, package disable, endpoint change, redaction on/off and restart; verify host records, visible warnings/states and independently observed network traffic for operational-only, log-only and combined grants.
- [ ] 5.3 Run a long session with hostile-content fixtures, many concurrent operations, log floods and failed collectors; verify memory/queue/processing bounds, no disk spool and bounded shutdown. Assert source-bearing content is absent from operational output, known sensitive patterns are redacted when enabled, and intentional unredacted content appears only under its explicit current grant.
- [ ] 5.4 Run affected focused/unit/integration suites through the repository's test wrappers and check xunit reports where required; validate this change with `openspec validate 2026-10-31-add-local-operational-diagnostics --type change --strict` and run tree-hygiene/licence checks.
- [ ] 5.5 Review shipped documentation, installer contents and export disclosures against the final behavior; verify the optional add-in is neither bundled nor automatically installed and update current capability claims only for completed behavior.

## Workflow follow-up

- Archive only after implementation, platform verification and review are complete, using the OpenSpec CLI.
- Read the merged capability Purposes and deltas after archiving; do not manually move this directory or mark documentation as implemented functionality.
