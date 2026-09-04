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

- [ ] 2.1 A record for one entry: id, display name, extensions, language id, transport + its parameters,
      priority, enabled
- [ ] 2.2 Source-generated `JsonSerializerContext`, camel-case, explicit property names — this is a
      persisted contract
- [ ] 2.3 Read from the per-user directory alongside the existing settings and consent files
- [ ] 2.4 Allow comments in this one file, and ship a header block saying that deleting the file restores
      defaults. A file a user hand-edits and can lock themselves out of should say so in itself
- [ ] 2.5 Merge over compiled-in defaults by id; absent file means defaults
- [ ] 2.6 Reject an entry missing what is required to reach a server; keep and report an entry carrying an
      unrecognised field
- [ ] 2.7 One bad entry never affects another, and never prevents startup

## 3. Routing by extension

- [ ] 3.1 Key routing on extension rather than a global extension-to-language table
- [ ] 3.2 Tell each server the language identifier **it** declared, on `didOpen` and everywhere else the
      identifier travels
- [ ] 3.3 Keep scheme-first precedence — the IDE's own documents carry no extension
- [ ] 3.4 Move the VB6 extensions into the bundled entry's declaration
- [ ] 3.5 Drop the Markdown extensions from the built-in table: nothing shipped claims them, and an
      extension should map to a language because an attached server says so

## 4. Ordering and workspace

- [ ] 4.1 Default entries rank below an entry that states no priority, so a user's server wins the
      pick-one features without them discovering the field
- [ ] 4.2 Start each server in the project's working directory and send that as the workspace root
- [ ] 4.3 Restart running servers when the project's working directory changes — which is what first save
      does today (#260)

## 5. Not silent on first sight

- [ ] 5.1 Record the command last seen for each entry
- [ ] 5.2 Surface a command not previously seen for that entry before starting it
- [ ] 5.3 No signing, no consent store, no revocation — see the proposal for why those are disproportionate

## 6. Tests

- [ ] 6.1 Defaults alone, no user file — language features work exactly as shipped
- [ ] 6.2 A user entry overrides the bundled server by id
- [ ] 6.3 An entry marked not enabled creates no client
- [ ] 6.4 Two entries claiming one extension under different identifiers: both receive the document, each
      told its own identifier
- [ ] 6.5 A malformed entry beside a good one: the good one still applies
- [ ] 6.6 A missing required field rejects that entry; an unrecognised field keeps it and reports
- [ ] 6.7 A user entry outranks a default for a pick-one feature
- [ ] 6.8 Deleting the user file restores defaults
- [ ] 6.9 **Each of the above verified to fail without its fix**, not merely to pass with it — the
      foreign-server work found three defects that every green test had missed

## 7. Verification

- [ ] 7.1 Suites green on Windows and under WSL — this touches path handling and file discovery, which is
      the bug class only the Linux job catches
- [ ] 7.2 Driven against a real foreign server attached **through the configuration file**, not through a
      test-constructed registration. This is the point of the change: the shipping path is the one that has
      never been exercised
- [ ] 7.3 Confirm the bundled server still works when the user file is absent, present-but-empty, and
      malformed

## 8. Deliberately not here

- [ ] 8.1 Project-level configuration — needs its own consent design (see the proposal)
- [ ] 8.2 Any UI, including surfacing failures and attached servers (#259) — that is where the
      localization budget should be spent, once, on the whole surface
- [ ] 8.3 Discovering servers already installed on the machine
- [ ] 8.4 An initialize timeout (#231) — independent, and made far more reachable by this change
