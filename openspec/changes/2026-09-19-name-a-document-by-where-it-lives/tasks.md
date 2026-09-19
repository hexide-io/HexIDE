# Tasks

## 0. Measurements the rest depends on

- [ ] 0.1 Confirm in the running IDE what a form's code window shows today. The code says the leading
  `Attribute VB_*` block is visible and editable, while `FormCodeText`'s remarks, the `set_file_content`
  description and its result message say hidden. Record the answer and correct whichever is wrong.
- [ ] 0.2 Prove both grammars on whole files. Every corpus `.frm`, `.cls`, `.ctl` and `.bas` parses, whole,
  through the interpreter's grammar and the bundled server's grammar with no syntax error, and the bundled
  server raises no diagnostic inside a header. No `.pag` exists in any corpus: author one in the VB6 VM and add
  it. This gates 3.6, and a failure here is a grammar task, not a reason to feed the body alone.
- [ ] 0.3 Oracle, recorded in `docs/vb6-fidelity-oracle.md`: the format and line base of a compile error in
  VB6's `/out` log (file line or code line, 0- or 1-based); whether two projects in one group may share a name;
  whether a form and a module in one project may share a name.
- [ ] 0.4 Foreign servers and `untitled:`. For each tolerant server, a test that opens
  `untitled:Project1/Module1.<ext>` and asserts diagnostics arrive under exactly that name. For the server that
  refuses non-`file:` URIs, assert the refusal on the wire or on stderr, not that the call returned. Include a
  non-ASCII name, which one server drops without replying. Re-measure the server whose delivery mode changed
  because the seeding probe accepted dynamic registration.
- [ ] 0.5 AvaloniaEdit 12.0.0 behaviours the protection and fold design assume: insertion at a read-only
  section's edge, whether `IsReadOnly` replaces the section provider, how undo treats an edit it did not record,
  and the first-update rule for folds that start closed. **Blocked: needs a decompiler, and none is installed.**
  Every one of these is inferred today from the library's lineage, not read from the binary.

## 1. Identity inside the IDE

- [ ] 1.1 A document identity: a value comparing by reference to the document's definition, holding its
  project. A UserControl or PropertyPage has one identity (its module), as the editor already chooses. Written
  `<Project>/<Name>` for display and lookup, case-insensitively, and never used as a key in that form.
- [ ] 1.2 Re-key the breakpoint and bookmark stores, both gutters and the F9 and bookmark commands on it. The
  gutters and commands must read one live value, never one frozen at attach and another recomputed later.
- [ ] 1.3 Replace the six places that read a module name out of a URI (debug module name, Run To Cursor, Set
  Next Statement, the runner's live push, `AddinDiagnosticsService.ExtractName`, the sidecar's project lookup)
  with the identity's definition and project. The runner's live push is scoped to the running project.
- [ ] 1.4 Replace the seven places that mint `vb6://` by hand. What remains of a URI at this phase comes from
  one converter at the seam, so phase 2 changes one function.
- [ ] 1.5 Sidecar keyed by document name within its project's file. Store unload and clear are scoped by
  project, not recomputed from current names.
- [ ] 1.6 Automation: resolve a document by project and name across every loaded project, then key by
  identity. That fixes minting from the caller's spelling. Replies carry `project` and `document`.
- [ ] 1.7 Names: new forms, modules and classes never repeat a name in their project; a rename that would is
  refused; a new project never takes a loaded project's name. Refusal reasons are localization keys.
- [ ] 1.8 Tests: a rename keeps marks shown, pushed and saved; two same-named modules in a group keep separate
  marks; a mark set by automation in the wrong case is visible in the gutter; name reuse is refused.

## 2. Names on the wire

- [ ] 2.1 The seam converter: `file:` from the document's own path when it has one; otherwise
  `untitled:<Project>/<Name>.<ext>`, extension from the kind, no leading slash, built through the URI type so
  it is percent-encoded. Fixed when the session opens, never read live from the path.
- [ ] 2.2 `LspDocumentUri`: `untitled` compares its path without regard to case; remove the `vb6` rules.
- [ ] 2.3 The reverse: a server's reply naming a document resolves to its identity (the existing file-path
  resolver in `EditorService`, plus an `untitled:` branch). Used by diagnostics, definition, rename and
  workspace-symbol results.
- [ ] 2.4 Close and reopen on a name change: on the save event when the path differs from the session's
  (never on the path changing, which a build does temporarily), and on a rename of a document or its project
  when the document has no file. Close then open, ordered per connection. Save notification afterwards under
  the new name. A pull result id for the old name is dropped.
- [ ] 2.5 Route every request through the session's current name, gated on the session being open, as the
  carried-file editor already does. The Object Browser's request for a document nobody opened goes through the
  same resolver.
- [ ] 2.6 Retire scheme routing: `SchemeLanguageOf`, the scheme branch of `ClaimantsFor` and the identifier as
  a claim.
- [ ] 2.7 The project-member gate: a project member on an ambiguous extension is offered only to servers that
  claim an unambiguous VB6 extension or declare `vb6`. Membership comes from the workspace projection the
  registry already holds.
- [ ] 2.8 Open documents survive a root restart: the registry re-opens every document it knows is open on
  each restarted connection before forwarding any change (filed separately as a defect; required here).
- [ ] 2.9 Compiler diagnostics injected under the wire name resolved from the compiler's own file path, for
  every kind, not only forms by file stem.
