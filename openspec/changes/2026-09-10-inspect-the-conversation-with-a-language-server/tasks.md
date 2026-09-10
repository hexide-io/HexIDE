# Tasks

Five phases, in order. Phase 3 deliberately precedes phase 4: the capture is driven and verified through
automation before any window exists, which is how everything else here has been checked.

## 1. The protocol prerequisite
- [x] 1.1 Send `trace` in `InitializeParams`, from the per-server configured level
- [x] 1.2 Send `$/setTrace` when the level is changed while a server is running
- [x] 1.3 Handle `$/logTrace` and surface it rather than discarding it
- [x] 1.4 Add the message severity LSP 3.18 defines, and stop ranking an unknown severity above a known one
- [x] 1.5 Emit `$/logTrace` from the bundled server, so the client half has a conformant server to prove itself against
- [x] 1.6 Wire tests, not just "the call returned" — assert the frame, per this repository's standing lesson
- [x] 1.7 Regenerate the client coverage table and its counts

## 2. The capture model, with no user interface
- [ ] 2.1 A tap that sees byte-exact JSON bodies both directions, installed at every connect unconditionally
- [ ] 2.2 Envelope ring per connection, capped in entries, with a non-evicting handshake prologue
- [ ] 2.3 Payload store per connection, capped in bytes under a global ceiling, evicting bodies while envelopes survive
- [ ] 2.4 Per-frame cap storing head and tail with the true length recorded
- [ ] 2.5 Deduplicate `didChange` bodies by content hash
- [ ] 2.6 Request and response paired by id, with latency recorded
- [ ] 2.7 Handshake payloads captured unconditionally; everything after, only when armed
- [ ] 2.8 Arming and trace level held per connection id, surviving a respawn, reset by an IDE restart
- [ ] 2.9 Process lifecycle, standard error and exit code in the same record, attributed to the server that produced them
- [ ] 2.10 Declined requests, and capabilities advertised but not consumed, recorded as never-sent entries
- [ ] 2.11 A per-transport note of what cannot be observed
- [ ] 2.12 Drop counts, per connection, exposed rather than inferred
- [ ] 2.13 A named, tested redaction component: consistent pseudonymisation over root URI, workspace folders, `file:` document URIs and server launch configuration
- [ ] 2.14 Export as one JSON-RPC message per line plus a manifest
- [ ] 2.15 Limits read from configuration, clamped, with a rejected value reported through the existing configuration-problems channel
- [ ] 2.16 Capture must never block or reorder the RPC — assert that, do not assume it

## 3. The automation surface
- [ ] 3.1 A launch flag that arms capture at start
- [ ] 3.2 Tools: list envelopes with filters, fetch one payload by id, arm and disarm, clear
- [ ] 3.3 The capture service lives outside the automation server's own folder, since the server is absent from Release and the capture is not
- [ ] 3.4 Verified by driving a real conversation and reading it back

## 4. The window
- [ ] 4.1 A `Protocol Inspector` document tab, registered in the view locator table, the dock factory and the layout manifest
- [ ] 4.2 One interleaved timeline, server as a filter, defaulting to the focused server
- [ ] 4.3 A grid: time, direction, server, method, latency, size — with failures marked in place and counted
- [ ] 4.4 Raw body one action away, truncation stating the true length
- [ ] 4.5 Export and copy, the copy action using the text trace shape a server author recognises
- [ ] 4.6 Redaction preview before anything leaves, with disclosure sized in human terms
- [ ] 4.7 Arming controls in the Language & Debug Servers window — its first interactive controls
- [ ] 4.8 A header link always present, and a per-row link when that server is armed, opening filtered
- [ ] 4.9 Wire `ToReportText`, carrying redaction from its first commit
- [ ] 4.10 Localisation keys in `en`, translated across every shipped pack in the same change
- [ ] 4.11 Verified against the running IDE, not only headlessly

## 5. Retiring the proxy
- [ ] 5.1 Remove `HexIDE.LspProxy` and the `VB6_LSP_DEBUG_PROXY` environment variable
- [ ] 5.2 Remove its launch profile and its references in documentation
- [ ] 5.3 Confirm the inspector covers what it was reached for, and say so where the proxy was documented
