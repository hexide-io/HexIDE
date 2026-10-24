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
drives five foreign servers across four different LSP frameworks, including `vscode-languageserver-node`, the
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

**Consumed from the server:** `textDocument/publishDiagnostics`, `$/logTrace`, plus `window/logMessage` and
`window/showMessage` — the channel a server uses to talk about *itself* rather than about a document.
The first goes to the log at the severity the server declared; the second goes there **and** to the
status bar, because a message the user never sees was the bug and a message with no trace afterwards
is the next one.

**Server-initiated requests.** Four are handled: `client/registerCapability` and
`client/unregisterCapability` (refused, with the refusal recorded rather than silent) and
`workspace/diagnostic/refresh`, which is acted on — every open document is asked about again. Everything
else gets whatever StreamJsonRpc answers an unknown method with, which is an error response rather than
silence, on the library's own account; those rows are the library's documented behaviour, not a measurement
taken here. A server awaiting a reply it never gets hangs rather than degrading, which is invisible until
you drive a server that asks.

**A handled request is not the same as an answered one, and the gap is silent.** `workspace/diagnostic/refresh`
takes no parameters, so a conformant server sends no `params` member — and the handler, declared like every
other one here with single-object deserialization, had a required argument nothing supplied. The client
answered `-32602 "An argument was not supplied for a required parameter"` and the refresh was refused, while
the connection stayed healthy and the coverage table said the message was implemented. It is declared
parameterless now, and `PullDiagnosticsReportKindTests` drives a scripted server that sends one and asserts
the client asks again.

---

## Describing the workspace

`initialize` carries **both** `workspaceFolders` — one per loaded project — and the deprecated `rootUri`,
and the client declares `workspace.workspaceFolders`. Sending both is not belt-and-braces: four of the five
servers this suite drives were measured, and they show three different behaviours. (ruff, the fifth, was
added later for the pull model and has not been measured on this question — which is stated rather than
quietly folded into the count.)

| Server | Reads `workspaceFolders` | Reads `rootUri` |
|---|---|---|
| texlab | **only** — no fallback | no |
| clangd | **never parses it** | **its sole root channel** |
| rumdl | preferred | falls back |
| vscode-json-language-server | no | no — resolves relative to the document |

So omitting either field silently un-scopes whichever servers read the other, with nothing observable from
the client side. texlab had **no root at all** until the folders were sent; dropping `rootUri` as
"deprecated" would do the same to clangd.

