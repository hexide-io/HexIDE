# Tasks

## 1. Widen the transport seam

- [x] 1.1 Give the stdio transport an explicit command, arguments and working directory, instead of asking
      `ILspServerLocator` for the one bundled server
- [x] 1.2 Keep `ILspServerLocator` as how the **bundled default entry** computes its command — it walks up
      from the base directory because that path differs between a dev build and a publish, and that problem
      does not go away
- [x] 1.3 A default entry whose command cannot be located is omitted, not contributed broken
- [x] 1.4 Confirm the named-pipe and WebSocket transports need no change — both already take their
      parameters explicitly

## 2. The configuration itself

- [x] 2.1 A record for one entry: id, display name, extensions, language id, transport + its parameters,
      priority, enabled
- [x] 2.2 Source-generated `JsonSerializerContext`, camel-case, explicit property names — this is a
      persisted contract
- [x] 2.3 Read from the per-user directory alongside the existing settings and consent files
- [x] 2.4 Allow comments in this one file — done, so a hand-written file that explains itself parses.
      **The header block is NOT shipped and cannot be yet:** nothing creates this file, and auto-creating
      one would defeat "absent means defaults" and make deleting it pointless. The template belongs to
      whatever first offers to create the file, which is the connections view (#259)
- [x] 2.5 Merge over compiled-in defaults by id; absent file means defaults
- [x] 2.6 Reject an entry missing what is required to reach a server; keep and report an entry carrying an
      unrecognised field
- [x] 2.7 One bad entry never affects another, and never prevents startup

## 3. Routing by extension

- [x] 3.1 Key routing on extension rather than a global extension-to-language table
- [x] 3.2 Tell each server the language identifier **it** declared, on `didOpen` and everywhere else the
      identifier travels
- [x] 3.3 Keep scheme-first precedence — the IDE's own documents carry no extension
- [x] 3.4 Move the VB6 extensions into the bundled entry's declaration
- [x] 3.5 Drop the Markdown extensions from the built-in table: nothing shipped claims them, and an
      extension should map to a language because an attached server says so. **The whole table went**, not
      just Markdown — once extensions are a server's claim there is nothing left for a global one to say

## 4. Ordering and workspace

- [x] 4.1 Default entries rank below an entry that states no priority, so a user's server wins the
      pick-one features without them discovering the field
- [x] 4.2 Start each server in the project's working directory and send that as the workspace root
- [x] 4.3 Restart running servers when the workspace moves. **The earlier reasoning for deferring this was
      wrong** and is recorded here rather than quietly corrected: it claimed first save was the only thing
      that changes a project's working directory, so #260 would remove the need. Closing one project and
      opening another changes it too — ordinary, permanent, and nothing to do with #260. Checked when a
      document opens rather than driven by a project event, since that is the moment the answer matters and
      it keeps this layer free of the project model
- [ ] 4.4 A project GROUP has several workspaces at once and gets only one. Filed as #261 — it needs
      `workspaceFolders`, a capability handshake and a "which project owns this document" answer that
      routing does not currently have. Out of scope here; #255 ships correct for the single-project case

## 5. Not silent on first sight

- [x] 5.1 Record the command last seen for each entry
- [x] 5.2 Surface a command not previously seen for that entry before starting it — **announced, not
      gated, and the difference matters.** The entry is reported and still runs. Refusing to run what
      someone typed into their own file would be theatre, and a gate needs somewhere to acknowledge it,
      which is UI that #255 deliberately does not ship (Q9). Today the notice reaches a person only through
      the log; the problem is carried on the result for #259 to render. **#255 is not complete on the trust
      requirement until #259 lands** — recorded here rather than left to be discovered from a green tick
- [x] 5.3 No signing, no consent store, no revocation — see the proposal for why those are disproportionate

## 6. Wire it up — the step the plan was missing

Sections 1-5 built the record, the loader, routing, ordering and the trust store, and connected none of
them: `DISetup` still hand-builds the bundled registration, and dropping a `lsp-servers.json` on disk does
nothing at all. Sections 7 and 8 below both silently assumed this existed — 7.2 cannot be written without
it. Recorded as its own section rather than smuggled into "tests", because that is how a step nobody owns
ends up half-done.

- [x] 6.1 A factory turning entries into registrations, with the transport each one names. Its own class in
      `HexIDE.Lsp`, not inline in `DISetup` — it needs tests, and a wiring table is a poor place for logic
- [x] 6.2 The bundled VB6 server becomes a default ENTRY rather than a hand-built registration. Its command
      comes from the locator, and an entry whose command cannot be resolved is not contributed
- [x] 6.3 `DISetup` builds defaults, loads the user file over them, and hands the result to the factory
- [x] 6.4 Read once, at DI construction. Changes take effect on restart (section 5), so resolving later
      buys nothing and makes "when did this take effect" ambiguous
- [x] 6.5 Problems reach `ILanguageConnectionRegistry` alongside `Connections` — that interface is already
      "what is attached and is it working", and a rejected entry is precisely something that is not
      attached and the reason why. Logged at warning too, since that is the only channel a person has until
      #259
- [x] 6.6 A configuration leaving zero usable servers starts normally with language features absent, and
      says so as a problem. "Zero servers" and "servers fine, nothing to say" are otherwise
      indistinguishable — the confusion #231 documents
- [x] 6.7 **Remove the env-var transport selection** (`HEXIDE_LSP_WS_URL`, `HEXIDE_LSP_PIPE`,
      `HEXIDE_LSP_PIPE_ROLE`). They are a second mechanism doing the config file's job, and the method that
      reads them is the one being replaced. Decided deliberately over keeping them: approximately nobody
      is using this project yet, so there is no compatibility to protect, and carrying two answers forever
      to spare users who do not exist is the worse trade. `VB6_LSP_DEBUG_PROXY` stays — it wraps whatever
      command is chosen rather than choosing one

## 7. Delete what the config file supersedes

Separable from 6, and only safe once 6 works. Kept apart because this is the part that touches shipped
language packs.

- [ ] 7.1 Remove the `LspWebSocketUrl` setting, its Options page and view, its `ViewLocator` registration
      and its node in the options tree
- [ ] 7.2 Remove its localized key from the canonical pack and every shipped pack — an unused canonical key
      fails the build, so this is not optional
- [ ] 7.3 Remove the tests that covered it

## 8. Tests

- [ ] 8.1 Defaults alone, no user file — language features work exactly as shipped
- [ ] 8.2 A user entry overrides the bundled server by id
- [ ] 8.3 An entry marked not enabled creates no client
- [ ] 8.4 Two entries claiming one extension under different identifiers: both receive the document, each
      told its own identifier
- [ ] 8.5 A malformed entry beside a good one: the good one still applies
- [ ] 8.6 A missing required field rejects that entry; an unrecognised field keeps it and reports
- [ ] 8.7 A user entry outranks a default for a pick-one feature
- [ ] 8.8 Deleting the user file restores defaults
- [ ] 8.9 **Each of the above verified to fail without its fix**, not merely to pass with it — the
      foreign-server work found three defects that every green test had missed

## 9. Verification

- [ ] 9.1 Suites green on Windows and under WSL — this touches path handling and file discovery, which is
      the bug class only the Linux job catches
- [x] 9.2 Driven against a real foreign server attached **through the configuration file**, not through a
      test-constructed registration. Done: rumdl 0.2.64, named only in a `lsp-servers.json` on disk,
      produces real diagnostics for a real document. Skips under WSL — the binary is a Windows executable —
      so this is a Windows-only proof today
- [x] 9.3 Confirm the bundled server still works when the user file is absent, present-but-empty, and
      malformed

## 10. Deliberately not here

- [ ] 10.1 Project-level configuration — needs its own consent design (see the proposal)
- [ ] 10.2 Any UI, including surfacing failures and attached servers (#259) — that is where the
      localization budget should be spent, once, on the whole surface
- [ ] 10.3 Discovering servers already installed on the machine
- [ ] 10.4 An initialize timeout (#231) — independent, and made far more reachable by this change
