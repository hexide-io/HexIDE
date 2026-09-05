# Tasks

## 1. The wire

- [x] 1.1 `DidSaveTextDocumentParams` in `HexIDE.Core/Lsp/Messages/LspMessages.cs`, taking the
      **unversioned** `TextDocumentIdentifier` — a save changes no version
- [x] 1.2 `text` omitted with a **per-property** `JsonIgnoreCondition.WhenWritingNull`, never a global
      default. The options ignore nothing today, so an absent text would serialize as `"text":null`, and a
      server testing for the field's presence takes the has-text branch with a null body. A global setting
      would change the wire shape of every existing outbound type, including a root URI that is
      legitimately nullable and that nothing pins
- [x] 1.3 `[JsonSerializable(typeof(DidSaveTextDocumentParams))]` in `LspJsonContext`, with a comment
      saying it is load-bearing. The client's serializer options come from that generated context and its
      resolver ends in `return null`, so an unregistered type throws — **under plain JIT, not only under
      AOT as the recipe in CLAUDE.md says**. The throw lands in a debug-level catch, reproducing this
      issue's exact symptom: a server that connects, initializes and answers nothing.
      Verified by deleting it: four of the five wire-shape tests fail. The fifth — the one asserting the
      registration *directly* — passed, because `LspJsonContext.Default.GetTypeInfo` answers for an
      unregistered type too. Rewritten to serialize through the options `VBLspClient` actually builds,
      which does fail
- [x] 1.4 Correct that AOT wording in CLAUDE.md, and the file path in the same recipe step, which names a
      directory that holds no message records

## 2. Negotiation

- [x] 2.1 `TextDocumentSyncClientCapabilities` with `didSave`, declared in the client's `initialize` —
      and asserted **from the far end of a real connection**, not by serializing the record. Building the
      capabilities in a test and checking they serialize proves the record works, which was never in
      doubt, and stays green if the client never sends them
- [x] 2.2 One reader returning three states — none, without text, with text — rather than two independent
      predicates a caller can check one of. The neighbouring comment already says why: a capability read
      one way for display and another for gating is how a feature ends up shown as available and refused
- [x] 2.3 A bare non-zero `textDocumentSync` number counts as asking for saves without text, matching the
      reference implementation and the neighbouring `AcceptsOpenClose`. Commented as a deliberate
      divergence from a strict reading of the specification
- [x] 2.4 An object form with no `save` means no save notifications — **the opposite polarity to its two
      neighbours**, and commented as deliberate, or the three will be harmonised later by someone reading
      them as a mistake

## 3. The client

- [x] 3.1 `SaveDocumentAsync(uri, ct)` on `ILspClient` — no text parameter, because the connection owns
      the negotiation and already tracks the text
- [x] 3.2 Implemented on the connection, copying the shape of the existing close notification: the same
      guards, the same notify method, the same swallowing catch — minus its tracked-document write, since
      a save changes neither text nor version and so has nothing to record
- [x] 3.3 Implemented on the router by fanning out to started claimants, exactly as a change does — and
      **not** starting a server, since saving a document nothing has opened is no reason to launch one
- [x] 3.4 No state filter beyond what the siblings use. Failed connections are already dispatched to and
      no-op internally; making this one notification behave differently would be an inconsistency with no
      reason behind it

## 4. Saving

- [x] 4.1 `NotifySavedAsync` on the document session: flush, then announce. The flush is not padding — it
      cancels the pending debounce, so without it a save is announced against text the server has never
      been given
- [x] 4.2 A `DocumentSavedEvent` published from the project service's form and module save paths, on the
      success return only — a refused save is the opposite of a save
- [x] 4.3 Suppressed when writing a copy elsewhere: building an executable writes every document to a
      temporary location through the same code and then restores the model. The save core is `internal`
      so a test can reach the flag — Make EXE itself refuses without a published standalone runtime, so
      without that the one exclusion in "which writes count" would have had no test at all, which
      mutation testing confirmed
