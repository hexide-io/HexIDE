# The foreign language servers the tests drive

HexIDE's test suite drives real, third-party language servers. This note explains why, which ones, how
they are obtained, and — because one of them is GPL-licensed — exactly why that is consistent with a
100%-MIT tree.

## Why third-party servers at all

A client and a server written by the same hand converge on their shared assumptions rather than on the
specification. They work together, and are wrong in matching ways.

HexIDE's own server once advertised no capabilities while HexIDE's own client called every method
unconditionally. Each was "correct" only because the other was wrong to match. Three defects hid in that
gap and surfaced within hours of pointing the client at a server nobody here had written.

So the value is precisely that the far end does not accommodate us. Writing a second server ourselves
would prove nothing.

## Which ones, and why each earns its place

| Server | Language | Licence | Framework | What it exercises that nothing else does |
|---|---|---|---|---|
| rumdl | Markdown | MIT | `tower-lsp` | The baseline foreign path; publishes on open, change **and save**, which is what proves save notifications reach a server that acts on them. |
| texlab | LaTeX | **GPL-3.0** | `lsp-server` | A different author, licence and release convention — and it claims `.cls`, which is a LaTeX class file *and* a VB6 class module. Declares **incremental** sync, where everything else declares full (#282). The only one that **discards a URI it cannot parse in total silence**, and then never answers a request about it. |
| vscode-json-language-server | JSON | MIT | **`vscode-languageserver-node`** | The **reference implementation**. See below. |
| clangd | C/C++ | Apache-2.0 WITH LLVM-exception | **LLVM's own** | The only server here that answers `textDocument/declaration` **differently** from `definition` — C++ separates a header's declaration from its definition, so the two return different lines and the difference is assertable rather than assumed. Also declares **incremental** sync and a nested `save` inside `textDocumentSync`, a shape the bundled server never sends, and it is the only one that **refuses a non-`file:` URI outright** — and says so, which is what makes a refusal assertable. |
| ruff | Python | MIT | `lsp-server` *(shared with texlab)* | The server that gives a client which never asks **nothing at all**. It is not the only one advertising `diagnosticProvider` — rumdl and the JSON server do too, and rumdl publishes as well — but it is the only one whose silence is total. It also switches model on what the client declares: told nothing it publishes, told the client can ask it publishes **nothing at all** — which makes a half-shipped negotiation fail loudly instead of quietly. |

**Framework diversity matters more than language diversity.** Interop bugs come from the server's LSP
library, not from the language being analysed — five servers on the same crate mostly re-test the same
wire behaviour. The framework column is the one to look at first.

**A sixth should earn its place by exercising a shape none of these does** — a different LSP framework,
or a protocol path nothing here reaches. Count is not the goal.

**ruff is the exception that shows what the rule is for.** It adds no framework — it is on texlab's crate —
and it was added anyway, because the axis that mattered was the delivery *model* rather than the library.
So read the framework column as the usual answer to "what would a new server prove", not the only one.

Its arrival is also the argument for the whole apparatus, in one change. Declaring a single client
capability to satisfy it caused rumdl to send
`{"jsonrpc":"2.0","method":"workspace/diagnostic/refresh","params":null,"id":1}` — invalid JSON-RPC, since
`params` may be an object or an array and nothing else — which HexIDE could not decode and which therefore
killed **the entire connection** rather than that one message. A malformed frame costing every language
feature is HexIDE's defect, not the server's; the server's mistake only revealed it (rumdl's half is
tracked as #428). Nothing written by one hand would have found it.

And it kept giving. Reading the repaired frame turned up two more, each of which had been sitting behind
something that looked like success: the repair was installed **only on connections being recorded**, so a
malformed frame killed an unwatched connection and spared a watched one; and the handler for the refresh
had a required parameter that a conformant server never sends, so the client answered
`-32602` and refused the refresh on a perfectly healthy connection. Both are the same shape — a claim in
the coverage table that nothing had ever driven end to end.

## What they do with a document that has no file (2026-09-20)

hexide-io/HexIDE#273 names a never-saved document `untitled:<Project>/<Name>.<ext>`, which is a scheme
HexIDE had never sent. Whether a server answers about one is a question about other people's code, so all
five were asked. `UntitledDocumentNamesTests` keeps the answers.

| Server | `untitled:` | leading slash | raw non-ASCII | percent-encoded |
|---|---|---|---|---|
| rumdl | accepted, echoed byte for byte | accepted | accepted | accepted |
| texlab | accepted | accepted | **dropped in silence** | accepted |
| vscode-json-language-server | accepted | accepted | accepted | accepted |
| ruff | accepted | accepted | accepted | accepted |
| clangd | **refused** | refused | refused | refused |

Three findings the table does not carry:

- **clangd refuses the *name*, not the buffer** — a distinction worth keeping straight, because the short
  version of this table invites the wrong reading. Given a `file:` URI it analyses the text it was handed
  whether or not anything is there: measured 2026-09-20, it diagnoses a document whose file does not exist,
  and one whose **directory** does not exist either, identically to one really on disk (the second logs a
  non-fatal `VFS: failed to set CWD`). What it will not take is a URI whose scheme is not `file:` — including
  `untitled:Untitled-1.cpp`, the spelling editors actually use for a never-saved buffer. So it is fully
  content-driven, and its constraint is on the name alone.
- **clangd refuses the scheme, and says so.** `clangd only supports 'file' URI scheme for workspace files`,
  on standard error, naming the `textDocument/didOpen` it threw away. The connection stays up; it is the
  document it will not take. This is the server that makes "assert the refusal, not the silence" mean
  something — every other way of failing looks identical from the client.
- **texlab drops a URI that is not strictly valid without a word**, and then never answers a request naming
  that document. No response, no error, nothing on standard error, and a later valid document on the same
  connection is answered normally. Its percent-encoded form works in the same process. That is why #273
  mints these through the URI type instead of interpolating a string.
- **Only a server that publishes can echo a name at all**, and the one that does echoes it byte for byte,
  in both spellings. A `DocumentDiagnosticReport` carries no URI, so a server answering the pull model has
  nothing to echo *with* — the client raises the event under the URI it asked about. Anything comparing a
  published URI against what was sent is therefore measuring the server on texlab and measuring itself on
  the other three, which is how the first version of these tests was wrong.

Currently unexercised by anything real: the `pipe` and `websocket` transports (both supported, both tested
only against fakes), a server that genuinely defers its analysis to save, and **workspace-wide pull
diagnostics** (`workspace/diagnostic`), which HexIDE does not implement (#284) and which neither ruff nor
rumdl advertises.

## The reference implementation, and why it costs a runtime dependency

`vscode-languageserver-node` is the library the specification is written around. Where the prose is
ambiguous, what that library does is what server authors treat as correct — so disagreeing with it is a
defect here regardless of what a permissive reading allows. No number of servers built on other frameworks
substitutes for it.

It is the only server here that is **not** a self-contained binary, and the only one that needed arguing
for. Its published package will not run from its own tarball — verified: it fails on a missing dependency
— so the tree is reproduced with `npm ci` against `tools/node-lsp/package-lock.json`, which pins all
thirty-odd packages by a registry-attested integrity hash. The lockfile is a text manifest and lives in
the repository; the installed tree does not, and is put under `artifacts/` rather than beside the
lockfile so `node_modules` can never appear in the tree even briefly.

**Node is a harness dependency, not a shipped one.** Nothing in HexIDE requires it. A machine without Node
skips these three tests, visibly — except where `HEXIDE_REQUIRE_FOREIGN_LSP=1` is set, which is CI, and
where the whole point is that coverage cannot quietly disappear. CI installs Node explicitly rather than
relying on the runner image happening to have it.

## The GPL question

texlab is GPL-3.0. HexIDE is MIT and guards that with `scripts/check-licences.sh` on every push. These are
consistent, for reasons worth stating explicitly rather than leaving to be re-derived:

- **It is never distributed.** It is downloaded at test time into `artifacts/`, which is gitignored, and
  no release artefact contains it.
- **It is never linked.** It is a separate process reached over stdio. Nothing of it is compiled into,
  statically bound to, or derived from anything here.
- **Running a GPL program does not make your program GPL.** The obligations attach to conveying the work
  or a derivative of it. Driving an unmodified binary across a protocol boundary is neither.

**And the guarantee is enforced, not merely intended.** `check-licences.sh` fails if any downloaded server
becomes a tracked file. That check exists because the whole argument above rests on the binary staying out
of the tree, and a `git add -f` or a re-scoped ignore rule would otherwise defeat it silently.

One caveat worth keeping in view: this project has deliberately said elsewhere that the out-of-process
server design is for crash isolation and a replaceable backend, **not** for licensing reasons. That
remains true. Nothing here relies on the process boundary to launder a licence — the shipped product
contains no GPL code at all, and this is test tooling that is fetched, run, and never shipped.

## How they are obtained

Downloaded on demand, once, at a pinned version, with a SHA-256 verified before anything is executed, into
`artifacts/foreign-lsp/`. Not committed: several megabytes per platform against a repository whose whole
history is a fraction of that, and every version bump would add the same again permanently.

Linux uses musl builds where offered, so one binary runs on any distribution.

### Digest provenance is recorded, because it differs

- **rumdl** publishes a checksum file beside each asset. Its digests are taken from those — they attest
  that the bytes are the ones the publisher intended.
- **texlab** publishes none, so its digests were computed here from a download. That pins *what was tested
  against*: it still catches a corrupted transfer, a replaced asset, or an unannounced rebuild under the
  same tag. It does **not** attest provenance. If the release had already been tampered with when it was
  first fetched, the digest records that faithfully.

That distinction is trust-on-first-use versus attestation, and the code names it per server rather than
letting a row of hex imply they are the same thing.

### Environment variables

| Variable | Effect |
|---|---|
| `HEXIDE_MARKDOWN_LSP`, `HEXIDE_LATEX_LSP`, `HEXIDE_JSON_LSP`, `HEXIDE_CPP_LSP`, `HEXIDE_PYTHON_LSP` | Point at your own build. Checked before the download, so an explicit choice is never silently replaced. |
| `HEXIDE_FOREIGN_LSP_DOWNLOAD=0` | Stay off the network. The affected tests then skip, visibly. |
| `HEXIDE_REQUIRE_FOREIGN_LSP=1` | Turn "unavailable" into a failure. **CI sets this**, because a silently skipped proof is the failure mode this whole fixture exists to prevent. |

## Bumping a version

Edit the version and the five digests in `ForeignServerAcquisition.cs`. Take each digest from the
publisher's own checksum file where one exists — never from a file you downloaded yourself, which verifies
nothing beyond that the download completed. Where the publisher offers none, compute it and leave the
provenance marked as computed here.