**One folder per loaded project**, because a `.vbg` group names its members by relative path and they
routinely live in different directories. Before this, every server was rooted at whichever project happened
to be the *startup* one, and changing that silently re-rooted every server — including for documents that
had not moved ([#261](https://github.com/hexide-io/HexIDE/issues/261)). Folders sharing a directory collapse
to one entry, so a server does not index the same tree twice.

`workspaceFolders` is **null, never an empty array**, when no project is open: the protocol gives those two
different meanings, and "no folders are open" is the true one.

**Not yet done:** `workspace/didChangeWorkspaceFolders`. A server started while a group was open is
correct; one already running when a project joins or leaves the group is not told, and keeps the folder list
it was given at `initialize`.

---

## Asking a server to describe its own work

A wire capture shows what a server said. It cannot show *why*. That a request took two seconds is on the
wire; that the server fell back to a slower parsing strategy to answer it is not, and only the server knows.

The protocol's answer is a trace level, and this client speaks all of it:

- **`trace` at `initialize`**, taken from the server's own entry in `lsp-servers.json`. It travels in the
  handshake, so it has to be known before the process exists, which is why it is configuration rather than
  a setting. It is sent even when the level is `off`, so a capture of a quiet session says *we asked for
  nothing* rather than leaving a reader to infer it from an absence.
- **`$/setTrace`** changes the level on a server that is already running, without restarting it.
- **`$/logTrace`** is what comes back, raised as an event rather than buried in a log.

Three levels, and only three: `off`, `messages`, `verbose`. A fourth spelling circulates because one
editor's client-side rendering enum carries it, but the specification's own model does not, so offering it
would mean sending a value no server is obliged to understand.

**No server ever confirms any of this.** `$/setTrace` is a notification, so there is no reply and no error,
and the protocol defines no capability by which a client can discover whether a server honours tracing at
all. The only signal available is negative — we asked, and nothing arrived. Any interface built on this must
therefore say *asked*, never *accepted*.

That distinction is not pedantic, because silence is the normal case. Five servers were driven with
`trace: "verbose"` **and** an explicit `$/setTrace`: not one emitted a single `$/logTrace`, and three of the
binaries contain no trace machinery at all. A fourth's framework implements the types and its server never
calls them. So a trace pane that is empty against most servers is correct, and has to say why.

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
publish to evict stale entries. A server that closes a document silently leaves squiggles behind. What it
evicts is that *server's* entries rather than the document's — see *Diagnostics have an owner* below, and
note that this is the honest scope: a server closing a document has stopped having an opinion about it,
while a compile error in the same file has not stopped being true.

**A document's versions run on from its open.** A document is first opened at version 1
(`LspDocumentVersion.Opening`), and its session numbers its changes from there, so the first change is 2. The
client sends the open and the session sends the changes, which is how they once came apart: the session
counted from 0, its first change repeated the open's 1, and a server that discards a change no later than
what it holds lost it. A foreign server measured against this does exactly that, so a single edit followed by a
pause was not analysed until the next one ([#470](https://github.com/hexide-io/HexIDE/issues/470)). The session takes a change's
version together with its text at the moment of the edit, so a number always describes the text it is sent
with.

**An edit made while a server starts reaches it.** A server is opened on a document only once it has
started, and starting takes long enough to type in. So a change made meanwhile is recorded rather than sent:
the session hands it on whether or not a server is running, the registry and the client each record it, and
the open that server receives carries the text as edited. Each document records which connection's server has
been opened on it. An open the reconnect replay has already sent is not sent again, and a change waits for its
connection's open rather than going ahead of it. Before this, the change was dropped while nothing was running,
and a second server still starting was opened twice, the second time at a lower version with the text from
before the edit ([#719](https://github.com/hexide-io/HexIDE/issues/719)).

**Every window holding one name holds one document.** One file shared by two projects of a group opens two code
windows that both name it by its file, and a server has one document for that name. So the windows' sessions share
it, per client: the first window to hold it opens it, every window draws its versions from one sequence, and the
last window to let go closes it. A rename closes the old name only for its last holder and opens the new one only
for its first. A window that joins an open document sends nothing of its own, because its copy as it opened could
undo another window's unsaved edits on the server. It shows what was last published for the document, and its own
edits reach the server as the next versions. The sequence is drawn where the edits are made, as above, so a send
cannot renumber older text. Before this, each window kept its own count, the server was opened twice and discarded
whichever count was behind, and either window's close closed the document for both
([#721](https://github.com/hexide-io/HexIDE/issues/721)).

The client drops a change whose version is no later than the one it tracks, keeping the newer text and
version for reconnect replay. Checking, tracking and sending changes are serialized, so a debounced change
crossing the flush that superseded it cannot send a lower version after the flush
([#720](https://github.com/hexide-io/HexIDE/issues/720)).

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

## Naming a document

**A document that has a file is named by that file's `file:` URI** — a form, module, class, UserControl or
PropertyPage of the project exactly as much as a file the project carries. A server can therefore read it
from disk, resolve something relative to it, and recognise it when it indexes the workspace folder. A
UserControl or PropertyPage is named by its *module's* file, which is the one the project file names; its
designer half's path can diverge from it today ([#474](https://github.com/hexide-io/HexIDE/issues/474)).

**A document of the project with no file yet is named `untitled:<Project>/<Name>.<ext>`** — the
project's name, the document's own name, and the extension its kind will be saved with: `.frm`, `.bas`, `.cls`,
`.ctl` or `.pag`. The extension is there because routing reads it. Three rules:

- **No leading slash.** `untitled:/Project1/Form1.frm` and `untitled:Project1/Form1.frm` normalise
  differently, so one spelling is chosen and never mixed. Two spellings of one name are two documents to a
  server.
- **Each segment is percent-encoded** (`LspDocumentUri.ForUntitled`), never interpolated raw. texlab drops a
  notification whose URI is not strictly valid without a word: no response, no error, nothing on standard
  error, and the connection stays up and answers about other documents normally. A request about that
  document is then never answered at all, because nothing but `initialize` has a timeout
  ([#486](https://github.com/hexide-io/HexIDE/issues/486)). The same server, in the same process, answers the
  percent-encoded form.
- **Compared without regard to case**, because both segments are VB6 names. This is only ever exercised
  against a server that echoes the name back: a pushing server returns it byte for byte, while a pulled
  report carries no URI at all, so the client files the answer under the name it asked about.

The rule reads the path and nothing else: a document whose path is unset is named `untitled:`, and any
other is named by its path. A newly added form or module has no file until the project is saved, as in VB6
([#500](https://github.com/hexide-io/HexIDE/issues/500)), so `untitled:` is the ordinary name of every new
document. Its first save writes the file and renames it to its `file:` name, as a close of the `untitled:`
name and an open of the file. A file in a folder the IDE chose, such as the temporary folder the
`--newproject` option saves its project into, is named `file:` like any other, because that is where the
bytes are and it is inside the root servers are told about; naming it `untitled:` would show a server that
indexes the root the same module twice. **A saved document whose file is then deleted keeps its `file:`
name**: `untitled:` means "no file yet", not "file missing".

All five foreign servers the suite drives were asked about an `untitled:` document on 2026-09-20, each under
an extension it claims, and the frames were read rather than the client's own events. The table records the
versions measured then, which are not all the suite's current pins; `UntitledDocumentNamesTests` keeps
asking at whatever is pinned:

| Server | `untitled:Project1/Module1.<ext>` | leading slash | raw non-ASCII | percent-encoded | delivery |
|---|---|---|---|---|---|
| rumdl 0.2.64 | accepted | accepted | accepted | accepted | pull **and** publishes |
| texlab 5.26.0 | accepted | accepted | **dropped in silence** | accepted | push |
| vscode-json-language-server | accepted | accepted | accepted | accepted | pull |
| ruff 0.16.7 | accepted | accepted | accepted | accepted | pull |
| clangd 22.1.6 | **refused** | refused | refused | refused | push |

clangd's refusal is read off its standard error (`only supports 'file' URI scheme`), and it refuses the
scheme rather than a spelling. A server like it cannot serve a document until the document has a file.

### What this replaced

Until [#273](https://github.com/hexide-io/HexIDE/issues/273) a form was `vb6://form/Form1` and a module or
class `vb6://module/Module1`, minted by string interpolation in eight places. It was a scheme HexIDE had
invented, so a server not written for HexIDE could not open the document, read it from disk, watch it,
resolve anything relative to it, or route it by extension — even once the project was saved and every one of
those documents had a file. A server asking for saves without text was told that `vb6://module/Module1` was
saved, and could do nothing with it. The same string was also the breakpoint key, the bookmark key,
the sidecar key and what automation reported, and some holders froze it while others recomputed it from the
live name, so after a rename they named different documents
([#269](https://github.com/hexide-io/HexIDE/issues/269)). Breakpoints and bookmarks now belong to the
document itself, and the wire name is only the wire name (`DocumentWireName.For`).

The cost was concrete. `demo/spring-tide` used to carry its module as `RelatedDoc=` rather than `Module=`,
because a module would have been named `vb6://module/TideTable`, which the demo's server could not read. It
is a `Module=` now, named by its file, and the server's answers are the ones it gave the carried copy.

### A name is fixed when the document opens

The name is chosen when the document is opened to the language layer and changes only by the close-and-open
below. It is **never** re-read from the document's current path. Make EXE points every document's path into
a temporary folder for the length of the build and back afterwards; a name recomputed from the path would
name a document no server was told about, twice. A build therefore tells no server anything
(`ABuildSaysNothingAtAllAlthoughItRepointsEveryPath`).

Every request is sent under the name the document's session holds, and only while the session is open. That
closed a silent defect: each request used to mint a fresh name from the document, which reads its path
live, so from a document's first save the lifecycle notifications still said `untitled:` while every
request said `file:`. The bundled server answers a URI it does not hold with an empty result and no error,
so hover, completion, folding, Go To Definition, rename and formatting went quiet with nothing logged.

### When the name changes: close, then open

A document's name changes when it is written to a file other than the one it is named by — its first save, a
Save As, a project saved into another directory — and, for a document with no file, when it or its project
is renamed. A saved form that is renamed keeps its wire name, because its file has not changed.

Each server that has the document open is then told it **closed under the old name, and then that it opened
under the new one**, with its current text and with its versions starting again where an open's do. This
is the shape the protocol itself prescribes for a rename, for the reason it gives: more than the name can
change, including which servers claim the document. Both halves are awaited, so they arrive in that order on
every connection, and `RenameWireShapeTests` asserts it on the bytes of a server spoken to by hand. Opens and
closes are otherwise fire-and-forget, and an open that overtook its close would leave a server holding the
document twice under a name it is never told to release. No request is sent between the two. Where a save
caused the change, the save notification follows, under the new name.

**Diagnostics follow the name.** Everything recorded under the old name is withdrawn as the document is
closed under it, from every source, the compiler's injected rows included (`DiagnosticLedger.Forget`), and
the client records an empty set for the old name on each closing connection, a pushing server's as much as a
pulled one's. An ordinary close does less on purpose: it clears a pulled server's set, leaves a pushing server
to clear its own, and leaves the compiler's rows alone, because a build's claim about a form outlives the
editor that showed it. On a rename no later publication is coming, because no server will ever be told about
the old name again, and a mark left under it would be one nothing could clear.

**Nothing the developer set moves.** Breakpoints and bookmarks belong to the document, not to the name it is
known by on the wire.

---

## A server is sent the whole file

A form, module or class reaches a server as the **whole file**: a class's `VERSION` and `BEGIN … END` block,
a form's `VERSION` line, object references and designer block, the run of `Attribute` lines that opens the
code, and every member's `Attribute` lines further down. It used to be sent what the code window then showed:
a module's code without its header, and a form's without its designer block. A position now means the same
line to the editor, to a server that reads the file from disk, to the interpreter, and to anything reading
the file afterwards.

The header and the members' `Attribute` runs are the IDE's to change, and read-only in the code window. A
server is expected to keep off them — the bundled one does, by the rule in
[`lsp-server-features.md`](./lsp-server-features.md) — but the client does not depend on it:

- **Folds.** The header's fold and each member's attribute fold are the code window's own, made from the
  file's layout with or without a server, and they do not come through the language-client interface at all.
  They describe the file format, which is the same whichever server is attached, and a fold that vanished with
  the server would expose the text it exists to keep out of the way. A server's folds are merged with them on
  every answer (`Vb6FoldingAdapter.Merge`): one that nests inside or around them is kept, and one that crosses
  one of them, **or starts where one of them starts**, is dropped. The second rule is the fold library's
  doing: it pairs the folds it is given with the sections it holds by start offset alone, so a server fold
  from the top of the file took the header's section, and the header came back unfolded. A server's
  procedure fold contains that procedure's attribute fold only because the client reads every server fold
  as starting at the beginning of its first line. LSP starts a fold after that line's text, and the client
  also ignores any character offsets a server sends
  ([#1022](https://github.com/hexide-io/HexIDE/issues/1022)). Read as LSP means it, the fold of a procedure
  declared on one line would start where its attribute fold starts, and the second rule would drop it.
- **Formatting.** An answer is applied to a copy, reduced to the lines it actually changes, and any change to
  a line of the header or a member's attribute run is dropped, as one undo step. Lines are compared without
  their terminators, so a server answering in `\n` for a `\r\n` buffer does not rewrite every line.
- **Rename** is not clipped, because a rename partly applied leaves one name meaning two things. An answer
  with any edit in the header or a member's attribute run is refused whole, and the developer is told why.
  The one edit let through is a member's own name where it qualifies one of its attribute lines
  (`Attribute Total.VB_Description`), when the same answer renames that member's declaration to the same new
  name. The code window also refuses some renames before it sends the request. Before it prompts for a
  name, it refuses when the caret is on a read-only line or on a name the designer block declares (the
  form's, a control's or a menu's, which is renamed in the Properties window). After the prompt, it refuses
  a new name that is not a VB6 name: a letter, then letters, digits and underscores, and not one of `Rem`,
  `Empty`, `New`, `Nothing`, `Me` and `Attribute`, the six words `vb6.exe` was measured to refuse though each
  has a name's shape.
- **Diagnostics** inside the header or an attribute run are shown like any other, because they are a
  server's claim about the file. The bundled server raises none there.
- **A rename's edits to other documents are dropped.** The code window applies the edits for its own
  document and discards the rest without saying so
  ([#1024](https://github.com/hexide-io/HexIDE/issues/1024)). No server attached to a code window sends
  more than one document's edits today; one that binds names would, and naming a saved member by its file
  is what lets such a server name it.

---

## Diagnostics have an owner

More than one source publishes onto the one diagnostics channel: each attached server, and the real VB6
compiler injecting what only a compiler knows. The protocol has no notion of who published what — a
`publishDiagnostics` is a whole-document replacement, and every consumer here implements it that way.

So the channel keeps the last set per **(document, source)** and raises the union. A source replaces only
its own rows; the event subscribers see is still one whole-document set, which is what they were written
for. Each connection is a source, keyed by its registration id; an injecting caller names its own key
(`DiagnosticOwner`).

The compiler's rows are filed under the document's wire name, found from the absolute path `vb6.exe` prints,
for every kind of document. They used to be matched to a form by its file's stem and filed under
`vb6://form/<Name>`, forms only, so a module's errors were dropped, and the pattern they were read with
expected a format `vb6.exe` never writes, so in
practice none had ever arrived ([#477](https://github.com/hexide-io/HexIDE/issues/477)). The line `vb6.exe`
prints counts only the code it showed, without the header or any `Attribute` line, and is converted to a
file line where the log is read and nowhere else. The conversion skips each physical line that starts with
`Attribute`, where VB6 skips each logical line whose first word is `Attribute`. Where a `_` continuation
meets an `Attribute` line, as one continued onto the next line or a comment continued onto one, the mark
lands one line off ([#968](https://github.com/hexide-io/HexIDE/issues/968)).

Without that, replacement meant last-writer-wins. Building with VB6 cleared its previous errors by
publishing an empty set for every form, which deleted whatever the language server had published for those
forms — on a *successful* build as much as a failed one, and the marks only returned on the next keystroke
([#358](https://github.com/hexide-io/HexIDE/issues/358)). Two servers claiming one document overwrote each
other by the same rule.

A source can also withdraw everything it published anywhere, which is how a build expires the previous
build's errors. That is driven by what the source published rather than by a list of documents the caller
assembles, because the two disagree exactly when it matters — a form renamed since the last build is absent
from the caller's list and still carries the marks.

### What a diagnostic carries

`range`, `message`, `severity`, `source`, `code` and `codeDescription.href`. `code` is the rule that fired
— ruff's `F401`, rumdl's `MD012` — and is `integer | string` in the specification, so it is held as a raw
JSON element and read through `CodeText`, which renders a number as the digits it arrived as. A server
using numeric codes is as usable as one using names, and neither can throw on the way in.

It reaches every consumer that shows a diagnostic's text: the automation server's `get_diagnostics`, the
add-in API and the AI-chat tool. It reaches nothing a human sees, because **the editor has nowhere to show
a diagnostic's message at all** — there is no tooltip on a squiggle and no error list
([#451](https://github.com/hexide-io/HexIDE/issues/451)). A code is most of what an error list would be
for, so the two belong together.

Still dropped: `tags`, `relatedInformation` and `data`, each for a different reason and tracked as
[#453](https://github.com/hexide-io/HexIDE/issues/453). `tags` would change how a diagnostic is drawn
rather than add text to it, which is a rendering decision rather than a plumbing one;
`relatedInformation` is waiting on somewhere to render it (#451); and `data` is only meaningful if
code actions from a foreign server are relayed, which is an open question rather than a settled no.

### Some servers publish; some only answer

A server may deliver diagnostics by answering `textDocument/diagnostic` rather than by publishing
(LSP 3.17's *pull* model). The client asks on open, on change and on save, and feeds the answer through the
same call push takes — so **nothing downstream can tell the two apart**, which is the requirement rather
than a convenience. There is no separate pacing for this: edits are already coalesced before they reach the
client, and a second authority over the same question would answer it differently.

Four things about the model are worth stating because each one fails quietly if missed.

**Declaring it is half a negotiation, and shipping that half alone is worse than shipping neither.** A
server is entitled to read `textDocument.diagnostic` as "the client will ask, so I need not announce" and
stop publishing. Measured: `ruff` publishes freely to a client that declares nothing and publishes *nothing
at all* to one that declares this. So a client that declares the capability and never asks has talked a
working server into silence — a strictly worse position than never having declared it.

**An `unchanged` answer is not an empty one.** A server may reply that its previous answer still stands,
carrying no diagnostics at all. Treating that as an empty set erases exactly the marks it was sent to
preserve — and only on the *second* request, so they render correctly and then vanish. The result
identifier that makes such an answer possible is retained per document and sent back with the next request.

**A server that advertises the capability has its publications ignored.** Both models on one connection
would put two whole-document sets under one source, where each replaces the other and the winner is decided
by arrival order. This is not hypothetical: `rumdl` advertises `diagnosticProvider` *and* publishes, and
once this client declares it can ask, what `rumdl` publishes is an empty set while the real diagnostics come
back as the answer. The dropped publications are recorded in the protocol inspector rather than discarded
silently, because "my `publishDiagnostics` is ignored" otherwise looks like a broken client to the person
whose server advertised the alternative.

**A server that takes no document sync is still asked, by design.** It is sent no document contents, so it
answers from what it can read itself, such as the file on disk, and the protocol does not require a document
to be open before it is asked about. Such a server is asked on open and when it asks for a refresh; it hears
of no change and no save, so those asks never reach it. Asking only about documents that had been sent would
leave it no way to give diagnostics at all, which is why
[#432](https://github.com/hexide-io/HexIDE/issues/432) was closed rather than fixed.

`workspace/diagnostic` — the same model at workspace scope — is **not** implemented; see the limitations
table. `workspace/diagnostic/refresh` is, because it is the only way a server that speaks when spoken to can
report a change that is not a document, such as its own configuration file being edited.

---

## Routing: which server gets which document

Two questions the protocol keeps separate, and so does HexIDE:

- **`extensions`** — which files a server *serves*. This is what routing reads, for every document.
- **`languageId`** — what that server wants those files *called* on the wire. It routes nothing on its own.

**HexIDE holds no global opinion about what a file is.** The identifier sent for a document is the one the
*receiving server* declared, not a project-wide constant. That is why the same file can be offered to two
servers under two different names, and why a server keying its state on the identifier stays coherent. An
extension no entry claims routes nowhere, and the document opens with language features absent. That is an
ordinary outcome, and nothing reports it as an error.

**Every form, module and class carries an extension on the wire**, including one with no file yet (see
*Naming a document* above), so there is one rule and no exception for HexIDE's own documents. That was not
true until [#273](https://github.com/hexide-io/HexIDE/issues/273): a form or module used to be named
`vb6://form/Form1` or `vb6://module/Module1`, which carries no extension, so routing had a second branch that
read the scheme instead, and a server's `languageId` doubled as a claim. A VB6 server attached as `vba` rather
than `vb6` therefore started, initialized and was never sent a document
([#277](https://github.com/hexide-io/HexIDE/issues/277)). The scheme branch, `SchemeLanguageOf`, and the
identifier as a claim are gone.

### The one place the identifier still counts: a project's class modules

The `.cls` collision ([#279](https://github.com/hexide-io/HexIDE/issues/279)) is the worked example: a `.cls`
is a VB6 class module *and* a LaTeX class file. A lone `.cls` claim therefore cannot be read as "serves VB6"
— a real LaTeX server claims it too, and routing a VB6 module there would have it parse Visual Basic as
LaTeX and report confident nonsense about the developer's own source.

So **a member of the project** is offered only to an entry whose claim establishes VB6
(`DocumentLanguage.EstablishesVb6`): one that claims a VB6 extension no other language uses — `.bas`, `.frm`,
`.ctl`, `.pag`, `.dob` or `.dsr`, every VB6 source extension but `.cls` — or that declares the identifier
`vb6`. The gate is applied to every member, and it only ever bites on `.cls`: on any other VB6 extension the
extension that matched already establishes VB6. It is deliberately about *VB6's* unambiguous extensions.
Stated loosely as "an extension no other language uses", it would admit a Markdown server through `.md`.

**A file the project carries is not a member**, is not known to be VB6, and is routed by its extension alone.
A carried `.cls` reaches the LaTeX server; a class module of the project does not. The gate closes only that
half of the collision. A carried `.cls` also reaches every VB6 entry that claims `.cls`, the bundled one
included, so a LaTeX class the project carries is parsed as VB6 and marked with errors it does not have.
That half is what #279 reports, and it is still open: telling the two kinds of file apart would take
something that reads the file, such as its first line, and extensions cannot.

**Membership is stated by whoever opens the document, not worked out by the router.** The code window opens
members and says so; the carried-file editor opens carried files and says not. The registry remembers the
answer for as long as the document is open, and routes its changes, its close, its save and any re-open by
the same record, so a server that starts later cannot be handed a document it was never offered. The router
deliberately holds no model of the project, and the two ways it could have answered for itself both fail:
parsing the project and document names back out of an `untitled:` name, which nothing is allowed to do, or
matching paths, which cannot see a document that has no file. It is an overload of `OpenDocumentAsync`
rather than a defaulted argument, because a defaulted `bool` would compile at every existing call site and
silently change what each one meant.

Two behaviours changed with this, and both are narrower than before:

- An entry that claims `.bas` and nothing else no longer receives forms and classes. The scheme branch used
  to hand it every VB6 document.
- An entry that declares `languageId: vb6` and claims no VB6 extension receives nothing. Declaring `vb6`
  narrows a claim on `.cls`; it does not make one.

**What was overturned on the way, and it is worth knowing because the guards said otherwise.** The `.cls`
gate used to live inside the scheme branch. Once documents stopped being named `vb6://`, that branch could
no longer be reached, so the gate silently went with it and a LaTeX server would again have been offered
class modules. Both existing guards stayed green, because both opened `vb6://module/Module1`: a name with no
extension routes nowhere, so "the LaTeX server did not receive it" held without ever reaching the rule.
Building the member gate before deleting the branch is what showed it. The guard
`ALatexServerClaimingClsIsNotOfferedVb6Modules` now opens `untitled:Project1/Class1.cls` against real texlab,
and `DeclaringTheLanguageWithoutClaimingAnExtensionNoLongerRoutesAnything` pins the second behaviour change
above so it reads as a decision rather than an accident.

### When the workspace moves

A project's first save, setting another project as the startup project, and Save Project As all move the
workspace root, and a root is sent once, at `initialize`. So the running servers are stopped and started
again at the new root, and **every document still open is opened again on each restarted connection that
claims it**, with its latest text, before any change to it is forwarded
([#469](https://github.com/hexide-io/HexIDE/issues/469)). Without that, a restarted connection holds nothing,
and every document except the one whose opening noticed the move goes silently dark. The re-open is driven by
the registry's own record of what is open rather than by the document that triggered it, because the
teardown stops every server and the triggering document's claimants are a smaller set. A move is noticed
when a document is next opened, not when the project changes. A save that gives a document its first file
re-opens it under its new name, and that open notices the move. A save that writes no document to a new file
notices nothing. Save Project As of a project already saved asks only where the project file goes, as does
one of the project `--newproject` saved into a temporary folder: a document is asked for a path only when it
has none (`ProjectService.DestinationOf`). The running servers
then keep the old root until a document is next opened. That much is read from the code; neither save has
been driven against a running server to watch it happen.

### Transports

`stdio`, `pipe` (named pipe, connecting or listening) and `websocket` are all supported. Only `stdio` is
exercised against a real foreign server; the other two are covered against fakes.

The three do not carry the same framing, and the difference is load-bearing rather than incidental: the two
byte-stream transports delimit messages with a `Content-Length` header, while a WebSocket carries message
boundaries itself and must therefore send **one JSON-RPC message per Text frame with no header at all**.
Both directions of that are asserted against a real socket in `WebSocketWireShapeTests`, including a proof
that the header framing genuinely would appear if the wrong handler were used — without which the
assertion would be guarding against nothing.

---

## Reading the conversation

**The protocol inspector is how you see what crossed the wire**, and it is the answer to the question a
diagnostics list cannot settle: whether a request was even sent, what came back, and how long it took. A
`Tools → Protocol Inspector` tab shows one interleaved timeline across every attached server; the same
record is reachable from automation through `list_lsp_messages` and `get_lsp_message`.

Envelopes — direction, method, id, size, outcome, latency — are recorded for every connection
**always**, without arming anything and without retaining any document content. Bodies are kept only for a
connection that has been armed, except each connection's opening, which is always kept because a handshake
cannot be captured after the fact. `--capture-lsp` arms everything before the first connection exists.

### A request and its answer are one row and two bodies

A response completes its request's entry rather than making one of its own. That is right for a timeline
whose whole job is to be read in order — showing both would double every exchange — and it is why sequence
numbers have gaps: the response consumed one and nothing displayed it.

**The reply's bytes were dropped along with the entry, and that was a defect rather than the same
decision** ([#429](https://github.com/hexide-io/HexIDE/issues/429)). No answer could be fetched by any
surface: not a hover's contents, not an error object's code, and not the `InitializeResult` that decides
what every later message is allowed to be. Reading a foreign server's capability table meant writing a
second client and driving the server from outside the IDE — which is exactly the work this inspector
exists to abolish.

The answer now keeps the sequence it was already allocated, so the gap is the address:

| On the request's row | Means |
|---|---|
| `answerSizeBytes` | How big the reply was. Recorded **whether or not bodies are being kept** — size is metadata, so it belongs to the tier that is always on. |
| `answerSequence` | Where the reply's body is. Present only when one was retained, so a row that has it can always be read. A row with an `outcome` and no `answerSequence` was answered on a connection nobody armed. |

Pass `answerSequence` to `get_lsp_message` and the reply comes back with `isAnswer`, `answerTo`, and the
request's method — a response carries none of its own on the wire. In the window, both halves appear under
the one row, separated by a rule stating the reply's size; they are never joined, because two JSON
documents run together are not a third one.

`export_lsp_conversation` writes a line per **frame** rather than per envelope for the same reason: the
format's claim is that it replays into a client, and a conversation with every reply missing replays into
nothing. The reply's manifest row carries `answerTo` and the opposite direction.

**This replaced an interposed debug proxy** (`HexIDE.LspProxy`, reached by setting `VB6_LSP_DEBUG_PROXY=1`),
which relaunched the server underneath a byte-forwarding process that wrote every frame to its own stderr.
The inspector reads strictly more, and the difference is not one of convenience:

| | the proxy | the inspector |
|---|---|---|
| Reached by | an environment variable and a restart | always on |
| Covered | `stdio` only | every transport, including `websocket` and named pipes |
| Retrospective | no — it had to be running already | yes |
| Requests never sent | invisible: nothing crossed the wire | recorded, with the capability that declined them |
| A message this client could not serialize | invisible for the same reason | recorded as never sent |
| Standard error and the exit code | its own stderr, mixed with the frames | on the same timeline, attributed per connection |
| When bytes actually reached the server | seen, as it sat on the bytes | `stdio` and named pipes: when the first write began and was flushed through, when the first read began waiting, and when the first bytes came back |
| Several servers at once | one proxy per server, one log each | one timeline, filterable |
| Output | a text log to read by eye | a grid, a redacted export, and an automation surface |

**"Sent" is when a message was serialized, not when its bytes reached the server.** Between the two sit
StreamJsonRpc's pipe writer and the loop that copies it into the channel, and a message held anywhere there
looks, from the timeline, exactly like a server that received it and said nothing. So the `stdio` and
named-pipe transports tap their channel and add four lifecycle entries, once each per connection: `began
writing N bytes to the server`, `the first N bytes reached the server's channel` (the write's flush returned),
`began waiting for the server's first bytes`, and `the first N bytes arrived from the server`. They carry their
times like every entry, so `list_lsp_messages` shows them. A server that answered nothing to `initialize`, then
died of SIGPIPE the moment the connection was torn down, is the case they were written for (#390): the four
say whether the request left this process and whether anything came back. `websocket` frames whole messages
and has no such gap to observe.

The one thing the proxy could do that the inspector cannot is observe a client so broken it never reaches
its own serializer — a failure outside the process is immune to a tap inside it. That was worth one
mitigation rather than keeping the whole thing: a serialization failure is now recorded as a never-sent
entry, and an inbound frame this client cannot decode is recorded with its bytes rather than lost.

**A second thing went with it, and it was not a logging feature.** The proxy had a `--ws-server` mode that
listened on a port and bridged a WebSocket to a freshly spawned stdio server, moving opaque bytes in both
directions. It logged nothing useful — connection events only — but it was the only WebSocket server this
repository could point the IDE at, so it was how the socket transport got exercised at all. Removing it
without replacing that would have left the transport untested rather than merely unlogged, which is why
[#382](https://github.com/hexide-io/HexIDE/issues/382) had to close first: `WebSocketWireShapeTests` stands
up a real socket server and asserts the framing. **What no longer exists is a runnable local bridge**, so
driving the IDE by hand over `ws://` now needs a WebSocket server of your own.

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

**HexIDE implements 29 of the 93.** That is not a deficiency in itself — no client implements them all, and
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

### `textDocument/*` — 17 of 41

| | Method | Dir | Notes |
|---|---|---|---|
| ○ | `textDocument/codeAction` | → | Needs a bound AST — a backend's job, see below |
| ✅ | `textDocument/codeLens` | → |  |
| ○ | `textDocument/colorPresentation` | → |  |
| ✅ | `textDocument/completion` | → |  |
| ✅ | `textDocument/declaration` | → | Where a name is declared, which a server may answer differently from `definition` |
| ✅ | `textDocument/definition` | → | All three reply shapes; opens the document a cross-file answer names |
| ✅ | `textDocument/diagnostic` | → | The pull model. Asked on open, change and save; honours `unchanged` reports and result identifiers |
| ◐ | `textDocument/didChange` | → | Full text only; a declared incremental kind is ignored ([#282](https://github.com/hexide-io/HexIDE/issues/282)) |
| ✅ | `textDocument/didClose` | → |  |
| ✅ | `textDocument/didOpen` | → |  |
| ✅ | `textDocument/didSave` | → |  |
| ○ | `textDocument/documentColor` | → |  |
| ✅ | `textDocument/documentHighlight` | → |  |
| ○ | `textDocument/documentLink` | → |  |
| ✅ | `textDocument/documentSymbol` | → | Both answers are read. The client declares `hierarchicalDocumentSymbolSupport`, because a server that honours it sends the flat form to a client that does not, and the outline loses its nesting ([#463](https://github.com/hexide-io/HexIDE/issues/463)) |
| ✅ | `textDocument/foldingRange` | → | Asked when an editor attaches, 500 ms after a change, and when a server comes up — the first of those runs before the first server of a session has initialized. The client declares `lineFoldingOnly`, because a fold is read by whole lines: told nothing, clangd ends a fold part-way along its closing line, and collapsing it hid the closing brace ([#1363](https://github.com/hexide-io/HexIDE/issues/1363)) |
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
| ○ | `textDocument/prepareRename` | → | No server is asked before the prompt; the code window itself refuses a protected line or a declared name |
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

### `workspace/*` — 3 of 21

| | Method | Dir | Notes |
|---|---|---|---|
| ○ | `workspace/applyEdit` | ← | A server cannot ask the IDE to edit a document |
| ○ | `workspace/codeLens/refresh` | ← |  |
| ○ | `workspace/configuration` | ← | A server asking for its configuration is refused |
| ○ | `workspace/diagnostic` | → | The pull model, workspace-wide ([#284](https://github.com/hexide-io/HexIDE/issues/284)) |
| ✅ | `workspace/diagnostic/refresh` | ← | Re-asks every open document; the only way a pull-model server can report a change that is not a document |
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
| ✅ | `workspace/symbol` | → | Asked of every running server that offers it, and unioned |
| ○ | `workspace/willCreateFiles` | → |  |
| ○ | `workspace/willDeleteFiles` | → |  |
| ○ | `workspace/willRenameFiles` | → |  |
| ○ | `workspace/workspaceFolders` | ← |  |

### `window/*` — 2 of 6

| | Method | Dir | Notes |
|---|---|---|---|
| ✅ | `window/logMessage` | ← | Raised as an event **and** logged at the severity the server declared. It used to be logged only, at a level the default configuration discards |
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

### `$/*` — 2 of 4

| | Method | Dir | Notes |
|---|---|---|---|
| ○ | `$/cancelRequest` | ↔ | Cancelling a token sends `$/cancelRequest` with the original request’s bare numeric or string `id` (#944), so a server can stop abandoned work. **Cancelling does not unblock the client’s await**: it still waits for the server to answer, as measured in [#231](https://github.com/hexide-io/HexIDE/issues/231) / [PR #291](https://github.com/hexide-io/HexIDE/pull/291). |
| ✅ | `$/logTrace` | ← | Raised as an event and logged. A server's own account of its reasoning, which a wire capture cannot reconstruct |
| ○ | `$/progress` | ↔ |  |
| ✅ | `$/setTrace` | → | Changes the level on a running server. A notification, so **no server ever confirms it**, and the protocol defines no capability for asking |

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

### `codeLens/*` — 1 of 1

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

- **Features needing a bound AST** — references, code actions, semantic tokens, inlay hints, call hierarchy
  and their `*/resolve` companions. Not wired because nothing here would answer them; see *What the client
  would consume* below. `workspace/symbol` used to sit in this list and no longer does: the client speaks it
  and the Object Browser surfaces it, so the missing half is a *server*, which is exactly the point of the
  seam.
- **Workspace-level protocol** — `workspace/*` is 2 of 21. There *is* a workspace model now — one folder
  per loaded project, sent in `initialize` — and `workspace/symbol` searches it. What is still absent is
  everything that keeps that model live or reacts to it: file-operation notifications, watched files,
  configuration round-trips, and `didChangeWorkspaceFolders` for a group whose membership changes while a
  server is running ([#261](https://github.com/hexide-io/HexIDE/issues/261)).
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
| No workspace-wide pull diagnostics (`workspace/diagnostic`) | Diagnostics are per open document; a server's findings about files nobody has open are never requested | [#284](https://github.com/hexide-io/HexIDE/issues/284) |
| No request but `initialize` has a timeout | A request about a document the server silently discarded is never answered, and nothing gives up on it | [#486](https://github.com/hexide-io/HexIDE/issues/486) |
| A rename's `WorkspaceEdit` is applied to the code window's own document only | Edits to every other document are dropped without a word, so a binding server's rename is applied in part | [#1024](https://github.com/hexide-io/HexIDE/issues/1024) |
| An error answered to `textDocument/rename` is turned into `null` | A server that refuses a rename with a reason is shown as one that found nothing to change | [#1015](https://github.com/hexide-io/HexIDE/issues/1015) |
| A server's fold is read from the start of its first line, and its character offsets are ignored | Collapsed, a server's fold hides the line LSP leaves showing. Read as LSP means it, under today's merge, a procedure with an attribute run under a one-line declaration would lose its fold | [#1022](https://github.com/hexide-io/HexIDE/issues/1022) |

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
| Workspace symbols | `workspace/symbol` | **Wired** — see below |

"Not wired" is a statement about the client, and it is the honest one: wiring each is a bounded piece of
client work with no architectural obstacle, worth doing when a backend exists that would answer. What would
*not* be honest is carrying them here as planned analysis work.

**`workspace/symbol` is the one that has been wired anyway, and it is worth saying why.** The bundled server
does not advertise it and will not — finding a name anywhere in a workspace is a binding question, and
binding is outside its hard limit. The client speaks it regardless, and the Object Browser's search box
asks every running server that offers it. So attaching a server that *can* answer is now the whole of what
it takes, with no client work in between. That is the seam working as designed, and it is the shape the
remaining rows would take too.

The Object Browser therefore has to distinguish three empty answers that look identical: **nothing has
started yet** (servers start lazily, when a document with an extension they claim is opened), **nothing running offers
the feature**, and **nothing matched**. Only the third is "not there", and a search box that said so for
all three would be confidently wrong two-thirds of the time.

---

*The behaviour contracts live under [`openspec/specs/lsp-client/`](../openspec/specs/lsp-client/spec.md).*
