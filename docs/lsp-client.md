# HexIDE as an LSP client — what it speaks

This document describes the protocol HexIDE speaks, **independently of who answers**. It applies equally to
the bundled VB6 server and to any server you attach yourself.

Its companion, [`lsp-server-features.md`](./lsp-server-features.md), describes what the *bundled VB6 server*
analyses. The two used to be one document, and conflating them hid a real distinction: what the client can
consume is bounded by the LSP specification, while what the bundled server can produce is bounded by
HexIDE's CST-not-AST limit. Those bounds are different, and a feature can be perfectly reasonable on one
side of the seam and forbidden on the other.

For *configuring* a server, see [`language-servers.md`](./language-servers.md). For the third-party servers
the test suite drives, see [`foreign-language-servers.md`](./foreign-language-servers.md).

---

## The design claim this document is accountable to

> The backend is replaceable. Any conformant language server should work, and where one does not, that is a
> defect in this client.

That claim is only worth as much as its evidence. HexIDE's client and HexIDE's server were written by the
same hand, so their agreeing with each other proves nothing about the specification — three defects hid in
exactly that gap until a server nobody here had written was pointed at the client. The suite therefore
drives three foreign servers on three different LSP frameworks, including `vscode-languageserver-node`, the
library the specification is written around.

---

## The wire contract

**Sent by the client:**

| Phase | Methods |
|---|---|
| Lifecycle | `initialize`, `initialized`, `shutdown`, `exit` |
| Document sync | `textDocument/didOpen`, `didChange`, `didClose`, `didSave` |
| Language requests | `hover`, `documentSymbol`, `foldingRange`, `completion`, `signatureHelp`, `definition`, `declaration`, `documentHighlight`, `rename`, `formatting`, `codeLens` |
| Actions | `codeLens/resolve`, `workspace/executeCommand` — see *Commands and lenses* |
| Custom | `vb/builtinSymbols` — see *Custom methods* |

**Consumed from the server:** `textDocument/publishDiagnostics`, plus `window/logMessage` and
`window/showMessage` — the channel a server uses to talk about *itself* rather than about a document.
The first goes to the log at the severity the server declared; the second goes there **and** to the
status bar, because a message the user never sees was the bug and a message with no trace afterwards
is the next one.

**Server-initiated requests.** The client registers no handlers for any of them, so what a server gets back
is whatever StreamJsonRpc answers an unknown method with — an error response rather than silence, on the
library's own account. **This has no test here**, and it matters: a server awaiting a reply it never gets
hangs rather than degrading, and the difference is invisible until you drive a server that asks. Treat the
table below as describing the library's documented behaviour on those rows, not a measurement taken in this
repository.

---

## Capability negotiation

The client reads the server's `initialize` result and gates on it. Nothing is called unconditionally.

**Every capability is read permissively, in both shapes the protocol allows.** Most are
`boolean | XxxOptions`, and a conformant server may send either. An options object counts as *enabled* — a
server returning options is describing *how* it supports a feature, which necessarily means it does. Only an
absent field or an explicit `false` means unsupported.

