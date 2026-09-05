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
| Language requests | `hover`, `documentSymbol`, `foldingRange`, `completion`, `signatureHelp`, `definition`, `documentHighlight`, `rename`, `formatting` |
| Custom | `vb/builtinSymbols` — see *Custom methods* |

**Consumed from the server:** `textDocument/publishDiagnostics`.

**Server-initiated requests are answered, not ignored.** A request the client does not implement receives a
JSON-RPC error response. That matters more than it looks: a server awaiting a reply it never gets hangs
rather than degrading, and the difference is invisible until you drive a server that asks.

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

## Custom methods

`vb/builtinSymbols` is HexIDE's own method, not an LSP one. It is gated on the server advertising it under
`experimental` — where the protocol says to put a method it does not define, and therefore the only thing a
client may legitimately gate a custom method on. A server that does not advertise it is never asked.

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
