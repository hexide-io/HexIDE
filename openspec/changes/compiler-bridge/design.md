## Context

The archive project format opens read-only in HexIDE today: browsing, previews and a managed source folder. Its
compiler can be started, initialised, given a project and shut down, all from a library in this tree. The library is
not wired into the shell. The decision of 2026-10-19 (#1178) puts everything compiler-facing into a bridge process
that HexIDE talks to over LSP and DAP, and leaves core with, at most, the archive reader. This document records the
five decisions #1690 asks for. Code references are to main at ece6ddc9.

## Decision 1: how an archive's documents reach a language server

**Chosen: an archive entry URI.** HexIDE names each entry by a URI that carries the container and the entry, and the
bridge resolves it against the container it reads. The managed source folder is not the bridge's input.

Today neither candidate works without changes. What each one needs, measured:

| | (a) Entry URI | (b) Source folder files |
|---|---|---|
| Naming the document | A third kind beside `LspDocumentUri.ForFile` and `ForUntitled` (`IDE/HexIDE.Core/Lsp/LspDocumentUri.cs:45, 64`) | Nothing: they're `file:` URIs |
| Opening it | Archive tabs need an `ILspClient`, an `LspDocumentSession` and markers (`ArchiveProjectService.cs:122`). Reload swaps the `TextDocument` (`ArchiveDocumentViewModel.cs:37-41`), so the session is recreated with it | No way to open a loose file: Open takes only project files (`ProjectService.cs:271`), and the text editor needs a VB6 project to own the file (`EditorService.cs:156-164`) |
| Choosing the server | Works by extension, if the URI's last path segment carries it (`DocumentLanguage.cs:50-64`). Opened with `isProjectMember: false` (`LspClientRegistry.cs:587-591`) | Works by extension |
| Workspace folders | `ILspWorkspace` has no archive source (`ProjectLspWorkspace.cs:67-110`) | The same, for the folder |
| Navigation | `EditorService.NavigateTo` resolves only VB6 identities and file paths (`EditorService.cs:41-77`) | Opens a second tab for an entry already open as an archive tab |
| Privacy | `ConversationRedactor` pseudonymises only `file:` and `untitled:`, so the container path would reach exports verbatim (`ConversationRedactor.cs:73, 122-124`) | Covered |
| Keeping in step | Nothing new: the container is the only copy | The folder exists only after an explicit Synchronize, is replaced wholesale by `Directory.Move` (`ArchiveSourceFolder.cs:138-141`), and no editor reloads after it (`FileWatcherService.cs:17-33`) |
| Editing | Read-only, as archive tabs are | Edits are never imported, and fail the next synchronisation's hash check (`ArchiveSourceFolder.cs:263-278`) |
| Spec | `archive-project-browsing` forbids attaching a language server, and this change amends it | The same requirement also covers this |

**Why (a).**
- The container is authoritative, and #1178 makes the folder a projection of it. A bridge fed from the folder would
  analyse a copy that can be stale, and that HexIDE has no way to reload.
- (a) keeps one identity per entry. `ArchiveEntryIdentity` already is that identity: the container path plus its
  ordinal segments (`ArchiveEntryIdentity.cs:5-26`). The URI is just its wire form.
- Every change (a) needs sits where `untitled:` is already handled generically: minting, comparison, redaction and
  disclosure. `LspDocumentSession`, the registry, `VBLspClient` and `DiagnosticLedger` pass any URI through.
- (b) would wait on Open Folder (#1206) for its first step, and it still couldn't take an edit back into the container.

**Why not a scheme the foreign servers already understand.** HexIDE's own `vb6://` scheme was retired because foreign
servers could not route it (#273). Here that cost doesn't apply: the only server that receives these URIs is the
bridge, which defines them. A server that doesn't claim the archive's extensions never sees one.

**The shape**, provisional until the bridge's first slice:

```
archive-entry:/Sources/Module%201.ext?container=file%3A%2F%2F%2FC%3A%2Fwork%2FDemo.ext
```

- **The path** is the entry's ordinal segments, each percent-encoded, because names may contain `/` and `\`
  (`ArchiveProjectSnapshot.cs:50`). The last segment carries the entry's extension, so choosing a server by
  extension works unchanged.
- **The `container` query** is the container's own `file:` URI. It compares the way `ArchiveEntryIdentity` compares
  its container path: without regard to case on Windows. `LspDocumentUri.PathIsCaseInsensitive` answers false for an
  unknown scheme (`:124-126`), and this must change with it. Otherwise a server that echoes a different
  drive-letter case drops diagnostics silently, which is the #236 class.
- **The scheme name carries no product brand.** A white-labelled HexIDE (#2042) sends the same URIs.

**Rejected shapes:**
- A nested `jar:`-style URI (`archive-entry:file:///…!/…`): many URI parsers don't take a scheme inside a scheme, and
  `!` is a legal file-name character.
- The container path at the front of the URI's path: nothing marks where the container ends and the entry begins.

**Kept from (b).** The source folder is for Git and outside tools, as #1178 intends. The bridge never reads it.

## Decision 2: the process

**One executable, in two modes.**

- **`lsp`, the default.** It serves LSP over standard I/O, and one process serves the whole IDE, as one registration
  does today (`LspClientRegistry.cs:89-93`).
  - It starts a compiler for a container on the first `didOpen` of one of its entries. It stops that compiler a short
    grace period after the container's last document closes, and at `shutdown`.
  - It owns every compiler it starts, holding each in the job object the `compiler-process` spec already requires.
  - It owns each compiler's staging directory. That sits under the bridge's own temporary folder, never beside the
    project, and is deleted when that compiler exits.
- **`dap`.** HexIDE's external debug session already starts its adapter as a process of its own (`docs/debug-launch.md`).
  In this mode the executable relays DAP between that session and the `lsp`-mode process, over a local pipe named from
  their shared parent's process id. The debugger channel belongs to the same compiler session as the language channel,
  so a separate adapter with its own compiler would run the project twice. With no `lsp`-mode process holding the
  project, `dap` mode fails with a message saying so.

**How HexIDE starts it.**
- **Now:** an `lsp-servers.json` entry, with a stdio transport and the archive's source extensions.
- **The compiler's install folder:**
  - an argument, which a user can add without replacing the entry through the file's top-level `arguments` (#262);
  - otherwise the bridge's own detection;
  - with neither, the bridge reports the problem, and opens no project.
- **Later:** a provider suite (#1208).
- **Not before #1210:** a default entry contributed by core. Core would then be shipping a compiler-specific default,
  which is what this change takes out.

**Lifetime.**
- The bridge ends with HexIDE through the launched-server teardown (#406): a job object on Windows, already built in
  #2007, and a watchdog on Linux and macOS.
- The compiler runs on Windows only. Elsewhere the bridge answers `initialize` and then reports, once, that it has
  nothing to run. A Windows-only feature should say so, not fail silently.

**Interactions to fix before the first slice ships.**
- `LspClientRegistry` restarts every running server when the VB6 startup project's directory moves
  (`LspClientRegistry.cs:683-760`). That would restart every compiler for a reason unrelated to the archives they
  hold.
- `DocumentWireName` builds `untitled:` names from the VB6 project, and archive entries need their own path.

## Decision 3: the first LSP mapping

**The language channel is already JSON-RPC 2.0, and mostly LSP by name.** The bridge's job is renaming, framing and
filling gaps, not translating a foreign protocol.

**Provenance, which matters here.** The mapping comes from observing the stock IDE on beta 1024, with the
compiler's author's permission and the maintainer's decision (recorded on #1690, 2026-10-26).
- Only what crossed the IDE's own WebSockets was recorded, through DevTools on its WebView2 front end.
- Nothing in the compiler was read, disassembled or decompiled, and neither was the front end's code.
- Raw captures stayed on the machine that made them. This table records message shapes, not their content.

**Do not take this as permission to repeat the observation without asking the author.** The vendor's licence restricts
reverse engineering.

### The channels

The front end opens four WebSockets on the compiler: `root`, `fs`, `language` and `debugger`, each on its own port,
behind the same key.
- **`language`** carries UTF-8 JSON. The compiler sends binary frames, and the IDE sends text frames.
- **`fs`** is binary. The IDE asks for the project tree, and for a file by its project path. The compiler answers from
  the project it loaded, so document text reaches the front end over `fs`, never `language`.
- **The compiler names documents** by its own URI scheme and the project-relative path:
  `<scheme>:/<Project>/Sources/<Module>.twin`. The bridge translates between those names and archive entry URIs.
  Decision 1's ordinal segments are exactly that path.

### Message by message

Positions are zero-based lines and characters, as in LSP.

| Observed on `language` | Shape | LSP 3.17 | What the bridge does |
|---|---|---|---|
| Compiler → IDE notification `textDocument/publishDiagnostics` | `uri`; `diagnostics[{range, code, message, severity}]`; also `errorCount`, `warningCount`, `hintCount`, `infoCount` and `docVersion` | `textDocument/publishDiagnostics` | Rename the URI. `docVersion` becomes `version`; drop the counts. **Diagnostics need nothing else.** |
| IDE → compiler request `textDocument/hover` | `textDocument{uri}`, `position`; answered `{contents: MarkupContent (markdown), range, selectionRange}` | `textDocument/hover` | Rename; drop `selectionRange` |
| IDE → compiler request `textDocument/definition` | as hover; answered with one `{uri, range, selectionRange}` | `textDocument/definition` | Answer a `Location`, or a `LocationLink` if the client takes them |
| IDE → compiler request `textDocument/completion` | `textDocument{uri, version}`, `position`, `context{triggerCharacter}`, plus `origCaretLine` and `origCaretColumn`; answered `{lazyRequestId, items[{l, t, i}], isIncomplete}` | `textDocument/completion` | `l` → `label` and `t` → `kind`; the values seen match `CompletionItemKind`: 7, 8, 9, 13, 14, 22. Keep `i` and `lazyRequestId` in `data` |
| IDE → compiler request `textDocument/lazyCompletion` | `{lazyRequestId, requests[{x, i}]}`; answered `{results[{x, type?, aliasType?, uri?, line?}]}`, or error 123 when the id is stale | `completionItem/resolve` | Resolve one item at a time from `data`, and put `type` or `aliasType` in `detail` |
| IDE → compiler request `textDocument/didChangeV2` | **Not JSON:** `:<header length>,<JSON header>` then CRLF then the document's full text. The header has `textDocument{uri, version}`, `contentChanges: ""`, `isActiveEditor`, `caretLine`, `caretColumn` and `activeDebugLines`. Answered with the decorations below | `didOpen` and `didChange`, full sync | Send the full text on each change, and cache the decorations for the requests below |
| IDE → compiler request `textDocument/getDecorations` | `textDocument{uri, version}`; answered with the decorations: `semanticTokens`, `lineSeperators`, `codeLens`, `breakpoints`, `symbols`, `scopes`, `extraDecorations` | none | Ask after open, and cache |
| (in the decorations) `symbols` | `{name, detail, kind, range, selectionRange}` | `textDocument/documentSymbol` | Answer from the cache |
| (in the decorations) `codeLens` | `{range, command{title, command, arguments}}` | `textDocument/codeLens` | Answer from the cache; HexIDE cannot run the command |
| IDE → compiler request `textDocument/getSemanticTokens` | `textDocument`, `pages[n]`; answered `{semanticTokens{<page>: [int…]}}` | `textDocument/semanticTokens/full` | **Not decoded.** The legend and paging are unknown, so it is not mapped yet |
| IDE → compiler request `textDocument/foldingRange` | `textDocument{uri}`, `docVersion`; answered with an array of arrays of `{startLine, endLine, tbBlockType?, hasErrors?}` | `textDocument/foldingRange` | Flatten, and drop the extras |
| Compiler → IDE request `custom/applyEdit` | `{PreserveCaretPosition, edit{documentChanges[{textDocument{version, uri}, edits[{range, newText, noUndo}]}]}}`, sent while typing: the compiler tidies the line | `workspace/applyEdit` | Archive entries are read-only and are never edited, so it shouldn't arrive. If it does, answer `applied: false` |
| Compiler → IDE request `custom/provideCustomControls` | `{customControls[…], customForms[]}` | none | Answer it in the bridge, and don't forward it: it is designer data |

**Not observed:** signature help, find references and rename. The stock IDE sent nothing on the language channel for
any of them, so they aren't mapped. The bridge doesn't advertise them.

### Where it departs from JSON-RPC

The bridge normalises all of these.
- **Ids:** responses carry the request's id as a string, even when the request's id was a number.
- **The version member:** the IDE's requests carry no `jsonrpc` member.
- **The result key:** `lazyCompletion` answers under `results`, not `result`.
- **Document names:** completion requests name the document without its scheme.
- **Framing:** `didChangeV2` is not JSON (above).

### Still to measure

- **Character units on a line with non-ASCII text.** UTF-16 is expected, since the front end is JavaScript, but it
  hasn't been measured.
- **The semantic-token encoding.**
- **What the compiler sends when a second document changes in the background.**

## Decision 4: where the bridge lives

**Recommended: in this tree, until #1210 ships.**
- It is its own executable project beside the archive and compiler libraries. The shell doesn't reference it.
- It moves out with the compiler library once the package format gives suites somewhere to install from.

| | In this tree now | Its own repository now |
|---|---|---|
| Build and CI | Shares the build, the central package versions and the licence guard | A second CI setup, release process and versioning scheme before there is anything to release |
| Changes across the seam | One PR changes both sides of the LSP seam, such as the entry URI | Every change to the seam is two PRs in lockstep |
| Installing | Ships beside the IDE, as the bundled server does | Needs the package format (#1210), or a manual install |
| The boundary | Enforced by a test (Decision 5) | Enforced by the repository boundary itself |

Creating a repository is the maintainer's decision, on the dashboard as `q-1690-home`.

## Decision 5: what core keeps

**Core keeps the archive reader.**
- Container reading, the snapshot and its workspace, entry identity, and the managed source folder.
- In files: `ArchiveEntryIdentity`, `ArchiveProjectSnapshot`, `ArchiveProjectWorkspace`, `ArchiveSourceFolder`, and
  the container type and its reader.

**Everything compiler-facing moves** into an assembly that only the bridge references: `CompilerProcess`,
`CompilerRootSession`, `CompilerChannelSession`, `CompilerWebSocketTransport`, `CompilerWireCodec` (both parts),
`CompilerRecording`, `CompilerProjectPreparation`, `CompilerPackageProvider`, `CompilerLanguagePack` and
`CompilerSessionMessages`, with their tests.

**The test**, measured on main:
- Nothing under `IDE/HexIDE`, `IDE/HexIDE.Core`, `IDE/HexIDE.Lsp` or `IDE/HexIDE.Desktop` names a compiler session type.
- The shell references the archive library as a whole assembly (`IDE/HexIDE/HexIDE.csproj:36`), compiler types
  included, but uses none of them.
- After the split, a test asserts that the shell's assembly doesn't reference the compiler assembly. A
  reference-level guard catches what a name search misses: a type used through `var`, or a new file.

**Moving files is not free.** The third-party name allowlist in `scripts/check-tree-hygiene.sh` lists these files by
path. Every move, and the bridge's own project, needs an entry, and the allowlist's additions are the maintainer's to
approve.

## Risks

- **The protocol is moving.** The compiler's author has said the protocol is going from JSON to binary. The mapping
  above is per beta, so the bridge should record each beta's traffic and replay it in CI (#1218), and check each new
  beta (#1219).
- **One process serves every container.** If one compiler hangs it shouldn't stall the others, so each compiler
  session runs on its own I/O loop, as the channel lifecycle already does per socket.
- **Archive entries are read-only.** Diagnostics and navigation fit that. Formatting, rename and code actions return
  edits nobody can apply, so the bridge doesn't advertise them until the archive format can be written.