This is not fussiness. Modelling one capability narrowly (`bool?` rather than a raw element) threw during
`initialize`, and a swallowing catch turned that into a **total silent LSP blackout** — no diagnostics, no
completion, no error visible anywhere ([#238](https://github.com/hexide-io/HexIDE/issues/238)). The cost of
being permissive is one helper call per use site. The cost of being precise is that a conformant backend can
silently disable every language feature by answering in its other legal shape, which is the opposite of what
a replaceable-backend seam is for.

`textDocumentSync` is the exception that needs its own reader: it is the one field whose two shapes mean
*different things* rather than the same thing said twice. A bare number is the sync kind; an object carries
that kind under `change`. Kind `0` means "send me nothing", so presence alone cannot be read as consent.

---

## Document synchronization

**Full text only.** Every `didChange` carries the whole document.

The client reads a server's declared sync kind well enough to know whether to send changes at all, but does
**not** honour an incremental declaration — a server asking for incremental updates receives full ones
anyway. That is legal, since full text is always a valid superset, but it is wasteful on large files and
leaves the incremental path with no coverage at all. Tracked as
[#282](https://github.com/hexide-io/HexIDE/issues/282); texlab is the server that surfaced it.

**`didClose` publishes an empty diagnostic set, and this is a hard requirement.** Three consumers — the
editor's marker service, `AddinDiagnosticsService`, and the MCP `DiagnosticsCache` — depend on that empty
publish to evict stale entries. A server that closes a document silently leaves squiggles behind.

**On reconnect, tracked documents are replayed** through the same code path as a first open, carrying the
version this connection has been tracking rather than `1`. Resetting to `1` would put the client's count
behind the session's, so the next change would arrive bearing a version the server had already seen.

That replay used to be a *second* implementation, and it drifted: it hardcoded the language identifier to
`vb6` and skipped the capability gate, so after any reconnect an attached foreign server was told every one
of its documents was Visual Basic — the exact global answer a per-server identifier exists to prevent,
reintroduced in the one path nobody looked at ([#272](https://github.com/hexide-io/HexIDE/issues/272)). The
symptom was language features that worked, silently stopped, and never came back. There is now one method.

### Save notifications

`didSave` is sent **only when negotiated**, in whichever of three modes the server asked for: not at all,
without text, or with the document's text attached. Sending it unasked would be harmless on the wire and
wrong in principle — the server said how it wants this, and overriding that makes the client unpredictable
to its author.

`includeText: false` does **not** imply "the server will read the file from disk". That was measured and
falsified; it means only that the notification carries no text.

---

## Routing: which server gets which document

Two questions the protocol keeps separate, and so does HexIDE:

- **`extensions`** — which files a server *serves*. This is what routing reads.
- **`languageId`** — what that server wants those files *called* on the wire.

**HexIDE holds no global opinion about what a file is.** The identifier sent for a document is the one the
*receiving server* declared, not a project-wide constant. That is why the same file can be offered to two
servers under two different names, and why a server keying its state on the identifier stays coherent.

The `.cls` collision is the worked example: a `.cls` is a VB6 class module *and* a LaTeX class file. A lone
`.cls` claim therefore cannot be read as "serves VB6" — a real LaTeX server claims it too, and routing a VB6
module there would have it parse Visual Basic as LaTeX and report confident nonsense about the developer's
own source. Documents the IDE carries have no extension at all and route by scheme instead.

### Transports

`stdio`, `pipe` (named pipe, connecting or listening) and `websocket` are all supported. Only `stdio` is
exercised against a real foreign server; the other two are covered against fakes.

---

## Commands and lenses

`textDocument/codeLens` is how a server offers an action against a range — the protocol's affordance for
*Run test* above a procedure — and `workspace/executeCommand` is how the client invokes it. They are
implemented as a pair because neither is useful alone: a lens carries a command, and a command with
nothing to trigger it is unreachable.

**A lens is resolved before it is handed on.** The protocol lets a server return the ranges cheaply and
compute each command only when asked, via `codeLens/resolve`. An unresolved lens is one the user can see
and cannot click, so the client resolves its own before answering. That has to happen *inside* the client
rather than at a call site: once lenses from several servers are gathered into one list, nothing records
which connection produced which, and a resolve sent to the wrong server is meaningless.

**A command is routed by who declared it, not by the document.** Every other request here is about a file,
so "which server" is answered by "which one claims this file". A command has no file. The only thing that
says who owns it is the list the server published in `executeCommandProvider.commands`, so that list is
the routing key — and a command no started server declares is not sent anywhere. Guessing would not fail
quietly; it would run something.

**Lenses combine across servers, and that is deliberate.** Two servers listing the same procedure as a
document symbol produce one duplicated dropdown entry; two servers offering a lens on the same line offer
two genuinely different things, and dropping either would hide something the user could have done.

## Custom methods

`vb/builtinSymbols` is HexIDE's own method, not an LSP one. It is gated on the server advertising it under
`experimental` — where the protocol says to put a method it does not define, and therefore the only thing a
client may legitimately gate a custom method on. A server that does not advertise it is never asked.

---

## Coverage against the specification

LSP 3.17 defines **93 messages** — 67 requests and 26 notifications. The table below is generated from the
specification's own [`metaModel.json`](https://raw.githubusercontent.com/microsoft/language-server-protocol/gh-pages/_specifications/lsp/3.17/metaModel/metaModel.json),
the canonical machine-readable list, so the method names and directions are the specification's rather than
this document's recollection of them.

**HexIDE implements 24 of the 93.** That is not a deficiency in itself — no client implements them all, and
most of the remainder are features no VB6 IDE needs. It is here so the shape of the gap is visible rather
than inferred.

**Legend** — ✅ implemented · ◐ partial · ○ not wired. Direction is → client-to-server, ← server-to-client,
↔ either.

> **○ on a ← row is not neutral.** Those are messages a server may *initiate*, and an unhandled one is
> refused or ignored — which looks, from the far side, like a client that does not work. A server that
> registers its capabilities dynamically is still refused — correctly, since this client never claims to
> support it — but now audibly ([#288](https://github.com/hexide-io/HexIDE/issues/288)). A server reporting its own problems is heard
> ([#289](https://github.com/hexide-io/HexIDE/issues/289)).

### `Lifecycle` — 4 of 4

| | Method | Dir | Notes |
|---|---|---|---|
| ✅ | `exit` | → |  |
| ✅ | `initialize` | → |  |
| ✅ | `initialized` | → |  |
| ✅ | `shutdown` | → |  |

### `textDocument/*` — 15 of 41

| | Method | Dir | Notes |
|---|---|---|---|
| ○ | `textDocument/codeAction` | → | Needs a bound AST — a backend's job, see below |
| ✅ | `textDocument/codeLens` | → |  |
| ○ | `textDocument/colorPresentation` | → |  |
| ✅ | `textDocument/completion` | → |  |
| ✅ | `textDocument/declaration` | → | Where a name is declared, which a server may answer differently from `definition` |
| ✅ | `textDocument/definition` | → | All three reply shapes; opens the document a cross-file answer names |
| ○ | `textDocument/diagnostic` | → | The pull model. A server publishing only this way connects and reports nothing ([#284](https://github.com/hexide-io/HexIDE/issues/284)) |
| ◐ | `textDocument/didChange` | → | Full text only; a declared incremental kind is ignored ([#282](https://github.com/hexide-io/HexIDE/issues/282)) |
| ✅ | `textDocument/didClose` | → |  |
| ✅ | `textDocument/didOpen` | → |  |
| ✅ | `textDocument/didSave` | → |  |
| ○ | `textDocument/documentColor` | → |  |
| ✅ | `textDocument/documentHighlight` | → |  |
| ○ | `textDocument/documentLink` | → |  |
| ✅ | `textDocument/documentSymbol` | → |  |
| ✅ | `textDocument/foldingRange` | → |  |
| ✅ | `textDocument/formatting` | → |  |
| ✅ | `textDocument/hover` | → |  |
| ○ | `textDocument/implementation` | → |  |
| ○ | `textDocument/inlayHint` | → | Needs a bound AST — a backend's job, see below |
| ○ | `textDocument/inlineCompletion` | → |  |
| ○ | `textDocument/inlineValue` | → |  |
| ○ | `textDocument/linkedEditingRange` | → |  |
| ○ | `textDocument/moniker` | → |  |
| ○ | `textDocument/onTypeFormatting` | → |  |
| ○ | `textDocument/prepareCallHierarchy` | → | Needs a bound AST — a backend's job, see below |
| ○ | `textDocument/prepareRename` | → | Why the rename dialog never pre-validates the caret position |
| ○ | `textDocument/prepareTypeHierarchy` | → |  |
| ✅ | `textDocument/publishDiagnostics` | ← |  |
| ○ | `textDocument/rangeFormatting` | → |  |
| ○ | `textDocument/rangesFormatting` | → |  |
| ○ | `textDocument/references` | → | Needs a bound AST — a backend's job, see below |
| ✅ | `textDocument/rename` | → |  |
| ○ | `textDocument/selectionRange` | → |  |
| ○ | `textDocument/semanticTokens/full` | → | Needs a bound AST — a backend's job, see below |
| ○ | `textDocument/semanticTokens/full/delta` | → |  |
| ○ | `textDocument/semanticTokens/range` | → |  |
| ✅ | `textDocument/signatureHelp` | → |  |
| ○ | `textDocument/typeDefinition` | → |  |
| ○ | `textDocument/willSave` | → |  |
| ○ | `textDocument/willSaveWaitUntil` | → |  |

### `workspace/*` — 0 of 21

| | Method | Dir | Notes |
|---|---|---|---|
| ○ | `workspace/applyEdit` | ← | A server cannot ask the IDE to edit a document |
| ○ | `workspace/codeLens/refresh` | ← |  |
| ○ | `workspace/configuration` | ← | A server asking for its configuration is refused |
| ○ | `workspace/diagnostic` | → | The pull model, workspace-wide ([#284](https://github.com/hexide-io/HexIDE/issues/284)) |
| ○ | `workspace/diagnostic/refresh` | ← |  |
| ○ | `workspace/didChangeConfiguration` | → |  |
| ○ | `workspace/didChangeWatchedFiles` | → |  |
| ○ | `workspace/didChangeWorkspaceFolders` | → |  |
| ○ | `workspace/didCreateFiles` | → |  |
| ○ | `workspace/didDeleteFiles` | → |  |
| ○ | `workspace/didRenameFiles` | → |  |
| ✅ | `workspace/executeCommand` | → | Routed to the server that declared the command |
| ○ | `workspace/foldingRange/refresh` | ← |  |
| ○ | `workspace/inlayHint/refresh` | ← |  |
| ○ | `workspace/inlineValue/refresh` | ← |  |
| ○ | `workspace/semanticTokens/refresh` | ← |  |
| ○ | `workspace/symbol` | → | Needs a bound AST — a backend's job, see below |
| ○ | `workspace/willCreateFiles` | → |  |
| ○ | `workspace/willDeleteFiles` | → |  |
| ○ | `workspace/willRenameFiles` | → |  |
| ○ | `workspace/workspaceFolders` | ← |  |

### `window/*` — 2 of 6

| | Method | Dir | Notes |
|---|---|---|---|
| ✅ | `window/logMessage` | ← | Written to the log at the severity the server declared |
| ○ | `window/showDocument` | ← |  |
| ✅ | `window/showMessage` | ← | Shown in the status bar, and logged |
| ○ | `window/showMessageRequest` | ← |  |
| ○ | `window/workDoneProgress/cancel` | → |  |
| ○ | `window/workDoneProgress/create` | ← |  |

### `client/*` — 0 of 2

| | Method | Dir | Notes |
|---|---|---|---|
| ○ | `client/registerCapability` | ← | **Refused deliberately, and logged.** `dynamicRegistration` is a *client* capability and this client declares none, so a conformant server must declare everything at `initialize`. One that asks anyway gets `MethodNotFound` and a local warning naming the method ([#288](https://github.com/hexide-io/HexIDE/issues/288)) |
| ○ | `client/unregisterCapability` | ← | Counterpart to the above, refused the same way |

### `$/*` — 0 of 4

| | Method | Dir | Notes |
|---|---|---|---|
| ○ | `$/cancelRequest` | ↔ | Tokens are passed to StreamJsonRpc, but **cancelling one does not unblock the client's await**: it notifies and then waits for the server to acknowledge. Measured while fixing [#231](https://github.com/hexide-io/HexIDE/issues/231) |
| ○ | `$/logTrace` | ← |  |
| ○ | `$/progress` | ↔ |  |
| ○ | `$/setTrace` | → |  |

### `notebookDocument/*` — 0 of 4

| | Method | Dir | Notes |
|---|---|---|---|
| ○ | `notebookDocument/didChange` | → |  |
| ○ | `notebookDocument/didClose` | → |  |
| ○ | `notebookDocument/didOpen` | → |  |
| ○ | `notebookDocument/didSave` | → |  |

### `completionItem/*` — 0 of 1

| | Method | Dir | Notes |
|---|---|---|---|
| ○ | `completionItem/resolve` | → |  |

### `codeAction/*` — 0 of 1

| | Method | Dir | Notes |
|---|---|---|---|
| ○ | `codeAction/resolve` | → |  |

### `codeLens/*` — 0 of 1

| | Method | Dir | Notes |
|---|---|---|---|
| ✅ | `codeLens/resolve` | → | Resolved by the issuing client before a lens is handed on |

### `documentLink/*` — 0 of 1

| | Method | Dir | Notes |
|---|---|---|---|
| ○ | `documentLink/resolve` | → |  |

### `inlayHint/*` — 0 of 1

| | Method | Dir | Notes |
|---|---|---|---|
| ○ | `inlayHint/resolve` | → |  |

### `callHierarchy/*` — 0 of 2

| | Method | Dir | Notes |
|---|---|---|---|
| ○ | `callHierarchy/incomingCalls` | → |  |
| ○ | `callHierarchy/outgoingCalls` | → |  |

### `typeHierarchy/*` — 0 of 2

| | Method | Dir | Notes |
|---|---|---|---|
| ○ | `typeHierarchy/subtypes` | → |  |
| ○ | `typeHierarchy/supertypes` | → |  |

### `workspaceSymbol/*` — 0 of 1

| | Method | Dir | Notes |
|---|---|---|---|
| ○ | `workspaceSymbol/resolve` | → |  |

### `telemetry/*` — 0 of 1

| | Method | Dir | Notes |
|---|---|---|---|
| ○ | `telemetry/event` | ← |  |

### This table is checked, not merely written

`ProtocolCoverageDocTests` fails the build when the table drifts from the model: a message missing, a method
name that no version of the protocol defines, a row listed twice, a direction that disagrees with the
specification, or a headline count that no longer matches the rows beneath it. All five were confirmed to
fail by mutating this file, which is the only way to know a guard has teeth.

The model is fetched once at test time into the gitignored `artifacts/lsp-metamodel/`, pinned to the commit
that last touched it — not a branch, so its bytes cannot change underneath the digest — and verified by
SHA-256 before it is read. It is never committed and never shipped. Set `HEXIDE_LSP_METAMODEL` to a local
copy to work offline; the tests otherwise skip visibly, except where `HEXIDE_REQUIRE_FOREIGN_LSP=1` forbids
that, which is CI.

**Regenerate rather than hand-edit.** Adding a row by hand is how the counts and the directions drift apart
in the first place; the guard will catch it, but the guard is a backstop, not a workflow.

### What the shape of that table says

Three clusters account for nearly all of the gap:

- **Features needing a bound AST** — references, code actions, semantic tokens, inlay hints, call hierarchy,
  workspace symbols and their `*/resolve` companions. Not wired because nothing here would answer them; see
  *What the client would consume* below.
- **Workspace-level protocol** — `workspace/*` is 0 of 21. HexIDE has no workspace model, which also rules
  out file-operation notifications, watched files and configuration round-trips.
- **Server-to-client courtesy** — `window/*` and `client/*` are 2 of 8 between them. The two that landed
  are the ones that cost no analysis depth and buy diagnosability: a server can now explain its own
  problems instead of appearing broken ([#289](https://github.com/hexide-io/HexIDE/issues/289)). Dynamic registration stays refused on
  purpose and is now logged rather than silent ([#288](https://github.com/hexide-io/HexIDE/issues/288)) — supporting it would mean
  declaring `dynamicRegistration` and honouring what arrives, which is real work with no server yet asking
  for it. `showMessageRequest` and `showDocument` need UI decisions and are lower value.

`notebookDocument/*` is 0 of 4 and will stay there — notebooks are not a VB6 concept.

---

## Known client limitations

| Limitation | Consequence | Tracked |
|---|---|---|
| Full-text sync only; a declared incremental kind is ignored | Wasteful on large files; the incremental path is untested | [#282](https://github.com/hexide-io/HexIDE/issues/282) |
| No pull-model diagnostics (`textDocument/diagnostic`) | A server publishing only by the pull model appears to connect and reports nothing — silent, not an error | [#284](https://github.com/hexide-io/HexIDE/issues/284) |
| `lsp-servers.json` resolved through a folder API that returns nothing on Unix | Config location is unreliable on Linux/macOS unless `XDG_CONFIG_HOME` is set | [#280](https://github.com/hexide-io/HexIDE/issues/280) |

---

## What the client would consume, given a server that offers it

These are ordinary LSP features that HexIDE's **own** server will never implement. They require a bound AST,
which is outside its hard limit and belongs to a real language engine delivered over this seam. They are
listed here rather than as a roadmap because the distinction is the point: they are not HexIDE features
awaiting effort, they are **server** features awaiting a server.

| Feature | Method | Client support today |
|---|---|---|
| Find all references | `textDocument/references` | Not wired |
| Code actions / quick fixes | `textDocument/codeAction` | Not wired |
| Semantic tokens | `textDocument/semanticTokens` | Not wired |
| Inlay hints | `textDocument/inlayHint` | Not wired |
| Call hierarchy | `textDocument/prepareCallHierarchy` | Not wired |
| Workspace symbols | `workspace/symbol` | Not wired |

"Not wired" is a statement about the client, and it is the honest one: wiring each is a bounded piece of
client work with no architectural obstacle, worth doing when a backend exists that would answer. What would
*not* be honest is carrying them here as planned analysis work.

---

*The behaviour contracts live under [`openspec/specs/lsp-client/`](../openspec/specs/lsp-client/spec.md).*
