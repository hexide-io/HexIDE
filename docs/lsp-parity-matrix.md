# LSP Server Swap — Parity Matrix & Sprint Scope (2026-07-22)

> **Update — swap COMPLETE (2026-07-26).** The MIT server shipped; the GPLv3 server, the Rubberduck
> grammar, and `LspServer/COPYING` are deleted and the tree is 100% MIT. This document is the *pre-swap
> scoping analysis* — its GPLv3 references describe the server that was replaced, kept as the record.

> Scope evidence for replacing `LspServer/HexIDE.VbLspServer` (GPLv3, Rubberduck grammar) with an MIT
> server (proleap grammar + EmmyLua.LanguageServer.Framework shell) before public launch.
> Produced from a verified multi-agent inventory of server surface, client consumption, the analysis
> engine, proleap readiness, and docs; every claim was adversarially re-checked against code.
> Companion doc: [`LSP_FEATURES.md`](./LSP_FEATURES.md). The transport analysis behind the client seam is a
> maintainers' document; its outcome is the [`lsp-client`](../openspec/specs/lsp-client/spec.md) spec.

## Headline

- **The wire contract is a perfect 1:1**: the client sends exactly the 17 methods the server handles,
  and consumes exactly one server-initiated message (`publishDiagnostics`). Nothing served is unused;
  nothing consumed is unserved. The whole contract is enumerable — see the matrix.
- **The grammar-coupled port is ~540 LOC across three files**, not the server. Everything else is
  static tables, regex code, and text-based handlers that don't touch the parse tree.
- **The five biggest launch risks are integration seams, not code volume** — packaging, ordering,
  publish cadence, eviction, and stale-exe hazards. All listed below with mitigations.

## The wire contract (all methods; every one is client-consumed)

