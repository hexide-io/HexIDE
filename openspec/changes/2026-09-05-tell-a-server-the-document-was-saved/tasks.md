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

- [ ] 2.1 `TextDocumentSyncClientCapabilities` with `didSave`, declared in the client's `initialize`
- [ ] 2.2 One reader returning three states — none, without text, with text — rather than two independent
      predicates a caller can check one of. The neighbouring comment already says why: a capability read
      one way for display and another for gating is how a feature ends up shown as available and refused
- [ ] 2.3 A bare non-zero `textDocumentSync` number counts as asking for saves without text, matching the
      reference implementation and the neighbouring `AcceptsOpenClose`. Commented as a deliberate
      divergence from a strict reading of the specification
- [ ] 2.4 An object form with no `save` means no save notifications — **the opposite polarity to its two
      neighbours**, and commented as deliberate, or the three will be harmonised later by someone reading
      them as a mistake

## 3. The client

- [ ] 3.1 `SaveDocumentAsync(uri, ct)` on `ILspClient` — no text parameter, because the connection owns
      the negotiation and already tracks the text
- [ ] 3.2 Implemented on the connection, copying the shape of the existing close notification: the same
      guards, the same notify method, the same swallowing catch
- [ ] 3.3 Implemented on the router by fanning out to started claimants, exactly as a change does — and
      **not** starting a server, since saving a document nothing has opened is no reason to launch one
- [ ] 3.4 No state filter beyond what the siblings use. Failed connections are already dispatched to and
      no-op internally; making this one notification behave differently would be an inconsistency with no
      reason behind it

## 4. Saving

- [ ] 4.1 `NotifySavedAsync` on the document session: flush, then announce. The flush is not padding — it
      cancels the pending debounce, so without it a save is announced against text the server has never
      been given
- [ ] 4.2 A `DocumentSavedEvent` published from the project service's form and module save paths, on the
      success return only — a refused save is the opposite of a save
- [ ] 4.3 Suppressed when writing a copy elsewhere: building an executable writes every document to a
      temporary location through the same code and then restores the model
- [ ] 4.4 Both editors subscribe and announce their own document. The code editor must de-duplicate: a
      UserControl or PropertyPage sets both its definition fields, so a naive match fires twice for one
      document
- [ ] 4.5 The carried-file editor announces from its own save path directly — nothing else writes a
      carried file

## 5. Tests

- [ ] 5.1 **Over a real JSON-RPC pair, not a substitute.** A mocked client synthesises the method and
      returns a completed task, so an assertion that it was called is green with the notification never
      leaving the process and the serializer entry missing. The existing recording-server harness is the
      pattern
- [ ] 5.2 The whole gating table, driven by a stub server's advertised capabilities: a bare number, an
      object without `save`, `save: false`, `save: true`, an empty save object, `includeText: true`, and
      an empty capabilities object
- [ ] 5.3 For every no-text case, assert the `text` property is **absent**, not null. That is the only
      assertion that catches the serialization trap
- [ ] 5.4 A pending edit is sent before the save is announced
- [ ] 5.5 Routing: two servers claiming one document where only one asked for saves
- [ ] 5.6 Saving a project with open documents announces each of them; building an executable announces
      none
- [ ] 5.7 The bundled server's deliberate absence of `save` is pinned, so it cannot be lost silently and
      flips to a positive assertion the day that decision is revisited
- [ ] 5.8 Each verified to fail without its fix, by mutation

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
