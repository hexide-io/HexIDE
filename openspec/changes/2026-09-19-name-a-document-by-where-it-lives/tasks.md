# Tasks

## 0. Measurements the rest depends on

- [x] 0.1 The running IDE shows a form's leading `Attribute VB_*` block (measured 2026-09-20 on
  `demo/bill-of-fare`, form `frmBillOfFare`, freshly launched and untouched). `get_file_content` returns a
  buffer opening on the five `Attribute VB_*` lines, and a snapshot of the code window shows them as its
  lines 1-5: syntax-coloured as ordinary code, not folded, not greyed, caret at Ln 1 Col 1. The code was
  right and the comments were wrong; the three that said the block is invisible are corrected. **So, for
  phases 3 and 4:** a form kind's composed prefix is the designer part alone, and so is its sidecar shift —
  the attribute run is already in the buffer and must not be counted twice. Also found while measuring:
  `get_file_content`'s `hasUnsavedChanges` is hardcoded per branch and reports provenance, not dirtiness
  (hexide-io/HexIDE#481); out of scope here.
- [x] 0.2 Both grammars now parse whole files, and did not before (2026-09-20). `WholeFileGrammarTests` in
  each half parses every corpus file whole and classifies three ways: an error in the prefix, an error that
  appears in the body only with the prefix in front of it, and a body that already fails alone. The first two
  block; the third is recorded. **30 of 46 files failed, in both grammars, at the same line of the same
  file** — one cause: VB6 decodes an enumerated property value into a comment when it saves
  (`MultiUse = -1  'True`), `COMMENT` is hidden, and the whitespace in front of it reached rules that went
  straight from the value to `NEWLINE`. Every `.cls` VB6 ever wrote failed at line 3. Fixed with a `WS?` in
  `moduleConfigElement` and `cp_SingleProperty` in both grammars; recorded as fix 7 in
  `docs/vb6-grammar-fixes.md`. Now 0 blocking over 48 files (7 `.bas`, 13 `.cls`, 3 `.ctl`, 24 `.frm`,
  1 `.pag`). **So 3.6 is unblocked: the composed buffer parses.**
  - The `.pag` and one `.ctl` are new, in `corpus/designer/`, with a `.vbp` that `vb6.exe` builds clean.
    Authored here and compiler-validated, **not written by the VB6 IDE** — driving the IDE needs an
    interactive session in the VM, and PowerShell Direct lands in session 0 with no desktop (measured).
    `corpus/designer/README.md` states the provenance and what it does not cover.
  - Two bodies fail on their own, and both are now named rather than left in a report: spring-tide's module
    is deliberately invalid, and `demo/neon-aurora/Class4.cls` line 174 is `1E+30`, which the server's
    grammar rejects and the interpreter's accepts (hexide-io/HexIDE#482).
  - Found on the way, because the new corpus project is the first with a UserControl or PropertyPage in it:
    HexIDE wrote `UserControl=Name; File` into every `.vbp`, which VB6 reads as a filename, so every such
    project it saved was unopenable (hexide-io/HexIDE#483). Fixed, with the five-key rule measured and
    recorded in the oracle; two existing tests had pinned the wrong shape.
- [x] 0.3 Oracle, recorded in `docs/vb6-fidelity-oracle.md` (2026-09-19): the `/out` log reads `Compile Error
  in File '<absolute path>', Line <N> : <message>`, with `N` a 0-based index into the code view — designer
  block and **every** `Attribute` line excluded, procedure-level ones included, physical lines counted. Two
  projects in one group may not share a name (the group is refused and nothing builds); a form and a module
  in one project may not share a name. Both comparisons are case-insensitive.
- [x] 0.4 Foreign servers and `untitled:` — measured against all five and kept as
  `UntitledDocumentNamesTests` (2026-09-20). Each server is asked with **its own** extension, because routing
  is by extension and none of the five claims a VB6 one: what is under test is the scheme, and sending `.bas`
  would test something else. Measured, HexIDE's own client, pinned versions:

  | Server | `untitled:Project1/Module1.<ext>` | leading slash | raw non-ASCII | percent-encoded | delivery |
  |---|---|---|---|---|---|
  | rumdl 0.2.64 | accepted | accepted | accepted | accepted | pull **and** publishes |
  | texlab 5.26.0 | accepted | accepted | **dropped in silence** | accepted | push |
  | vscode-json-language-server | accepted | accepted | accepted | accepted | pull |
  | ruff 0.16.7 | accepted | accepted | accepted | accepted | pull |
  | clangd 22.1.6 | **refused** | refused | refused | refused | push |

  - **Every assertion reads a frame, and the first version did not.** It compared
    `PublishDiagnosticsParams.Uri` with the name sent, which is a real echo from a push server and a
    tautology from a pull one: a `DocumentDiagnosticReport` carries no URI, so `ApplyDiagnosticReport`
    raises the event with the client's own string. Three of the five deliver that way, so three of the tests
    were comparing HexIDE with HexIDE. The capture is armed and the bodies are read instead — the sent
    `didOpen`, the sent pull request, and the received publication.
  - The branch is read from the connection's declared `diagnosticProvider`, never from a list in the test. A
    pull server is proved by a `textDocument/diagnostic` naming that URI, answered, with items in the answer
    — findings it could only produce from text it holds under that name. A push server is proved by the URI
    in its publication **and** by never having been asked. Both halves of the second are needed: rumdl
    advertises a provider and publishes anyway, so either alone leaves the branch undetectably reversible —
    measured by inverting it, which stayed green until the second half was added.
  - clangd's refusal is read off its standard error (`only supports 'file' URI scheme`, naming
    `textDocument/didOpen`), never inferred from the absence of diagnostics. It refuses the scheme, not the
    spelling: the percent-encoded form is refused identically.
  - texlab's silent drop and its encoded form are one test, on one server, in one process, with only the
    spelling changed — the negative half alone would assert an absence. Its patience is a multiple of what
    the positive half just took, so a loaded runner scales it rather than defeating it.
  - Found on the way: a request about a document a server discarded is never answered and nothing gives up
    on it, because only `initialize` has a timeout (hexide-io/HexIDE#486); and a foreign server whose
    download fails its pinned digest makes its tests *vanish* rather than fail, because the refusal throws
    from an attribute constructor (hexide-io/HexIDE#487).
  - Every assertion here was checked by breaking it, including the one that matters most: opening the
    document under a name one character different from the one asserted fails all four tolerant tests, pull
    and push alike. That mutation changes what is *sent*; the earlier round only changed what was expected,
    which is exactly why it could not see the tautology above.
- [x] 0.5 AvaloniaEdit 12.0.0 behaviours, read out of the assembly (2026-09-19): the stock read-only provider
  allows insertion at a region's edges and carves read-only text out of a deletion, and both methods are
  `virtual`; binding the editor's read-only property replaces the whole section provider; a fold's
  closed-by-default flag applies only on the manager's first update to a fold created in it, `UpdateFoldings`
  throws on an unsorted list and skips a zero-length fold; undo entries hold absolute offsets, are internal and
  immutable, cannot be rebased, and any push clears the redo stack, while a group can carry a caller's marker
  that identifies the most recent group. The design records what each one settles.

## 1. Identity inside the IDE

- [ ] 1.1 A document identity: a value comparing by reference to the document's definition, holding its
  project. A UserControl or PropertyPage has one identity (its module), as the editor already chooses, and its
  file is the module's, not the form part's (they diverge today, #474). Written `<Project>/<Name>` for display
  and lookup, case-insensitively, and never used as a key in that form.
- [ ] 1.2 Re-key the breakpoint and bookmark stores, both gutters and the F9 and bookmark commands on it. The
  gutters and commands must read one live value, never one frozen at attach and another recomputed later.
- [ ] 1.3 Replace the six places that read a module name out of a URI (debug module name, Run To Cursor, Set
  Next Statement, the runner's live push, `AddinDiagnosticsService.ExtractName`, the sidecar's project lookup)
  with the identity's definition and project. The runner's live push is scoped to the running project.
- [ ] 1.4 Replace the eight places that mint `vb6://` by hand. What remains of a URI at this phase comes from
  one converter at the seam, so phase 2 changes one function.
- [ ] 1.5 Sidecar keyed by document name within its project's file. Store unload and clear are scoped by
  project, not recomputed from current names.
- [ ] 1.6 Automation: resolve a document by project and name across every loaded project, then key by
  identity. That fixes minting from the caller's spelling (#467). Replies carry `project` and `document`.
- [ ] 1.7 Names: new forms, modules and classes never repeat a name in their project, including one adopted
  from an existing file; a rename that would is refused; a new project never takes a loaded project's name and
  cannot be renamed to one (#468). Every such name must be a valid VB6 name, which keeps a slash, hash,
  question mark or space out of a wire name at the point it is chosen. Both collision rules are VB6's own,
  measured in 0.3. Refusal reasons are localization keys.
- [ ] 1.7b Add-ins: name a document by project as well as name, resolve across every loaded project, and
  refuse an ambiguous bare name. A trailing optional argument and trailing record fields, following the
  convention the diagnostics change used, so existing add-ins keep compiling. The file-opened and
  file-closed events stop carrying the dock title as the document's name and path.
- [ ] 1.8 Tests, one per scenario in this phase's delta, named after it, plus: a rename keeps marks shown,
  pushed and saved; two same-named modules in a group keep separate marks; a mark set by automation in the
  wrong case is visible in the gutter; name reuse is refused; a module named `Utilities` saved as `util.bas`
  shows the current-statement bar when a run pauses in it, which is the regression the name-from-URI readers
  would cause.

## 2. Names on the wire

- [ ] 2.1 The seam converter: `file:` from the document's own path when it has one (for a UserControl or
  PropertyPage, the module's path); otherwise
  `untitled:<Project>/<Name>.<ext>`, extension from the kind, no leading slash, built through the URI type so
  it is percent-encoded. Fixed when the session opens, never read live from the path.
- [ ] 2.2 `LspDocumentUri`: `untitled` compares its path without regard to case; remove the `vb6` rules.
- [ ] 2.3 The reverse: a server's reply naming a document resolves to its identity (the existing file-path
  resolver in `EditorService`, plus an `untitled:` branch). Used by diagnostics, definition, rename and
  workspace-symbol results.
- [ ] 2.4 Close and reopen on a name change: on the save event when the path differs from the session's
  (never on the path changing, which a build does temporarily), and on a rename of a document or its project
  when the document has no file. Close then open, ordered per connection, asserted on the wire. Save
  notification afterwards under the new name.
- [ ] 2.4a Two signals the trigger needs and does not have: saving a project into another directory repoints
  every form without raising the save event (add it, beside the module loop that already does), and a form's
  rename is not announced until the designer's pending state is flushed (the "layout changed" notification of
  3.3 carries it).
- [ ] 2.4b Diagnostics under the old name are withdrawn as the close is sent: the pull result id is dropped,
  and for a push server the client records an empty set for that name on that connection, so the ledger and
  the caches keyed on it clear through the existing channel.
- [ ] 2.5 Route every request through the session's current name, gated on the session being open, as the
  carried-file editor already does. The Object Browser's request for a document nobody opened goes through the
  same resolver.
- [ ] 2.6 Retire scheme routing: `SchemeLanguageOf`, the scheme branch of `ClaimantsFor` and the identifier as
  a claim.
- [ ] 2.7 The project-member gate: a project member on an ambiguous extension is offered only to servers that
  claim a VB6 extension no other language uses (every VB6 source extension but `.cls`) or declare `vb6`.
  Membership is stated by the caller when the document is opened and remembered with the session — the
  workspace projection exposes only a directory and folders, and neither parsing an `untitled:` name nor
  matching a path can answer it. Change, close and save route by the same record.
- [ ] 2.8 Open documents survive a root restart: the registry re-opens every document it knows is open on
  each restarted connection before forwarding any change (#469; required here).
- [ ] 2.9 Compiler diagnostics injected under the wire name resolved from the compiler's own (absolute) file
  path, for every kind, not only forms by file stem. Parse the format the compiler actually writes (0.3), and
  convert its line number there and nowhere else: file line = `N + 1 + hidden lines above it`, where hidden
  means the header **and** every `Attribute` line above the error, so the count is computed from the document
  rather than taken as a constant per kind. Depends on #477.
- [ ] 2.10 Export redaction pseudonymises `untitled:` path segments; the redactor's rationale and the
  disclosure's grouping key are rewritten.
- [ ] 2.11 Tests, one per scenario in this phase's deltas, plus the rewrites the retired scheme forces: the routing tests that open `vb6://`, the ambiguous-extension
  guards against a real foreign server (now asserting the project-member gate, plus a carried `.cls` still
  reaching it), the per-server identifier test, the scheme theory. New: close-before-open asserted on the wire
  bytes, in the style of the shutdown wire-shape tests; a build sends nothing; a first save of a pathless form.
- [ ] 2.12 Acceptance: `demo/spring-tide` carries its module as `Module=` rather than `RelatedDoc=`, and the
  demo's server reads it from disk.

## 3. The whole file in the code window

- [ ] 3.1 Keep a form's, UserControl's and PropertyPage's designer text (`VERSION` through the root `End`) as
  read, on the definition beside the model, so the interpreter, the syntax check and the standalone runner can
  compose the file without a code window. A reload adopts it with the rest of the fidelity state.
- [ ] 3.2 Compose the buffer as prefix plus `Code`, and split there on flush. **The prefix is not the protected
  region**: for `.bas`/`.cls` it is the whole header; for `.frm`/`.ctl`/`.pag` it is the designer part alone,
  because their `Code` already begins with the leading `Attribute` run; where load split nothing off (an
  unparseable `.ctl`/`.pag`, a `.bas`/`.cls` whose header was not recognised — #472) it is empty and the buffer
  is `Code`. Prepending the attribute run to a form would show it twice; splitting after it would strip
  `VB_Name` out of `Code` and write a form without one.
- [ ] 3.2a Dirty detection compares the buffer's body — split exactly as the flush splits it — with `Code`,
  for modules and forms alike. Comparing the whole buffer would class every open document as edited and turn
  every external change into a conflict, disabling the silent reload the file-watcher capability requires.
- [ ] 3.3 A "layout changed" notification on the form, raised on a designer commit and by the three paths that
  bypass the designer's undo stack today: the menu editor, the colour palette, and automation's property set
  with no designer open. It flushes the designer's working collections into the model before rendering (they
  run ahead of it until the apply-unsaved-changes event, so a render before the flush misses the control just
  added), refreshes the prefix once per commit, never per drag step, and carries a rename of the form.
- [ ] 3.3a A save is the second refresh trigger: the prefix becomes the header that was just written, before
  the save is announced, so the buffer follows the file even when the render differs from what was read and
  when a companion reference changes with the file's name. A reload is the third.
- [ ] 3.3b A refresh that changes the prefix's line count shifts that document's breakpoints and bookmarks by
  the difference, in the stores rather than the gutter, so a document with no open code window moves too.
- [ ] 3.4 Header render for a form with no file uses `<Name>.frx`. A form held read-only is never re-rendered.
- [ ] 3.5 Where a document's header carries `VB_Name`, it follows a rename, as an edit the IDE makes itself
  (#473: a form's is never retargeted today, so a renamed form's file names two different forms). A form
  HexIDE created carries no attribute block at all, unlike one imported from VB6, which writes five — a
  fidelity gap of its own, recorded rather than fixed here.
- [ ] 3.6 The interpreter and the pre-run syntax check parse the whole text (after 0.2).
- [ ] 3.7 Protection: one section provider subclassing the stock one over the header and member-attribute
  regions, overriding both of its virtual methods — refusing insertion at a region's edges, which it allows,
  and widening a deletion over a member's attribute run so deleting the line it describes takes the run with
  it. It also carries the whole-document verdict, because binding the editor's read-only property would
  replace the provider outright, and it is re-evaluated on reload. A read-only
  *region* is the header or a member's attribute run and nothing else: a form held read-only as a whole must
  still take breakpoints and answer Find, so the mark, Find and attribute rules test the region, never the
  whole-document gate.
- [ ] 3.8 Undo, by the mechanism 0.5 settled: record the refresh in a marked group so every offset stays
  valid; when the developer undoes and the stack reports that group as the most recent, revert it, undo the
  edit beneath, and re-apply the current header. The redo stack is lost at that point, which is stated in the
  release notes rather than left to be discovered. Test: a code edit, a designer move, then undo — the edit is
  undone, the move is not, and a second undo still undoes the right text.
- [ ] 3.9 One guarded write path, with the policy in the design record for each of the twenty programmatic
  writers: formatting reduced to changed lines and clipped; server rename refused if it touches the header;
  Replace, Replace All, completion, Insert File, Enter auto-close, event stubs, add-in `SetContent` and
  `ApplyEdits`, automation `set_file_content`, `type_text` and `press_key`; reload and the Edit-and-Continue
  revert as owner.
- [ ] 3.10 The bundled server keeps to its own new requirement: no diagnostic inside a header, the formatter
  leaves it untouched, and rename and highlight skip it and member attribute runs, through one shared helper
  so the three cannot drift apart. The client clipping stays as the guard against servers that do not.
- [ ] 3.11 Find and Replace search outside read-only regions only.
- [ ] 3.12 Marks refused on read-only lines, including a gutter click on a folded header.
- [ ] 3.13 Edits the IDE makes itself do not raise Edit-and-Continue's reset prompt.
- [ ] 3.14 Folds: the header fold is merged into every fold application, including an empty or absent server
  answer, with the merged list sorted by start offset (the manager throws otherwise) and zero-length folds
  discarded (it skips them silently). It sets its own folded state rather than relying on the library's
  closed-by-default flag, which applies only on the first update. Folded when created or re-created unless
  expanded in this window. Overlapping server folds dropped.
- [ ] 3.15 Greying: a named palette colour for each theme, meeting the dark palette's recorded contrast bar,
  applied after syntax colouring. Theme packs carry the key.
- [ ] 3.16 Line numbers from the top of the file in the margin, status bar, Call Stack, automation and add-in
  surfaces. Record the divergence from VB6's code-window numbering.
- [ ] 3.17 Sidecar migration to the next format (#466 first, so an older build keeps what it cannot read): read the recorded format, re-key, move lines by each header's
  length, carry unmeasurable entries unchanged, keep unrecognised content, rewrite only on change and never
  after a failed read.
- [ ] 3.18 Automation: `get_file_content` returns the whole file open or not; `set_file_content` accepts it
  back, or a body alone with the header kept. Tool descriptions say lines count from the top of the file.
- [ ] 3.19 Add-ins: content is the whole file; positions from the top of the file; `SetContent` refused if the
  header changes. The AI Chat add-in's prompt and apply paths updated.
- [ ] 3.20 New strings (fold labels, refusal reasons, the reworded read-only banner) added to `en` and every
  shipped pack in the same change.
- [ ] 3.21 Tests, one per scenario in the deltas this phase implements, named after the scenario, plus: buffer equals the file on open; an unchanged save writes the buffer; formatting leaves the
  header; Replace All does not touch a control's `Begin` line; a designer move is not undone by code-window
  undo; the header stays folded after formatting; a migrated sidecar keeps marks on their statements and is
  idempotent; an add-in replacement that changes the header is refused.

- [ ] 3.22 Verify in the running IDE, through the automation tools rather than by asking anyone to click:
  open a VB6-authored `.frm`, `.cls` and `.bas`; snapshot the header folded and greyed under a light and a
  dark theme; confirm typing, Replace All and F9 in the header are refused and say why; confirm the margin
  and status bar count from the top of the file; confirm a designer move updates the header with the code
  window open. UI work is not complete until it is seen running.

## 4. Member attributes

- [ ] 4.1 Detect procedure-level and declaration-level `Attribute` runs, and anchor each to the line it
  describes.
- [ ] 4.2 Read-only and greyed, through the same provider and colour as the header.
- [ ] 4.3 Folds from the end of the described line, built with character offsets and nested inside a server's
  procedure fold.
- [ ] 4.4 A member's rename rewrites its own attribute qualifiers — a procedure, property or module-level
  variable alike, since VB6 writes `VB_Var*` lines after a declaration. A server rename's edits to them are
  permitted; its edits to the header are not.
- [ ] 4.5 Enter at the end of a described line inserts after the attribute run, not between the line and its
  attributes.
- [ ] 4.5a A run follows the line it describes: deleting that line deletes the run with it (the provider's
  deletable span widens over the run), and a cut takes it along. An orphaned run, however it arises, is
  preserved as inert text.
- [ ] 4.6 Corpus lane: files carrying member-level attributes round-trip, which no lane covers today.
- [ ] 4.7 Tests, one per scenario in this phase's delta, plus a deletion case for a described procedure.
- [ ] 4.8 Verify in the running IDE, through the automation tools: a procedure's attributes folded into its
  declaration line, refusing an edit, and surviving a format.

## 5. Documentation

- [ ] 5.1 `docs/lsp-client.md`: identifiers, the scheme retirement, the project-member gate, close and reopen.
- [ ] 5.2 `docs/language-servers.md`: extensions are the only claim; `languageId` names a language and routes
  nothing except as the ambiguity gate.
- [ ] 5.3 `docs/lsp-server-features.md`: the example trace line; client-made folds; the formatter's header
  rule; what the server now accepts.
- [ ] 5.4 `docs/mcp-server-gaps.md`: every automation contract change, recorded and filed.
- [ ] 5.5 `docs/debugger-vb6-divergences.md`: the visible header, file-counted line numbers, and designer
  changes exempt from the reset prompt.
- [ ] 5.6 `docs/MISSING_FEATURES.md` and `docs/MISSING_LANGUAGE.md`: line numbers, folding, rename, Find and
  the Procedure Attributes row (#62). The attribute rows whose mechanism text says "stripped from the body".
- [ ] 5.7 Comments that describe the old model: `FormCodeText`, `ModuleFileFormat`, `ModuleDefinition`,
  `DirtyDetector`, the redactor and disclosure, `DocumentSavedEvent`, `IBreakpointService`, the automation
  tool descriptions.
- [ ] 5.8 Validate and archive; rewrite the Purpose of each spec the archive touched if the CLI replaces it,
  and fill in the new capabilities' Purpose.
