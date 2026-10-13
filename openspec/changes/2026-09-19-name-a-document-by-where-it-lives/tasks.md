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
  — **The last clause is true and insufficient, found while implementing 3.8 and corrected in `design.md`.**
  `LastGroupDescriptor` identifies the most recent group *opened*, which is not the same as the top of the
  stack: an `Undo()` clears it, an ordinary change clears it, and an empty group sets it with nothing behind
  it. It cannot answer the question after a second undo. What does is an `IUndoableOperation` inside the
  group — see 3.8.

## 1. Identity inside the IDE

- [x] 1.1 `DocumentIdentity` (HexIDE.Core) — the definition compared by reference, carrying its project;
  `Name`, `AbsolutePath` and `Display` read live and nothing keys on any of them. A UserControl or
  PropertyPage resolves to its module, and its file is the module's. **Read off the form rather than found
  by scanning**: `ModuleDefinition.UpdateFormPart` now records the link on the form too, because a scan
  answers "a plain form" in the several statements between a UserControl's halves being joined and its
  module joining the project — in both the creation path and the load path — and that identity compares
  unequal to every later one.
- [x] 1.2 Both stores, both gutters and the F9 and Ctrl+F2 commands take the identity. `ClearProject` was
  added to both stores for the unload path and **filters the store's own keys**, never the project's current
  documents: a document removed while it carried marks is in neither list, and its entry would be
  unreachable *and* would hold the whole project graph alive through the definition's `Owner`. Found and
  fixed while re-keying: `BookmarkService.SetBookmarks(document, [])` emptied the store without raising its
  change event, so the gutter kept its dots and the sidecar never rewrote — against the `set_bookmarks`
  tool's own description.
- [x] 1.3 All six gone. The live push is scoped through a new `IRunScope`, which holds the running project
  as a **value rather than an event**: the editor that most needs it is the one opened *by* a break, so it
  was never listening when the event fired. It is a service of its own because an editor cannot depend on
  the runner (the cycle runs through the editor factory). `RevealBreak` now opens the editor in the running
  project rather than the startup one.
- [x] 1.4 One converter, `DocumentWireName.For`. All eight sites go through it.
- [x] 1.5 Keyed by the document's name within its project's file. **Old `vb6://` keys are still read**, and
  the two are told apart by the key itself: a VB6 name cannot look like a URI, so re-keying costs no version
  step and `version` still records the line base, which this change does not move. Unload and clear are
  scoped by project through `ClearProject`, and `FindProjectForUri` is gone — the identity carries its
  project. Duplicate names within one project union their lines rather than overwriting, so a legacy project
  holding both a form and a module called `Thing` cannot lose one's marks to the other.
- [x] 1.6 One resolver (`DocumentLookup.Find`) across every loaded project, case-insensitive, with an
  optional `project` argument; an ambiguous bare name is refused and the refusal lists the candidates.
  Replies carry `project`, `document` and the wire `uri`, and a mutating reply reports what the document
  holds afterwards — including when that is nothing. Recorded in `docs/mcp-server-gaps.md`.
  `clear_all_breakpoints`'s description is corrected to say it clears every loaded project; whether VB6
  agrees with that is filed as #492.
- [x] 1.7 Generation (`ProjectNaming.NextFreeName`) is the lowest unused index pooled across **every** kind,
  which the two Add Form commands and the four Add Module commands now share; new project names likewise.
  Adoption refuses a taken or invalid name and says which name it objected to, because Add File is
  multi-select. A form's rename is refused at the root component's Name property; a project's in the
  properties dialog, which now shows the reason beside the box rather than only disabling OK. Five
  `Str.Naming.Msg.*` keys, translated into all 29 shipped packs.
  - **A module, class, UserControl or PropertyPage cannot be renamed through any path in the IDE**:
    `ModuleDefinition.Name` has a public setter with no assignment anywhere outside its constructor, and the
    Properties window binds only a form designer. So the rename half of this task covers forms and projects,
    which are the only two renames that exist. The rule itself is in `ProjectNaming` and applies wherever a
    module rename is eventually built. Filed as #493.
- [x] 1.7b Project-qualified **overloads** rather than trailing optional arguments on the interface methods,
  deviating from this task's letter for a measured reason: an add-in is loaded as a pre-built assembly
  through `Assembly.Load`, and C# bakes a default argument into the *call site*, so a default parameter
  keeps an add-in compiling and breaks every one already packaged with `MissingMethodException`. The records
  do take trailing optional fields, which is the precedent this task cites and where it holds.
  `IProjectAccess.GetProjects()` was added, without which the new argument is undiscoverable. The
  file-opened and file-closed events carry the document, and a tab with no document reports an empty path
  rather than its title.
- [x] 1.8 `DocumentIdentityTests`, `DocumentLookupTests`, `ProjectNamingTests`, `ProjectNameRefusalTests`,
  `CurrentStatementBarTests`, plus the re-keyed `BreakpointServiceTests`, `UserSidecarBreakpointTests` and
  `AddExistingFileTests`. The sidecar's on-disk key is asserted by reading the file, which no round-trip
  through two service instances could show. **Both halves of the current-statement check were proved by
  mutation**: removing the project test fails the two-projects case, and removing the name test fails the
  renamed-module case.

## 2. Names on the wire

> **#489 is settled and does not gate this phase** (2026-09-20). Measured against the real VB6 IDE: no file
> is written for a form or module until the project is saved, in a saved project as much as an unsaved one.
> So a pathless document is a state this phase must *name*, not one the IDE could have designed away. 2.1 is
> unchanged. See the design record, and `docs/vb6-fidelity-oracle.md` for the measurement.

- [x] 2.1 The seam converter: `file:` from the document's own path when it has one (for a UserControl or
  PropertyPage, the module's path); otherwise
  `untitled:<Project>/<Name>.<ext>`, extension from the kind, no leading slash, built through the URI type so
  it is percent-encoded. Fixed when the session opens, never read live from the path.
  — `DocumentWireName.For` is the whole change; `LspDocumentUri.ForUntitled` owns the spelling, beside
  `ForFile`, because construction and comparison have to agree. The extension comes from a new
  `DocumentIdentity.Extension`, read off the kind rather than off a path a pathless document does not have.
  **Fixing the name at session open is 2.5's half** — this answers what a session should be *opened* under.
  Found on the way: `DocumentIdentity.AbsolutePath` was `module?.AbsolutePath ?? form!.AbsolutePath`, which
  reads correctly and throws for any module with no file — `??` evaluates the right side and dereferences a
  form that is null by construction. Nothing had asked a pathless document for its path until this did.
- [x] 2.2 `LspDocumentUri`: `untitled` compares its path without regard to case; remove the `vb6` rules.
  — Both segments are VB6 names. Asserted that an `untitled:` URI survives normalisation at all, on its own,
  because it has no authority and is therefore not the hierarchical shape every `file:` case exercises: a
  scheme the URI parser handled differently would make the other cases pass for the wrong reason.
- [x] 2.3 The reverse: a server's reply naming a document resolves to its identity (the existing file-path
  resolver in `EditorService`, plus an `untitled:` branch). Used by diagnostics, definition, rename and
  workspace-symbol results.
  — No `untitled:` branch was needed in the end: `EditorService.Names` already compared the URI against
  `DocumentWireName.For(document)` as well as the file path, and 2.1 made the first of those the `untitled:`
  spelling. The two branches now answer the same string for a document that has a file and diverge only for
  one that does not, which is what lets a single method answer for modules, forms and carried files alike.
  Proved by rewriting the navigation tests onto the new spellings, plus two cases the retired scheme could
  not express at all: a `Module1` in another project of the same group, and the same name with the wrong
  extension.
- [x] 2.4 Close and reopen on a name change: on the save event when the path differs from the session's
  (never on the path changing, which a build does temporarily), and on a rename of a document or its project
  when the document has no file. Close then open, ordered per connection, asserted on the wire. Save
  notification afterwards under the new name.
  — `LspDocumentSession.RenameAsync`: cancel the debounce (it belongs to the old name), close under the old
  name, reassign, reset the version, open under the new one. **Both halves awaited**, unlike `Start` and
  `Dispose` which are fire-and-forget — nothing else orders them, and an open that overtakes its close
  leaves the server holding the document twice under a name it will never be told to release.
  — **`Uri` is reassigned only after the close returns.** The close raises a clearing publication under the
  old name, which comes back through the session's own diagnostics filter; reassigning first makes the
  session ignore its own withdrawal and the markers stay on screen, attributable to nothing.
  — A `renaming` flag, because `IsOpen` is `started && !disposed` and both describe the whole session rather
  than this moment. Without it a hover issued between the close and the open passes 2.5's gate and names a
  document neither connection has heard of.
  — Three triggers, not one. A save (via `ReconcileThenAnnounceSaveAsync`, which renames **then** announces
  — the other order tells a server about a write to a document it is about to be told to forget); the
  document's own rename; and its **project's** rename, since the project name is half the `untitled:`
  spelling. The last two fire with no save at all, which after #489 is the ordinary case for a new document.
  — Compared against `DocumentWireName.For(Identity)`, never against the event's own `Form.AbsolutePath`:
  the event matches either half of a UserControl and the code window's Save repoints the form part's path
  alone (#474), so reading the path off the event re-opens a UserControl under the wrong name, sometimes.
  — All five view-model tests checked by disabling both triggers and watching four of them redden.
- [x] 2.4a Two signals the trigger needs and does not have: saving a project into another directory repoints
  every form without raising the save event (add it, beside the module loop that already does), and a form's
  rename is not announced until the designer's pending state is flushed (the "layout changed" notification of
  3.3 carries it).
  — One publish in `SaveProjectToDirectory`'s form loop. `SerializeFormToFile` repoints every form's
  `AbsolutePath`, so that loop renames each of them as far as the language layer is concerned — and the
  module loop beside it had always announced, through `SaveModuleCore`. Two halves of one method
  disagreeing, which is why it survived: the existing test asserts the modules and passes either way.
  — The form-**rename** half of this task is deferred to 3.3 and is stated rather than silently dropped: a
  `FormDefinition`'s `Name` is derived from its root component and only raises `PropertyChanged` when
  `UpdateComponents` runs, so a rename in the designer is not observable until the pending state is flushed.
- [x] 2.4b Diagnostics under the old name are withdrawn as the close is sent: the pull result id is dropped,
  and for a push server the client records an empty set for that name on that connection, so the ledger and
  the caches keyed on it clear through the existing channel.
  — Needed a new ledger operation, and the reason is worth keeping: `Record(uri, owner, [])` removes that
  owner's row and returns the union of the **rest**, so a form carrying both a server diagnostic and an
  injected compiler one still publishes a non-empty set. Under a name the document no longer answers to,
  the compiler's rows are marks nothing will ever clear. `DiagnosticLedger.Forget(uri)` is the per-URI,
  all-owners counterpart to `Withdraw`'s per-owner, all-documents.
  — A **rename-specific close** (`CloseDocumentForRenameAsync`) rather than widening the existing gate.
  `ForgetPullState` raises its clearing publication only for a server that answers when asked, and that is
  right for an ordinary close — a push server clears a document itself, and pre-empting it fights a server
  that has a view. For a rename it is wrong: the name is going away, so no later publication is coming.
  — An ordinary close deliberately still leaves the compiler's rows alone, and that has its own test. A
  build's claim about a form outlives the editor that happened to be showing it.
- [x] 2.5 Route every request through the session's current name, gated on the session being open, as the
  carried-file editor already does. The Object Browser's request for a document nobody opened goes through the
  same resolver.
  — `GetDocumentUri()` is replaced by `LiveDocumentUri`, which reads the session and answers null when none
  is open; the ten request methods answer emptily rather than asking. The mint survives in exactly one
  place, the `LspDocumentSession` constructor.
  — **The defect this closed was silent, and it is the one 2.1 would otherwise have shipped.** The session's
  name is fixed at open, but every request minted a fresh one from the identity, which reads the path live.
  So from a document's first save the lifecycle notifications still said `untitled:` while every request
  said `file:`. The bundled server answers an unknown URI with an empty array and no error, so hover,
  completion, folding, Go To Definition, rename and formatting went quiet with nothing logged and nothing
  thrown, and the procedure dropdown emptied on the next diagnostics tick.
  — `GetDocumentUriPublic` deliberately keeps a fallback to the minted name. Its two callers compare a
  server's reply against "this document", and a null would read as "not this one" — the wrong answer for a
  reply that can only have been about this document. A comparison is not a request, so it is not gated.
  — **The spec delta does not reach this.** The contract is requirement prose in `specs/lsp-client/spec.md`
  ("fixed when the document is opened … never by reading the document's current path") and its only scenario
  is *Building the project*, which speaks about closed/opened/moved and so says nothing about a request sent
  under a recomputed name. Covered by a deliberate test rather than a scenario count, and both new tests
  were checked by reverting `LiveDocumentUri` to the old expression and watching them redden.
- [x] 2.6 Retire scheme routing: `SchemeLanguageOf`, the scheme branch of `ClaimantsFor` and the identifier as
  a claim.
  — Done **after** 2.7 and 2.8, per 2.7a, and that ordering earned itself: the branch was already
  unreachable, so removing it first would have turned every negative routing assertion vacuous instead of
  red, with nothing left to notice.
  — The identifier's claim is the one behavioural loss, and it is asserted rather than left implicit.
  `AnEntryDeclaringTheSchemeLanguageStillWorksWhateverItsExtensions` is **inverted** into
  `DeclaringTheLanguageWithoutClaimingAnExtensionNoLongerRoutesAnything`, with a companion,
  `DeclaringTheLanguageIsStillWhatLetsAnEntryBeOfferedAProjectsClassModules`, so that the inversion cannot
  be read as "the identifier means nothing now" and the 2.7 gate deleted after it. Nothing real regresses:
  an entry claiming a made-up extension and nothing else was never going to be handed a `.bas` on disk
  either, so this removes an inconsistency rather than a capability.
  — `OnlyHexIdesOwnSchemeNamesALanguage` tested a function that no longer exists and is **replaced** by
  `OnlyAVb6ClaimEstablishesVb6`, which covers what took its job. Its `.md` row is the load-bearing one:
  said loosely as "an extension no other language uses", the rule would admit a Markdown server.
  — The sweep: the `Vb6Doc` / `Form1` / `Form1Uri` constants in five suites, the workspace-symbol open, the
  foreign-server exclusion guard and one integration publish. Two `vb6://` uses are deliberately left —
  `UserSidecarService` still *reads* the old key, which is the migration phase 1 recorded, and
  `ConversationDisclosureTests` uses opaque URIs that 2.10 owns.
- [x] 2.7 The project-member gate: a project member on an ambiguous extension is offered only to servers that
  claim a VB6 extension no other language uses (every VB6 source extension but `.cls`) or declare `vb6`.
  Membership is stated by the caller when the document is opened and remembered with the session — the
  workspace projection exposes only a directory and folders, and neither parsing an `untitled:` name nor
  matching a path can answer it. Change, close and save route by the same record.
  — **Done first, and it turned out to be repairing a live regression rather than adding a guard.**
  `SchemeLanguageOf` requires a literal `://`, which neither `untitled:` nor a `file:` URI's `vb6` test can
  satisfy, so once 2.1 stopped minting `vb6://` the scheme branch of `ClaimantsFor` became unreachable and
  the `.cls` gate went with it. Both existing guards stayed green because both opened
  `vb6://module/Module1` — a string with no extension, so routing returned no claimants and the assertion
  held without reaching the rule it names. Proved by writing the same assertion against
  `untitled:Project1/Class1.cls`: red before this task, green after. `ALatexServerClaimingClsIsNotOfferedVb6Modules`
  was **retargeted rather than supplemented**, because a guard that cannot fail is worse than no guard, and
  it now runs against real texlab.
  — The predicate is `DocumentLanguage.EstablishesVb6(extensions, languageId)`, lifted from the dead
  `Claims`. Applying it to *every* member is the same rule as applying it only on ambiguous extensions, so
  there is one predicate and no second test: on `.bas`, `.frm`, `.ctl` or `.pag` the extension that matched
  is itself unambiguous, so the entry satisfies the gate and it is the identity. `.cls` is the only VB6
  source extension it can exclude, and excluding it is the point.
  — An **overload** on `ILspClient`, never a defaulted `bool`. A `bool` defaulted after the cancellation
  token compiles at every call site and then silently changes what each means: every
  `Received().OpenDocumentAsync(uri, text, Arg.Any<CancellationToken>())` would assert against an implicit
  `false` production no longer passes, and the resulting red reads as a routing regression. The same
  reasoning as the add-in overloads in phase 1, for the same reason.
  — The registry's record is keyed **ordinally**, deliberately not `LspDocumentUri.Comparer`: the
  per-connection tracker it shadows uses the default comparer, and two stores keyed differently
  desynchronise on exactly the case-folding case a URI comparer exists for. `CloseDocumentAsync` routes
  **before** dropping the record, or the close would reach a different set of servers than the open did.
  — The five view-model assertions now pin the **value** (`true` from the code window, `false` from the
  carried-file editor) rather than `Arg.Any`, because stating the wrong one is the defect.