- [x] 4.4 Both editors subscribe and announce their own document. **The de-duplication turned out to be
      unnecessary, and mutation testing is what established that.** A UserControl does set both definition
      fields, but one save publishes one event, the handler runs once, and the session's URI is fixed — so
      matching either half and preferring the module give identical results for every reachable input.
      The handler now matches either half and says plainly that it guards nothing, rather than carrying a
      comment claiming a protection it does not provide
- [x] 4.5 The carried-file editor announces from its own save path directly — nothing else writes a
      carried file

## 5. Tests

- [x] 5.1 **Over a real JSON-RPC pair, not a substitute.** A mocked client synthesises the method and
      returns a completed task, so an assertion that it was called is green with the notification never
      leaving the process and the serializer entry missing. The existing recording-server harness is the
      pattern
- [x] 5.2 The whole gating table, driven by a stub server's advertised capabilities: a bare number, an
      object without `save`, `save: false`, `save: true`, an empty save object, `includeText: true`, and
      an empty capabilities object
- [x] 5.3 For every no-text case, assert the `text` property is **absent**, not null. That is the only
      assertion that catches the serialization trap
- [x] 5.4 A pending edit is sent before the save is announced. **This was ticked a section early and had
      no test**, which is worth recording rather than quietly fixing: it is the session's most important
      behaviour in section 4 and it was claimed without being checked
- [x] 5.5 Routing: two servers claiming one document where only one asked for saves — both real
      connections, which is what proves the gate belongs on the connection rather than the router
- [x] 5.6 Saving a project with open documents announces each of them; building an executable announces
      none
- [x] 5.7 The bundled server's deliberate absence of `save` is pinned, so it cannot be lost silently and
      flips to a positive assertion the day that decision is revisited
- [x] 5.8 Each verified to fail without its fix, by mutation — twelve defects across the wire, the
      negotiation, the client, the router and both save paths.

      Ten were caught by tests. Two were caught by the **compiler**: ignoring the negotiation gate leaves
      unreachable code, and an earlier attempt at a version-counter mutation left a field never assigned —
      both build failures under `TreatWarningsAsErrors`, which is stronger than a test.

      One was caught by nothing: **the carried-file editor's announcement**. Deleting it left all 942
      tests green. Closed at the integration level, because the announcement follows an asynchronous file
      write and a plain unit test has no synchronization context to bring the continuation back to the
      thread that owns the editor buffer — the first attempt to test it there did not fail, it threw
      "call from invalid thread", and an attempt to make the session marshal the read instead produced a
      deadlock against a dispatcher nothing pumps. The production arrangement is sound and is now stated
      in the method's own remarks rather than left implicit

## 6. Verification

- [ ] 6.1 Suites green on Windows and under WSL
- [ ] 6.2 **Against a server we did not write.** The foreign Markdown server advertises
      `save: {includeText: false}` and re-publishes when told of a save — measured, not assumed. Assert
      the **increment** in publications, never an absolute count: it publishes more than once on open
- [ ] 6.3 Assert that server advertises `save` at all. The capability the entire gate depends on is
      currently assumed rather than observed by any test
- [ ] 6.4 Driven against the running IDE, saving a carried Markdown file and seeing the server respond

## 7. Deliberately not here

- [ ] 7.1 `save` on the bundled VB6 server. It defers nothing to save, and advertising what is not
      implemented is the defect its capability file exists to prevent. Its absence is what makes it a
      control for the gate
- [ ] 7.2 `willSave` / `willSaveWaitUntil` — same rule, nothing implements them
- [ ] 7.3 Naming a saved project's documents by their files rather than by a scheme URI, which is what
      would make a save notification useful to a server that reads from disk. A spec-level question
- [ ] 7.4 The reconnect replay announcing every document as VB6 (#272) — a live defect found while
      mapping this one, unrelated to it, and folding it in would make this diff unreviewable