| Method | Server today (evidence: `LspServer.cs`) | Client dependency / quirk | Port cost |
|---|---|---|---|
| `initialize` | Ignores client capabilities; returns fixed 10-capability set (`:761-779`) | Response deserialized, **never consulted** — client calls everything unconditionally | Trivial (EmmyLua initialize handler) |
| `initialized` | No-op | Sent with **`{}` params, not omitted** — must tolerate | Trivial |
| `shutdown` / `exit` | `result:null`; `exit` → `Environment.Exit(0/1)` per spec | `exit` also sent with `{}` params; client kills process after | Trivial |
| `textDocument/didOpen` | Cache source → parse → publish diagnostics (`:125-132`) | URIs are `vb6://form/{Name}` / `vb6://module/{Name}` — opaque, exact-string equality. Replayed on reconnect **with last tracked version, not 1** | Shell trivial; parse = port item P1 |
| `textDocument/didChange` | Takes **last** contentChange as full text (`:134-141`) | Full-text only (client debounces 300 ms); `FlushDocumentAsync` forces one immediately before signatureHelp/definition/rename/formatting/completion | Shell trivial |
| `textDocument/didClose` | Evicts caches + **publishes empty diagnostics array** (`:143-158`) | ⚠️ HARD REQUIREMENT — three consumers (editor, `AddinDiagnosticsService`, MCP `DiagnosticsCache`) rely on the empty publish to evict stale entries | Trivial but mandatory |
| `textDocument/publishDiagnostics` (server→client) | Pushed on **every** didOpen/didChange, instantly, no debounce | ⚠️ HARD REQUIREMENT — `RefreshSymbolsAsync` (procedure dropdown, event-stub flow) is piggybacked on this event's arrival. Publish-on-every-change, even when the diagnostic set is unchanged | Trivial but mandatory (`Server.Client.PublishDiagnostics`) |
| `textDocument/hover` | Plaintext `name As Type` from scope analyzer + overlapping diagnostic messages (`:160-214`) | Contents **must be a `MarkupContent` object** — string/MarkedString forms won't deserialize. Gated by `AutoQuickInfo` + manual `TriggerQuickInfo` path | Low (consumes P2's name→type map) |
| `textDocument/documentSymbol` | Flat array from symbol provider; `selectionRange` synthesized as start+name-length (`:241-271`) | **Flat only** — `children` ignored by STJ; `SymbolInformation` shape won't bind. Requested after every publishDiagnostics + by Object Browser | Port item P3 |
| `textDocument/foldingRange` | Cached regex line-scanner results (`:216-239`) | Also requested once on editor attach (before any didChange lands) — must tolerate unknown URI with empty array | Ports as-is (regex, no grammar) |
| `textDocument/completion` | Position-insensitive: all keywords + declared names (`:273-316`) | Response must be the **`CompletionList` envelope** (bare array won't bind). Items: label/kind/detail/insertText only (kind deserialized, unused) | Low (consumes P2) |
| `textDocument/signatureHelp` | Backward scan for unclosed `(` + comma count; ~89-entry `VbSignatures` table (`:318-366`, count verified) | Client flushes didChange first → **server must apply didChange before the next request** (see Ordering) | Ports as-is (text scan + static table) |
| `textDocument/definition` | Same-document, case-insensitive symbol match; synthesized name-length range (`:436-485`) | Response **must be `Location[]`** — single object or LocationLink[] throws, swallowed as null. Triggers: Shift+F2, context menu, hardcoded F12 | Low (consumes P3's symbol list) |
| `textDocument/documentHighlight` | Textual whole-word occurrences (`:487-510`) | Matches inside strings/comments today (known wart, acceptable to reproduce) | Ports as-is |
| `textDocument/rename` | Keyword-refusal (null) + whole-word textual replace → `WorkspaceEdit{changes}` (`:512-568`) | Client consumes `.Changes` **only** (`documentChanges` unsupported). Same strings/comments wart | Ports as-is |
| `textDocument/formatting` | Regex indent + keyword-casing (~123 entries, verified); one whole-document TextEdit (`:570-607`) | Also triggered by **format-on-save** (`SaveWithFormattingAsync`), not just Shift+Alt+F → see Ordering risk | Ports as-is |
| `vb/builtinSymbols` (custom) | Full `VbSignatures` dump: `{name, signature, documentation}[]` (`:609-622`) | ⚠️ **Custom non-spec method the Object Browser depends on** — must exist in the new server (EmmyLua supports custom handlers) | Ports as-is (static table) |

Server behaviors that are *absent* and must stay absent-compatible: it never sends JSON-RPC error
objects, never sends `window/logMessage` (Serilog to file only), never issues server→client requests.
The client ignores every server message except `publishDiagnostics`, so extra messages are harmless —
but error *responses* to its own requests would surface as `RemoteInvocationException` where today it
gets `result:null`. Prefer null results over errors for "nothing found."

## The actual port: three grammar-coupled components (~540 LOC verified)

| # | Component | LOC | What it does | Proleap notes |
|---|---|---|---|---|
| P1 | `VbDiagnosticsProvider` parse pipeline | ~110 | Full reparse per change; ANTLR error listener → severity-1 diagnostics; ERRORCHAR token scan | Proleap's existing listeners **throw on first error** (fail-fast, line-only). Need a trivial collecting `BaseErrorListener` with line+column. `VbErrorMessages` prettifier regexes likely near-identical (proleap is also ANTLR) |
| P2 | `VbScopeAnalyzer` | ~217 | Declaration collector (~12 visit methods, name→type map) + the **single semantic inspection**: Option Explicit undeclared-variable warnings (severity 2, only when zero syntax errors) | Grammar rules exist (`variableStmt`, `subStmt`, …). `PrePass` visitor is NOT reusable (throws NotImplementedException on Function/Property/Type/Enum). Write a fresh purpose-built visitor; `VB6Visitor<T>`'s `ExtractType` helper is reusable |
| P3 | `VbSymbolProvider` | ~112 | Flat symbols from 7 declaration rules (Sub/Function/Property×3/Enum/UDT) | All rules present in proleap's `VB6.g4` (2,234 lines, MIT, in-tree, Antlr4BuildTasks-built — same build integration as the GPL server uses) |

Optimization opportunity while porting: the GPL server parses each document **three times** per
change (diagnostics, symbols, completion). Parse once, share the tree.

**Everything else ports as-is** — `VbFoldingProvider`, `VbFormatter`, `VbErrorMessages`,
`VbKeywords`/`VbSignatures`/`VbBuiltins` tables, and the text-based handler logic — **subject to the
licensing check below.**

## Licensing: what "ports as-is" legally means

The GPL on `LspServer/` stems from the Rubberduck grammar. Code solely authored by us can be
relicensed MIT at will — **the port plan assumes every non-grammar file in `LspServer/` is solely
ours; verify before copying anything.** Known exceptions that must NOT carry over:

1. `Grammar/VBALexer.g4` + `Grammar/VBAParser.g4` (the Rubberduck grammar — the taint source).
2. `RubberduckGrammarTests.cs` — **its test inputs are ported from Rubberduck's GPL test suite**
   (per its own header). The "reuse the server tests" plan must exclude this file's inputs.
3. Generated ANTLR code in `obj/` (derived from the GPL grammar; regenerated from proleap's anyway).

Docs/notices to update at swap time (verified list): `THIRD-PARTY-NOTICES.md` (drop Rubberduck-grammar
rationale, add/confirm proleap + EmmyLua MIT notices), delete `LspServer/COPYING`, README GPL sections
(≥2 locations), `LSP_FEATURES.md` architecture note, the transport analysis (its four upstream "asks" are mooted), `MISSING_FEATURES.md` overstated rows, ROADMAP's GPL §6 pre-launch gate (obsolete),
CLAUDE.md server sections + test counts.

## Launch-risk checklist (integration seams — the part no code inventory shows)

1. **Packaging trap (top risk):** `HexIDE.Desktop.csproj` copies the server via
   `ReferenceOutputAssembly=false` + Content copy — **only the server's own outputs, not its NuGet
   closure**. The GPL server survives this only because it has no unique runtime deps. A
   framework-dependent EmmyLua server will crash at spawn with a missing dll. **Mitigation: publish
   the new server self-contained (ideally `PublishAot=true` — `HexIDE.Standalone` already proves AOT
   works in this repo, and the IDE-side AOT blockers, StreamJsonRpc/add-ins/COM, don't bind the
   server) and wire the copy to the publish output.** Single-file AOT also solves the dll question
   outright and is the EmmyLua-verified path (`JsonProtocolContext` source-gen; the production
   EmmyLua server ships `PublishAot=true`).
2. **Ordering is a hard contract:** the client's `FlushDocumentAsync`-then-request pattern (and
   format-on-save) assumes didChange is fully applied before the next request is served. The GPL
   server is a sequential single-threaded loop. **EmmyLua's default scheduler is
   `SingleThreadScheduler` — keep it; do not opt into concurrent scheduling.**
3. **Publish cadence is a hard contract:** diagnostics on *every* didOpen/didChange (no server-side
   debounce/dedup — symbol refresh rides on it) and the empty-array publish on didClose (three
   cache-eviction consumers).
4. **Exe name + stale-binary hazard:** keep the name `HexIDE.VbLspServer(.exe)` (locator hardcodes
   it, probes ≤4 parent dirs, launches with no args). `PreserveNewest` never deletes: after the swap,
   dev output dirs still contain the old GPL exe — **clean output dirs, and build the launch zip
   from a fresh clone** or the launch artifact ships GPL bits.
5. **Compiler-injection race:** `MakeWithVb6Async` waits 150 ms before launching VB6 while didChange
   debounces at 300 ms; injected compiler errors win today only because the GPL server publishes
   instantly. A slower first parse changes who wins last-write-per-URI. Test the make-path with the
   new server; if flaky, sequence injection after the server's publish for affected URIs.
6. **Test the seam CI never tests:** no workflow runs `IDE/HexIDE.Tests` or the integration tests,
   and no test anywhere spawns the real server through the real client. Add one smoke test to the
   sprint: spawn new server exe via `LspServerLocator`, didOpen a module with an error, assert a
   diagnostic arrives. That single test converts most of risks 1–4 from silent to loud.
7. **EmmyLua onboarding facts:** zero in-repo footprint today; add via `Directory.Packages.props`
   (central pinning, nuget.org-only source); framework TFMs are net8/net9 (net9 asset loads on
   net10 — verify at first build); pin exact version; MIT notice to THIRD-PARTY-NOTICES.
8. **Behavioral spec to port against:** the GPL server's test suites (~256 tests incl.
   `LspServerProtocolTests` 556 LOC) define the contract, and most assertions are grammar-agnostic —
   reusable **except** `RubberduckGrammarTests.cs` inputs (GPL, see above). Note the protocol tests
   construct `LspServer` in-process; against the EmmyLua server they only port if the new server
   exposes a stream-pair entry point (it does: `LSPCommunicationBase(Stream, Stream)`).

## Descope confirmation (things you do NOT need in 10 days)

- No workspace/project model, no cross-file anything (definition is same-document by design).
- No incremental sync, no pull diagnostics, no dynamic registration, no capability negotiation
  (client never reads capabilities).
- No hierarchy in symbols; no semantic rename (textual whole-word matches today's behavior).
- No `window/*` messages; file logging via Serilog to `%LOCALAPPDATA%/HexIDE/logs/lsp/` (50 MB cap,
  best-effort prune) reproduces today's operational contract.
- `.frm` Attribute-line leakage into body buffers (imported VB6-authored forms) is a pre-existing
  wart shared by the interpreter path — not a swap regression; ignore for launch.
