# Pull-model diagnostics

## Why

HexIDE consumes diagnostics only by push. There is no `textDocument/diagnostic` request anywhere in the
client and nothing reads a server's `diagnosticProvider` capability, so **a server that only pulls appears
to do nothing**: it launches, completes the handshake, advertises honestly, and never says a word about the
developer's code. No error, no log line — the same silence as #236, #238, #267 and #277
(hexide-io/HexIDE#284).

It has not surfaced because every server driven so far pushes: HexIDE's own and all four foreign servers in
the suite. The bundled configuration can never reveal it, and that is deliberate — our own server does not
advertise `diagnosticProvider`, because advertising a model it does not implement would be the same defect
pointed the other way.

It surfaces now because a real pull-only server is about to be attached.

## What changes

**A fifth foreign server: `ruff server`.** It earns its place on protocol *shape* rather than on language,
which is the bar `docs/foreign-language-servers.md` sets — and it is the only kind of server that can prove
this path, because a client that never asks gets a loud nothing rather than a quiet fallback. Measured
against 0.16.7 before choosing it:

- it advertises `diagnosticProvider` as an **object**, `{"identifier": "Ruff", "interFileDependencies":
  false, "workDoneProgress": true, "workspaceDiagnostics": false}` — the `boolean | Options` shape that
  caused #238, arriving in its less-common form;
- **it switches model on what the client declares.** Told the client can pull, it publishes nothing at all
  and waits to be asked. Told nothing, it publishes. Both measured, by running it each way;
- it accepts `shutdown` with no `params` member and exits 0, so it is safe in the teardown tests that
  #312 came out of.

That second property is the reason to want it here, and it is worth stating precisely because the first
guess about it was wrong. The expectation going in was "a pull-only server, so a client that never asks
fails loudly". It is not pull-only: it honours the negotiation properly, which means a client that never
asks *and never declares* sees a perfectly working push server and learns nothing. What it will not
tolerate is **half** the negotiation — declare the capability without issuing the request and it goes
silent, because it has been told it does not need to speak first.

So this server does not merely exercise the pull path; it makes the #267 lesson mechanically enforceable.
The two halves cannot ship separately here, because shipping the declaration alone is observably worse than
shipping neither.

It duplicates texlab's `lsp-server` crate rather than adding a fifth framework. That is stated plainly
rather than glossed: the framework-diversity rule is about where interop bugs come from, and this server is
here for a protocol shape instead.

**The client asks.** Read `diagnosticProvider`; declare `textDocument.diagnostic`; request on open and
after change; honour `full` and `unchanged` reports and the result identifiers that make `unchanged`
possible; feed the answers into the one diagnostics channel everything else uses, under the same owner as
push, so nothing downstream can tell the two apart.

## What does not change

- **No `workspace/diagnostic`.** A larger question and its own change, as #284 says. None of the servers
  this is being wired for advertises `workspaceDiagnostics`, so nothing is waiting on it.
- **No `relatedDocuments`.** `relatedDocumentSupport` stays undeclared, which is the honest position for a
  client that would ignore them. Claiming a capability nothing implements is the same defect as failing to
  claim one, pointed the other way — the rule already written into `TextDocumentSyncClientCapabilities`.
- **No new debounce.** `LspDocumentSession` already coalesces edits at 300ms *before* calling
  `ChangeDocumentAsync`, so a pull triggered from inside the client inherits that for free. A second timer
  here would be a second authority over the same question.

## Decisions

**The request is issued by the client, not by a caller.** A pull is not a feature anyone asks for; it is
how this server delivers what a push server delivers unbidden. Putting it on `ILspClient` would make every
caller ask "which kind of server is this", which is precisely the question the seam exists to absorb.

**A server that does not advertise `diagnosticProvider` is not warned about.** Every other gate warns,
because not advertising `hoverProvider` means hover is unavailable. Not advertising `diagnosticProvider`
means *the server pushes* — a correct, complete answer given by four of the five servers in the suite. The
existing warning path would emit a line blaming every conformant push server for a deficiency it does not
have.

**`unchanged` is proved against a scripted server, not against ruff.** Measured: ruff returns no `resultId`
at all, so it never sends an `unchanged` report and cannot exercise that branch. That branch carries the
trap worth guarding — an `unchanged` report has no `items`, so handling it as an empty set *erases the
markers it was meant to preserve* — so it is proved in-process where the server's answer can be dictated.
Recording this because the gap is invisible otherwise: the foreign test would pass while half the feature
went unexercised.