- [x] 2.7a Order 2.6 after 2.7, and prove the gate before the scheme goes. `Claims()` exists because of
  #277, where a VB6 server attached as `vba` started, initialized and was then never sent a document, and it
  is reachable today only from the branch 2.6 retires. Retiring that branch before 2.7's gate is built and
  asserted against a real foreign server reopens exactly that silence, with nothing to catch it.
  — Honoured, and it bought more than it promised. The branch was not merely *about* to become unreachable:
  it already was, so the `.cls` gate had gone with it and #279 was live on the branch. Building 2.7 first
  is what surfaced that; deleting the branch first would have turned every negative routing assertion
  vacuous instead of red, and there would have been nothing left to notice with.
- [x] 2.8 Open documents survive a root restart: the registry re-opens every document it knows is open on
  each restarted connection before forwarding any change (#469; required here).
  — Driven from 2.7's record rather than from the triggering document's claimants, because those are two
  different sets and the teardown used the wider one: every entry is stopped, while the caller goes on to
  start and open only the claimants of the one document that triggered it. So a Markdown server holding a
  carried file was stopped with nothing to re-open it.
  — The record carries the latest **text**, updated on change **before** the change is routed. Between the
  teardown and the next server starting, `StartedClaimantsFor` yields nothing and a change goes nowhere;
  remembering only text that had been routed successfully would re-open the document with the text from
  before it and silently undo what was typed.
  — The triggering URI is skipped, because its caller opens it immediately afterwards. It is not yet in the
  record on a first open — the record is written after the restart check returns — but a re-open of a
  document already known would otherwise be sent twice.
  — `RestartIfWorkspaceMovedAsync` now holds a `SemaphoreSlim` across the whole of the teardown and the
  replay. Without it two opens can both pass the `SameDirectory` check before either reaches
  `_rootedAt = current`, which is ordinary — loading a `.vbg` opens several editors and nothing gates
  `LspDocumentSession.Start`. That used to race to a duplicate `StopAsync` and be swallowed; it would now
  replay every open document twice.
  — **The five tests were each checked by removal, not just written.** Deleting the replay reddens two;
  the other three pass vacuously without it, which is recorded here rather than left to be discovered:
  they constrain the replay's behaviour and only bite once it exists. Removing the triggering-URI skip
  reddens the duplicate guard specifically.
  — Not covered, and filed as a gap rather than papered over: **Save Project As of an already-saved project
  moves the root with no document event at all**, because `SaveProject` hardcodes `saveAs: false` for every
  document and only the `.vbp` is repointed. It is a 2.8 trigger with no 2.4 counterpart.
- [x] 2.9 Compiler diagnostics injected under the wire name resolved from the compiler's own (absolute) file
  path, for every kind, not only forms by file stem. Parse the format the compiler actually writes (0.3), and
  convert its line number there and nowhere else: file line = `N + 1 + hidden lines above it`, where hidden
  means the header **and** every `Attribute` line above the error, so the count is computed from the document
  rather than taken as a constant per kind. Depends on #477.
  — **The line conversion counts rather than offsets, and is correct today rather than after phase 3.**
  VB6's `N` is a 0-based index into the code view, which excludes **every** `Attribute` line, module-level
  and procedure-level alike (oracle probe d1). The editor's buffer today hides a module's header run,
  shows a form's attribute run, and shows procedure-level attributes in both — so no per-kind offset is
  right for all three, and one that is right today would be wrong the moment phase 3 composes the whole
  file. `EditorLineOf` walks the buffer skipping exactly what VB6 skipped; when 3.2 puts the header in the
  buffer, `IsHiddenFromVb6LineCount` gains the header's lines and nothing else changes. The rejected
  alternative was building `N + 1 + hidden` now and accepting a marker 1–25 lines too low until phase 3,
  which is a wrong answer that runs.
  — The regex matched `path(N) : error C0001: …`, which is a C compiler's format and nothing VB6 has ever
  written, so that consumer had never had live traffic at all (#477). Anchored on `', Line ` and on ` : `
  rather than on the message, because a message may contain a colon (`Expected: expression`, measured) and
  a path may contain an apostrophe.
  — The log is read through `Vb6TextFile.Decode` rather than `File.ReadAllText`, which assumes UTF-8 when
  the log is ANSI. A non-ASCII character anywhere in the absolute path became U+FFFD before the regex saw
  it, and the path is how a diagnostic finds its document — so every error in such a project would have
  been dropped in silence. Latin-1 only; a true ANSI codepage needs `System.Text.Encoding.CodePages`, a
  package and a licence row, and is not worth that here.
  — Resolution is by **path, across every kind**. It matched `project.Forms` only, by file stem, so a
  `.bas`, `.cls`, `.ctl` and `.pag` were all dropped, and so was a form whose file is not named after it —
  which VB6 permits and the oracle records.
  — No severity is parsed, because `vb6.exe` writes none: the log says `Compile Error in File` and a
  compile error is never a warning.
  — `AFailedBuildsErrorsSitBesideTheServersOnTheSameForm` now proves its own name. Its fixture published
  under one URI while production injected under another, two ledger documents that `GetAll()` flattens
  into one list — so "both are present" passed whether or not they shared a document. It now asserts one
  distinct `FileName`.
  — The attribute walk was checked by neutering `IsHiddenFromVb6LineCount` and watching its test redden.
- [x] 2.10 Export redaction pseudonymises `untitled:` path segments; the redactor's rationale and the
  disclosure's grouping key are rewritten.
  — **The premise the redactor was built on is gone, and it was written down as a reason to do less.** Its
  opening paragraph said VB6 forms and modules rode an opaque scheme carrying only a component name, so the
  primary editor's traffic was already free of paths and only three places needed rewriting. Task 2.1
  retired that scheme. The editor's traffic is now the *largest* source of real names in a capture, and
  `untitled:<Project>/<Name>.<ext>` is made of two words the developer typed. Both paragraphs are rewritten
  rather than amended, and the old claim is recorded, because a reader meeting it would reasonably conclude
  this file had less to do than it does.
  — Two places, and **both** were needed: the expression that finds a URI inside a body, and the guard in
  `Uri`. Widening either alone leaves a working-looking redactor with a hole — the body path is the one
  every export actually takes.
  — Pseudonymised **segment by segment, as a path**, not replaced whole. The project's name and the
  document's name stay distinguishable and stay related, so a reader can still see that two messages
  concerned two documents of one project; that relationship is the only structure an `untitled:` name
  carries. The extension survives for the same reason it does in a `file:` URI — routing is by extension,
  so `.cls` against `.frm` is a diagnosis.
  — Case is **not** folded, deliberately, and it matters more here than for a path: two `untitled:` names
  are compared case-insensitively by the client, so two spellings reaching the wire is exactly the defect an
  export is sent to diagnose.
  — An unknown scheme is still left alone, and the test that used to assert this of `vb6:` is **retargeted
  rather than deleted** — a guard that can no longer fail is worse than none. The rule it now states is the
  one that survives: a scheme this file does not understand may not hold a path at all, so splitting it on
  slashes would produce a plausible URI meaning something else.
  — **The disclosure now counts per document rather than per filename.** It keyed on the URI's last
  segment, so two projects each holding a `Module1` — or two directories, which is just as ordinary —
  became one row whose copy count and byte total belonged to neither. The reader sees one modest file and
  sends two. `DisclosedDocument` gains `Uri` (the key) beside `Document` (the label), because making
  `Document` the URI would put a pseudonym-shaped wire name on the one surface addressed to the person who
  owns the file.
  — A colliding label is widened by one segment and no further — the project's name for an `untitled:`
  document, the containing directory for a saved one. Widening everything would print a path in front of
  every filename to someone who already knows where their own files live. The label unescapes and the key
  does not, so a project called `Bill of Fare` reads as itself while the grouping still uses what was sent.
  — `Trail` drops the scheme before splitting. An `untitled:` URI has no `//` after its colon, so its
  scheme is part of the first path segment and a naive split hands the wire name straight back — caught by
  a test rather than by reading.
  — **Three of the six new redactor tests passed with the feature deleted** when first written: they
  asserted the output ended in `.frm`, split into two segments, or differed from its sibling — all true of
  the untouched input. Each gained a *was replaced at all* clause, and the removal check now reddens five of
  six (the sixth is the unknown-scheme guard, which must stay green). Recorded because it is the same
  vacuous-assertion shape this phase has now hit three times.
  — Both shipped descriptions of what redaction covers are updated: the manifest note that travels inside
  every export, and `export_lsp_conversation`'s tool description. An export that misstates its own coverage
  is worse than one that never redacted, because it is believed.
  — `LanguageServerRowViewModel` needed no change: the only URI it redacts is the workspace root, which is
  a directory and always `file:`.
  — **Verified in the running IDE, and the verification turned up something worth stating plainly.** The
  export preview was driven end to end (Tools → Protocol Inspector → Export conversation): it reports
  *"Includes 266 B of your source code: Form1.frm"* — the leaf, unwidened, because nothing collides — and
  the body shows every path segment replaced (`file:///C:/hx-Aconite/hx-knot/…/hx-Solstice.frm`) with the
  drive letter and the extension kept. The account name, `AppData`, `Local`, `Temp` and the scratch GUID are
  all gone from a path that carried them.
  — **But that was a `file:` URI, and `untitled:` did not appear — because the IDE does not currently
  produce one.** Measured twice on a fresh `--newproject` session: the startup form opened as
  `file:///…/Temp/hexide_Project1_<guid>/Form1.frm`, and a module added afterwards opened as
  `…/Ledger.bas` in the same scratch directory. That is route A (#500) — every `AddNew*` writes into a
  scratch folder at Add time — and the spec is explicit that a file in a folder the IDE chose is named by
  its `file:` URI like any other.
  — So **this task closes a leak that the default flow cannot currently reach**, and that is recorded rather
  than left to be inferred from a green suite. It is still the right change: the converter emits
  `untitled:` for any document whose `AbsolutePath` is null, the model permits that (the NRE fixed in 2.1
  was exactly such a module), and #500 is open on whether route A should become route B — under which every
  document is genuinely pathless until the project is saved and this becomes the ordinary case overnight.
  A redactor that had to be extended at that moment would be extended in a hurry.
- [x] 2.11 Tests, one per scenario in this phase's deltas, plus the rewrites the retired scheme forces: the routing tests that open `vb6://`, the ambiguous-extension
  guards against a real foreign server (now asserting the project-member gate, plus a carried `.cls` still
  reaching it), the per-server identifier test, the scheme theory. New: close-before-open asserted on the wire
  bytes, in the style of the shutdown wire-shape tests; a build sends nothing; a first save of a pathless form.
  — **Not a step of its own: each scenario's tests landed with the task that implemented it**, and are
  recorded there rather than restated here. What this item was still owed was the three the list names
  separately, which belong to no single earlier task.
  — **Close-before-open, on the wire bytes** — `RenameWireShapeTests`, in `ShutdownWireShapeTests`' style,
  over a server spoken by hand. The five existing assertions all run against an `ILspClient` substitute,
  which proves the caller invoked its client in order with the right arguments; it cannot prove a server
  saw anything. That gap is not hypothetical here: **both notifications are gated on the server's
  `textDocumentSync.openClose` and both swallow their own exceptions**, so the calls can return cleanly
  having put nothing on the wire at all, and every substitute assertion records that as a pass. Checked by
  removal — with the probe server answering `"openClose":false`, all five redden.
  — The guarantee is deliberately split across two layers rather than driven end to end. A wire test
  through `LspDocumentSession` would need the session's `TextDocument`, and **AvaloniaEdit's `TextDocument`
  carries thread affinity of its own**, separate from Avalonia's dispatcher: every `await` on a real
  transport resumes on a pool thread and the next read of `.Text` throws. `SetOwnerThread(null)` does not
  help — it is a two-step handoff, and a null owner makes `VerifyAccess` throw for *every* thread. So the
  substitutes pin that the session closes the OLD name before opening the new one, and the wire file pins
  what those two calls become in bytes.
  — A timeout there reports what it was waiting for and what did arrive, because a frame this client
  declines to send is dropped silently and the caller's await completes normally. Left bare, the expected
  shape of that bug reads as a hung test.
  — **A build sends nothing** — `ABuildSaysNothingAtAllAlthoughItRepointsEveryPath`. Make EXE repoints
  every `AbsolutePath` into `%TEMP%` and restores it in a `finally`, so for the length of a build the path
  a document would be named from is a file about to be deleted. `AbsolutePath` is a `SetField` property and
  does raise `PropertyChanged`, so a reconcile subscribed to it is a one-line mistake rather than a
  theoretical one — this test is what stops it. Checked by removal: adding that subscription reddens it.
  Make EXE itself cannot be driven from a unit test (it refuses without a published standalone runtime),
  so its own silence is pinned separately in `DocumentSavedAnnouncementTests`.
  — **A first save of a pathless form** — `AFirstSaveOfAPathlessFormReopensItUnderItsFrmFile`. The form
  half of 2.4a, written against a form on purpose: the two kinds are separate `Initialize` overloads with
  separate subscriptions, and every module test in this file passed throughout the period the form path
  was broken.
- [x] 2.12 Acceptance: `demo/spring-tide` carries its module as `Module=` rather than `RelatedDoc=`, and the
  demo's server reads it from disk.
  — **Done, and measured against the demo's real foreign server rather than argued.** `SpringTide.vbp`
  now carries `Module=TideTable; TideTable.bas`, and HexIDE names it on the wire as
  `file:///…/demo/spring-tide/TideTable.bas`. The server answered `textDocument/foldingRange` with six
  regions and `textDocument/diagnostic` with one syntax error at 0-based line 47 — the same answers it gave
  the carried document. Confirmed four ways rather than one: the frame in the protocol capture, the folds
  in the gutter of a snapshot, `get_project_info` reporting a module and no related documents, and
  `get_diagnostics` reporting the squiggle at line 48. Which server, and which published build, is recorded
  in `demo/spring-tide/README.md`, which is where this tree names it.
  — **No `didOpen` is sent, and that is correct** at the commit measured: the server advertises no
  `textDocumentSync`, so HexIDE tells it nothing about the buffer and it reads the file from disk. Worth
  recording because an absent open notification is otherwise indistinguishable from a document that failed
  to open. Pinned to the commit rather than stated as a property of that server — its maintainers confirmed
  the sync handlers are written and merely unregistered, so the trace gains three notifications the day
  they are wired. HexIDE needs no change either way; it gates on the advertised capability.
  — `Module=` takes `Name; File` and rejects a bare path — `Module=TideTable.bas` makes `vb6.exe` call the
  whole project file corrupt, naming no line. Measured with the other four item keys, which do not all
  agree; the README points at that oracle section rather than restating it.
  — The demo's README section that made the case for `RelatedDoc=` is rewritten as history, because it was
  this change's acceptance test and losing the record of *why* it was a workaround would lose the point.
  Both reasons it gave are now closed: 2.1 for the `vb6://module/TideTable` naming, and #446 independently.

## 3. The whole file in the code window

- [x] 3.1 Keep a form's, UserControl's and PropertyPage's designer text (`VERSION` through the root `End`) as
  read, on the definition beside the model, so the interpreter, the syntax check and the standalone runner can
  compose the file without a code window. A reload adopts it with the rest of the fidelity state.
  — **The designer half is `FormDefinition.DesignerText`, recorded by the reader and adopted on reload**,
  mirroring `ModuleDefinition.OriginalHeader` exactly. Not named `Original`-anything, because 3.3a replaces
  it with the header a save wrote: it is the current designer text, not the first one.
  — **It cannot be recovered from the model, which is why it is kept at all.** The `VERSION` line is dropped
  at parse and regenerated from the literal `VERSION 5.00`, and the block's own `Begin`/`End` are rebuilt at
  a computed indent — so a re-render is a reproduction, not the text. `HeaderLines` keeps only the run
  between them and stays, because the serializer still replays it.
  — **Both halves are now slices of the input, and that was not cosmetic.** The code body was accumulated
  with `StringBuilder.AppendLine`, which terminates with `Environment.NewLine`. Slicing only the designer
  half would have made the composition CRLF-prefix + LF-body on Linux and left the invariant every later
  task rests on false from the first commit. `_codeBuilder` is gone; `LinesWithEnds` walks the input with
  offsets, following `TextReader.ReadLine`'s terminator rules exactly so the parse is unchanged.
  — **The old behaviour was a live cross-platform defect, measured on both hosts.** With the accumulator
  restored, `WholeFileCompositionTests` fails **3 of 14 on Windows and 5 of 14 under WSL** — the two extra
  are the plain CRLF fixture and the real corpus files, which a Windows host cannot fail by construction.
  So every `.frm`/`.ctl`/`.pag` opened on Linux had its whole code body rewritten to LF beside a designer
  half pinned to CRLF. The design record's open question blamed *typed* lines; that diagnosis is corrected
  there, and the narrower original point stays open.
  — Two behaviour changes to `Code` come with it and are deliberate: terminators are the file's own rather
  than the host's, and a file whose last line had no newline no longer grows one.
  — **One accessor for both kinds**, `FormCodeText.WholeFile`, in Runtime because Core references nothing
  and the module side needs `ModuleFileFormat`'s canonical-header fallback. A `.ctl`/`.pag` does **not** go
  through `ToFileContent` — `HandlesHeader` is false for those kinds, so it would hand the body straight
  back — it composes from the `FormPart`'s designer text with the module's own code, which is the pairing
  the save path uses for them too.
  — **Two fields the reload should already have been adopting, found while adding the third.**
  `CitedCompanionBlobCount` is the guard on `File.Delete(companionPath)`: a stale non-zero count after a
  reload defeats it, so a form whose citations were removed externally deletes a companion whose bytes
  exist nowhere else — `serialization-outcomes.md` outcome 3, filed as
  [#506](https://github.com/hexide-io/HexIDE/issues/506) rather than left inside a commit about the code
  window. `LockControls` is the milder sibling. Both fixed here because leaving known-stale state beside a
  third field being added to the same method is indefensible.
  — Nine tests, all checked by removal: the three line-ending ones redden against the old accumulator, the
  three adoption ones against the removed lines. The corpus cases are real VB6-authored files and assert
  the corpus was found, so they cannot pass vacuously.
  — Scope held: no consumer is wired (3.6), no buffer composition (3.2), and no render for a form with no
  file (3.4).
- [x] 3.2 Compose the buffer as prefix plus `Code`, and split there on flush. **The prefix is not the protected
  region**: for `.bas`/`.cls` it is the whole header; for `.frm`/`.ctl`/`.pag` it is the designer part alone,
  because their `Code` already begins with the leading `Attribute` run; where load split nothing off (an
  unparseable `.ctl`/`.pag`, a `.bas`/`.cls` whose header was not recognised — #472) it is empty and the buffer
  is `Code`. Prepending the attribute run to a form would show it twice; splitting after it would strip
  `VB_Name` out of `Code` and write a form without one.
  — **`FormCodeText.Prefix` is the one rule, and `WholeFile` is now expressed through it**, so the
  composition and the split cannot drift apart. `BodyOf` splits by the prefix's LENGTH and by the prefix the
  buffer actually carries, not the one the model would render now: the two diverge the moment a document is
  renamed, because `Attribute VB_Name = "Utilities"` is longer than `= "Mod1"`, and splitting at the model's
  current length would cut into the body.
  — **`ModuleFileFormat.BufferHeader` distinguishes null from empty, which `ToFileContent` does not.** Null
  means never read from disk, so the canonical literal is what the file will say; empty means read and
  nothing was split off, so the body already IS the file and the buffer must add nothing. `ToFileContent`
  tests `IsNullOrEmpty` and falls back to the literal for both — that conflation **is** the mechanism of
  #472, and without the distinction the code window would have shown a second header on exactly the files
  that defect affects. This does not fix #472; it stops the buffer reproducing it.
  — **The three external surfaces were redirected rather than left to change by accident.** Automation's
  `get_file_content` and the add-in `GetContent` now read `BufferBody`, and `set_file_content`, the add-in
  `SetContent` and `ApplyEdits` write through `ReplaceBody`. The write half is not cosmetic: those assigned
  a bare body straight over `Document.Text`, which since this task would destroy the header and leave the
  next flush splitting into the body. The read half is deliberately unchanged in behaviour — 3.18 and 3.19
  move those contracts with their own docs and tests, and `set_file_content`'s own description promises it
  accepts what `get_file_content` returns, so the pair has to move together or the tool contradicts itself.
  — Servers now receive the whole file, which is the point rather than a side effect: a server that reads a
  document from disk and one that is handed the buffer must see the same text or their positions do not
  mean the same thing. Task 0.2 proved both grammars parse whole real files before anything relied on it.
  — **Verified in the running IDE** against the spring-tide demo with its real foreign server attached:
  `Attribute VB_Name = "TideTable"` is line 1 where the buffer used to open at `Option Explicit`, folds
  still arrive from the server at 6/12/20/27, and the server's syntax error renders on line 48, on
  `dim xyz = As Int` itself. That last one is the whole argument for the phase in miniature — that server reads
  the file from disk and reports file lines, so with the buffer holding the body from file line 2 its
  diagnostic could not have landed on the statement it describes.
  — The demo's committed screenshot is now stale in two ways (it predates the `Module=` switch, so the
  Project Explorer label differs, and it predates the visible header). **Not refreshed here on purpose**:
  3.7, 3.14 and 3.15 change how the header looks again, and 3.22 is the task that snapshots the finished
  appearance. Refreshing twice would be worse than refreshing once.
  — Three existing tests pinned `buffer == Code` and were **retargeted rather than adjusted**, since that
  identity is exactly what this task replaces. Four new ones cover the form half, a form with no file, and
  the #472 empty-header case.
- [x] 3.2a Dirty detection compares the buffer's body — split exactly as the flush splits it — with `Code`,
  for modules and forms alike. Comparing the whole buffer would class every open document as edited and turn
  every external change into a conflict, disabling the silent reload the file-watcher capability requires.
  — Both comparisons go through the editor's own `BufferBody`, so the detector cannot disagree with the
  flush about where the split is — the design asks for "split exactly as the flush splits it", and sharing
  the accessor is the only way to mean it rather than assert it.
  — **The failure this prevents is louder than "everything looks edited".** A `Conflict` verdict is not
  merely "skip the reload": it queues the `ConflictGate` and raises a dialog, so every external change to
  any open document would prompt, and the silent `CleanReload` the file-watcher capability requires would
  never be reached once. Checked by removal — reverting either comparison reddens
  `AnUneditedOpenDocumentIsACleanReloadRatherThanAConflict` and nothing else.
  — The opposite direction is covered too (`AnEditedOpenDocumentIsStillAConflict`), because a careless split
  could just as easily make everything look clean, and a `CleanReload` over unsaved work discards it.
- [x] 3.3 A "layout changed" notification on the form, raised on a designer commit and by the three paths that
  bypass the designer's undo stack today: the menu editor, the colour palette, and automation's property set
  with no designer open. It flushes the designer's working collections into the model before rendering (they
  run ahead of it until the apply-unsaved-changes event, so a render before the flush misses the control just
  added), refreshes the prefix once per commit, never per drag step, and carries a rename of the form.
  — **One notification, `FormLayoutChangedEvent`, carried on the event bus rather than hung off
  `FormDefinition`.** The four raisers are all in the IDE assembly and the single subscriber is a service
  that has to exist whether or not any window is open; an event on the model would have needed that service
  to subscribe to every form as it is added and unsubscribe as it goes, which is lifecycle work the bus
  already does. Every other cross-cutting notification here — `ApplyAllUnsavedChangesEvent`,
  `FormUnloadedEvent`, `DocumentSavedEvent` — goes the same way.
  — **Raised from `Push`, `Undo` and `Redo`, and deliberately NOT from `Clear`.** The undo stack's existing
  `Changed` event fires from all four, which is right for the CanExecute plumbing it was written for and
  wrong here: `Clear` runs when the designer is rebuilt from a freshly-reloaded model, so treating it as a
  commit would re-render the form and replace the text just read from disk with a reproduction of it —
  exactly what the invariant forbids outside a save. `ClearingTheUndoStackIsNotACommit` pins it.
  — **Once per gesture is free, because the stack already had the shape.** `Push` discards while
  `IsDragging` and `EndDrag` pushes one `MoveResizeCommand`, so a twenty-step drag announces once.
  `ADragAnnouncesOnceWhenItEndsRatherThanPerPointerMove` asserts the zero as well as the one — every pointer
  move writes the model through a two-way `Canvas.Left` binding, so the wrong hook is one property write
  away and would cost a full render and a whole-document `didChange` per pixel.
  — **The flush runs before the publish, and the test reads both facts inside the handler.** Checking after
  the publish would pass just as happily with the flush second, and second is useless: whatever re-renders
  the form does so while handling the event. Checked by removal — swapping the two lines reddens it.
  — **Two gates on the render, neither of them tidiness.** A form that cannot be saved faithfully is never
  re-rendered: `FormSerializer` carries no fidelity check of its own (`SerializeFormToFile` refuses *before*
  calling it), so without the gate the code window would show a flattened menu hierarchy as though it were
  the file — for precisely the forms whose save is refused to stop that reaching disk. And a form with no
  file is left alone until 3.4 settles what its companion references are called. Both log at Debug, because
  "the header did not refresh" is otherwise indistinguishable from "nothing was raised".
  — **The rename half of 2.4a is closed, for both paths.** The designer's is closed by the flush, since
  `UpdateComponents` is what raises `PropertyChanged(Name)`. Automation's `set_control_property` with no
  designer open has nothing to flush, so `FormDefinition.NotifyRootPropertiesChanged` was added and it calls
  that — renaming a form through the automation surface was otherwise invisible to the tab title and to the
  language layer.
  — The menu editor's refresh will usually stop at the fidelity gate, since HexIDE flattens nested `Begin`
  blocks and a form with a real menu is one it refuses to save. That is the correct outcome rather than a
  missing one, and the comment at the call site says so.
  — **Verified in the running IDE** on a scratch copy of `demo/neon-aurora`: adding a `CommandButton`
  through the designer grew the code window's header from twelve lines to twenty, with `Option Explicit`
  moving from line 13 to line 21 and its breakpoint and bookmark moving with it.
- [x] 3.3a A save is the second refresh trigger: the prefix becomes the header that was just written, before
  the save is announced, so the buffer follows the file even when the render differs from what was read and
  when a companion reference changes with the file's name. A reload is the third.
  — **Recorded at the write, not at the announcement**, so every announce site is covered by construction:
  `SerializeFormToFile`, the `.ctl`/`.pag` branch of `SaveModuleCore`, and — which was not in the task and
  matters more than it reads — `AddNewUserControl` and `AddNewPropertyPage`, which write the file at
  creation, so without this a brand-new UserControl opened with no header in the window while its file had
  one. **That sentence used to say "after #489 a document getting its file at creation is the ordinary
  case", which reads #489 backwards.** #489 decided the opposite — VB6 writes nothing until the project is
  saved — and #500 is the implementation. The creation paths do write today, so this is the ordinary case
  now and will not be; 3.4 is what covers the document that has no file.
  — **The clause "before the save is announced" already holds on the wire, and it is worth saying why.**
  `LspDocumentSession.NotifySavedAsync` flushes a pending `didChange` before sending `didSave`, so the
  header this writes reaches the server ahead of the save notice even though the write is debounced. Nothing
  new was needed; it would have been a silent ordering bug had that flush not been there.
  — **`FormCodeText.DesignerHalfOf` is the inverse of the render and is pinned by a test.** The serializer
  appends the code verbatim as its last write with no separator, so the header is `rendered[..^code.Length]`
  — and `DesignerHalfIsTheRenderMinusTheCode` asserts that three-way agreement (whole render, designer-only
  render, slice) because a blank line introduced between the two halves later would look like tidying and
  would move the save's recorded header, the code window's split and the next flush all at once.
  — **The reload was a live defect, not merely an unimplemented trigger.** `FileReloader` pushed the bare
  code section into a buffer whose prefix was still the header read at open: the window lost its header and,
  because `BufferBody` splits by the prefix's length, the next flush cut the head off the reloaded code and
  wrote the remainder back as the document. Silent data loss on any file whose code section is longer than
  its header, which is most of them. `ReloadFrom` now takes both halves and sets the prefix;
  `AReloadReplacesBOTHHalvesSoTheNextFlushDoesNotCutIntoTheBody` was written first and went red on the
  branch.
  — A refused save leaves the header alone, through the same `CanSaveFaithfully` gate the write uses. A
  buffer re-headed from a render the save refused to write would show a file that does not exist.
- [x] 3.3b A refresh that changes the prefix's line count shifts that document's breakpoints and bookmarks by
  the difference, in the stores rather than the gutter, so a document with no open code window moves too.
  — **`RefreshPrefix` replaces the REGION rather than assigning `Document.Text`.** A whole-document
  assignment collapses every anchor AvaloniaEdit holds — the caret, the selection, the marker segments the
  diagnostics hang off, the folding sections a server sent — so the cursor would jump to the top of the file
  on every nudge of a control. Replacing the first `bufferPrefix.Length` characters moves everything below
  by the difference, which is what actually happened. `ReloadFrom` is the exception and still assigns,
  because the body changed too.
  — **HAZARD, open until 3.8, and measured rather than predicted.** The region replace lands on the
  editor's own undo stack, so Ctrl+Z in the code window after a designer commit reverts the header while
  `bufferPrefix` still holds the new one — and the split is by length, so the body is then taken from the
  wrong offset. Probed on 2026-09-21: type `Dim x As Long`, refresh the header from
  `Attribute VB_Name = "Module1"` to a longer one, press Ctrl+Z. The buffer is back to the short header
  correctly, and `BufferBody` answers `x As Long` — the next flush would write that as the whole document.
  It is the same data-loss class this task fixed on the reload path, on the commonest keystroke there is.
  — **So 3.8 moves ahead of 3.5**, and this is the reason. The fix is the marked-group protocol 0.5 settled
  from the decompiled library, not "do not record it" — which 0.5 measured as unsafe, because an unrecorded
  change above an existing entry invalidates that entry's absolute offset. 3.5 adds a second owner write
  (`VB_Name` on rename) into the same hole, so closing it first is cheaper than widening it. `ReloadFrom`
  has always had the same exposure, since a whole-text assignment is recorded too; 3.8 closes both.
  — **A mark inside the header stays where it is**, rather than being clamped to the new first body line.
  The header is being replaced by another header, so its line 3 is still its line 3; clamping would pile
  every header mark onto one line. 3.7 stops a mark being set there at all.
  — **The two stores disagree about the base and the test has a mark on the boundary in each.** Breakpoints
  are 1-based, bookmarks 0-based; the same source line is a different integer in each store, so only a mark
  on the header's last line and one on the code's first can tell the two rules apart. Without those, the
  test passes whichever rule the code uses — checked by removal in both directions.
  — Counted by `'\n'`, never `Environment.NewLine`, and tested on both terminators: `build-ide` runs on
  `ubuntu-latest`, and a count that asked the host what a line ending is would move every mark on one
  machine and not the other.
  — **The no-code-window case is tested directly, because it is the clause this task exists for.** A
  refresher that lived on the code editor would move the marks of whatever happens to be open and leave
  every closed document pointing at the wrong statements. Verified live too: the reload shift was measured
  with only the code window open and no designer, and separately with neither.
  — Twenty-nine tests across five files, and **every mechanism they cover was checked by removal**: both
  render gates, both shift bases in both directions, the no-op guards on the shift and on the re-head, the
  caret carry, the bus subscription, the prefix tracking on both write paths, the save's recording and its
  slice, and each of the four undo-stack hooks. Twenty-two mutations; all of them redden.
  — **Left standing, and it belongs to #884, filed from the 3.17 review:** an external change the dirty
  detector calls a Conflict is not applied, so its header delta never reaches the stores — and when the
  project is next opened the sidecar restores the marks in the numbering of the file as it was before that
  change. Seen during the live verification, where the first external edit landed while the designer had an
  undo history and was therefore classified as a conflict; the reload that followed a restart shifted
  correctly.
- [x] 3.4 Header render for a form with no file uses `<Name>.frx`. A form held read-only is never re-rendered.
  — **The read-only half was already done, and saying why is the deliverable.** Being unable to reproduce a
  form is the ONLY thing in the tree that holds one read-only: both `IsReadOnly` properties that can be
  backed by a form are the same one-line expression over `CanSaveFaithfully`, there is no disk-attribute
  check, no project-level or safe-mode gate, and a running project prompts rather than locks. So 3.3's
  fidelity gate is the whole of it, and a second check would have been a second answer to one question.
  — **The no-file branch cannot reach an unfaithful form, by construction.** Only `FormDeserializer` ever
  marks a form unfaithful, so a form with no file has never been through it. Pinned by a test rather than
  left as reasoning, because a third cause added later would quietly make the sentence above false.
  — **`<Name>.frx` is right only for a `.frm`.** The companion extension is derived by the serializer from
  the file name's own (`.ctl` → `.ctx`, `.pag` → `.pgx`), so the spelling comes from `DocumentIdentity` —
  its `Name` and its `Extension`, the same two pieces `DocumentWireName` puts after `untitled:`. One
  `FormCodeText.RenderFileNameFor` answers for both the has-a-file and the no-file case, so the save path
  and the refresh cannot drift apart about what a form is rendered against.
  — **The name reaches the rendered text through exactly one thing: the companion citations.**
  `FormSerializer` reads the argument only to derive the `.frx` name, and writes that name only for a
  property holding a blob. Every form HexIDE has just created carries none, so for them this changes the
  render not at all — which means a test that only removed the gate could not tell `<Name>.frx` from any
  other string. The test therefore puts an `Icon` on the form and asserts the citation.
  — **An empty name is refused rather than rendered.** `Path.ChangeExtension("", ".frx")` returns `""`
  (measured on .NET 10), so a nameless document would have emitted a citation of `"":HHHH` — a file that
  looks valid and names nothing. Nothing produces a nameless form; if one arrives it gets no render.
  — **The existing gate test did not model the production case and was rebuilt, not inverted.** It set
  `AbsolutePath = null` on a form read from disk, which is a state nothing produces and left the subject
  with a header already. The fixture is now `new FormDefinition(project, FormComponentClass.Instance, ...)`,
  which is what `IProjectTemplate` builds — no file AND no designer text.
  — `AFormHexideCreatedComposesToItsCodeAlone` was **kept** rather than inverted: it pins the state before
  the first commit, which is still right. Its sibling asserts the state after one, and the prose in
  `FormCodeText` now says *when* the header appears instead of implying it never does.
  — **What this task does NOT close, recorded in `design.md` as an open question of this phase**: a created
  form that is opened and never touched still shows no header, while the code-editor delta says a document
  with no file shall hold the header its first save will write. It needs a trigger the design record does
  not have, and it cannot be settled apart from the sidecar's numbering, which is #885, filed from the 3.17
  review — a header appearing at open has no previous header to measure a mark shift against, and the
  sidecar's own numbering may already count it.
  — **Verified in the running IDE** on a `File > New Project` Standard EXE, whose `Form1` genuinely has no
  file: the code window opened with no header, and adding a control in the designer put the whole designer
  block in front of `Option Explicit`.
- [x] 3.5 Where a document's header carries `VB_Name`, it follows a rename, as an edit the IDE makes itself
  (#473: a form's is never retargeted today, so a renamed form's file names two different forms). A form
  HexIDE created carries no attribute block at all, unlike one imported from VB6, which writes five — a
  fidelity gap of its own, recorded rather than fixed here.
  — **Three kinds, three homes for the line, one entry point.** `IHeaderRefresher.NameChanged(identity)`
  makes the `VB_Name` a document carries say its current name. A `.bas`/`.cls` keeps it in the header, which
  the model already renders from the live name, so only an open buffer can be stale and re-heading it is the
  whole job. A `.frm` keeps it in `Code`, a `.ctl`/`.pag` in its module's `Code` — below the prefix, because
  their code section opens with the attribute run — so for those the model's text is rewritten as well, which
  is what makes a closed document's next save right. `LayoutChanged` calls it on every commit, because a
  form's rename is a change to its root control; a module has no rename gesture at all (#493), and the
  comment there names this as the call it will need.
  — **Only a live rename in the tree today is a form's.** `ModuleDefinition.Name` has no assignment outside
  its constructor. The module half is therefore tested by setting `Name` on the model, exactly as the
  identity tests already do.
  — **It follows the document's name, which for a UserControl is its module's**, not the root control's. The
  two are separate fields nothing connects, so renaming a UserControl's root in its designer moves the
  `Begin` line alone — #473 again, in a `.ctl`. Following the root here would make the file disagree with
  the name the project knows it by instead, so that is recorded on #493, where the rename belongs.
  — **The locator walks the text's own offsets**, not a normalised copy. `FormCodeText.AttributeBlock` counts
  one character per terminator after splitting on `'
'` and so cuts one short per CRLF line (#465); the
  span here is handed straight to a document replace, where that would glue two lines together. #465 itself
  is a `good first issue` and was left for a contributor rather than fixed in passing.
  — **Compared by value, not by the line's text**, because it runs on every committed nudge of a control: a
  line VB6 did not space the way HexIDE writes one would otherwise be rewritten by the first nudge. A commit
  that renames nothing pushes no undo entry.
  — **3.8's protocol widened to cover it.** The `VB_Name` write is marked as the IDE's own by a stateless
  operation beside the header write's; the code window's Undo re-applies BOTH afterwards (re-applying the
  header alone would leave the code naming the old form with nothing that would ever repair it); and one
  commit's writes are wrapped in one outer group, so an undo that does not come through the code window's own
  (#513) cannot separate a rename's two halves. The marker matters on its own for a `.ctl`, where the
  `VB_Name` write is the only write.
  — **Not in `FormSerializer`**, although `ModuleFileFormat.ToFileContent` retargets at save for modules. For
  a form the line is in the developer's text, and a save-time rewrite would make the file differ from the
  buffer — the one thing 3.21's "an unchanged save writes the buffer" forbids.
  — **The created-form gap is now #516**, with the measurement it needs: VB6's own template corpus has five
  attributes on nineteen of its twenty forms, and whether VB6 defaults a missing `VB_PredeclaredId` to
  `False` decides whether a HexIDE-created form can still be shown by name.
  — Fifteen mutations, all reddening.
  — **Verified in the running IDE** on a scratch copy of `demo/bill-of-fare` (VB6-authored, five
  attributes): renaming the form in the Properties window moved the `Begin` line and `VB_Name` together in
  the open code window; typing a line, renaming again and pressing Ctrl+Z there removed the typed line and
  kept both new names; and the saved `.frm` equals the original with exactly those two lines changed. That
  save had to be taken with Format on Save off, because the default save destroys the top of the code —
  recorded under 3.9, and not caused by this task. Found on the way: the Properties window applies a value
  only on leaving the box, never on Enter (#518).
- [x] 3.6 The interpreter and the pre-run syntax check parse the whole text (after 0.2).
  — **Four call sites, all now `FormCodeText.WholeFile`:** `VBLoader.InterpreterModules`, which every run
  path shares for its standard and class modules; `VBLoader.RunForm`, which the standalone runner uses too;
  the IDE's own form run (`ProjectRunnerService.RunFormInBrowser`); and the pre-run syntax check on the
  startup form. The Sub Main path's syntax check already ran over `InterpreterModules`, so it follows. No
  UserControl or PropertyPage code is executed anywhere, so there was no fifth.
  — **Between 3.2 and this task the debugger was out by the header**, and nothing recorded it. Breakpoints
  have been counted from the top of the file since the buffer became the whole file, while the interpreter
  still numbered from the top of the code section. So a breakpoint on a form's first statement never fired,
  and a syntax error was reported by its code-section line. No code converted between the two, so the fix
  is only the input: nothing downstream (breakpoint push, current-statement bar, Run To Cursor, Set Next
  Statement, Call Stack) needed to change.
  — **Measured before wiring:** a whole `.bas`, `.frm` and `.cls` parse and run with their headers, and the
  walk executes none of the header. A member-level `Attribute` line still raises `NotImplementedException`
  when the walk reaches it, which is the pre-existing gap the proposal puts out of scope (#174 step 1). It is not new
  here, because those lines were already in `Code`.
  — `DebuggerLinesAreFileLinesTests` (4): a syntax error in the startup form reported on its file line,
  through the real `ProjectRunnerService`; a breakpoint on `Form_Load`'s first statement stopping on file
  line 9, through `VBLoader.RunForm`; a form run given its file byte for byte; and each module a run loads
  given its whole file, including a created class, which gets the canonical header the code window shows.
  All four fail with the old inputs restored.
  — **Verified in the running IDE** on a scratch copy of `demo/bill-of-fare` (VB6-authored, 85-line designer
  block, five attributes): `set_breakpoints` on line 98, `Form_Load`'s first statement counted from the top
  of the file, then `run_project` paused at `frmBillOfFare` line 98. `get_call_stack` reported `Form_Load`
  at 98, and a snapshot shows the current-statement bar on line 98 beside the breakpoint dot.
- [x] 3.7 Protection: one section provider subclassing the stock one over the header and member-attribute
  regions, overriding both of its virtual methods — refusing insertion at a region's edges, which it allows,
  and widening a deletion over a member's attribute run so deleting the line it describes takes the run with
  it. It also carries the whole-document verdict, because binding the editor's read-only property would
  replace the provider outright, and it is re-evaluated on reload. A read-only
  *region* is the header or a member's attribute run and nothing else: a form held read-only as a whole must
  still take breakpoints and answer Find, so the mark, Find and attribute rules test the region, never the
  whole-document gate.
  — **`CodeWindowReadOnlySections`**, owned by the view model and installed by the view whenever its data
  context changes. The `IsReadOnly` binding on the editor is gone. Both verdicts are read live on every
  call, so a reload that flips either one needs nothing reinstalled. The regions are cached against the
  document's version and the prefix length, because the provider is asked on every keystroke.
  — **A deletion can be narrowed, never widened, and the task's wording assumed otherwise.**
  `TextArea.GetDeletableSegments` (decompiled from 12.0.0) throws `InvalidOperationException` if a provider
  returns a segment outside the span it was given. So "takes the run with it" is done by *not carving the
  run out* when the deletion already covers the run and the whole line above it: a selected procedure,
  deleted or cut, goes with its attributes. Deleting the declaration line alone cannot reach the run below
  it through this seam. That case stays with 4.5a and the guarded write path. `design.md` is corrected.
  — **One protection the task did not name.** A member's protected span starts at the terminator of the
  line it describes, not at the run. Delete at the end of a declaration, or Backspace at the start of its
  first attribute line, would otherwise join the attribute onto the declaration. It is the deletion twin of
  refusing insertion at the run's first character.
  — **#475 fixed with it.** `ReloadFrom` raises `IsReadOnly` and `ReadOnlyReason` before its early return,
  because the verdict can flip on a byte-identical code body (an edit to the `.frx` alone). The provider
  needed no notification. The banner did. A rendered-tree test drives the real `FileReloader` both ways.
  — Tests: `ReadOnlySectionProviderTests` (15) and three in `ReadOnlyBannerIntegrationTests`. Mutating the
  widening, the terminator span and the reload notification each turns tests red.
  — **Verified in the running IDE** with `type_text`. In a class module: refused at offset 0 and at the
  start of a member's `Attribute` line (the reply put the run's end exactly past that one line), and
  accepted in ordinary code. Both refusals come from the automation tool's own region guard (3.9), which
  asks before the provider does; the provider's region rules are covered by the unit tests. In a form
  citing a `.frx` blob with no companion: the banner showed, typing in its code was refused by the
  provider itself ("TextEditor is read-only"), and a breakpoint was still set on a code line, so the
  whole-document gate did not become a region. A frame nesting a button no longer triggers the verdict,
  since #84 hosts real containers, so a missing companion was the trigger that remained.
- [x] 3.8 **Ahead of 3.5 — there is an open data-loss path until this lands; see the hazard under 3.3b.**
  Undo, by the mechanism 0.5 settled: record the refresh in a marked group so every offset stays
  valid; when the developer undoes and the stack reports that group as the most recent, revert it, undo the
  edit beneath, and re-apply the current header. The redo stack is lost at that point, which is stated in the
  release notes rather than left to be discovered. Test: a code edit, a designer move, then undo — the edit is
  undone, the move is not, and a second undo still undoes the right text.
  — **`LastGroupDescriptor` cannot do the job, and 0.5's own wording is what gave it away.** It reports the
  last group *opened*; an `Undo()` clears it to null even when marked groups remain below, a plain change
  clears it too, it is already null inside `Changed` during the replay, and an empty group leaves it set with
  nothing behind it. So it answers "is the top of the stack mine" only when nothing has happened since — and
  the ordinary sequence *type, designer change, type, undo, undo* is not that. All measured against
  AvaloniaEdit 12.0.0 by reflection over the exact assembly the repo ships.
  — **What replaces it is an `IUndoableOperation` inside the group**, carrying the buffer's prefix: `Undo()`
  restores the old prefix, `Redo()` the new, and it tells the view model that the group it sat in was the
  one undone. Pushed BEFORE the text change, so a group replays it after the text on undo and before it on
  redo — the prefix is therefore right at every point an observer could look, in both directions.
  — **That gives two layers, and both earn their place.** The operation is unconditional safety: whatever
  pops the entry, the buffer and the prefix move together. It matters because HexIDE does not own every
  route — AvaloniaEdit answers **Ctrl+Y** itself and nothing in the keymap binds it, so an undo or redo can
  reach the document without passing through the command HexIDE does own. On top of that sits the policy
  loop, which is what makes a designer change not undoable *from the code window*.
  — **The loop, and why it is a loop.** Pop entries while each one turns out to be a header write; stop on
  the first that is not; put the current header back if any header write came off. One Ctrl+Z therefore
  costs two real pops and reads as one, at any depth — two designer commits in a row still cost one undo.
  The re-application is conditional, because doing it after an ordinary undo would push an entry and take
  the developer's redo with it.
  — **The header is undone rather than stepped over, and that is the whole mechanism.** An undo entry holds
  an absolute offset: the developer's edit was recorded against a document with the OLD header, so it only
  replays correctly once that header is back. Stepping over the header write would put the edit's offsets
  into shifted text. 0.5 measured the same thing from the other side, which is why "simply do not record
  it" was refused.
  — **The redo stack is lost at that point**, stated in `CHANGELOG.md` under Unreleased rather than left to
  be discovered, alongside the two smaller behaviour changes below.
  — **Two pre-existing defects closed on the way, because the loop walks straight into both.**
  `Initialize` assigns `Document.Text`, AvaloniaEdit records every assignment, and nothing cleared it — so
  the first Ctrl+Z in a freshly opened code window emptied the buffer (measured). And `ReloadFrom` left a
  history describing a document that had just been replaced wholesale from disk; it now clears it, as the
  designer half of the same reload already did.
  — **A third defect, and it was mine from 3.3a.** Make packages every form into a temporary directory under
  a name built from the form's own name, and `SerializeFormToFile` was adopting that render as the
  document's header — citations and all — with nothing to put it back, because unlike the paths beside it
  the model keeps no second copy. `adoptHeader` now says so, and the `.ctl`/`.pag` half rides on the
  `announceSave` flag that already existed for exactly this condition.
  — **`HeaderRefresher.ApplyHeader` now refreshes an open buffer unconditionally.** It used to return early
  whenever the render equalled what was recorded, which after an undo meant a window showing a header for a
  form that no longer looked like it — *permanently*, because every later render would equal the record too.
  — Sixteen tests, fourteen mutations, all of which redden: the loop, its conditional re-application, its
  termination, the operation's push, its two directions, its self-report, the group that makes the pair
  atomic, both cleared histories, both halves of the packaging gate, and the refresher's unconditional half.
  — **Verified in the running IDE** on a scratch copy of `demo/neon-aurora`, driven by real keystrokes:
  typed a line at the end of Form1's code, added a `CommandButton` in the designer, pressed Ctrl+Z in the
  code window. The typed line went; the `Begin VB.CommandButton Command0` block stayed at lines 12-19; and
  `get_file_content` answered from `Option Explicit` with nothing of the header in it.
  — **Also verified live, because a unit test cannot reach it:** a keystroke typed immediately AFTER a
  designer commit does not fuse into the header write's group. AvaloniaEdit coalesces typing through
  `StartContinuedUndoGroup`, and if that merged with the group this opens, one Ctrl+Z would take the
  keystroke, the header and the edit below it together. Measured through real key events: commit, type,
  Ctrl+Z — the typed line went and the control's block stayed.
  — **Recorded and filed rather than fixed:** AvaloniaEdit's redo gesture is Ctrl+Y and HexIDE binds nothing
  to it, so that route bypasses both the command HexIDE owns and the user's keymap. Harmless to this work
  — the operation keeps the split honest whatever route arrives — but it wants deciding rather than
  patching, because VB6 used Ctrl+Y for Delete Line. Filed with two neighbours found in the same pass
  (`InsertAtEnd` costing two or three undos, Replace All pushing an entry into every other open window) as
  hexide-io/HexIDE#513.
  — **One claim this phase had been making is false and is corrected**: the region replace does NOT preserve
  the diagnostic markers. `LspMarker` is a record struct of two offsets in a plain list, not an anchored
  segment, so a header that changes height leaves every underline drawn at a stale offset until the next
  `publishDiagnostics` — which the replace's own debounced `didChange` brings within a few hundred
  milliseconds. Transient and self-healing; anchoring them properly is hexide-io/HexIDE#512. The caret, the
  selection and the folds ARE preserved, which is what was measured.
- [x] 3.9 One guarded write path, with the policy in the design record for each of the twenty programmatic
  writers: formatting reduced to changed lines and clipped; server rename refused if it touches the header;
  Replace, Replace All, completion, Insert File, Enter auto-close, event stubs, add-in `SetContent` and
  `ApplyEdits`, automation `set_file_content`, `type_text` and `press_key`; reload and the Edit-and-Continue
  revert as owner.
  — **HAZARD, open until this task and 3.10, and measured rather than predicted: Format on Save destroys the
  top of every form's code.** Probed on 2026-09-21 against a scratch copy of `demo/bill-of-fare`: open the
  code window, press Ctrl+S, touch nothing else. The saved `.frm` has lost its whole `Attribute` block and
  the first sixteen lines of code, and ends the designer block with a stray `"`. `FormatOnSave` defaults to
  **true**, so this is every save from a form's code window with the bundled server running.
  — **The mechanism, read off the wire** (protocol inspector, `--capture-lsp`): the bundled server answers
  `textDocument/formatting` with ONE edit from `(0,0)` to the end of the document, whose text re-indents
  every designer line to column zero and uses `\n` throughout. `ApplyFormattingToDocumentAsync` applies it
  unfiltered. The header in the buffer is then hundreds of characters shorter than `bufferPrefix`, and the
  flush splits by that length — so the body loses exactly what the header lost. The save writes the model's
  own designer block, which is why the damage is all in the code; the header the buffer then shows is
  put back by the adopt-on-save, which hides the cause.
  — **Since 3.2, and only on this branch.** Before the buffer held the whole file the server was handed the
  code section alone and there was nothing of the header for it to flatten. `main` does not contain 3.2.
  — **Proposed: this task and 3.10 move ahead of 3.6**, for the reason 3.8 moved ahead of 3.5: a live data-loss
  path on the commonest keystroke there is, opened by an earlier task of this phase. The design record's
  policy for formatting (reduce the edit to the lines it changes; drop changes inside a read-only region)
  is the fix; nothing new has to be decided.
  — **The inventory, rebuilt, because the design record's "twenty paths were inventoried" was never written
  down.** Four independent sweeps (by editor API, by external surface, by editor feature, by server edit) and
  a reconciling pass that opened every site, 2026-09-22. What needs THIS task's guarded path, because it
  writes the document directly and so bypasses the read-only section provider 3.7 installs:
  - **Formatting**, twice: Format on Save (`CodeEditorViewModel`) and Format Document (`CodeEditorView`,
    Shift+Alt+F), which were verbatim copies of one unfiltered loop.
  - **Server rename** (`CodeEditorView.RenameSymbolAsync`), applied unfiltered from `WorkspaceEdit.changes`.
  - **Replace** and **Replace All** (`FindReplaceViewModel`), the latter into every open window in scope.
  - **Completion commit** (`VbCompletionData.Complete`), **Insert File** (`CodeEditorView.InsertFile`), and
    **Enter** (`HandleEnterAutoClose`, `InsertNewlineWithIndent`, taken by a tunnel handler before
    AvaloniaEdit's own provider-checked Enter).
  - **Add-in `SetContent` and `ApplyEdits`** (`AddinEditorService`), and **automation `set_file_content`,
    `type_text` and `press_key`** (`HexIdeTools`, `UiAutomationDriver`).
  Already safe by construction: event stubs and Add Procedure append at the end of the document. Owner
  writes, already routed: the header refresh, a save's header, `VB_Name`, reload, and the undo loop's
  re-assertion. **Not this task, because AvaloniaEdit checks the section provider on these routes itself**
  (read from the 12.0.0 assembly): paste, cut, delete and backspace, Ctrl+D, and text drag-and-drop. 3.7
  covers them.
  — **Four gaps in the design record's table, found by the inventory:** (1) *initial load* is an owner write
  and is now named as one; (2) *Enter's keyword re-casing* rewrites existing lines, which the insert row
  does not describe, so it skips lines inside a region; (3) *paste* is provider-checked, so inside a region
  the library refuses it silently rather than putting the text after the region as the insert row says, and
  that refusal is accepted rather than replaced with a custom paste; (4) *member attributes following a
  rename* has no writer of the IDE's own, and belongs to phase 4, which is where member attributes are
  built. **Corrected while implementing it:** a server's rename does carry that edit. The bundled server's
  rename is whole-word, and `Total` in `Total.VB_Description` is a whole word, so the rename path lets
  exactly that edit through (`IsOwnAttributeQualifier`) and the delta's "Renaming a procedure that has a
  description" scenario holds today. What phase 4 adds is the IDE's own rename following it.
  — **Two things the inventory found that belong to other tasks:** the Edit-and-Continue revert is a raw
  whole-buffer assignment, not an owner write (3.13, already recorded there); and redo re-asserts nothing
  after replaying a header write (#513, where the Ctrl+Y decision is pending).
  — **Done, 2026-09-22.** Every writer in the inventory either goes through a guarded member of
  `CodeEditorViewModel` (`ApplyFormatting`, `ApplyRename`, `ReplaceContent`, `ApplyEdits`,
  `InsertFileAsync`/`InsertPastReadOnlyRegion`) or asks it first (`IsReadOnlyRegion` for completion, Enter
  and Replace; `SnapshotReadOnlyRegions` for Replace All; the automation driver for `type_text` and
  `press_key`). The whole-file rule for `SetContent` and `set_file_content` is one function,
  `GuardedContent.Replace`, used by both and by the no-window branch of the tool. The design record's
  writer table now gives each writer its own row. The completion row changed from "text goes after the
  region": a completion completes the word at the caret, so nothing moved after the region would mean the
  same thing, and it is simply not applied.
  — **Each guard was removed in turn and a test failed every time**: thirteen mutations, thirteen caught
  (`GuardedWritersTests`, `AddinEditorGuardTests`, the two new `FindReplaceViewModelTests`,
  `GuardedEditorInputTests`).
  — **Two defects in the harness and one in the product, found on the way.** (1) The integration test app
  never loaded AvaloniaEdit's control theme, so every `TextEditor` in that suite had no template and its
  `TextArea` was never parented or given a DataContext. The automation refusal reads the view model the
  text area inherits, so in that suite every refusal passed as "not a code window", while the live IDE
  refused correctly. The theme is now loaded as `App.axaml` loads it, and the rest of the suite is
  unchanged. (2) Replace All asked for
  the regions afresh at every match, which is quadratic in a large module. It now takes one snapshot,
  which stays valid because Replace All works backwards. (3) **Insert File was unreachable by
  automation:** it called the storage provider directly, the only picker in the IDE that did, so
  `answer_next_file_dialog` never answered it and an automated run left a native dialog waiting. Moved to
  the view model and routed through the window manager (#532, and the closed-gaps archive). Its
  hard-coded "Open Text File" title is now a key, translated in every pack from that pack's own Insert
  File menu item.
  — **Verified live**, on a copy of `demo/bill-of-fare`. `set_file_content` with a changed attribute block
  was refused with the reason. The code alone was accepted, and the saved designer block was
  byte-identical. `type_text` and `press_key` Enter in the header were refused with the offset where the
  code starts, while `press_key` Down still moved the caret. Replace All `lblChosen` → `lblPicked` made two
  replacements and left the designer's `Begin VB.Label lblChosen` alone. Insert File with the caret in the
  header put the file on its own line after the header. **Renaming a local `Caption` was refused**, and
  the captured answer shows why that matters: 21 edits, 19 of them in the designer block (every menu
  caption in the form).
  — **Found and filed, not this task's:** Enter inserts a bare `\n` into a CRLF document and the save
  writes it, and `set_file_content`/`SetContent` keep a caller's line endings (#530). The
  `get_file_content` flag that reads `true` whenever a window is open was already #481. #465's route
  through `set_file_content` is gone on this branch, because the tool no longer calls
  `FormCodeText.PreserveAttributes`. `AttributeBlock`'s arithmetic is still wrong and still reachable on
  `main`, so the issue stands as filed.
- [x] 3.10 The bundled server keeps to its own new requirement: no diagnostic inside a header, the formatter
  leaves it untouched, and rename and highlight skip it and member attribute runs, through one shared helper
  so the three cannot drift apart. The client clipping stays as the guard against servers that do not.
  — **Done, 2026-09-22.** One helper, `VbProtectedRegions`, read by diagnostics, formatting, rename and
  highlight. It is the text-only branch of the IDE's `ReadOnlyRegions`, ported rather than shared because the
  two halves do not reference each other; `BundledServerRespectsTheIdesRegionsTests` (in `HexIDE.Tests`) is
  what keeps the copies in step.
  — **Diagnostics**: left out at the ANTLR listener when they start on a protected line. The two
  paused-analysis notices at (0,0), for a file too large or nested too deep, are about the file and are kept.
  **Leaving the header's errors out turned out not to be enough, found by a test rather than predicted.**
  Damage one designer line (`ClientHeight = = 3000`) and put a syntax error in the code below: unfiltered,
  the parse reports three errors, all in the header, and **nothing for the code**. ANTLR's recovery from the
  designer block consumed the rest of the file, so the procedure also fell out of the outline. With the
  header's errors left out, that form published no diagnostics at all and gave no reason. So when an error
  lands on a protected line the file is parsed again with the protected lines emptied, line breaks kept so
  every position holds, and that parse answers: measured, the code's error at 20:14 comes back and nothing
  in the header does. A header the grammar can read stays in the tree, and no file in the corpus pays for
  the second parse. The delta now says so, with a scenario.
  — **Formatting**: one edit per run of changed lines, instead of one spanning the document, which pointed
  inside the header even when its text left the header alone. Protected lines are copied through and not
  classified, so a property's attribute lines neither get indented nor stop the body after them being
  indented. **Each line now keeps its own terminator.** The formatter joined its output with `\n`, so a
  CRLF file was never already formatted: every save produced an edit covering the file, and the edit's
  text was LF. Measured live on a copy of `demo/bill-of-fare`: Ctrl+S on the untouched form now gets `[]`
  from the server, where it used to get one edit from (0,0), and the saved file is byte-identical to the
  original. A lowercase `dim y as string` typed into `Report` got one edit on that line alone.
  — **Rename and highlight**: occurrences on protected lines are left out, and a caret on one answers null.
  **Two decisions, both now in the delta.** (1) Rename keeps the renamed member's own qualifier in
  `Attribute Total.VB_…`. The language-server delta as first written forbade any rename edit inside those
  lines, while the code-editor delta requires a member's attribute lines to follow its rename "whether the
  IDE or a language server makes it". Resolved for the code-editor rule, which is the design record's, and
  the language-server requirement now carries the exception. Highlight has none. (2) **A name the designer
  block declares is not renamed at all**: the form's, a control's, a menu's. Found while preparing the live
  check, and a regression this task would otherwise have introduced. Before it, renaming `lblChosen` from
  the code was refused, because the server's answer reached the `Begin VB.Label lblChosen` line. With the
  server keeping off the header, the same answer carries the code's references alone, the code window
  applies it, and the code names a control that no longer exists. A lexical rename cannot tell a reference
  to the control from a local that shares its name, so the server declines both, and the code window
  refuses before the name prompt. Both read one rule, the names on the header's `Begin` lines
  (`VbProtectedRegions.Declares`, `ReadOnlyRegions.DeclaredNames`). A property name the layout sets is a
  different thing and still renames: a local `Caption`, or the `Index` parameter of a control array's event
  handler.
  — **The client**: Rename from a read-only line, or on a declared name, is refused before the name prompt
  (`CodeEditorViewModel.RenameRefusalAt`), with the existing message. A server keeping to the rule answers
  both with null, and a null rename showed nothing at all, which a developer cannot tell from a rename that
  found no occurrences. Verified live: from the top of the header, and on `lblChosen` in the code, Rename
  showed the message and sent no request; on the parameter `Chosen` it prompted, the server answered two
  edits, both in the code, and they applied.
  — **The user-visible change**: renaming a local `Caption` in a form, refused under 3.9 because 19 of the
  server's 21 edits were in the designer block, is now applied to the code.
  — **The parity test drives the built server with the real client** over `demo/`, `corpus/designer`,
  `HEXIDE_ROUNDTRIP_CORPUS` when set (here it added the VB6 templates, including a `.DSR`), and two inline
  files carrying members' attribute lines, which no corpus file has. Formatting is the exact check, in both
  directions: every line is given two trailing spaces, so the lines the answer changes must be exactly the
  non-empty lines outside the IDE's regions. It also judges every diagnostic, a rename and a highlight from
  every read-only line, and a rename of every word the layout and the code share, by the code window's own
  qualifier rule (`IsOwnAttributeQualifier`, made internal for it).
  — **Mutation, each guard removed in turn.** Server: 8 of 9 caught. The survivor filters scope-analysis
  diagnostics, which cannot run while `EnableUndeclaredVariableCheck` is a `const false`; it is kept so that
  switching the check on does not reopen the header. Parity test, by making the server disagree with the
  IDE: 7 of 7 caught — a header a line short (failed on `neon-aurora\Form1.frm` line 11) or a line long
  (the inline form's first line of code), members' attribute lines unprotected, a designer walk that
  ignored `BeginProperty` (failed on the VB6 templates' `Mover ListBox.frm` and `DATARPT.DSR`), no second
  parse, rename keeping region occurrences, highlight answering from a region. The declared-name rule, on
  each side: removing the server's refusal fails the server suite and the parity test, and removing the
  code window's fails `GuardedWritersTests`.
  — **Not this task's, recorded where they belong:** automation's `get_file_content` returns the module's
  code, not the buffer, so its line numbers still start after the header (3.18); `type_text` inserts a
  bare `\n` into a CRLF buffer verbatim, which the formatter now rightly leaves alone (the Enter-key case is
  #530). The rename prompt's own title and label are hard-coded English (#537).
  **From main (#651, hexide-io/HexIDE#649), merged 2026-09-23:** `type_text` asks the installed
  `ReadOnlySectionProvider.CanInsert` before inserting and refuses otherwise, so once this provider is
  installed the automation route honours it with no further change; a live check of 3.7 can use `type_text`
  into the header and expect a refusal. The merge kept both guards rather than choosing: this branch's
  `RefusedInReadOnlyRegion` runs first, because it names the header and gives the offset where editable code
  starts, and main's `WhyNotTypable` follows as the general case (disabled, read-only `TextBox`, a wholly
  read-only editor). `RefusedInReadOnlyRegion` gained a `mechanism` parameter so `type_text` reports
  `"document"` while `press_key` keeps `"keyboard"`, which is #649's point.
- [x] 3.11 Find and Replace search outside read-only regions only.
  — **One acceptance test at every place a scan yields a match.** `Accepts` combines Whole Word with the
  document's own region predicate. It is called at the plain scan's forward, backward and both wrap
  loops, in the pattern scan's two helpers, and in Replace All's backward walk, which no longer needs its
  own post-hoc check. Each open document is searched against its own regions under All Open Documents. A
  carried file has none. A form held read-only as a whole is not a region, so Find still searches all of it.
  — **The pattern scan walks, and steps past a refused match by one character.** It used to take the
  first match and stop, and `NextMatch`/`Matches` resume at a match's end, so a refused match hid any
  match overlapping it (`\s*Private Sub` refused at the header's last line break hid the `Private Sub`
  inside it). The same stepping makes Whole Word in pattern mode true: a first match that was not a whole
  word used to mean "not found".
  — **Changing a region is more than touching it**, found by review and now in the design record. A
  Replace of `Currency\r\n` removed the line break a procedure's attribute run hangs from, joining the
  attribute onto the declaration, because that break is outside the run. 3.7's provider already refused
  typing the same deletion. The rule now lives once: a member run carries an **anchor** (its
  declaration's line end), `TextRegion.Changes` protects from there, and every writer asks it:
  `IsReadOnlyRegion` and its snapshot, rename, add-in `ApplyEdits`, a formatting hunk, and the provider's
  protected span. `Touches` stays as membership for formatting's per-line rewrite and Enter's re-casing,
  which put the line break back. Enter's re-casing now asks about the line's text alone, so a described
  declaration is still re-cased. A formatting hunk asks `Changes` only when its replacement ends without
  a line break (a deletion); one that ends with one keeps the break where the hunk ends, so membership
  decides, and adding a line above a described declaration while re-indenting it is not dropped for
  nothing. A side effect worth knowing: automation's `press_key` Backspace at a run's first character is
  now refused with a reason, as the provider already refused it silently.
  — **AvaloniaEdit's own search panel is uninstalled** from every code window, on `TemplateApplied`,
  which Avalonia raises after `TextEditor.OnApplyTemplate` installs it. Its Replace All writes anywhere.
  MainView claims Ctrl+F and Ctrl+H first only while a searchable document is active and MainView is in
  the route, so without this a floated code window, or a moment when the command cannot run, reached it.
  — **Reviewed adversarially** by a workflow: one finder per scan (plain Down, plain Up, pattern Down,
  pattern Up, Replace, scope and conformance), each required to give a concrete input and a test that
  fails if the finding is real, then a critic re-tracing every finding. Fifteen findings came to two
  root causes in scope (the straddle and the anchor, with four duplicates), the search panel, and five
  outside the task, each filed rather than folded in: the whole-document gate is honoured by typing only
  (#701); the event-stub lookup searches the header (#702); Up from offset 0 ignores "no wrap" (#703);
  Down skips an adjacent match (#704). Every reviewer test that held was run here, not taken on trust.
  — **Mutation: every guard reddens a test.** Fifteen sites in the first pass left three survivors, all
  wraps a header can never reach (the plain and pattern Up wraps, and the final wrap into the active
  document), each now pinned by a test that puts a region match inside that scan. The pattern helpers'
  region checks and both one-character steps, the search-panel hook, and nine sites of the anchor rule
  were then mutated in turn, and each was caught.
  — **Noticed and left to the spec's wording:** text that exists only in the header is reported "not
  found" while it is visible on screen, and Replace All does not say it left header matches alone. Filed
  as #705, which needs a spec scenario and two translated strings.
  — **Verified in the running IDE**, on a copy of `demo/bill-of-fare`: Find `lblChosen` from the top
  selected line 98, not the designer block's line 17. Two more Find Nexts went to 139 and wrapped to 98.
  `VB_PredeclaredId` was reported not found. Pattern `\s*Private Sub` found the code. In a class added to
  the project, a pattern Replace All of `Currency\s+` left the function's `Attribute` line attached.
- [x] 3.12 Marks refused on read-only lines, including a gutter click on a folded header.
  — **Done together with 3.16, because the range checks below could only be fixed in its numbering.**
  — **One toggle per kind, and every UI route goes through it.** `CodeEditorViewModel.ToggleBreakpoint` and
  `ToggleBookmark` refuse a NEW mark on a line in a read-only region and put the reason in the status bar
  (`Str.CodeEditor.Msg.NoMarkOnReadOnlyLine`, in `en` and all 29 packs). F9 and Debug ▸ Toggle Breakpoint
  (`MainViewViewModel`), Ctrl+F2 (the view) and both gutters (handed the toggle instead of the store) all
  call them. A mark already on such a line can still be removed: only an older sidecar can have put one
  there, and nothing else in the window could clear it. The bookmark toggle takes a 1-based line like
  everything else the window gives out, converting to the store's 0-based numbering in one place.
  — **Membership, as the note above says.** `ReadOnlyRegions.IsReadOnlyLine` asks `Touches` about a line
  without its terminator. A described declaration takes a breakpoint and its attribute line does not, and
  the mutation that asks the edit question with the terminator turns two tests red.
  — **The folded header needs no case of its own.** A click maps to the folded row's first document line,
  which is the header's. Tested headlessly with a real fold and a press raised on each gutter. The control
  case, a click beside code, is in the same class, because `window.MouseDown` at the same point set nothing
  even beside code, which would have let a refusal test pass while refusing nothing.
  — **The range checks now count the code window's text.** `CodeWindowText` (Runtime) is a document's text as
  the window numbers it: the open buffer, or `FormCodeText.WholeFile` with no window. `MarkLineRules`
  (HexIDE, so a test reaches it) is the refusal the four automation tools relay: a line the file does not
  have, or a new mark on a read-only line, with the reply naming the header's range and where the code
  starts. A held read-only line is let through, so a get-then-set round trip is never refused.
  — **The sidecar's load was dropping real marks.** `WithinDocument` counted the model's code section, so on
  this branch every mark on a form's last lines of code (as many lines as the designer block is long) was
  dropped at load and gone from the next save. It now counts the whole file. It does **not** refuse
  read-only lines: a sidecar written by `main` counts from the first line of code, so a header test in file
  lines would drop a form's breakpoint on its fifth line of code. That filter belongs after 3.17's migration.
  — **Run To Cursor from the Debug menu is not refused on a header line**: it runs as from any line that
  never executes. Only the automation tool refuses it. Recorded as D24 in `debugger-vb6-divergences.md`.
  — **Mutation: fifteen guards, all caught** — both toggles' refusals and their held-mark exemptions, the
  status message, each gutter and Ctrl+F2 reverted to the store, the edit question asked of a line, the
  automation refusal, its held exemption and its range, the sidecar's range, the model text, and a
  read-only filter added to the sidecar.
  — **Verified in the running IDE** on a scratch copy of `demo/bill-of-fare` (header 1..90): with the code
  window CLOSED, `set_breakpoints [3, 98]` was refused naming the header and "code starts at 91", and
  `[98, 139]` was accepted, which the old code-section count would have refused. With it open, caret on
  line 2 and `invoke_menu_item Debug/Toggle Breakpoint` set nothing and the status bar gave the reason;
  `Edit/Bookmarks/Toggle Bookmark` on line 89 (an attribute line in the form's code section, so the
  straddle) set nothing, and on line 98 set bookmark 97. A snapshot shows both marks on 98. `run_to_cursor`
  on line 50 was refused (its wording was then changed to "execution can never stop there", which is
  unit-tested rather than seen live). **Not driven live: the gutter click itself.** No automation tool can reach a
  gutter (#707, recorded in `mcp-server-gaps.md`), so it rests on the headless test.
  **A mark is membership, not an edit (left by 3.11).** Since 3.11, `IsReadOnlyRegion` answers the *edit*
  question (`TextRegion.Changes`), which also refuses the line break a member's attribute run hangs from.
  Ask it about a line with its terminator (`dl.TotalLength`) and every described declaration refuses a
  breakpoint. Test a mark with `ReadOnlyRegions.Touches`, or with `IsReadOnlyRegion(dl.Offset, dl.Length)`.
  **Still open after merging main (2026-09-23):** #570 (hexide-io/HexIDE#569) refuses `set_breakpoints` /
  `set_bookmarks` lines outside the document, #592 (#591) does the same for `run_to_cursor` /
  `set_next_statement` through the same `OutsideDocument`, and #620 (#574) drops sidecar marks outside the
  document at load (`UserSidecarService.WithinDocument`, counting the model's code). Each counts lines in
  the open editor's buffer, else the model's code. On main those agree; on this branch the buffer carries
  the header and the code does not, so the valid range still depends on whether the editor is open.
  Reconcile it with 3.16's numbering, and fold "a read-only line" into the same refusal, which is this task.
  The merge itself changed none of them — this is work, not a conflict.
- [x] 3.13 Edits the IDE makes itself do not raise Edit-and-Continue's reset prompt. **Already measured, so
  this task is narrower than it reads**: the prompt is raised from `OnTextEntering` and `OnEditorKeyDown`
  only, never from a document event, so a header refresh does not reach it today. What DOES need this task
  is the prompt's own "No" arm: it reverts by restoring a whole-buffer snapshot taken when the prompt opened
  (`CodeEditorView.axaml.cs`), without touching `bufferPrefix` — so a designer commit or a save landing while
  the prompt is open makes answering No reproduce exactly the header/prefix split 3.8 closed everywhere else.
  — **One more, found by reading 3.9's guards (measured below):** both call sites raise the prompt before
  anything decides whether the edit will land. `OnEditorKeyDown` calls `MaybeStartResetPrompt` ahead of the
  Enter guard, and `OnTextEntering` ahead of the read-only section provider 3.7 installs. So while a project
  runs, a key in the header that writes nothing still asks the developer to reset the project for it.
  — **Both measured first, as failing tests, then fixed.** `ResetPromptTests` (19, against a rendered editor
  with the answer held open as a modal would hold it) went red on five: three refused keystrokes in the
  header that prompted, and No after a header refresh in each direction, grown and shrunk, which left the
  old header under the new prefix.
  — **No restores the code, never the whole buffer.** `CodeEditorViewModel.RevertBodyTo` puts the code back
  as it stood when the prompt opened, under the header the buffer carries now, replacing only the span that
  differs so anchors outside the edit stay put. The caret is kept relative to the code for the same reason;
  restoring its absolute offset put it on the line above once a header had grown (seen live, then pinned).
  The design record's writer table had this row as "replaces the whole buffer", which was the defect, and
  now says what it does.
  — **Input arms the prompt; the document's `Changing` event opens it.** The input handlers run before
  anything decides whether a key writes, so asking from them asked about keystrokes that wrote nothing.
  Now they only arm, for as long as that input is being handled, and the prompt opens on `Changing`, which
  fires only when an edit lands, and before it is applied, which is when the code is captured. A change
  nobody armed for is the IDE's own and never asks, which is the code-editor delta's "changing a form's
  layout while paused" scenario.
  — **Paste, cut and Delete never reached the old handler at all, found by writing the control tests.** Each
  key is bound to a code-window command (`PasteCommand`, `CutCommand`, `DeleteCommand`) that takes it first,
  so a paste while paused landed with no prompt by every route, the key, both menus and the toolbar. The
  commands now ask. Paste and cut cannot use the arm, because the clipboard is asynchronous and their edit
  lands after the arm has gone (measured: the arm-only version let a paste in silently), so they ask the
  same section provider up front. Recorded as a behaviour change in `CHANGELOG.md` and in D2.
  — **Two older defects found on the way, confirmed live and filed rather than fixed here:** Delete with
  nothing selected deletes nothing, because the bound command calls `TextEditor.Delete()`, which only deletes
  a selection (#708); and Indent/Outdent do nothing by any route, while Ctrl+Tab opens the Dock framework's
  document switcher instead (#709). Indent and Outdent arm the prompt all the same, so a fix to either keeps
  it, but that arm cannot be tested until an indent can land: the one mutation that survived.
  — **Mutation: twelve, ten caught by the first run, one more after re-running it with a non-constant
  condition** (removing the arm check left the field unread, which warnings-as-errors refuses). The
  survivor besides Indent was a length test on the cut's deletable segments that the provider makes
  equivalent, so it was removed rather than kept untestable.
  — **Verified in the running IDE** on a scratch copy of `demo/bill-of-fare`, paused at a breakpoint in
  `Form_Load`: Edit ▸ Paste with the caret in the header wrote nothing and asked nothing; Edit ▸ Paste in
  code opened the reset prompt; while it was open, `set_control_property` gave the form a longer caption,
  which rewrote the header and saved; answering No left the window showing the new caption above the code
  exactly as it was before the paste, with `BufferBody` starting at `Attribute VB_Name` and
  `hasUnsavedChanges` true, because the save had written the pasted text before the answer came. A
  keystroke in the header cannot be driven live: `type_text` and `press_key` refuse it before raising it,
  by design, so that case rests on the headless tests.
- [x] 3.14 Folds: the header fold is merged into every fold application, including an empty or absent server
  answer, with the merged list sorted by start offset (the manager throws otherwise) and zero-length folds
  discarded (it skips them silently). It sets its own folded state rather than relying on the library's
  closed-by-default flag, which applies only on the first update. Folded when created or re-created unless
  expanded in this window. Overlapping server folds dropped.
  — **The fold library measured first, headlessly, on AvaloniaEdit 12.0.0 (2026-10-02)**, then read in its
  decompiled source, which agrees on every point. An update with the same offsets keeps a section, its
  folded state and its `Tag`. Equal start offsets are accepted, outer first. `Document.Replace` inside a
  fold keeps it folded and moves its end. `DefaultClosed` is honoured only in the manager's first update.
  `Document.Text =` removes every fold, a replace covering a fold's whole extent collapses it to nothing and
  removes it, and **every removal unfolds the section**, so its state cannot be read afterwards. Text
  inserted exactly at a fold's end is left outside it. And **the manager pairs the folds it is given with
  the sections it holds by start offset alone**: a server fold from the top of the file, listed first, took
  the header's section, and the header's fold came back as a new, expanded one.
  — **So the window keeps the state and sets it itself.** `CodeEditorViewModel.HeaderFoldExpanded` holds
  whether the developer expanded the header in this window; a dock move re-materialises the view around it.
  It is read from the fold before each fold application, on the document's `Changing` event (before a
  reload or a whole-header rewrite can remove the fold), and as the view detaches. After every application
  the header's section is found by its offsets, tagged, and folded unless expanded.
  — **The fold is the window's own, with or without a server.** It is applied as the view attaches, before
  any request goes out, and merged into every server answer (`Vb6FoldingAdapter.Merge`). On every change the
  window compares the fold with where the header now is and re-makes it at once when they differ, keeping
  the server's folds already shown and their state: a reload otherwise showed the header expanded until the
  debounced request came back, and a header that grew at its end showed the new text after the folded row.
  — **A server fold starting where the header starts is dropped as well as one crossing it**, because of
  the pairing rule above; one wholly inside the header nests. The fold ends before the header's last line
  break, so the first line of code keeps its own row, and a one-line header (a `.bas`) is not folded: a fold
  on one line hides nothing. Its label is its first line, as a server fold's is; 3.20 owns labels.
  — **Tests:** `Vb6FoldingAdapterTests` (17) for the fold, the conversion and the merge; `HeaderFoldTests`
  (25) against a rendered editor for every fold scenario in the code-editor and lsp-client deltas (greying
  is 3.15's) plus the paths the library makes fragile: reload, a class header rewritten whole, growth at the
  fold's end, a server fold at offset 0 answered twice, a dock move, a server that never answers.
  `MarkGutterOnReadOnlyLineTests` now uses the window's own fold instead of making one.
  — **Adversarial review (four lenses, two skeptics per finding) found one product defect, and it was this
  task's.** The editor unfolds any fold the caret moves strictly inside (read in the decompiled
  `FoldingManagerInstallation`), and the window takes an unfolded header as the developer's choice. A reload
  put the caret back at its old offset, which a longer header had moved into the header, so a reload after
  the form gained something on disk opened the header and kept it open. **Seen live before the fix:** caret
  on line 91, the designer block grown by 39 characters on disk, and after the reload the header expanded
  with the caret on line 89. `ReloadFrom` now moves the caret with the prefix's change in length, as the
  reload already moves the marks, and `CodeEditorViewModelTests` pins it both ways. Moving the caret into
  the header by navigation still expands it and counts as expanding it, deliberately (design.md). Reading
  every other place the IDE moves the caret found two searches that a caption naming a handler can send
  into the header, filed as #711.
  — **The other findings were test strength, all fixed:** a re-fold test that a flag which could only ever
  be set would pass; two expanded-header tests that asserted before anything re-made the fold; a comment
  claiming the attach-time fold was asserted alone. And neither delta said a one-line header is not folded:
  it is now a requirement paragraph and a scenario, and the lsp-client scenario names a form or class.
  — **Mutation: 18 at first, 15 caught.** The fold at attach survived because the stub answered at once and
  its answer made the fold too, and is caught by a test whose server never answers. A branch clearing a
  stale header tag was unreachable, because the merge drops any fold that could take the header's section,
  and was removed. So was a check that the header region starts at offset 0, which the region builder
  guarantees, in favour of the anchor check, which the unit tests do distinguish. Three more after the
  review were all caught: the reload's caret shift, a flag that could only be set, and the always-folded
  mutation again against the strengthened tests.
  — **Verified in the running IDE** on a scratch copy of `demo/bill-of-fare` with the bundled server. The
  form opened with its 90-line header as one row labelled `VERSION 5.00` and code from line 91, and kept it
  folded when the server's eleven procedure folds arrived. None of those starts in or crosses the header,
  so the same-start rule only ever matters for another server. The reload above, before and after the fix.
  A caption changed by `set_control_property` with the header folded left it folded, and with it expanded
  left it expanded through the server's next answer. The window closed and reopened came back folded. A
  new `.bas` showed its one header line with no fold marker, and a new class folded its 13-line header
  under `VERSION 1.0 CLASS`. **The fold margin's own click cannot be driven** (added to #707), so expanding
  was done by moving the caret into the header.
  — Seen on the way and added to #512: a document highlight stays at its old offsets across a header write,
  exactly as the diagnostic markers do.
- [x] 3.15 Greying: a named palette colour for each theme, meeting the dark palette's recorded contrast bar,
  applied after syntax colouring. Theme packs carry the key.
  — **The key is `ReadOnlyText`** (`ReadOnlyTextBrush` in Classic.axaml), with a value in each shipped pack:
  `#707070` on Classic (4.95:1 on `#FFFFFF`), `#929292` on Dark (4.92:1 on `#252526`), `#7C7C7C` on Abyss
  (4.72:1 on `#000C18`). Each stands at least 1.5:1 from its pack's own text. `GrayText` was measured first and
  misses 4.5:1 in all three. Every read-only region is greyed, the members' attribute runs included, by one
  line transformer between the syntax colouring and the diagnostics colouring, so an error inside the header
  still shows red. The fold labels take the same colour through AvaloniaEdit's one process-wide label brush,
  which every editor that folds now sets from the key. design.md has the reasoning for each.
  — **Tests:** `ShippedThemePackTests` (12) holds every shipped pack to the bar against the background and
  text colours its own file declares, and checks the reference pack matches Classic.axaml.
  `ReadOnlyGreyingTests` (11) runs against a rendered editor with the real `ThemeService` and Classic.axaml
  merged where the IDE merges them. It covers both delta scenarios, the attribute runs, an error inside the
  header, no key resolving, a switch to Dark, a switch between Dark and Abyss (which changes no theme
  variant), a change of the read-only colour alone, a carried file, the Protocol Inspector, and an address
  inside a region.
  — **Adversarial review (four lenses, two skeptics per finding): six findings, five confirmed, all fixed.**
  - *The Protocol Inspector folds and did not follow the key*, so with the inspector the only editor showing, a
    theme switch left its labels in the previous theme's grey, about 3.1:1, worse than the old default.
    It now follows while on screen, as the other two editors do.
  - *An address inside a region was still drawn as a link*, in AvaloniaEdit's blue, which the link takes
    after every line transformer has run: 1.78:1 on Dark. A generator ahead of the link generators now
    claims it as plain text, and the grey reaches it. A link in the code is unchanged.
  - *Three test weaknesses.* The label brush is process-wide and no test reset it, so the one assertion
    that an opening editor sets it could pass on an earlier test's value; each test now starts from a
    colour nothing uses. Every label assertion was written `(brush as ISolidColorBrush)?.Color.Should()`,
    which asserts nothing when the cast fails. The no-key test excluded only Classic's grey, and now
    compares the header with the same lines built without the greying.
  - Rejected 2–0: that design.md's opening paragraph contradicts the settled bullets.
  — **Found while fixing the no-key test:** it claimed `Begin` was drawn in the keyword colour, and `Begin`
  is drawn in no colour at all. AvaloniaEdit honours `ignoreCase` only on a `<RuleSet>`, so the definition's
  `BEGIN`/`END` never match: #716, which also makes #713 visible once fixed. The same file is MPL-2.0 in a
  tree that promises MIT throughout, and nothing checks file notices: #715. A link in the code is
  AvaloniaEdit's blue in every theme, 1.78:1 on Dark: #717. The ratios measured for this task showed that
  the dark packs lack `WarningBanner*` and `ValidationError`, and that ValidationError is 2.60:1 on Dark:
  #712. The light syntax palette has no bar at all: #713.
  — **Mutation: 19, all caught.** Thirteen were run against the first version and six more against the review's
  fixes, the review's own suggested mutations among them: a literal grey in the colorizer or in the follower,
  and a carried file that repaints without setting the labels. Two of the six needed a second run. The
  inspector's did not compile as first written. Claiming every address survived, because the generator
  checked membership twice and a region is whole lines, so the line check alone decides it. The second check
  was removed.
  — **Verified in the running IDE** on a scratch copy of `demo/bill-of-fare`:
  - *Classic*: the expanded 90-line header is grey and `Option Explicit` on line 91 keeps its colours.
  - *Launched on Dark*: the folded label and its box are grey.
  - *Dark to Abyss, live*: the header samples `#7C7C7C` on `#000C18`.
  - *Classic to Dark, live, three times*: the editor went dark each time, with lines 87–90 grey and line
    91 coloured.
  - *Addresses*: a URL and a mail address were put into a label's `Caption` and a URL into a comment in
    `Form_Load`. On Classic and on Dark the header's were grey and plain, while the comment's stayed a blue,
    underlined link.
  - *The Protocol Inspector*: it showed a pretty-printed `initialize` body with its fold markers. Switching to
    Dark, with it as the only editor showing, repainted it dark. Its fold labels could not be checked live,
    because automation cannot close a fold (#707); `TheProtocolInspectorFollowsTheReadOnlyColourToo` covers
    them.
  — **Seen once and not again.** On the first live run, a Classic to Dark switch left the code window and its
  minimap white with light syntax colours while the chrome went dark. It did not recur in three further
  runs of this build, one of them repeating the first run's launch flags, caret and scrolling exactly, and
  the pre-3.15 build switched correctly. Nothing in this change touches the editor's background, so it is
  recorded here rather than explained.
  — **Driving the Options dialog found #714**: `interact select` on a page-tree item reports success and
  selects nothing, so the theme page was reached by keyboard.
- [x] 3.16 Line numbers from the top of the file in the margin, status bar, Call Stack, automation and add-in
  surfaces. Record the divergence from VB6's code-window numbering.
  — **Most of it was already true, and this task pins it.** The margin numbers the document and the status bar
  reads the caret's document line, and since 3.2 the document is the file. The Call Stack and current
  statement are the interpreter's lines, which have been file lines since 3.6. `LineNumbersFromTheTopOfTheFileTests`
  pins the code-editor delta's class scenario (the status bar on a class's first line of code is the
  header's length plus one, asserted against the header rather than a constant) and that the margin
  numbers the whole file. `DebuggerLinesAreFileLinesTests` (3.6) already covers the debugger.
  — **What was not true was the range the automation tools and the sidecar counted in**, fixed under 3.12.
  — **The automation descriptions now say how lines are counted**: `get_`/`set_breakpoints`,
  `get_`/`set_bookmarks`, `run_to_cursor`, `set_next_statement`, `get_debug_state`, `get_call_stack` and
  `get_diagnostics`, which also says a VB6 compiler error arrives converted. A caller who counts from the
  first line of code is told where the code starts when refused.
  — **Add-in positions were already file lines** (`GetSelection`, `NavigateTo`, `ApplyEdits`, diagnostics) and
  their documentation now says so. **`AddinDocument.Content` is still the code section**, so a line an add-in
  finds by reading it is out by the header; its documentation says that too, and 3.19 changes it.
  — **Recorded as D23 in `debugger-vb6-divergences.md`**, with VB6's own status-bar numbering left
  unmeasured as the design record has it, rather than guessed.
  — **Verified in the running IDE**: on `frmBillOfFare` the margin runs from `VERSION 5.00` as line 1, and the
  status bar read `Ln 98` with the caret on `Form_Load`'s first statement; on a class added through the IDE
  the header is 1..13, the status bar read `Ln 14` on the first line of code, F9 there set breakpoint 14,
  `set_breakpoints [13]` was refused with "code starts at 14", and `set_bookmarks [13]` (0-based) was
  accepted.
- [x] 3.17 Sidecar migration to the next format (#466 first, so an older build keeps what it cannot read): read the recorded format, re-key, move lines by each header's
  length, carry unmeasurable entries unchanged, keep unrecognised content, rewrite only on change and never
  after a failed read.
  — **Version 2, marks under `fileBookmarks` and `fileBreakpoints`**, as settled in the design record; the old
  keys are migrated whatever the version says, and written back only with what could not be placed.
  `UserSidecarService.Apply` places both formats' entries for one document into one set before the store is
  set, because a store's setter replaces.
  — **The shift is `FormCodeText.LineBreaks(FormCodeText.Prefix(document))`**, and that counter is the one
  `HeaderRefresher` now uses too (lifted from its private `LineCount`), with `Prefix(DocumentIdentity)` added
  beside the two existing overloads. `CodeWindowText.HeaderLineCount` would have moved a form's marks five lines
  too far and shifted a #472 module that should not move at all.
  — **Settled here, and recorded in the design record**: a missing file is found with `File.Exists` as the project
  opens, which unlike `OriginalHeader is null` also answers for a UserControl; new-key entries for a name the
  session does not own are carried from the file like the old keys; each save subtracts what the stores wrote at
  the last one before carrying, or a document renamed twice leaves its middle name's marks behind; and until a
  write lands, a save — the migration's own first of all — subtracts what the load placed from the old keys.
  — **The read-only drop the earlier tasks deferred to here is on**, for both formats, logged apart from the range
  drop. The live requirement "A mark on a line its document does not have SHALL be dropped when the sidecar is
  read" still counted lines "in the code without its Attribute header"; the delta now MODIFIES it to count from
  the top of the file, check an earlier sidecar's lines in their own numbering before the move, and drop a mark
  on a read-only line. A scenario was added to the ADDED requirement for old-key marks written into a migrated
  file.
  — **3.21's sidecar items landed here**, in `SidecarMigrationTests`, which opens real files through
  `ProjectService` for all four kinds: the three delta scenarios under their own names, marks kept on their
  statements (asserted by reading the line, not by arithmetic) for a form, a `.bas`, a `.cls` and a UserControl,
  a second opening of a file the first one migrated, which changes neither the bytes nor the write time, a
  missing `.bas`, `.cls` and `.ctl` whose entries survive load and save value for value, old keys beside new
  ones, a deleted sidecar that stays deleted of carried entries, and a migrated mark landing on a read-only line.
  Plus the three refuted carry rules, a failed migration write, a newer file's old keys, a file with nothing to
  place left untouched, a mark under the new keys on a read-only line, a #472 module that does not move, and a
  null entry failing the read in either format before anything reaches the stores.
  — **Mutation: 14 runs against the service, each reddening a test.** A shift taken from the header region
  (the form, the UserControl and the #472 module), no subtraction of what the load placed, that subtraction
  dropped after a failed write, no subtraction of what this session wrote, a missing file counted as owned, no
  read-only drop in either format, carrying the new keys or the old from memory, setting the stores per format,
  entries placed as they are read, the range checked after the move, no write at load, and a write at every
  load. The range-after-move mutation shows only in the log, because every line it would wrongly keep lands
  inside the prefix, which the read-only drop then removes. A load that never wrote its migration first passed
  the second-opening test, which read the same earlier file twice; that test now checks the file the first
  opening left before opening it again.
  — **The review's mutation pass found four more that passed, and each now reddens a test**: an old-key entry
  subtracted by its key alone rather than while it is exactly what the load moved
  (`AnEarlierBuildsLaterWriteUnderAMigratedKeyIsKeptForTheNextOpening`); a name shared with a document whose
  file is missing placed rather than carried (`ANameSharedWithADocumentWhoseFileIsMissingIsCarried`); an entry
  consumed only when a line of it was placed (`AnEarlierSidecarsEntryWhoseEveryLineIsPastTheEndLeavesTheFile`,
  and the read-only test now checks the old keys are gone); and a carried entry's lines dropped where this
  session has marks under its name (`AMarkSetWhereAnEntryIsCarriedIsWrittenBesideIt`). The design record had said
  a drop writes nothing at load, which was true only of the new keys, and now says so. Also added: a later
  opening that finds the missing file migrates the carried entry, and the missing-file theory covers a
  PropertyPage. `OnlyNamesTheProjectLackedWhenItOpenedAreCarried` lost a third assertion that could not fail.
  — **Fixtures revised, not weakened**: `"version": 2` meant "a newer build" in seven tests and now means this
  build, so those say 3; the four that asserted version 1 assert 2; reads of what this build writes read the new
  keys; four fixtures written in this build's numbering under the old keys moved to the new ones; and a test
  that set a document's path with no file behind it now writes the file, since a path naming nothing is a missing
  file. `AMarkOnAFormsLastLineOfCodeIsKeptAtLoad` no longer keeps a header-line mark, which its own remark had
  said was the migration's call.
  — **Not closed: the conflicted external change parked under 3.3b**, for the reason the design record gives,
  **nor 3.4's header that appears at open**, which version 2 cannot tell apart from a header its marks already
  count. They are #884 and #885.
  `debugger-vb6-divergences.md` D23's status now says the migration is done. D24, and the remark on
  `CodeEditorViewModel.ToggleBreakpoint`, still say only an older sidecar can leave a mark on a read-only line,
  which the load now drops; 5.5 and 5.7 rewrite them.
- [x] 3.18 Automation: `get_file_content` returns the whole file open or not; `set_file_content` accepts it
  back, or a body alone with the header kept. Tool descriptions say lines count from the top of the file.
  The delta's naming half, which this line does not name, was done after it, in part; the last three notes.
  — **Both tools changed in one commit**, as design.md requires. The read is the open window's buffer or the
  model's composition of the file, the text the mark tools count in. `hasUnsavedChanges` still compares the
  buffer's body with `Code`. The write goes through `GuardedContent` for every kind, and the whole-`.frm`
  refusal (#338) is gone, with `FormCodeText.LooksLikeFormFile`, its only production use, and its two tests.
  — **The read and write rule moved where a test can reach it**: `DocumentContent` in `HexIDE`, beside
  `MarkLineRules`. The name lookup followed it into `NamedDocuments` with the naming half, so the tools keep only
  which window is open on the document, the picker refusal, the save and the reply. `DocumentContentTests`
  carries the delta's scenario, `ReadingAndWritingBackAForm` (`BillOfFare.frm` read, written back, saved, and its
  bytes unchanged), and reads and writes back every document in `demo/` and `corpus/designer/`, which is the only
  check that the prefix-measured header and the text-walked one agree on files VB6 wrote. They agree on all
  of them. What stays unreachable is which editor a tool picks and the save after the write: 3.22's.
  — **The reply states the range the lines are numbered in, read after the save** (settled in design.md).
  `set_breakpoints` refuses a line outside that range, counted in the same text: both read
  `DocumentContent.TextOf`, and `ToolDocumentNamingTests` fails when a tool reads the code window's text any
  other way, so the two cannot drift apart.
  — **Descriptions:** both content tools; the run-control replies (`run_project`, `break_program`,
  `continue_program`, the three steps) and `get_last_runtime_error`, whose lines are file lines already;
  and `type_text`, `press_key`, `hover`, `inspect_element`, whose offsets index what `get_file_content`
  returns. `hover` was at the 2048-character limit, so its `dwellMs` detail moved into that parameter's own
  description. The refusal that gives an offset says what it counts from.
  — **Still open:** a form created and not yet saved or committed reads as its code alone, which is 3.4's open
  question.
  — **A standing rule, found while merging main on 2026-09-23.** On this branch `editor.Document.Text` is the
  whole file, so code that means *the code section* must read `BufferBody`. Main's `get_file_content` read
  `Document.Text`, which on main is the code section. Merged unchanged, it would have returned the designer block
  from a tool whose contract then excluded it, silently, and in the direction that costs a caller a form. The
  merge changed both its arms to `BufferBody`, and this task has since made the whole file that tool's contract.
  The rule outlives the case: anything merged from main that reads `Document.Text` meaning the code gets the file
  here. On this branch `Document.Text` answers "the file" and never "the code".
  — **The naming half was finished after this task, against the hexide-mcp-server delta rather than a task
  line of its own, for the tools that take a document's name.** Inventoried first: eleven tools already
  resolved a name through `DocumentLookup` across every loaded project, case-insensitively, with `project` and
  the ambiguity refusal. They were the two content tools, `open_file`, `view_designer`, `get_form_controls`,
  the four mark tools, `run_to_cursor` and `set_next_statement`. Three did not. `set_control_property` looked
  in the startup project only, and `move_control` and `add_control` matched open designers by name and took the
  first. They now resolve as the rest do and take `project`, and so does `add_file`, which now takes `project`
  to name where it adds. The rule moved out of the tool bodies into `NamedDocuments` (`HexIDE/IDE`), beside
  `MarkLineRules` and `DocumentContent`, and `NamedDocumentsTests` covers its refusals. The replies of those
  fourteen tools carry `project` and `document` (`MutateResult`, `FileContentResult`, `FormControlsResult`,
  `AddControlResult`, `BookmarksResult`, `BreakpointsResult`), a carried file's included, refusals of a
  document the name did find included. So do `add_file`'s reply and each `get_diagnostics` item, through a
  reverse lookup of its `uri` that leaves them out when the `uri` names nothing loaded, or names a file two
  loaded projects share, since nothing then says which project's copy a diagnostic is about. `get_debug_state`
  and `get_call_stack` carry the running `project`. `uri` is now described as the name a language server
  knows the document by in `get_bookmarks`, `get_breakpoints` and `get_diagnostics`.
  — **Not done: the tools that name a document without taking its name.** The note of the run-control replies
  (`Paused at Form1 line 9 (Step).`, from `run_project`, `break_program`, `continue_program`, the three steps
  and `run_to_cursor`) gives a bare module name. `get_locals`' `context` and the watch tools' `context` are
  `Module.Procedure`. `get_undo_state`, `invoke_designer_undo`, `invoke_designer_redo` and
  `invoke_format_command` act on the active designer and do not say which form it is. `activate_document_tab`
  and `close_document_tab` take a tab's title, and `get_open_editors` and `get_document_tabs` report titles.
  With a group holding two `Form1`s, none of them can say which one it means. design.md ("Settled while
  building phase 3") says why they were left. Filed as #886. The hexide-mcp-server
  requirement ("a tool that accepts or reports a form, module or class") is therefore only partly met, and
  5.8 has to carry that into the archive rather than record it as met.
  — **Tests.** The scenario *Setting a breakpoint by name in the wrong case* is
  `BreakpointByNameTests.SettingABreakpointByNameInTheWrongCase`, in the integration suite because it renders
  the gutter and runs the project. Its three outcomes (the dot in `Form1`'s gutter, a run stopping on the line,
  the sidecar naming `Form1`) each failed when the breakpoint was keyed under a second definition called
  `form1`. That test repeats the tool's steps rather than calling the tool, which no test can, so the tools'
  use of the rule is guarded separately, by `ToolDocumentNamingTests` reading the tool source. Every tool that
  takes `project` finds the name through the lookup, and the lookup searches every loaded project. The name is
  used for nothing else, so a designer or editor is matched by the document found, never by name. Every
  `MutateResult` after the lookup goes through `Naming`, whose body is pinned, and every other reply record
  names the document itself. Two regressions passed every test before it and each fails it now: a
  `move_control` that took the first open designer whose form had the caller's name, and a `Naming` that
  returned its reply unchanged. Recorded in `docs/archive/mcp-server-gaps-closed.md`, under the #467 and #575
  entries, and in the CHANGELOG. Not live-verified; that belongs with 3.22's pass through the running IDE.
- [x] 3.19 Add-ins: content is the whole file; positions from the top of the file; `SetContent` refused if the
  header changes. The AI Chat add-in's prompt and apply paths updated.
  — **`AddinDocument.Content` is the code window's buffer**, so it is the text every position on the surface
  counts in, which 3.16 found it was not. Positions needed nothing more.
  — **The add-in is told why** through `TrySetContent`, `TryApplyEdits` and `TryNavigateTo`, default interface
  members answering `AddinEditResult`; the members that answer true or false are those with the reason dropped.
  Names, shape and the rejected alternative are in design.md, "Settled while building phase 3". Names resolve
  through `NamedDocuments`, so an add-in gets automation's refusals; an edit's refusal names its line and column.
  — **A line the document does not have is refused**, where it threw: on the UI thread, inside `NavigateTo`'s post,
  which ended the process. Settled in design.md with the rest.
  — **AI Chat:** the prompt and tool descriptions say content is the whole file, the header is read-only and what
  `apply_edit` takes; `apply_edit`, `navigate_to` and the Apply button report the IDE's answer; four tools take
  `project`; the privacy notice says "contents", and is shown again once. A new ai-chat delta in this change holds
  the add-in to reporting a refused change.
  — **Tests:** `AddinWholeFileTests` carries the addin-system delta's four scenarios, "an add-in replacement that
  changes the header is refused" with its reason, the line-and-column refusal, out-of-range positions, a round trip
  of `BillOfFare.frm` and `Game.cls` through the surface, the six shipped signatures, and a v0.2.0-shaped host
  answering the new members. `AIChatWholeFileTests` carries the ai-chat delta's two scenarios and the prompt,
  description, navigation and notice cases.
  — **Not done:** AI Chat's `get_file_content` still reads through navigate-then-active-document and needs a
  read-by-name member; since the review it refuses rather than answers with the wrong file. `GetFor` cannot say a
  name is ambiguous. Both recorded in design.md. The first is #887; the second is an unmet scenario of this change's own delta, so 3.21 takes it up. Not
  live-verified; 3.22 carries the checks.
  — **Review** (design.md, "Settled in the 3.19 review"). The ai-chat requirement is met on both paths: a refused
  `apply_edit` is shown in the transcript as well as told to the model (`TheAssistantsEditIsRefused`, which drives
  the tool call through the chat), and an Apply's outcome is told to the model as well as shown, held while the
  model is responding (`ApplyingABlockTheIdeRefuses`, `ABlockAppliedWhileTheAssistantIsRespondingIsToldOnlyWhereANoteMayGo`,
  `ANoteFromTheIdeIsNotShownAsTheDevelopersOwnMessageWhenTheChatReopens`). The add-in contract no longer implies a
  replacement checks a member's attribute run; 4.5b owns that. Navigation brings the window forward, or says it
  could not. AI Chat's `get_diagnostics` refuses an unknown or ambiguous name, replies use one spelling, and blank
  `apply_edit` content is refused. An `ApplyEdits` refusal names the edit by its index and the positions sent.
  `TheFileListCoversEveryLoadedProject` fails under the review's missed mutation (`GetProjects().Take(1)`).
- [x] 3.20 New strings (refusal reasons, the reworded read-only banner) added to `en` and every shipped pack in
  the same change. A fold's label is its first line, so the header fold has no string of its own (settled in
  design.md, "Settled while building phase 3").
  — **Fold labels: no key.** The header fold's label is its first line (`VERSION 5.00`, `VERSION 1.0 CLASS`), as
  every server fold's is, so there is nothing to translate. Phase 4's attribute folds decide their own.
  — **Refusal reasons landed with the code that shows them**, each in `en` and all 29 packs in its own commit:
  `Str.CodeEditor.Msg.RenameTouchesReadOnly` and `Str.CodeEditor.Dialog.InsertFileTitle` under 3.9 (52ff9d0),
  `Str.CodeEditor.Msg.NoMarkOnReadOnlyLine` under 3.12 (0b5aa6f). Nothing was owed for them here.
  — **The banner changed key here**: `Str.CodeEditor.ReadOnly.Banner` is gone from `en` and every pack, and the
  code window binds `Str.CodeEditor.FileNotSaved.Banner`: "This file will not be saved — HexIDE cannot yet
  reproduce it faithfully, so saving it would change it. Its code is part of the file, so typing in this window
  is refused." It speaks of the whole file, so it cannot be read as the header's grey lines, and claims only the
  two things enforced. It no longer says the code "cannot be edited", which #701 shows is untrue of Replace All,
  formatting, rename, add-ins and automation. "This form" became "this file" too: a UserControl's or
  PropertyPage's code window shows the same banner. Translated into all 29 packs; `en-GB` needs no override.
  — **Audited by script** against the merge base with `upstream/main` (994f1a8): the branch adds four keys to
  `en` (the three above and the banner's), removes one (the old banner's), and rewords none under an existing
  key. All four are in all 29 shipped packs, none equals the English, `{0}` survives in every
  `NoMarkOnReadOnlyLine`, no pack (nor `en-GB`, `zz`, `zz-ZZ`) keeps the removed key, and every `Str.*` key on
  an added code line is in `en`. Tasks 3.17 to 3.19 added no literal a person sees in IDE chrome: no `.axaml`
  changed, and every added literal is an automation or add-in reply, a log line, or AI Chat's own text. Add-in
  replies and AI Chat are English by design (design.md, "Settled while building phase 3": "The reasons are the
  ones automation gives, and are English", and the AI Chat `project` paragraph).
  — **Test:** `ReadOnlyBannerIntegrationTests.CodeEditor_ForAnUnfaithfulForm_TheBannerSaysTheFileWillNotBeSaved`
  supplies the `en` text under the new key on the view and asserts the rendered banner shows it, and that the
  text does not say "cannot be edited". `LocalizationCoverageTests` passes for any key `en` has, the designer
  banner's included, so it could not pin which key the banner binds. Binding the designer's key fails it.
  — **Not yet seen running**: the banner needs a form HexIDE cannot save, so it is added to 3.22's live check.
- [ ] 3.21 Tests, one per scenario in the deltas this phase implements, named after the scenario, plus: buffer equals the file on open; an unchanged save writes the buffer; formatting leaves the
  header; Replace All does not touch a control's `Begin` line; a designer move is not undone by code-window
  undo; the header stays folded after formatting; a migrated sidecar keeps marks on their statements and is
  idempotent; an add-in replacement that changes the header is refused.
  — **Already done:** "an add-in replacement that changes the header is refused" is
  `AddinWholeFileTests.AnAddinReplacementThatChangesTheHeaderIsRefused`, written with 3.19. Do not duplicate it.

- [ ] 3.22 Verify in the running IDE, through the automation tools rather than by asking anyone to click:
  open a VB6-authored `.frm`, `.cls` and `.bas`; snapshot the header folded and greyed under a light and a
  dark theme; confirm typing and F9 in the header are refused and say why, and that Replace All leaves the header alone (saying
  why is #705); confirm the margin
  and status bar count from the top of the file; confirm a designer move updates the header with the code
  window open. UI work is not complete until it is seen running.
  From 3.20: open a form HexIDE cannot save (one whose `.frx` is missing) and snapshot its code window's banner,
  which should read "This file will not be saved", in English and under Pseudo (LTR), where it is bracketed.
  From 3.19, in the AI Chat panel, its transcript driven through `ChatViewModel.Say` and the reply's Apply button
  through `interact`: an approved whole-file Apply of a form's own content, unchanged, is accepted and says so; an
  Apply whose designer block has one caption changed is refused, with the reason visible in the transcript, and
  the reason is in what the model is told (the session's IDE note, readable in `chat-history.json`); after each
  whole-buffer write, where the caret ends up and whether the header fold is still folded. `get_file_content` on a
  file open behind another tab returns that file, not the one in front, and leaves the header fold folded or
  says why not; `navigate_to` into a background tab brings it forward.
  Refresh `docs/mcp-first-contact.md`'s transcript from that run: its `open_file` reply still reads
  `{"success": true}`, where the tool now returns `project`, `document` and a `note` too, and
  `FirstContactTranscriptTests` checks only that the fields it shows still exist.

## 4. Member attributes

- [ ] 4.1 Detect procedure-level and declaration-level `Attribute` runs, and anchor each to the line it
  describes.
- [ ] 4.2 Read-only and greyed, through the same provider and colour as the header.
  — **Both halves exist already:** the runs have been read-only since 3.7, and 3.15 greys every read-only
  region, these included. What is left is checking both once 4.3 folds the runs.
- [ ] 4.3 Folds from the end of the described line, built with character offsets and nested inside a server's
  procedure fold.
- [ ] 4.4 A member's rename rewrites its own attribute qualifiers — a procedure, property or module-level
  variable alike, since VB6 writes `VB_Var*` lines after a declaration. A server rename's edits to them are
  permitted; its edits to the header are not.
- [ ] 4.5 Enter at the end of a described line inserts after the attribute run, not between the line and its
  attributes.
- [ ] 4.5a A run follows the line it describes: deleting that line deletes the run with it, and a cut takes
  it along. An orphaned run, however it arises, is preserved as inert text. 3.7 already covers a selection
  spanning the line and its run. What is left is the line on its own, which the provider cannot widen to
  (AvaloniaEdit refuses segments outside the requested span), so it needs a guarded write.
- [ ] 4.5b Whole replacements refuse a change to a member's attribute run: add-in `TrySetContent` and
  `SetContent`, and automation `set_file_content`, as the code-editor requirement "Only the IDE SHALL change a
  read-only region" requires. Until then `GuardedContent.Replace` compares the header alone (design.md, "A whole
  replacement checks the header"), and the add-in contract's doc comments say a replacement that changes a run is
  applied. Close both, and refuse with a reason that names the member.
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
  — **And D24's sentence "A mark already on one, which only an older sidecar can have left there, can still be
  removed"**, which 3.17 made wrong: the load now drops such a mark from any sidecar, in either format. D24 has
  to say where one can still come from, if anywhere, and go on saying that one already there can be removed.
- [ ] 5.6 `docs/MISSING_FEATURES.md` and `docs/MISSING_LANGUAGE.md`: line numbers, folding, rename, Find and
  the Procedure Attributes row (#62). The attribute rows whose mechanism text says "stripped from the body".
- [ ] 5.7 Comments that describe the old model: `FormCodeText`, `ModuleFileFormat`, `ModuleDefinition`,
  `DirtyDetector`, the redactor and disclosure, `DocumentSavedEvent`, `IBreakpointService`, the automation
  tool descriptions.
  — **And the remark on `CodeEditorViewModel.ToggleBreakpoint`**, which says, as D24 does, that only an older
  sidecar can have left a breakpoint on a read-only line. It is rewritten to agree with what 5.5 makes D24 say.
- [ ] 5.8 Validate and archive; rewrite the Purpose of each spec the archive touched if the CLI replaces it,
  and fill in the new capabilities' Purpose. The hexide-mcp-server requirement is only partly met (3.18's "Not
  done" note): unless that issue is closed first, note it under that spec's `## Purpose` with the issue linked,
  as `openspec/config.yaml` asks of a requirement the code does not yet meet.