- [ ] 2.10 Export redaction pseudonymises `untitled:` path segments; the redactor's rationale and the
  disclosure's grouping key are rewritten.
- [ ] 2.11 Tests rewritten for the retired scheme: the routing tests that open `vb6://`, the ambiguous-extension
  guards against a real foreign server (now asserting the project-member gate, plus a carried `.cls` still
  reaching it), the per-server identifier test, the scheme theory. New: close-before-open asserted on the wire
  bytes, in the style of the shutdown wire-shape tests; a build sends nothing; a first save of a pathless form.
- [ ] 2.12 Acceptance: `demo/spring-tide` carries its module as `Module=` rather than `RelatedDoc=`, and the
  demo's server reads it from disk.

## 3. The whole file in the code window

- [ ] 3.1 Keep a form's, UserControl's and PropertyPage's header text as read, alongside the model the reader
  builds today.
- [ ] 3.2 Compose the buffer as header plus `Code`. Split at the header's end on flush. `Code`, the serializers
  and dirty detection are unchanged.
- [ ] 3.3 A "layout changed" notification on the form, raised on a designer commit and by the three paths that
  bypass the designer's undo stack today: the menu editor, the colour palette, and automation's property set
  with no designer open. It refreshes the header once per commit, never per drag step.
- [ ] 3.4 Header render for a form with no file uses `<Name>.frx`. A form held read-only is never re-rendered.
- [ ] 3.5 `VB_Name` follows a rename, for every kind, as an edit the IDE makes itself.
- [ ] 3.6 The interpreter and the pre-run syntax check parse the whole text (after 0.2).
- [ ] 3.7 Protection: a read-only section provider over the header and member-attribute regions, combined with
  the whole-document gate, re-evaluated on reload, and refusing insertion at the top of the file.
- [ ] 3.8 Undo: a designer change is not undoable from the code window, and earlier code edits still undo the
  right text (after 0.5, which decides the mechanism).
- [ ] 3.9 One guarded write path, with the policy in the design record for each of the twenty programmatic
  writers: formatting reduced to changed lines and clipped; server rename refused if it touches the header;
  Replace, Replace All, completion, Insert File, Enter auto-close, event stubs, add-in `SetContent` and
  `ApplyEdits`, automation `set_file_content`, `type_text` and `press_key`; reload and the Edit-and-Continue
  revert as owner.
- [ ] 3.10 The bundled formatter leaves the header untouched, as well as the client clipping.
- [ ] 3.11 Find and Replace search outside read-only regions only.
- [ ] 3.12 Marks refused on read-only lines, including a gutter click on a folded header.
- [ ] 3.13 Edits the IDE makes itself do not raise Edit-and-Continue's reset prompt.
- [ ] 3.14 Folds: the header fold is merged into every fold application, including an empty or absent server
  answer. Folded when created or re-created unless expanded in this window. Overlapping server folds dropped.
- [ ] 3.15 Greying: a named palette colour for each theme, meeting the dark palette's recorded contrast bar,
  applied after syntax colouring. Theme packs carry the key.
- [ ] 3.16 Line numbers from the top of the file in the margin, status bar, Call Stack, automation and add-in
  surfaces. Record the divergence from VB6's code-window numbering.
- [ ] 3.17 Sidecar migration to the next format: read the recorded format, re-key, move lines by each header's
  length, carry unmeasurable entries unchanged, keep unrecognised content, rewrite only on change and never
  after a failed read.
- [ ] 3.18 Automation: `get_file_content` returns the whole file open or not; `set_file_content` accepts it
  back, or a body alone with the header kept. Tool descriptions say lines count from the top of the file.
- [ ] 3.19 Add-ins: content is the whole file; positions from the top of the file; `SetContent` refused if the
  header changes. The AI Chat add-in's prompt and apply paths updated.
- [ ] 3.20 New strings (fold labels, refusal reasons, the reworded read-only banner) added to `en` and every
  shipped pack in the same change.
- [ ] 3.21 Tests: buffer equals the file on open; an unchanged save writes the buffer; formatting leaves the
  header; Replace All does not touch a control's `Begin` line; a designer move is not undone by code-window
  undo; the header stays folded after formatting; a migrated sidecar keeps marks on their statements and is
  idempotent; an add-in replacement that changes the header is refused.

## 4. Member attributes

- [ ] 4.1 Detect procedure-level and declaration-level `Attribute` runs, and anchor each to the line it
  describes.
- [ ] 4.2 Read-only and greyed, through the same provider and colour as the header.
- [ ] 4.3 Folds from the end of the described line, built with character offsets and nested inside a server's
  procedure fold.
- [ ] 4.4 A procedure rename rewrites its own attribute qualifiers. A server rename's edits to them are
  permitted.
- [ ] 4.5 Enter at the end of a described line inserts after the attribute run, not between the line and its
  attributes.
- [ ] 4.6 Corpus lane: files carrying member-level attributes round-trip, which no lane covers today.

## 5. Documentation

- [ ] 5.1 `docs/lsp-client.md`: identifiers, the scheme retirement, the project-member gate, close and reopen.
- [ ] 5.2 `docs/language-servers.md`: extensions are the only claim; `languageId` names a language and routes
  nothing except as the ambiguity gate.
- [ ] 5.3 `docs/lsp-server-features.md`: the example trace line; client-made folds; the formatter's header rule.
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
