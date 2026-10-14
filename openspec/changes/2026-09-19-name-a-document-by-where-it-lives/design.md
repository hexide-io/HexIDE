# Design record

Settled with the maintainer in an interview, against a mapping pass over the codebase. Seven readers each
covered one subsystem, and an adversarial verifier re-checked each reader's claims. All five foreign servers
the suite drives were then run against the proposed `untitled:` shape; four accepted it and one refused it.
The reasoning is recorded because most of it rests on facts about today's code that the code's own comments
contradict.

## What the code does today

These were read, not assumed. Several contradict comments in the tree.

- **One string does five jobs.** `vb6://form/{Name}` or `vb6://module/{Name}` is at once the wire URI, the
  breakpoint and bookmark key, the sidecar key, the diagnostics key and what the automation surface reports.
  It is minted by string interpolation in eight places across seven files, with no shared helper: the code
  editor, the Object Browser, `EditorService`, `ProjectRunnerService`, the sidecar (twice), the toolchain
  service and the automation tools. Six places read the module name back out of it with `LastIndexOf('/')` or
  a prefix strip: the code editor view's debug module name, Run To Cursor, Set Next Statement, the runner's live breakpoint
  push, the add-in diagnostics service and the sidecar's project lookup. Under a `file:` or `untitled:` URI
  every one of those reads `Module1.bas` or a file stem where it expects `Module1`.
- **Classes share `module` with modules, and nothing names the project.** Two projects in a group with a
  `Module1` share one key today. Their breakpoints, bookmarks and sidecar entries bleed into each other.
- **A rename already breaks identity.** The LSP session freezes its URI when it is created. The gutters
  freeze theirs when the view attaches. Every request, and the F9 and Ctrl+F2 commands, recompute the URI
  from the live name. After a rename they disagree (#269).
- **A form's leading `Attribute VB_*` block is already in its editor, visible and editable.** The `.frm`
  reader appends everything after the root `End` to `Code`, and the editor shows `Code` verbatim.
  `FormCodeText`'s remarks, the `set_file_content` description and the closed automation-gap entry all said
  the block is hidden. The code was right: measured in the running IDE on 2026-09-20 (task 0.1), a form's
  buffer opens on its `Attribute VB_*` run, shown as ordinary code, unfolded and editable. A `.bas` or
  `.cls` has its whole header split off into `OriginalHeader`. So today the two kinds differ, and in the form
  case the one that "hides" nothing leaves `VB_Name` open to deletion.
- **Member-level `Attribute` lines are in every buffer** (procedure-level, and `VB_Var*` after a declaration),
  because only the leading run is ever split off. The interpreter raises when it reaches one.
- **No line is tracked.** Breakpoints and bookmarks are bare integers. Inserting a line above one leaves it on
  the old number, which is now a different statement. There is no anchor anywhere in the IDE.
- **Almost every document has a file, and which ones do is an accident of the menu used.**
  `ProjectService`'s Add Form, Module, UserControl and PropertyPage write the file the moment they are
  created, and the MCP `add_file` tool goes the same way. In a never-saved project that file goes into a
  per-project scratch folder, which servers are already told is their workspace root — and which is minted
  but not created until something writes into it. Exactly **two** routes produce a document with no path:
  the Standard EXE template's `Form1` (`IProjectTemplate.cs:16`), and the Project Explorer's Add Form
  (`ProjectToolViewModel.cs:128`), which builds the definition itself and never goes through
  `ProjectService`.

  **Earlier drafts of this bullet also named add-in project templates. That is wrong** — no add-in path adds
  a form or a module, checked 2026-09-20. The fork between those two routes is hexide-io/HexIDE#489, which
  was settled by measurement on 2026-09-20 and closed.

## Identity inside the IDE

**The identity is the document, qualified by its project.** In memory it is a value comparing by reference
to the document's definition, where the definition is the form, or the module for a module, class,
UserControl or PropertyPage (a UserControl's module is preferred over its form part, as the editor already
does). It also holds a reference to the owning project. It survives rename, first save, Save As and a project
rename, because none of those replaces the definition. It does not survive unloading the project, and does
not need to, because the sidecar carries state across that.

It is deliberately not a string. Today's stores disagree about case: two compare ordinally, the debugger
ignores case and the ledger normalises. A value with its own equality ends that argument. It is also not an
id we would have to allocate and persist. The open crash-recovery change proposes a stable *project*
identity for matching across sessions. Nothing here conflicts with it: this identity lives only for a
session, and the per-user sidecar is already one file per project, so it needs no project identity inside
it.

**For humans and automation it is written `<Project>/<Name>`.** That form is for display and lookup only, and
nothing keys on it. Lookup is case-insensitive, because VB6 names are.

**Consumers, and what each one keys on after the change:**

| Consumer | Today | After |
|---|---|---|
| Breakpoint store, gutter, F9 | URI string, ordinal | identity |
| Bookmark store, gutter, Ctrl+F2 | URI string, ordinal | identity |
| Debugger module name, Run To Cursor, Set Next Statement, live push | name parsed from URI | the definition's name, and its project |
| Sidecar | URI string, parsed back | document name within the project's file |
| Editor diagnostics | wire URI | wire URI of the editor's own session (unchanged in kind) |
| Compiler diagnostics | `vb6://form/{Name}`, forms only | wire URI resolved from the compiler's file path |
| Add-in diagnostics `GetFor(name)` | last URI segment | identity, looked up by name |
| Automation `uri` fields | `vb6://…` | `project` and `document` fields, plus the wire `uri` |

**Names that form the wire name must not collide, and VB6 agrees.** An `untitled:` name embeds the project
name and the document name. Both were measured against the real compiler (recorded in the oracle document):
a form and a standard module in one project may not share a name, case-insensitively, and two projects in
one group may not share a `Name=` — the group is refused at load and nothing in it builds. HexIDE enforces
neither today: the Project Explorer's Add Form reuses `Form{Count+1}` after a delete, a form rename checks
only the form's own controls, and a new project takes `Project{LoadedProjects.Count+1}`, which repeats after
an unload. So these become requirements, and they are VB6's own rules rather than something this change
invents to make its URIs work.

## Names on the wire

**A document with a file is named by its `file:` URI.** "Has a file" means it has a path and that path is its
own. Make EXE temporarily repoints every path into a temporary folder and puts them back afterwards, and that
must never reach a server. So the wire name is fixed when the language session opens, and changes only
through the events below. It is never derived live from the path.

**A document with no file is named `untitled:<Project>/<Name>.<ext>`.** Details:

- `<ext>` comes from the kind: `.frm`, `.bas`, `.cls`, `.ctl`, `.pag`. It is the extension the document will
  be saved with, and it is what routing reads.
- There is no leading slash. The two spellings normalise differently, so one is chosen and never mixed.
- It is minted through the URI type, never by interpolation, so a non-ASCII name is percent-encoded.
  **texlab** silently drops a notification whose URI is not strictly valid: no response, no error, nothing on
  standard error, and the connection stays up and answers about later documents normally. A request naming
  that document is then never answered at all — measured on the wire as sent with no outcome, indefinitely,
  because only `initialize` is bounded by a timeout (hexide-io/HexIDE#486). Its percent-encoded form is
  answered by the same server in the same process. That difference is the whole argument for minting rather
  than interpolating, and it is asserted as one test rather than two.
- Comparison is case-insensitive over the path, as it was for `vb6:`, because both segments are VB6 names.
  **A push server echoes the name back byte for byte, in either spelling** — measured on texlab, raw and
  percent-encoded. A **pull** server cannot echo anything: a `DocumentDiagnosticReport` carries no URI, so
  the client files the answer under the name it asked about. So the comparison rule is only ever exercised
  against a server that speaks the name back, and on the other three the name HexIDE sent is the only name
  in play. Worth stating because the obvious test — compare the published URI with the one sent — is a
  tautology on three of the five, and was written that way before it was caught.

**An identifier changes only by close and reopen.** The protocol's own guidance for a rename is a close under
the old name followed by an open under the new one, and the reason it gives applies here: more than the name
can change. The triggers are:

- a document saved for the first time, or saved to a new path (the save event already fires only for real
  writes and carries the definition, so the handler compares the session's name with the new one). One
  write does not raise it and must: saving a project into another directory repoints every form without
  announcing it, which is a gap in the event rather than a second trigger to invent;
- a document with no file whose name changes, or whose project's name changes. A form's name change is not
  announced today until the designer's pending state is flushed, so the same "layout changed" notification
  that refreshes the header carries it.

On a trigger, each server that has the document open is sent `didClose` under the old name and then
`didOpen` under the new one, with the current text, **in that order on each connection**. Today both are
fire-and-forget, so nothing orders them, and the order is asserted on the wire. Where the trigger was a save,
the save notification follows under the new name.

**Diagnostics under the old name are withdrawn.** A pull server's result id for the old name is discarded.
For a push server, the client records an empty set for the old name on that connection when it sends the
close, which is what it already does for a pull server's state. Otherwise a server that does not itself
clear on close leaves its markers under a name nothing answers to any more — which is #269's shape, arriving
by a different route — and the automation and add-in caches, which key on the wire name, keep them too.

**A UserControl or PropertyPage is named by its module's file.** It has two paths, and they diverge today:
creation sets only the module's, and the code window's Save writes the form part, repointing that one alone
(#474). The module's path is the one the project file names and the one the save event carries, so it is the
one that names the document. The divergence is a defect in its own right, filed there rather than papered
over here.

**A file that disappears keeps its name.** A saved document whose file is deleted from disk stays `file:`,
following the protocol maintainers' own guidance. `untitled:` means "no file yet", not "file missing".

**Open documents survive a root restart.** Saving a never-saved project moves its workspace root from the
scratch folder to the `.vbp`'s folder. Setting another project as the startup project and Save Project As
move it too. Each move restarts the running servers, and today the restarted connections have lost every
document except the one whose open triggered the restart. Later changes to the others reach a server that
never saw them opened. The registry keeps a record of what is open and re-opens those documents on the
restarted connection. This is filed separately as #469, and stated here as a requirement because a first save
triggers it.

## Routing

**By extension, for every document.** The `vb6` scheme branch, `SchemeLanguageOf` and the language identifier
doubling as a claim all go.

**Project members on an ambiguous extension are gated.** `.cls` is a VB6 class and also another language's
class file. A project member is known to be VB6, whatever its extension shares. So a project member whose
extension is ambiguous goes only to servers whose claim establishes VB6: they claim a **VB6** extension no
other language uses — every VB6 source extension except `.cls` — or they declare the identifier `vb6`. Said
loosely as "an extension no other language uses", the rule would admit a Markdown server through `.md`, which
is the opposite of what it is for.

**Membership is stated when the document is opened, not asked of the workspace.** The registry deliberately
holds no model of the project: the projection it has exposes a directory and a list of folders, and nothing
else. Answering "is this a member?" from it would mean either parsing project and document names back out of
an `untitled:` URI, which this change forbids everywhere else, or matching paths, which cannot see a document
with no file. The opener already knows the answer — the code window opens project members, the carried-file
editor opens carried files — so it says so when it opens the document, and the registry remembers it for as
long as the document is open. Change, close and save route by the same record, so a server that starts later
cannot be handed a document it was never offered. Every `untitled:` document is a member, because a carried
file with no path is never opened at all.

Two behaviours change, and both are more precise than today:

- An entry that claims only `.bas` stops receiving forms and classes. Today the scheme branch handed it every
  VB6 document.
- An entry that declares `languageId: vb6` and claims no VB6 extension stops receiving VB6 documents. The
  identifier names the language a server is told, and no longer routes anything, except as the ambiguity
  gate above.

## The whole file in the editor

**Two boundaries, not one. Conflating them is the trap.** The model keeps `Code` exactly as it holds it
today, and the buffer is `Code` with a **prefix** in front of it. What the prefix is differs by kind, because
what `Code` already holds differs by kind:

| Kind | `Code` holds today | Prefix | Flush splits at |
|---|---|---|---|
| `.bas`, `.cls` | the body, header split off | `OriginalHeader`: the class `VERSION`/`BEGIN…END` block and the leading `Attribute` run | the prefix's end |
| `.frm`, `.ctl`, `.pag` | everything after the root designer `End`, **including the leading `Attribute` run** | the designer part only: `VERSION`, `Object=` lines, and `Begin…End` | the prefix's end, the root `End` |
| any of these where load split nothing off | the whole file | nothing | nothing is split |

**The protected region is the other boundary, and it is one rule for every kind**: the top of the file
through the last line of the leading `Attribute` run. For a form that region straddles the split — part of
it is the prefix, part of it is the first lines of `Code` — and that is fine, because protection and
composition answer different questions. Getting this wrong in either direction corrupts a file: prepend the
attribute run and the buffer shows it twice; split after it and the run leaves `Code`, so the next save
writes a form with no `VB_Name`.

The third row is not a corner case. An unparseable `.ctl` or `.pag` keeps its whole file in `Code`, and so
does a `.bas` or `.cls` whose first line is blank, because the header reader recognises nothing (which is
also a live defect, #472). For those the buffer already is the file, the prefix is empty, and the protected
region is found from the text rather than from the model.

**Where the prefix comes from:**

- `.bas`/`.cls`: `OriginalHeader`, verbatim, which is already kept, or the canonical header for a module
  HexIDE creates. A rename retargets `VB_Name` as an edit the IDE makes itself.
- Forms, UserControls, PropertyPages: the file's designer part as read, which the reader keeps only in part
  today (the `Object=` lines survive, the `VERSION` line and the block do not), so it is kept whole. After
  the model changes, and after any save, it is the text that was written. A form held read-only because the
  IDE cannot reproduce it is never re-rendered. A form with no file renders its companion references from
  the name its first save will use.

**Open, and it belongs to this phase rather than to a later one: where a created form's FIRST header comes
from.** The three triggers below are a committed designer change, a save and a reload; none of them is
creation or opening a window. So a form that has just been created and not yet touched shows no header at
all, while the code-editor spec says a document with no file "SHALL hold the header its first save will
write". Task 3.4 closes the render, not that gap.

It is not a gap to patch with a fourth trigger, for two reasons that have to be settled together:

- **A header that materialises when a window opens has no *before*.** Every refresh shifts the document's
  marks by the change in the header's height, and that needs a recorded previous header to measure against.
  Rendering lazily out of the accessor looks like the answer and is not: the accessor is also what the
  refresh reads to find out what the header WAS, so a lazy render would return the post-change value and the
  marks would never move at all. Whatever supplies the first header has to store it.
- **It collides with the sidecar's numbering.** Marks restored from the sidecar are numbered against the
  buffer as it stood when they were written. If a header appears at open and the marks then shift, a
  document whose sidecar already counted the header is shifted twice. That question is #885,
  filed from the 3.17 review, which built the migration without settling it, and it cannot be answered
  separately from this one.

And #500 raises the stakes rather than lowering them: it implements what #489 decided, which is that a new
document gets no file until the project is saved — so after it, *every* newly created document is in this
state rather than only the template's `Form1` and the Project Explorer's Add Form.

**The prefix lives on the model, not in the editor.** The interpreter, the pre-run syntax check and the
standalone runner all read the definition and have no access to a code window, and a form open only in its
designer has no code window at all. So `FormDefinition` carries its designer text beside its components, the
way `ModuleDefinition` already carries `OriginalHeader`, and one accessor composes the file text from either.
A reload adopts it with the rest of the fidelity state.

So the invariant, stated exactly: **the buffer is the text of the file as it stands, and after a save it is
the text that save wrote.** Not "equals the disk at every moment", which no editor with unsaved changes can
promise — and not "until the model changes" either, because a save re-renders a form whether or not the model
changed, and the serializer is not byte-faithful to every file.

**When the prefix is refreshed.** Three triggers:

1. **A committed designer change.** Never per property write: a drag writes the model on every pixel, and a
   refresh per pixel would mean a full render and a whole-document `didChange` per mouse move. The designer's
   undo stack signals a commit, and three paths bypass it today and must raise the same signal: the menu
   editor, the colour palette, and the automation tool that sets a control property with no designer open. A
   single "layout changed" notification on the form carries all four. It **flushes the designer's working
   collections into the model first**: they run ahead of `FormDefinition.Components` until the existing
   apply-unsaved-changes event, so a render before that flush would miss the control just added.
2. **A save.** Every write of a form goes through the serializer, so the file can differ from the buffer even
   with no model change. The header the save wrote becomes the buffer's prefix, as a write the IDE owns. This
   also covers the companion-file references, which are derived from the name being written and therefore
   change on Save As and on a first save to a name of the developer's choosing.
3. **A reload** after an external change, which replaces the whole buffer anyway.

**A refresh that changes the prefix's line count moves every mark below it.** Adding a control adds lines;
a property returning to its default removes one. Breakpoints and bookmarks are bare integers with no
anchors, so the guarded write path that replaces the prefix also shifts the marks of that document by the
difference, in the stores rather than in the gutter, so a document with no open code window moves too. This
is new work that the old arrangement did not need, and it is the price of one numbering. It is also the
answer to the pre-existing gap that nothing tracks a line: the fix is scoped here to the one edit the IDE
makes itself, not extended to typing.

**The interpreter is given the whole file.** Its line numbers are then the editor's, with no conversion at
the breakpoint push, the current-statement bar, Run To Cursor, Set Next Statement or the Call Stack. Both
grammars declare the whole-file shape (module header, references, designer block, class config, module
attributes), and the walk executes only module blocks and procedures. But no test has ever parsed a whole
real file with either grammar, so task 0.2 proves it over the corpus before anything relies on it. The
syntax check before a run parses the same text, and the standalone runner composes the same way, so a
compile error's embedded line agrees with the IDE's.

**Dirty detection has to move with it.** It compares the code window's text against `Code` today, for modules
and forms alike. Once the buffer carries a prefix those are never equal, so every open document would count
as edited and every external change would be reported as a conflict — silently disabling the silent reload
the file-watcher capability requires. It compares the buffer's body, split exactly as the flush splits it,
against `Code`.

**Line numbers are file lines everywhere.** In a class with the canonical header, the first line of code
becomes line 14. This diverges from VB6, whose code window hid the header, and is recorded as such. Whether
VB6's status bar counted from the hidden header is unmeasured. It cannot be probed with `/make` and is
recorded as unmeasured rather than guessed.

**The compiler is the one place a conversion remains, and it is now measured.** VB6's `/make` log reports
`Line N` as a **0-based index into its own code view**: the file minus the designer or class block, and
minus every `Attribute` line, *including the ones inside procedures*. So the file line is
`N + 1 + hidden lines above it`, and the hidden count is not a constant per file kind — a procedure
attribute halfway down the file shifts everything below it. The conversion is unavoidable, because it is
the compiler's numbering and not ours, but it lives in exactly one place: where the log is parsed.
Everything downstream of that already speaks file lines. The measurement, the exact log format (which the
current expression does not match at all) and what `Line 0` cannot distinguish are recorded in
`docs/vb6-fidelity-oracle.md`.

## Protection

**Read-only sections cover typing only.** Every programmatic write in the editor (twenty paths were
inventoried) goes straight to the document and ignores them. The existing whole-document read-only gate for
unfaithful forms has the same hole today. So protection is two things:

1. A read-only section provider over the header and member-attribute regions, combined with the
   whole-document gate, and re-evaluated after a reload that changes the fidelity verdict. That verdict went
   stale until 3.7 (#475). Inserting at the very top of the file is refused, because the
   stock provider allows insertion at a region's edge.
2. One guarded write path that every programmatic writer goes through, with a stated policy for each.

**"Read-only region" means the header and the member-attribute runs, and nothing else.** A form the IDE
cannot reproduce is held read-only as a whole, and that must not make every line of it a read-only *region*:
otherwise a developer could not set a breakpoint anywhere in such a form, and Find would match nothing in it.
The two are separate gates, and the rules below that turn on a region (marks, Find, member attributes) test
the region, never the whole-document gate.

**Changing a region is more than touching it.** A member's attribute run is whole lines, so the line break at
the end of the declaration it describes lies outside it. Removing that break still destroys the run: the
first attribute line joins the declaration, stops starting with `Attribute`, and the file stops compiling. So
a writer asks whether an edit *changes* a region (`TextRegion.Changes`), which protects a member run from its
**anchor**, the declaration's line end, and the typing provider starts its protected span at the same place.
Whether a line is *in* a region (`Touches`) stays the narrower question, for the callers that rewrite a whole
line and put its break back. Found by the review of task 3.11, where a Replace of `Currency\r\n` made the
join.

| Writer | Policy |
|---|---|
| Designer or model header refresh; a save's rendered header; a rename's `VB_Name` | Owner. Writes the region, not undoable from the code window (see **Undo** below, which supersedes the "off the undo history" this row used to say), and shifts marks by the change in line count. `VB_Name` is in the header for a `.bas`/`.cls` and in the first lines of the code section for a `.frm`/`.ctl`/`.pag`, so for those it is a write below the prefix; it is marked as the IDE's own all the same, and one commit's writes are one undo entry. It follows the **document's** name, which for a UserControl or PropertyPage is its module's (the root control's name is not connected to it; #493). |
| A member's own `Attribute <Member>.…` qualifiers, on that member's rename | Owner. Follows the rename, whether the IDE or a server makes it. A server's rename carries this edit itself — the bundled server's is whole-word, and `Total` in `Total.VB_Description` is a whole word — which is why the rename row lets exactly that edit through. The IDE renames no member of its own yet; phase 4 is where member attributes are built. |
| Formatting (server answer, any server) | The whole-document edit is reduced to the lines it changes, and changes inside a read-only region are dropped. The bundled formatter also leaves the header alone. |
| Server rename | Refused as a whole if any edit lands in a read-only region, and the developer is told why in a message. Edits to the renamed member's own attribute qualifiers are allowed. Refusing is not rare: the bundled server's rename is lexical and whole-word over the buffer, so with the designer block in it, renaming a local called `Text`, `Top`, `Caption` or `Index` would otherwise reach the header. The bundled server therefore skips those regions itself, using the same rule, so the refusal is reserved for a server that does not. |
| Replace, Replace All, Find | Match only outside read-only regions, and never a match whose replacement would remove the line break a member run hangs from. VB6 never searched the hidden header. A pattern match refused this way is stepped past by one character, so it cannot hide a match inside it. AvaloniaEdit's own search panel, whose Replace All writes anywhere, is uninstalled from every code window, so the dialog is the only Find surface. |
| Insert File | Never into a read-only region: when the selection touches one, the file's text goes after the region, on lines of its own, and replaces nothing. Elsewhere the selection is the developer's choice and is replaced as always. |
| Completion commit | Not applied inside a read-only region. A completion completes the word at the caret, so there is no "after the region" that would still mean the same thing. |
| Enter (auto-close, auto-indent) | Writes nothing with the caret inside a read-only region: there is no "after" a line break could sensibly go to. Its keyword re-casing of the lines just committed skips any line inside one, because re-casing rewrites a line and no insert rule covers that. |
| Event stubs, Add Procedure | Append at the end of the document, which is never inside a region. Safe by construction. |
| Paste, cut, delete, backspace, Ctrl+D, text drag-and-drop | Left to the read-only section provider, which the editor library consults on each of these routes itself. Inside a region a paste is therefore refused silently rather than moved after it, and that is accepted rather than replaced with a paste of our own. |
| Add-in `SetContent` | The same rule as automation: the whole file with its header unchanged, or the body alone with the header kept. Refused only when the header would change. The shipped chat add-in applies a model's fenced block, which is a body. `TrySetContent` gives the add-in the reason (task 3.19); `SetContent` answers false, as it always has. |
| Add-in `ApplyEdits` | All or nothing: if any edit touches a read-only region, or names a line or column the document does not have, none is applied, because the rest alone would leave the add-in's change half made. `TryApplyEdits` gives the reason, naming the edit by its line and column; `ApplyEdits` answers false. |
| Automation `set_file_content` | Accepts the whole file (header unchanged) or the body alone (header kept). That settles #338's asymmetry. |
| Automation `type_text`, `press_key` | Refused inside a read-only region, and the reply says so and gives the offset where editing can resume. `press_key` judges only a key that would change text — Enter, Tab, Backspace, Delete, Ctrl+V, Ctrl+X, Ctrl+D — against what that key would change; a navigation key is never refused. |
| Initial load; reload after an external change | Owner. Replaces the whole buffer. |
| Edit-and-Continue revert (answering No) | Restores the code as it stood when the prompt opened, under the header the buffer carries **now**, replacing only the span that differs. Not the whole buffer: a designer commit or a save landing while the prompt is open replaces the header and the prefix with it, and restoring a whole-buffer snapshot put the old header back under the new prefix, so the next flush took the code from the wrong offset (measured in both directions, task 3.13). |

**Undo: read out of the library, not guessed.** A designer change must not be undoable from the code window,
which the form-designer undo capability requires, and it must not stop the code window undoing an earlier
code edit either, because that capability also says the two histories do not interfere. The editor library
was decompiled (2026-09-19, AvaloniaEdit 12.0.0) and these are its actual mechanics:

- An undo entry is a document change holding an **absolute offset**, and undoing it replays a replace at that
  offset. A change the stack did not record, above an entry, therefore invalidates it: undoing that entry
  afterwards edits the wrong text. "Do not record it" is unsafe, measured rather than assumed.
- Entries are internal, sealed and immutable, and nothing exposes the stack's contents. **They cannot be
  rebased** from outside the library, which removes the candidate that looked most promising.
- A change can be recorded inside a group with a caller's marker on it, and the stack reports the marker of
  the most recent group. Undo pops one group atomically and clears that marker. So "is the top of the stack
  the header refresh?" is answerable exactly when it matters: when nothing has been recorded since.
- Pushing any change clears the redo stack.

That settles the shape. The refresh is recorded in a group, so every offset stays valid. When the developer
undoes and the thing that came off is that refresh, the code window undoes the edit beneath it — whose
offsets are valid again, because the document is back in the state that edit left it — and re-applies the
current header. The designer's change is not undone, the developer's edit is, and nothing points at the
wrong text. **The cost is the redo stack**, which the re-application clears, so a code edit undone across a
designer change cannot be redone. That is a real and stateable loss, and it is smaller than either
alternative: clearing the history destroys the undo the developer still wants, and stopping undo at the
refresh blocks it.

**Correction, 2026-09-21, from implementing it: the marker is not how the question gets asked.** The third
bullet above is true and insufficient, and the gap is not subtle once seen. `LastGroupDescriptor` reports
the last group *opened*: an `Undo()` clears it to null even when marked groups remain below, an ordinary
change clears it too, it is already null inside `Changed` during the replay, and an empty group leaves it
set with nothing behind it. So it answers "is the top of the stack the IDE's own write" only when nothing
has happened since — and the ordinary sequence *type, designer change, type, undo, undo* is not that: by the
second undo the descriptor is null and the header write reads as an ordinary edit.

What asks the question instead is an `IUndoableOperation` pushed into the same group as the text change,
carrying the buffer's prefix. It restores the old prefix on undo and the new one on redo, and it reports
that the group it sat in was the one undone — at any depth, and whatever route the undo arrived by, which
matters because AvaloniaEdit answers **Ctrl+Y** itself and nothing in HexIDE's keymap binds it. The policy
above then runs as a loop: pop while each entry turns out to be a header write, stop at the first that is
not, and re-apply only if one came off. Everything in this paragraph was measured against the same
AvaloniaEdit 12.0.0 assembly the repo ships, by reflection and by probe.

**Marks are refused in a read-only region.** A header line never executes, and the folded header is a single
visual line, so a click on it would otherwise set a mark on whichever line it happened to map to. A form the
IDE cannot reproduce is not a read-only region, so its code lines still take marks.

**Edits by the IDE itself never prompt a reset.** Edit-and-Continue's prompt used to fire from keystrokes. A
header refresh or reload is not the developer editing.

**And a keystroke that writes nothing does not prompt either** (task 3.13). The prompt used to be raised from
the input handlers, which run before anything decides whether the key writes: the read-only section
provider refuses typing in the header after `TextEntering`, and the Enter handler a line break there after
the view's `KeyDown`. So input now only *arms* the prompt, for as long as that input is being handled, and
the prompt opens on the document's own `Changing` event, which fires only when an edit lands. A change
nobody armed for is the IDE's own and never asks. Paste and cut are the exception, because the clipboard is
asynchronous and their edit lands after the arm has gone: they ask the same section provider up front.
Delete, paste and cut never reached the old handler at all, because each key is bound to a code-window
command that takes it first; their commands now ask, which also covers the Edit menu, the context menu and
the toolbar.

## Folds and greying

**Folds the editor makes itself, merged into every fold application.** Today an empty server answer, or no
server, clears every fold, so the header fold would vanish with it. The header fold and each attribute fold
are added after the server's folds, and a server fold that partly overlaps one of them is dropped. A region
is folded when its fold is first created, and folded again whenever it is re-created, unless the developer
expanded that region in this editor. The reason not to rely on the fold library's "closed by default" flag:
it is honoured only on a fold manager's first update, and whole-document replacements recreate folds.

**Settled while building 3.14**, from the fold library's behaviour measured headlessly on AvaloniaEdit
12.0.0 (the measurements are in the task's notes):

- **A server fold that starts where one of the editor's own starts is dropped too**, whatever its length.
  The library pairs the folds it is given with the sections it holds by start offset alone, so two that
  start together can trade sections, and with them whether each is folded. Measured: a server fold from the
  top of the file, listed first, took the header's section and left the header's fold new and expanded. One
  identical to the header's is a duplicate, and the others nest awkwardly inside it at best.
- **The header's fold ends before the header's last line break**, so the first line of code stays a line of
  its own and the folded header reads as one line above it. **A one-line header is not folded**: a fold
  shows its first line in place of the lines it hides, so on one line it hides nothing. A `.bas` whose
  header is its `VB_Name` line is that case.
- **Whether the developer expanded the header is held by the code window's view model**, so a dock move,
  which re-materialises the view around it, brings the fold back as it was. The library raises nothing when
  a fold is toggled and unfolds every section it removes, so the state is read from the fold at the three
  points before it can be lost: before each fold application, when the document is about to change (a
  reload removes every fold, and a header rewrite covering the whole fold removes that one), and as the view
  detaches.
- **On every change the window compares the header's fold with where the header now is, and re-makes it at
  once when they differ**, rather than waiting for the debounced server request. Otherwise a reload shows
  the header expanded for half a second, or for good if the request fails, and a header that grows at its
  end shows the new text after the folded row: text inserted exactly at a fold's end is left outside it. The
  server's folds already shown are kept through that re-make, with their state.
- **Moving the caret into the folded header expands it**, because the editor unfolds any fold the caret moves
  inside, **and the window counts that as the developer expanding it.** A developer who navigates to a header
  line wants to see it, and folding it again under them on the next server answer would be worse (confirmed
  by the maintainer, 2026-10-02). So nothing the IDE does on its own may put the caret there. The one writer that did, found by review and then seen live,
  was a reload: it put the caret back at its old offset, which a longer header had moved into the header, and
  the header opened and stayed open. It now moves the caret with the header's change in length, as the
  reload already moves the marks.
- **The fold margin's click cannot be driven by automation**, like the other margins (#707). What it does was
  read in the library's source: it flips the section's folded state and nothing else, which is what the
  headless tests do in its place.

The lsp-client contract says folding comes through the language-client interface. These folds present the
file's structure and are not language intelligence. The contract is amended to say so explicitly.

**An attribute fold starts at the end of the line it describes.** For a procedure attribute that is the
procedure's own line. For a `VB_Var*` attribute it is the declaration the attribute follows. Folded, the
attributes disappear into a marker on that line, which is as close to VB6's hiding as visible text allows.
The fold adapter's line-start folds cannot express this, so these folds are built with character offsets.

**Greying is a named palette colour** with light and dark values, each meeting the contrast policy the dark
palette already records. It is applied after syntax colouring. Diagnostics inside a read-only region are
still shown, because they are a server's claim about the file. The bundled server is proved to raise none on
real headers (task 0.2).

**Settled while building 3.15:**

- **The colour is a theme key, `ReadOnlyText`**, declared in Classic.axaml as `ReadOnlyTextBrush` and carried
  by every shipped pack: `#707070` on Classic's white editor (4.95:1), `#929292` on Dark's `#252526` (4.92:1),
  `#7C7C7C` on Abyss's `#000C18` (4.72:1). Not `GrayText`, which is Win32's disabled-text colour and misses
  4.5:1 in all three packs. A pack nobody ships may omit the key, as the theme-packs capability allows, and
  then inherits Classic's grey, which is chosen for a white editor. `ShippedThemePackTests` holds each shipped
  pack to the background and text colours its own file declares, and to a floor of 1.5:1 from its text: the
  project's own figure for "reads as different", since no standard sets one.
- **Every read-only region is greyed, the members' attribute runs included.** They have been read-only
  since 3.7, and read-only lines in syntax colours would be the inconsistency. Task 4.2's greying half has
  therefore landed; what remains of it is verifying the runs once phase 4 folds them.
- **One line transformer, after the syntax colouring and before the diagnostics colouring.** AvaloniaEdit
  keeps its highlighting colorizer first, so the grey replaces the syntax colours, and an error's red text
  is drawn over the grey. The squiggles are drawn on another layer and are unaffected either way.
- **The fold labels take the same colour, all of them.** AvaloniaEdit draws every folded section's label with
  one process-wide brush, which defaulted to `#808080`: 3.95:1 on white and 3.88:1 on Dark. Every editor that
  folds sets it from the key while it is on screen (the code window, a carried file, and the Protocol
  Inspector's message body), so the procedure folds and a carried file's folds are now legible too. The
  inspector was missed at first, and review caught it: with the inspector the only editor showing, a theme
  switch left its labels in the previous theme's grey, worse than the old default.
- **An address inside a region is text, not a link.** AvaloniaEdit gives a link the text view's link colour
  while it formats the line, after every line transformer, so the grey alone left a URL in a `Caption` or a
  `VB_Description` blue and underlined, at 1.78:1 on Dark. A generator ahead of the link generators claims
  what they would have matched inside a region as plain text, which the grey then reaches. The address is no
  longer clickable there; VB6 showed none of this text at all. Outside a region a link is unchanged, and its
  colour is unthemed everywhere, which is #717 rather than this task.
- **The key itself is followed, not the theme variant.** A switch between Dark and Abyss changes no variant,
  so nothing the syntax palette listens to fires. Each editor watches the resource and repaints when it
  changes. Where the key does not resolve, nothing is greyed: no literal colour stands in for a theme.
- **The minimap is not greyed.** It colours text with its own highlighter rather than the editor's line
  transformers, and nothing requires it to.

**New strings** (fold labels, refusal reasons, the distinction between "the header is read-only" and "this
file is read-only") are localization keys, translated into every shipped pack in the same change. The
existing banner "Its code cannot be edited" becomes ambiguous and is reworded.

## Sidecar migration

A sidecar with `version` 1 (or none) holds `vb6://` keys and lines counted against the old code window.
`version` is written today but never read, so reading it starts here. Migration happens while the project
loads, once every document is loaded:

- **the shift is the prefix, not the header region.** It is the number of lines the composed buffer now puts
  *above what used to be its first line*: `OriginalHeader` for a `.bas` or `.cls`, and the designer part
  alone for a form, UserControl or PropertyPage, whose old buffer already began at the `Attribute` run. Using
  the header region instead would move every mark in a VB6-authored form five lines too far — the length of
  its attribute run — onto a different statement, which is precisely the failure the migration exists to
  prevent. Where nothing was split off at load, the shift is zero;
- a key naming a document of the project is re-keyed to that document's name and shifted. Bookmarks stay
  0-based and breakpoints 1-based: changing bases would also break the automation surface;
- **an entry whose shift cannot be measured is carried forward unchanged and marked unmigrated**, and
  migrated on a later load once it can be. That covers a name the project does not have and a document whose
  file is missing or unreadable — the second is the case that matters, because such a module is still a
  document of the project, so a rule written in terms of "no such document" would not catch it. Guessing an
  offset misplaces a mark; dropping the entry loses it;
- unrecognised top-level content is kept, as every save has kept it since #466 was fixed (#718);
- the file is rewritten as version 2 only if something changed, and never after a read that failed.

**Downgrade.** An older build cannot be fixed. Given a version-2 sidecar it ignores the new keys and, on its
next save, erases them — which is #466, the unrecognised-content defect, biting where the spec already says
it should not. That is the lesser harm: reusing the old keys with moved lines would instead have the old
build show every mark the prefix's length too low, silently. #466 is fixed (#718, merged 2026-10-05), so the
window is builds released before the fix, 0.2.0 and earlier, and the rest is recorded rather than mitigated.

**Two things the #466 fix leaves for this migration**, found while building it:

1. **Version-1 keys inside a version-2 file.** A build with the #466 fix but without this migration keeps a
   version-2 file's version and its new keys, and writes its own `bookmarks` and `breakpoints` beside them,
   under the version-2 label. Only builds counting lines from the first line the code window showed write
   those two keys. **So a reader treats `bookmarks` and `breakpoints` as version-1-counted whatever
   `version` says**, and migrates them as it would in a version-1 file.
2. **Absent documents versus renamed ones.** Carrying forward an entry for a document that is absent must
   tell one absent since the project opened from one renamed or removed this session. The obvious rule,
   "carry every entry naming no document of the project", fails three ways:
   - a renamed document's old-name entry stays in the file beside the new one;
   - a mark removed while it was renamed comes back when the name does;
   - a later document of the old name inherits marks nobody set in it.

   **Keep the names the project had when it opened, and carry only entries naming none of them.** This is
   why #466 itself carries no such entries: a first attempt at the obvious rule was refuted in review.

## The shipped surfaces

**Automation.** Tools that name a document take and return `project` and `document`. The `uri` field, where
there is one, is the wire name and is documented as such. Lines are file lines. Resolution covers every
loaded project, not only the startup one. The existing defect of minting a key from the caller's spelling
(#467) disappears, because a name now resolves to a document before anything is keyed. `get_file_content` returns
the whole file, whether the editor is open or not, and `set_file_content` accepts what `get_file_content`
returns. Each change is recorded in `docs/mcp-server-gaps.md`.

**Add-ins.** A document's content is the whole file, and positions are file lines. `SetContent` takes the
whole file with its header unchanged, or a body alone with the header kept, exactly as automation does: the
shipped chat add-in applies a model's fenced block, which is a body, so a rule that refused anything but the
whole file would break it. Naming needs work too: the editor surface names a document by its own name, but
the file-opened and file-closed events carry the *dock title* as both the name and the path, and lookups
resolve in the startup project only. Both are read through the identity, and a document is nameable by its
project as well as its name — as an overload beside each existing member, not a trailing optional argument
(see "The add-in surface takes overloads" under phase 1, which superseded the argument this paragraph first
proposed), so existing add-ins keep compiling and keep running.

**Export redaction.** Today `untitled:` would pass through unredacted, carrying the project and document
names. Its path segments are pseudonymised like a `file:` URI's (an extension of #397). The rationale in the
redactor, that VB6 documents ride an opaque scheme and are therefore path-free, is rewritten: saved modules
now carry paths and are pseudonymised as paths. The disclosure's per-document grouping keys on the full name,
not the last segment, so two `Module1`s no longer merge.

## Measurements this depends on (phase 0)

1. **What the editor shows today: done, 2026-09-20.** A form's buffer does begin at its attribute run —
   `get_file_content` and a snapshot of the code window agree, on an untouched `frmBillOfFare` in a freshly
   launched IDE. So for a form kind the composed prefix is the designer part and the sidecar shift is the
   designer part alone; counting the attribute run into either would double it. The three comments saying
   the block is invisible are corrected.
2. **Both grammars parse whole real files: done, 2026-09-20 — after a fix.** They did not. Thirty of
   forty-six corpus files failed inside the header, in both grammars, on one cause: the whitespace before
   the comment VB6 writes when it decodes an enumerated property value (`MultiUse = -1  'True`). A `WS?` in
   two rules per grammar fixed it, and `WholeFileGrammarTests` in each half now reports 0 blocking over 48
   files, including the `.ctl` and `.pag` added as `corpus/designer/`. The composed buffer parses, so 3.6
   stands as designed.
3. **Oracle: done, 2026-09-19**, recorded in `docs/vb6-fidelity-oracle.md`. The compile-error log reads
   `Compile Error in File '<absolute path>', Line <N> : <message>`, which the current expression cannot
   match; `N` is a 0-based index into the code view, attribute lines excluded, procedure-level ones
   included; two projects in a group may not share a name; a form and a module in one project may not
   share a name.
4. **Foreign servers and `untitled:`.** For each server, a test asserting that diagnostics arrive under the
   exact name sent. The server that refuses non-`file:` URIs is asserted to refuse, on the wire or on stderr,
   because "did not throw" proves nothing. Which half of that assertion applies is decided by what the server
   declared at initialization, not by a list written here: a pull server answers no publication at all, so a
   test that waited for one would pass vacuously, and at least one server's delivery mode flips depending on
   whether the client accepts dynamic registration — which HexIDE refuses and the seeding probe accepted.
5. **AvaloniaEdit behaviours: done, 2026-09-19**, read out of the 12.0.0 assembly rather than inferred:
   - the stock read-only provider allows insertion **at** a region's first and last offset, and only refuses
     strictly inside, so typing at the top of the file would go in front of the header. It carves read-only
     text out of a deletion, which is what would strand a member's attribute lines when its procedure is
     deleted. Both methods are `virtual`, so one subclass fixes both;
   - binding the editor's read-only property **replaces the whole provider**, so the composite provider and
     that binding cannot both be used; the composite owns it and consults the document's own verdict;
   - a fold's "closed by default" flag is honoured **only on a manager's first update, and only for a fold
     created in it**, so the folds this change adds set their folded state themselves. `UpdateFoldings`
     throws unless the whole merged list is sorted by start offset, and silently skips a zero-length fold;
   - the undo mechanics above.

## Settled while building phase 1

Five decisions the design above left implicit, each recorded with what it was weighed against.

**The sidecar's `version` records the line base, not the key shape.** Phase 1 changes how entries are keyed
and phase 3 changes how lines are counted, and only one of those needs a version step. A VB6 name is a
letter followed by letters, digits and underscores, so it can never look like a URI: `vb6://form/Form1` and
`Form1` are told apart by the key itself, and the reader accepts both. Spending a version on the re-key
would have meant two migrations for phase 3's author to branch on, and a `version: 1` file with either key
shape is unambiguous without one. What `version` therefore means, unchanged, is *lines counted from the
first line the code window showed*.

**A `Thing` form and a `Thing` module in one project union their sidecar lines rather than overwriting.**
Phase 1 stops such a project being *created*, but one loaded from a `.vbp` written elsewhere may already
hold both, and the key within a project's own file is the bare name. Writing the second over the first
would silently destroy the on-disk record on the next debounced save, which is the harm the migration
exists to prevent.

**Which project is running is a value, not an event.** The editor needs it to decide whether a pause
reported by bare module name is in *its* document, and a group's two `Module1`s make the name alone
insufficient. An event would not do: the editor that most needs this is the one opened **by** the break, so
it did not exist when the event fired. It is a service of its own (`IRunScope`) rather than a property on
the runner because an editor cannot depend on the runner — the dependency closes a cycle through the editor
factory, which is why the reset prompt is already routed through the bus.

**A form records the module it is the designer half of.** `DocumentIdentity.For(FormDefinition)` resolves a
`.ctl` or `.pag` to its module, and finding that by scanning the project's modules is wrong in the window
between the two halves being joined and the module being added to the project — several statements, in both
the creation path and the load path. A scan answers "a plain form" there, and that identity compares
unequal to every one produced afterwards. The link is recorded on the form by `UpdateFormPart`, which is the
one place the two are joined.

**The add-in surface takes overloads, not trailing optional arguments.** The task list asked for optional
arguments "so existing add-ins keep compiling", citing `AddinDiagnostic`'s `Code` and `Source`. That
precedent states its own precondition — the IDE builds that record and add-ins read it — and the
precondition does not hold for an interface method an add-in *calls*: add-ins are loaded as pre-built
assemblies through `Assembly.Load`, and C# bakes a default argument into the call site, so a default
parameter keeps an add-in compiling and breaks every one already packaged. Overloads satisfy both. The
records still take trailing optional fields, where the precedent does hold.

**A module cannot be renamed at all, so the rename half of 1.7 covers forms and projects.**
`ModuleDefinition.Name` has a public setter and no assignment outside its constructor; the Properties window
binds only a form designer. The collision rule lives in `ProjectNaming` and applies wherever a module rename
is eventually built; building it is filed as hexide-io/HexIDE#493. The delta's rename scenarios are
therefore proved at the store and in the editor by setting the model property, which is the only gesture
that exists.

## Settled while building phase 3

Decisions the design above left open, made before tasks 3.17 to 3.22, each with what it was weighed against.

**Version 2 writes its marks under new keys, `fileBookmarks` and `fileBreakpoints`.** They have the shape
the old two have, a document name mapped to its lines, with the same bases: bookmarks from 0 and breakpoints
from 1. The difference is that lines count from the top of the file. `bookmarks` and `breakpoints` keep their
version-1 meaning in every file, whatever `version` says, as the #466 note above requires. This build never
fills them from its own marks.

**The version-1 keys are content this build keeps but does not write, as #466 keeps a newer build's keys.**
On each save they are carried as the file holds them at that moment, never from memory. That is what makes
the live scenario "A sidecar deleted while the project is open" hold: an in-memory copy of the unmigrated
entries would bring them back after the developer deleted the file. It also keeps whatever a #466-fixed older
build writes under those keys while this one has the project open, for the next load to migrate. On load, the
migration takes every entry it can measure out of the version-1 keys, into the marks, shifted. When it moved
or re-keyed anything, it rewrites the file once, leaving only what it could not measure under the old keys.

**What cannot be measured:**
- a key naming no document the project had when it opened;
- a document whose file is missing when the project loads. A file that cannot be read never becomes a
  document, so it is the first case.

A document with no file yet is measurable: its shift is the header the code window shows above its code.
A carried entry stays carried for the rest of the session, even if its document is added or removed meanwhile;
the next load decides again. A mark set this session in a document whose old entry is still carried is
written under the new keys. The next load that can measure the old entry migrates it and joins the two sets.

**The shift is the number of line breaks in `FormCodeText.Prefix`**, one counter shared by the migration and
`HeaderRefresher`. A version-1 line is checked against the old numbering, the code section's length, before it
is shifted, so a line past the old end is dropped as it always was.

**A mark that lands on a read-only line when the sidecar is read is dropped and logged.** Since 3.12 nothing
can set one there, so only a migrated mark, or one written by a build without 3.12, can arrive on such a line.
The old window let a form's leading `Attribute` lines and a member's `Attribute` lines take a mark. A
breakpoint there never fires, and a bookmark there marks a line the IDE writes. The bookmarks delta already
says none lands in the header.

**This branch's own version-1 sidecars are not covered.** From 3.2 until 3.17, builds of this branch wrote
file-counted lines under the version-1 label. Migration moves those marks a second time. Only development
machines that ran this branch have such files; nothing that shipped wrote one. Deleting the sidecar is the
remedy.

**Settled while building 3.17**, where the paragraphs above left a choice:

- **A missing file is found by asking the file system, once, as the project opens.** A document whose path names
  no file is unmeasurable; one with no path yet is measurable. `OriginalHeader is null` was the other candidate
  and was rejected: a `.ctl` never records a header, so it says nothing about a UserControl. Where two documents
  share a name, as a project written elsewhere can, the name is carried if either is unmeasurable, because one
  entry cannot be half applied.
- **The new keys are carried the same way as the old.** An entry under `fileBookmarks` or `fileBreakpoints` for a
  name the session does not own is not placed: a module whose file is missing is loaded empty, so a range check
  against it would keep its low lines and drop the rest. It is taken from the file at each save, as the old keys
  are, and joined with any marks the stores hold under that name.
- **Less what this session wrote there.** "Names the project had when it opened" is not quite enough, because a
  name outside it can still be one this session writes — a document renamed to it — and the next save would then
  carry that entry from the file as though someone else had written it. Renamed twice, a document would leave its
  middle name's marks behind; a mark removed under that name would come back. So each save remembers what the
  stores put under the new keys, and the next one carries only what the file holds beyond that. A line in both is
  taken as this session's, since it is the side that can say the line is gone. Memory is used to subtract, never
  to supply, so a deleted sidecar still stays deleted.
- **"Changed" means an old-key entry was consumed**: one naming a document the session owns, which leaves the old
  keys whatever becomes of its lines. That alone triggers the write at load, through the owed write `LoadAsync`
  already makes for a change held back during the read, and it does so even when every line of the entry was
  dropped, for its range or for a read-only line. Left in the old keys, such an entry would be dropped and logged
  at every opening and never leave the file, where the live requirement says the next save does not write it.
  A mark dropped under the new keys is not a migration and writes nothing by itself; the next save leaves it out.
- **Until that write lands, every save subtracts what the load placed from the old keys.** Every save takes the
  old keys from the file as it is, and the file holds the placed entries until a write lands — including the
  migration's own write, which reads the file it is about to replace, and any save after a migration write that
  failed because another program held the file. Without the subtraction they would be written back beside the
  marks they became. An entry is subtracted only while it is exactly what was placed, so an older build's later
  write under the same key is still kept for the next opening.
- **The old numbering is checked over the code section, before the move**, counting `'\n'` plus one as `main`
  did. No second range check follows it: the file is the prefix's lines plus the code's, so a line inside the old
  range is inside the new one.
- **The read-only drop applies to the new keys too.** No build writes a mark there under them, but the file can
  change between sessions — VB6 adds a member's `Attribute` line below a declaration whose next line was marked —
  and a sidecar can be edited by hand. The rule is about the line, whoever wrote the mark.
- **A null where an entry's lines belong fails the read**, as it did before, for either format, before anything
  reaches the stores.

**Not closed by 3.17: the conflicted external change parked under 3.3b.** An external change the dirty detector
calls a Conflict is not applied, so its header delta never reaches the stores and the sidecar records the marks
in the numbering of the file as it was. Version 2 records no header length, so a later opening has nothing to
measure the difference against. Recording each document's prefix line count beside its marks would let it; that
is a format change of its own and is not made here. It is #884.

**Nor 3.4's header that appears at open**, which the open question of where a created form's first header comes
from said could not be answered apart from 3.17. It was not answered by it. Version 2 counts a document's lines
from the top of the file as the code window showed it when the marks were written, and a created form shows no
header until its first designer commit, save or reload, so a sidecar cannot say whether a document's marks
already count a header that a fourth trigger would then add. The prefix line count above would answer this too.
It is #885, beside the conflicted change (#884).

**`get_file_content` returns the whole file, and `hasUnsavedChanges` still compares the code section.**
Comparing the whole buffer against `Code` would mark every open document unsaved, which is the trap 3.2a
fixed in the dirty check. `set_file_content` takes what `get_file_content` returns, or the code alone, through
the same `GuardedContent` rule as an add-in's `SetContent`. Its separate refusal of a whole `.frm` (#338) goes,
in the same commit as the read: changing one without the other would refuse exactly what the read returned.

**`set_file_content`'s reply states the range the document's lines are numbered in, read after the save.**
It used to count the code it had just written. It now gives `1..N` from the top of the file, counted as the
code window counts, which is also the range `set_breakpoints` accepts and states when it refuses, so the two
replies cannot disagree about where a document ends. It is read after the save, not after the replacement,
because a form's save re-renders its designer block and adopts that as the header: a form with no header yet
gains one there, and one VB6 wrote can change height. Counted before the save, the number would disagree with
the next `get_file_content` in exactly the case a caller cannot foresee. The path in the reply is read then for
the same reason, since a first save through an armed file dialog is where a document gets one.

**The read and write rule lives in `HexIDE`, as `DocumentContent`, not in the tools.** Tool bodies compile into
an executable no test references, which is why `MarkLineRules` sits there too. The name lookup has since followed
it into `NamedDocuments` (below), so the tools keep which window is open on the document, the picker refusal, the
save and the reply. `set_breakpoints`' range check reads the same `DocumentContent.TextOf` that the reply above
counts, so the two agree by construction rather than by two copies of one expression; `ToolDocumentNamingTests`
fails if a tool reads the code window's text any other way. `FormCodeText.LooksLikeFormFile` lost its only
production caller with #338's refusal and went with its tests. `FormCodeText.PreserveAttributes` already had
none before this task, and is left for 5.7, which owns `FormCodeText`'s comments.

**A whole replacement checks the header, and an edit to a range checks every read-only region.**
`GuardedContent.Replace` compares the incoming header with the document's and nothing else. A member's
`Attribute` lines sit in the code it replaces, and phase 4 decides how they follow the line they describe.
`ApplyEdits` and every other range write refuse any read-only region. The addin-system delta now says this
in those terms, which design.md's row for `SetContent` already did. **This stands for phase 3 only.** The
code-editor delta's requirement "Only the IDE SHALL change a read-only region" counts a member's attribute run
as one and names add-in and automation commands among the edits that may not change it, so until phase 4 a
whole replacement (add-in `TrySetContent` and `SetContent`, automation `set_file_content`) that rewrites or
drops a member's run is applied, and that requirement is not met for it. Task 4.5b owns closing it, once 4.1
can say which run belongs to which line. The add-in contract's doc comments say so, rather than implying a
refusal (3.19 review).

**The read-only banner gets a new key rather than new words under the old one.** Shipped-pack parity checks
only that a key is present, so rewording under the same key would leave every translation saying the old
thing with nothing failing. The key is `Str.CodeEditor.FileNotSaved.Banner` (3.20), and its words follow two
rules. They are about the whole file, and do not say "read-only", so they cannot be taken for the header's grey
lines. And they claim only what is enforced today: the file is not saved, and typing in the window is refused.
That second claim holds for every key, not only the ones AvaloniaEdit's own typing takes: the 3.20 review found
Enter and a completion's commit wrote the document themselves past the section provider, as #701 had listed, so
both now ask `CodeEditorViewModel.RefusesTyping`, and automation's `type_text` and `press_key` refuse such a file
with the reason. They do not say the code cannot be changed, because until #701 lands Replace All, formatting,
rename, Insert File, undo and redo, add-ins and automation's whole replacement still change it. "This form" became "this file", since a UserControl or PropertyPage shows the
same banner. When #701 makes every writer refuse, the banner may say more, and the new words take a new key
again. **A fold's label is the fold's first line**, as the code-editor delta says, so the
header fold has no string of its own. **Find and Replace explaining what they left in the header is #705**,
outside this change: 3.22 checks that Replace All leaves the header alone, not that it says so.

**Automation's naming half: the parameters keep their names, and the replies gain `project` and `document`.**
"The shipped surfaces" says tools *take* and return `project` and `document`. They take `project`, and keep
the name parameter they had: `name`, `formName` or `module`. Renaming a parameter would break every caller that
passes it, to gain a spelling, while the reply is where a caller needs the normalised name. The reply of every
tool that takes a document's name carries both, as optional trailing fields, whenever the name found a document,
refusals included. The MCP SDK's serializer omits nulls (its options say `WhenWritingNull`, and the replies
recorded in the gaps archive show it), so the tools that name no document reply exactly as before. A carried
file is named the same way, by its project and its own name.
A `get_diagnostics` item gets its names by resolving its `uri` back to a loaded document through the wire name,
as `EditorService` resolves a server's reply. When the `uri` names nothing loaded the fields are absent, not
guessed, and so they are when it names a file two loaded projects share: two documents with one file have one
wire name, a diagnostic for it cannot say which project's copy it describes, and naming the first project to
list the file would be the guess `Resolve` refuses to make for a name. `get_debug_state` and `get_call_stack`
keep `module`, a bare name, and gain the running `project` beside it, since a run is one project. `add_file`
takes `project` to say where to add. That is a capability rather than a rename, and without it a group's other
projects could not be added to at all.

**Not done: the tools that name a document without taking its name.** The delta's requirement covers a tool
that *reports* a form, module or class as well as one that accepts its name, and these still report or act on
one without saying which project's it is:
- the note of every run-control reply, `Paused at Form1 line 9 (Step).`, from `run_project`, `break_program`,
  `continue_program`, `step_into`, `step_over`, `step_out` and `run_to_cursor`;
- `get_locals`' `context` and the watch tools' `context`, both `Module.Procedure`;
- `get_undo_state`, `invoke_designer_undo`, `invoke_designer_redo` and `invoke_format_command`, which act on the
  active designer and do not name its form;
- `activate_document_tab` and `close_document_tab`, which take a tab's title, and `get_open_editors` and
  `get_document_tabs`, which report titles.

With a group holding two `Form1`s, none of these can say which one it means. They were left because the work
is different in kind from what was done. The tools that take a name had a lookup to route through and a
document in hand to name. These have no name to resolve: the run-control notes and the debugger contexts need
the running project put beside a bare module name, as `get_debug_state` now has it; the designer tools need to
name the document behind the active tab; and the tab tools need a decision about what identifies a tab to a
caller, since a title is the IDE's display string and two `Form1` tabs can share one. Filed as
#886. Until it is closed the hexide-mcp-server requirement is only partly met, and task 5.8 notes that
under the spec's Purpose when it archives, rather than recording the requirement as satisfied.

**The resolution rule lives in `HexIDE`, as `NamedDocuments`, for the reason `DocumentContent` does.** The tools
keep only what needs the running IDE: which designer or editor is open on the resolved document, the save and
the reply. `move_control` and `add_control` now resolve the name to a document first and then find the designer
open on it by reference. So a module is refused as having no designer rather than as "not open", and a name two
projects share is refused rather than answered with whichever designer was found first. Whether the tools keep
to this is checked by reading their source, since nothing can call them: `ToolDocumentNamingTests` fails when a
tool that takes `project` does not resolve through the lookup, when it uses the caller's name for anything but
the lookup, when a `MutateResult` after the lookup does not go through `Naming`, or when `Naming` changes.

**An add-in is told why through three new members, `TrySetContent`, `TryApplyEdits` and `TryNavigateTo`, each
answering an `AddinEditResult(bool Succeeded, string? Reason = null, bool HeaderKept = false)`** (task 3.19). They
take the same arguments as the project-qualified members beside them; only the answer differs. That is why they are
not overloads of those members in the strict sense: C# cannot overload on the return type alone, and the v0.2.0
`Task<bool>` returns are frozen because add-ins are loaded built. "Overloads" in phase 1's decision means an
additional member rather than a default parameter, and these keep to it. Considered and rejected: a same-named
overload told apart by a new parameter type, such as a record naming the document. It is a true overload, but it
gives an add-in two ways to make one call that answer differently, chosen by the shape of the first argument. The
`Try` prefix puts each beside the member it supersedes in a completion list, and the interface remarks say they are
the ones to call. Each member that answers true or false is now its `Try` counterpart with the reason dropped, so
the two cannot disagree about what is refused.

**They are default interface members, whose bodies call the members a host already had.** A host or test double
written against v0.2.0 compiles and answers them unchanged; its refusal reads `AddinEditResult.WithoutReason`,
which says no reason was given rather than inventing one. HexIDE's own host overrides all three. NSubstitute 5.3
intercepts them like any other member, measured by the AI Chat tests, which configure them on a substitute.

**The reasons are the ones automation gives, and are English.** A name resolves through `NamedDocuments`, so an
add-in hears what an automation caller hears: no project of that name, no document of that name with what the
projects hold, or a name two projects share with both listed. A replacement's refusal is `GuardedContent`'s. An
edit's refusal names the edit by its line and column from the top of the file, where it used to name a buffer
offset no add-in had given. They are English for the reason automation's are: an add-in may pass one to a model
or a log as readily as show it, and the IDE never shows one itself, so task 3.20 does not apply to them.
`HeaderKept` comes from `GuardedContent` too, and is true when what was sent lacked some or all of the header.

**A position the document does not have is refused, not thrown, and no longer ends the process.** `NavigateTo`
moved the caret inside a post to the UI thread, and `GetLineByNumber` threw there for a line outside the document,
which ends the process (DebugCrashHook's measurement). The AI Chat add-in passed a model's line straight through.
`TryNavigateTo` waits on the UI thread and refuses such a line with the range the document has; `NavigateTo` keeps
answering as soon as the name resolves, as it always has, and moves nothing. `ApplyEdits` threw the same way into
its caller, and now refuses the whole set instead, as it refuses an edit to a read-only region. A column below 1
is refused too; a column past a line's end still means the line's end.

**The AI Chat add-in takes a `project` argument on `get_file_content`, `get_diagnostics`, `apply_edit` and
`navigate_to`, and names a file's project in `get_active_file` and every `list_files` entry**, which now lists
every loaded project rather than the startup project's alone. Otherwise the refusal for a name two projects share
would tell the model to pass something it had nowhere to put. `apply_edit` and the Apply button report the IDE's
answer, success or the reason, where both used to report nothing or success. The add-in's strings stay English:
it has no localization at all, the tool texts and the notices included, and it reaches the IDE only through the
add-in surface, which offers none.

**The AI Chat privacy notice says "the active file's contents (for a form, its layout and captions too)", and is
shown again once to a developer who acknowledged the old one.** It said "the active file's code", which stopped
being what was sent when a form's designer block joined it. The acknowledgement file now records which notice was
shown, and anything else counts as unacknowledged, because more now leaves the machine than that developer was
told. The ai-chat delta added here holds the add-in to reporting a refused change; the egress requirement already
says "contents" and needs no change.

**Not done here, and why.** The AI Chat `get_file_content` tool reads a named file by navigating to it and then
reading the active document. It read the wrong document when the file was open in a background tab, because the
host's open did not activate an editor that was already open, and it could read before the posted open ran.
Fixing it needs a member that reads a document by name, which the add-in surface lacks; adding one is its own
decision. Filed as #887. Its navigation to line 1 also puts the caret in the folded
header, which "Folds and greying" says expands it; that is unmeasured, and is 3.22's to look at.
`IDiagnosticsAccess.GetFor` answered empty for a name two projects share rather than saying so, and was the one
member that named a document and could not tell an add-in why, so the addin-system scenario "Two projects with a
document of the same name" was unmet for it. Being a scenario of this change's own delta, 3.21 took it up rather
than filing it, and closed it with `TryGetFor` (below).

**An add-in asking for one document's diagnostics is told why the name found none, through
`IDiagnosticsAccess.TryGetFor(fileName, project)`, answering an `AddinDiagnosticsResult(IReadOnlyList<AddinDiagnostic>
Diagnostics, string? Reason = null)`** (task 3.21). `GetFor`'s list return is frozen, as `SetContent`'s `Task<bool>`
is, so the reason comes from a new member beside it, and `GetFor(fileName, project)` is now `TryGetFor` with the
reason dropped. The reasons are `NamedDocuments.Resolve`'s, as the editor members' are. The record has no
`Succeeded` flag, unlike `AddinEditResult`: a refusal is a reason (`Refused` reads it), and a document with no
diagnostics is an empty list with none, which is the one distinction the list alone could not draw.
**The default body answers `GetFor`'s list with no reason, and never refuses.** A host that implements only `GetFor`
cannot tell an ambiguous name from a clean file, so neither can the body, and inventing a refusal for an empty list
would report a clean file as a name that found nothing. `AddinEditResult.WithoutReason` is not the precedent here:
it maps an answer of false, which those older members did give, whereas `GetFor` never said it was refused at all.
Only HexIDE's own host, and any that implements the member, refuses; the interface remarks say so.

**Settled in the 3.19 review.** Each of these closes a way the add-in or AI Chat answered something other than
what happened.
- *Both halves of the ai-chat requirement, on both paths.* A refused `apply_edit` reached the model and not the
  transcript, which showed only "Using tool"; a refused Apply reached the transcript and not the model. The
  transcript now shows what every write tool came to (an `apply_edit` applied or refused, any other write that
  failed), and the Apply button's outcome, accepted or refused, goes to the model as a note. A note is a `user`
  message flagged as from the IDE: a `tool` message needs a call to answer, and a `system` message mid-conversation
  is refused by some endpoints. It is held while the model is responding and put in only where a user message may
  come, because the Apply button stays live between a tool call and its result, where anything but the result makes
  the endpoint refuse the request and leaves the saved history unusable. The flag keeps the note out of the
  developer's own bubbles when the transcript is rebuilt.
- *Navigation brings the window forward.* `TryNavigateTo` and `NavigateTo` activate an editor that was already open
  behind another, and `TryNavigateTo` refuses, saying the caret did move, when the window cannot come forward. A
  write does not come forward, so an add-in changing a background document does not take the developer's view.
  AI Chat's `get_file_content` now waits on `TryNavigateTo` and refuses when the active document is not the one
  asked for, which keeps the wrong file from being answered while the read-by-name member above is outstanding.
- *AI Chat's `get_diagnostics` resolves the name first*, as `get_file_content` does, and refuses a name no project
  has or two share, which the host answered with an empty list that a model reads as a clean file.
- *One spelling for every AI Chat reply field*, camelCase through a serializer policy rather than hand-renamed
  members. `get_active_file` said `Project` and `list_files` said `project`.
- *`apply_edit` with missing or blank content is refused.* The IDE takes such text as code alone, keeps the header
  and empties the code, and the reply said success.
- *An `ApplyEdits` refusal names the edit as the add-in sent it*: its place in the set and the positions given,
  where it named the buffer position, which differs for a column past a line's end.

**Settled while building 3.21.** What "a test per scenario" and two of the task's own cases came to.
- *A restated scenario counts where this change narrowed what it claims.* The bookmarks delta removes a requirement
  and adds it back narrowed, and the interpreter-debugger delta modifies one to the same effect, so their
  carried-over scenarios ("Toggling a bookmark", "Clearing every breakpoint in the project") are scenarios of this
  change and get tests by their names, like the new ones. The lsp-client delta modifies its first requirement too,
  but 3.14 only added to it: the paragraph on folds the editor makes itself, and "No server is attached", which has
  its test. Its two older scenarios, "Adding a feature that needs language data" and "Replacing the backend",
  say what they said before this change, and are claims about how the code is built rather than behaviour a test
  drives, so they got no tests by their names here.
- *A test already proving a scenario was renamed to it, unless something outside the test file cites its name.*
  `MarksOnLinesTheDocumentDoesNotHaveAreDroppedAtLoad_AndNotWrittenBack` is cited by the archived change that
  introduced the sidecar's range drop, and an archive is a record rather than a document to keep in step, so the
  two sidecar scenarios got tests of their own beside it. Where a scenario-named test existed and left part of
  its THEN unasserted (a standard module's header "cannot be edited", a form's header "folded into one line"), the
  missing clause went into that test rather than into a sibling.
- *"An unchanged save writes the buffer" is asserted only on a form the serializer reproduces exactly*, because a
  form is written by rendering its model, not by copying what was read, so for any other form the claim is false
  and a test of it would enshrine the wrong invariant. The fixtures are a form the serializer wrote itself, which
  is its own fixed point, and `BillOfFare.frm`, which it reproduces today; each test checks that premise before
  relying on it. For a form it does not reproduce, the requirement is the other half, that the window follows the
  file the save wrote, and that is the scenario's own test.
- *The language server's "Opening a form" has no single point of failure in today's code.* A header VB6 wrote
  raises nothing in the grammar (task 0.2), and the server withholds anything on a protected line (task 3.10), so
  the test pins the two together, on a header carrying the enumeration comment that once failed both grammars.
  Measured: with 0.2's `WS?` taken out of `cp_SingleProperty` it stays green, with the listener given no protected
  regions it stays green, and with both it fails on that comment's line. Each layer has its own tests.

## Settled before building phase 4

Decided on 2026-10-13 from a survey of every phase-4 task against the code at `ce02d65`, and **revised the same
day** on the oracle's measurements: 165 headless `/make` builds read back through their type libraries, and two
sessions in the VB6 IDE itself that wrote attributes through Tools > Procedure Attributes, loaded hand-written
shapes and saved them, and edited described members in the code window: renames, kind changes, deletions and
pastes. The first session's probes are named by letter (IDE probe C8), the second's by number (IDE probe V4). All
are recorded in
`docs/vb6-fidelity-oracle.md` under "Member attributes". The paragraphs below are the revised rules; where the
first draft was overturned, it says so.

**What a run is, and what it describes (4.1).** A member run is a maximal block of consecutive logical lines below
the header whose first word is `Attribute`, in any case and at any indent, each with the physical lines it
continues into with ` _`. A physical line that continues the line above it is never an attribute line: it is part
of that line's statement, or of its comment (p07, p07c), and `Attribute` is a reserved word in code (p07e). Each
line's *qualifier* is its name up to the first `.`; a dotless line has none. The run's *described line* is the
logical line directly above it: the physical line above, and the lines before that it continues with ` _`, read
back to the first.

VB6 attaches the two kinds of member attribute in opposite ways, and nothing in the first draft anticipated it:

- **A procedure attribute** (any dotted name not starting `VB_Var`) goes to the procedure declared on the line
  directly above it, **whatever its qualifier names** (p04, p05a, p05b, p12). Away from that line, under a blank
  line, a comment, a statement or `End Sub`, it reaches nothing, even naming a real procedure (p01 to p03, p05e).
- **A variable attribute** (`VB_Var…`) goes to the module-level variable its qualifier names, **wherever** it sits
  after that declaration (p09e, p09g, p09h, p09j), and to nothing when it comes first or names nothing (p09l, p09f).
- **A dotless line below code** is a module attribute (p06a to p06c).

So a run **describes a member**, and is a read-only region, exactly when it sits directly under a declaration and
VB6 reads every line of it as that declaration's:

- under a procedure's declaration (`Sub`, `Function`, `Property Get|Let|Set`, `Event` or `Declare`, with any
  modifiers), when every line is a dotted procedure attribute. The run describes that procedure, and its
  qualifiers are not consulted;
- under a module-level variable or constant declaration, when every line is a `VB_Var…` line and either each line
  names a name that line declares, compared without case, or the line declares exactly one name and every line
  names one and the same name that nothing in the module declares, which is the run a rename by retyping leaves
  (4.4, below). Every statement on the line counts, `:`-separated ones included, because VB6's own dialog writes a
  run under `Public A As Long: Public B As Long` for `B` (IDE probe E).

A constant takes a `VB_Var…` run exactly as a variable does: by name, public or private, typed or not, and the
second constant of `Const C1 = 1, C2 = 2` too. VB6 keeps it (five shapes), moves it under its `Const` from
elsewhere (one case), and shows it in the Object Browser as the constant's description (the one constant looked at
there), though neither the dialog nor the Object Browser offers a way to make one (IDE probe V5). All of that is
for a declaration VB6 accepts; the one it rejects is below. `Enum` and `Type` lines and enum members take no run of either kind, and a
`VB_Description` line under a `Const` reaches nothing (V5).

Declarations are read lexically, the same kind of read as `DeclaredNames`. This is collection, not binding: it
compares a qualifier with the names written on the adjacent line, and, for a one-name line, with the set of names
the module declares, read the same way from every line of it. That second comparison is what keeps a misplaced
line safe. VB6 binds a `VB_Var…` line by name wherever it sits, so `Attribute Other.VB_VarDescription` under
`Public A As Long` is `Other`'s when `Other` is declared elsewhere, and reading it as `A`'s would hand `Other`'s
description to `A` at the next save. The set errs towards too many names, a procedure's own `Dim` included, which
errs towards an orphan and never towards re-qualifying a line that names something else. The first draft's rule,
that every qualifier must name the member declared above, orphaned a procedure run VB6 honours whenever its
qualifier differed, which a rename leaves behind (below).

A run's **key**, wherever state has to outlive its offsets (fold expansion, matching runs between two texts), is
the name of the member it describes, as its declaration spells it, without case, plus its ordinal among runs
with that key. A variable run whose lines name several declarators of one line is keyed by the first it names;
a one-name run is keyed by the name its line declares, not the stale one its lines carry. The ordinal is there
because a property's Get and Let may each carry
one and both survive a save (IDE probes A and B2). The data travels beside `TextRegion` as a separate record rather
than widening it, because `TextRegion`'s positional equality is pinned by tests and a list field in a record
struct compares by reference.

*Settled while building 4.1.* `ReadOnlyRegions.MemberRuns` reports every run, orphans included, as a `MemberRun`
from the same scan as `Of`, which keeps only the runs that describe a member. It was first built to the first
draft, and rebuilt to the rules above once the oracle overturned that. What the paragraphs above left open:
- *The ordinal counts only runs that describe a member*, and an orphan has no key. An orphan is free text, so
  adding, repairing or removing one must not renumber the runs of a member that still has its declaration, which
  is what 4.3's remembered expansion and 4.5b's comparison would otherwise key wrongly.
- *A continuation is VB6's own rule*, an underscore after whitespace at the end of the line, trailing whitespace
  aside. Not `VbAutoCloseProvider.IsContinuationLine`, which also takes an identifier ending in `_` for one; 4.5,
  which reads the same lines for Enter, should use this rule or say why not. The whitespace is required at the
  start of a line too: a line of nothing but an underscore continues only when it is indented, as the oracle
  file already recorded, so one in column 1 continues nothing (the first build took it for a continuation).
- *Only the first statement on the described line is read*: a `:` ends it, as a comment does. `Public A As Long:
  Public B As Long` declares `A` alone here. **Overturned by the oracle**: VB6's dialog writes `B`'s run under
  that very line (IDE probe E), so every statement on the line is read. A comment still ends the line, its `:`
  included, and so does `Rem` where any statement starts; a string is one token, so a `:`, `'` or `,` in a
  constant's value is none of those.
- *VBA7's `Declare PtrSafe` is read as `Declare`*, for the VBA personalities. `Const`, `Enum` and `Type` declare
  nothing a run can describe. **Overturned for `Const` by the oracle** (IDE probe V5): a constant takes a variable
  run. `Enum` and `Type` take none.
- *A blank line inside one member's run leaves the half after it an orphan*, because a blank line ends a run.
  **Measured since**: VB6 reaches nothing with that half either, and drops it at its next save (IDE probe B,
  SplitRun).
- *An orphan is the server's ordinary text too*: it is formatted as code, and the grammar reads it as an attribute
  statement anywhere a statement goes, so a well-formed one raises nothing and a damaged one is reported. Seen
  while building: the bundled grammar takes `WithEvents` once per declaration, so `Public WithEvents A As T,
  WithEvents B As T`, which the VBA reference's `Dim` syntax allows, is reported as a syntax error. Not measured on
  `vb6.exe`, and not filed yet. The reader here accepts it.
- *A logical line whose first word is `Attribute` is an attribute line however it goes on*, `Attribute = 5` and
  `Attribute(2) = 5` included, because `Attribute` is a reserved word in code (p07e). Such a line names nothing,
  so it describes nothing; all that changes is that it joins a run beside it, and a run with a nameless line in it
  is an orphan anyway. The header's own reading still wants whitespace after the word, so a line like that
  directly below the header starts the code, and a run there hangs from no line of code at all.
- *The two kinds are told apart without case*: a dotted name is a variable attribute when what follows its first
  `.` starts `VB_Var`, in any case. Not measured with another case; VB6 compares names without case everywhere
  else.
- *A module-level declaration is one in the declarations section*: before the first logical line that declares a
  `Sub`, a `Function` or a `Property` accessor, which open a body. An `Event` or a `Declare` opens none, and
  Ledger.cls, which VB6 wrote, declares its event above its variables. VB6 refuses a module-level declaration
  anywhere else, so a variable run under a procedure's own `Dim`, `Static` or `Const` is an orphan, even naming
  the name that line declares.
- *What the module declares*, for the renamed kind, is what the reader finds on every logical line below the
  header, a procedure's locals included, and the designer block's names (`DeclaredNames`). It is read only when a
  run might be the renamed kind, so a file whose runs name their members pays nothing for it. The reader does
  not read an `Enum`'s members or a `Type`'s fields, so a stale name that is one of those still reads as a
  rename's leftover. An enum member takes no variable run (V5), so no other member's description is handed on
  that way; a `Type`'s field was not measured.
  *The guard is weaker than "errs towards too many names" makes it sound*, as review found, and 4.4 should be
  built knowing where. The reader never sees a procedure's parameters, a label, or the name of an `Enum` or a
  `Type`, so a run naming one of those reads as a rename's leftover too. The guard is a name declared somewhere
  *now*: under `Public Tally As Long`, a run naming `Count` is an orphan while `Public Count As Long` stands below
  it, and Tally's renamed run once that line is retyped to `Public Zed As Long`, where VB6, by V1's rule, would
  carry Count's description to `Zed`, and 4.4's save would hand it to `Tally`. Two one-name lines whose runs
  both carry the same stale name both read as renamed. And p09f's shape, `Gone.VB_VarDescription` under `Public A`, which VB6's
  compiler attaches to nothing, reads as A's renamed run, so the save would give `A` a description it never had;
  what VB6's IDE does with that shape at a save was not measured. Every case needs a `VB_Var…` line VB6 itself
  does not write where it stands, which is why it is recorded rather than handled: text alone cannot tell any of
  them from the run a rename by retyping leaves.
- *The greying follows a run that changes kind.* The first build said the colourizer and the link suppression
  follow `Of` without a change of their own, and review found they do not: AvaloniaEdit rebuilds only the lines an
  edit touches, and a run's membership now depends on the line above it. So commenting out a declaration, or
  retyping one variable of a line declaring two, left the run greyed until something else redrew it, and a
  language server's publish was the only thing that did. `ReadOnlyRegionRedraw` compares the regions before and
  after each change, moved through it, and redraws the view on any difference.
- *What 4.4 and 4.5b need is on the run*, so neither reads the text again: `MemberRun.Kind` (which runs the save
  re-qualifies), `Member` (the name it writes, as declared), and each `AttributeLine`'s `Qualifier` and
  `QualifierOffset`, which may be on a physical line after the line's first.
- *A run is read-only from the moment it describes a member, however it came to, and a run part-way through a
  retype is no exception* (found reviewing the rebuild, settled 2026-10-14). Protection follows the text, so an
  edit that makes a run describe the line above locks it at once. Retyping an orphan's name under a line declaring
  one name locks it after the first key, once the name it carries is declared nowhere:
  `Attribute Count.VB_VarDescription` under `Public Tally As Long`, with `Public Count As Long` further down, is an
  orphan VB6 would move under `Count`, and one Backspace leaves `Coun`, Tally's renamed run, greyed and read-only.
  A run typed by hand does the same at its first `.`: `Attribute Halt.` under `Public Sub Halt()` is already the
  procedure's. Leaving the renamed kind editable, ungreyed and unfolded was considered and rejected. That kind is
  above all what a rename by retyping leaves, so a run left unprotected would unfold under the line the developer
  is typing on, and 4.4's save would then re-write text the developer could see and edit, where every other run
  the save re-writes is folded and read-only. So an orphan is repaired by whole lines: cut, pasted, or deleted
  with the line it sits under (4.5a). A retype the run locks part-way through is finished by the save, which
  writes the name the line declares; one meant for another member is cut and pasted there. Typing a run character
  by character is not a way VB6 offers either: its code window takes attribute text as code (V4), and the
  Procedure Attributes dialog (#62) is the way it does offer.

**A run that describes nothing is inert text, not a region.** The code-editor requirement defines a read-only
region as the header or "a run of attribute lines describing a member", and says an attribute line left
describing nothing is preserved as inert text. So a run that does not describe a member as above is an *orphan*:
not read-only, greyed or folded, and kept byte for byte. That covers a run under a blank line, a comment, a
statement, an `Enum` or `Type` line or an enum member; a procedure-attribute run under a `Const`; a dotless line
below code; a `VB_Var…` line away from its variable; a run mixing the two kinds; and a variable's run left naming
a name the line no longer declares, unless it is the one-name kind above. One shape is read the other way: a run
under a `Public Const` in a class, form or UserControl, a declaration VB6 rejects there (it shows the line red) and
whose run it drops (V5). The read does not know the module's kind, and the run is protected text either way, so
this is recorded rather than handled. Protection changes for orphans only,
so the server's port (`VbProtectedRegions`), the sidecar's read-only drop and the mark rules change with it, kept
in step by `BundledServerRespectsTheIdesRegionsTests`.

**This is a deliberate divergence, and the oracle shows its size.** VB6 hides every `Attribute` line, attached or
not, says nothing when it loads a misplaced one, and at its next save **drops** a procedure attribute that reaches
nothing, **moves** a variable attribute under its variable and **moves** a dotless line into the header (IDE probe
B). HexIDE does not rewrite text the developer cannot see, so it shows what VB6 would silently discard or move,
where it can be repaired by hand. While there is no Procedure Attributes dialog (#62), that is the only way to
repair one. A file HexIDE saves with an orphan in it is the same text VB6 would have loaded, so VB6 reads it the
same way. A run mixing the two kinds is a divergence of another sort: VB6 gives each of its lines to its own
member, a procedure's by position and a `VB_Var…` line by name (p09n), where HexIDE orphans the whole run, because
a run describes a member only when VB6 reads every line of it as that member's. Nothing VB6 reads is lost by it,
since the text stands and VB6 binds each line again when it next loads the file. The first draft's open
question, whether the described line should widen past a blank line or a comment, is answered: VB6 does not
honour such a run (p01, p02), so it does not widen.

**Folds (4.3).** Every run that describes a member is folded by default, from its anchor (the described line's
content end) to the content end of its last line, so the line after the run stays a line of its own. The fold
has no title, so the library draws its own `...` box, and there is nothing new to translate. Expansion is
remembered per run key on the view model, and a section the folding manager paired keeps its live state. A run
re-created after a reload, a format or a dock move is folded again unless its key was expanded. The window's own
folds are re-made at once whenever they are not current, not only the header's, and none of them is ever
carried forward as a server fold.

With a server's procedure fold on the same row, AvaloniaEdit's margin draws one marker per row, the procedure's,
so a run expanded there is folded again only by closing and reopening the window. That is filed as an issue
rather than solved here with a margin of our own.

**The end of a folded described line is its anchor.** A collapsed run fold gives the row two caret stops:
before the box (the anchor) and after it (the fold's end, inside the run). A caret landing on the second is moved
to the first, or past the run to the next line when it came from the first. So typing, Enter, F9 and the status
bar all see the end of the declaration the developer sees. This is measured headlessly before it is relied on,
as 3.14 did for the header.

**Renames (4.4).** A procedure's run follows its procedure by position, so no rename separates them. What a rename
leaves behind is the qualifiers, and VB6 rewrites those **at every save**: to the procedure's name as declared,
in its declared casing, after a name retyped on the declaration, after Replace All, after the whole declaration
retyped over a selection, and for a loaded run whose qualifier names nothing, another member, or the member in
the wrong case (IDE probes C2, C3, C8 and B). HexIDE does the same, as an owner write when it saves a document it
changed, in the same step as the save's header: every run describing a procedure has its qualifiers set to the
declared name. So a rename by a server that does not know attribute lines, by Replace, by an add-in, by AI Chat or
by retyping ends with the qualifiers VB6 would write, and nothing has to fix them up at the moment of the edit.
A procedure retyped from `Function` to `Sub` or back keeps its run in VB6 (IDE probe V3), which binding by position
gives here without a rule of its own. `ApplyRename` lets a server's qualifier edits through on any run describing
a procedure the same answer renames, whatever the qualifier said, and refuses every other edit to a run, as now.

A variable's run binds by name, and VB6 carries it across a rename too: a name retyped on a line declaring one
variable keeps the description, re-qualified to the new name, and so does a retyped type (IDE probes V1, V8). So
the save also re-writes a one-name variable run (4.1, above) to the name its line declares. A variable run that
already names its variables is left as written. On a line declaring several, VB6 keeps only the first declarator's
attributes across an edit of the line and drops every other declarator's, renamed or not, including one the edit
never touched (V2 and three more, all renames). HexIDE does not reproduce that. A run whose names the line still
declares stays attached, and one naming a declarator the line no longer declares is an orphan, visible and
editable. Both keep text VB6 would discard. The one loss is the first declarator renamed, whose run VB6 carries
and HexIDE leaves naming the old name: text alone cannot tell it from a run naming a renamed later declarator, which
VB6 drops, and carrying that one to the first declarator would give it a description that was never its own.

A new name that is not a VB6 identifier is refused before the request, with a reason (a new string), and the
bundled server answers nothing for one. The first draft said a rename by retyping leaves an orphan with no fix-up;
VB6 carries a procedure's description across all three kinds of rename, so that is overturned.

**Enter, and a line break at an anchor (4.5).** A line break inserted at a described line's anchor goes after
the run, as a line of its own, in the document's newline. That covers Enter and Shift+Enter, a multi-line paste,
Insert File and automation's `type_text`. Enter's auto-close looks past the run for the closer, and its
re-casing of the declaration still applies, all as one undo step. An add-in's range edit that would insert a line
break at an anchor is refused, like any edit that would separate a run from its line.

**Deleting the described line (4.5a).** A deletion that removes the whole text of a described line, by Delete,
Backspace, Cut or Ctrl+D, removes its run with it as one undo step, and a cut puts both on the clipboard. The
window widens the request before the library asks the provider, because the provider can only narrow. Retyping
over a selected declaration is a replacement, not a deletion: the run stays, and describes whatever the new line
declares, which is what VB6 does (IDE probe C8). Automation's `press_key` judges a deletion by the same rule, so
it does what a person's key does. A drag-move that would carve a run stays a copy, as the library makes it today.

VB6 removes the description with the declaration line or the whole procedure, as here (IDE probes C4a, C9a). It
then loses it for good: Undo restores the text and not the description, and a cut puts only the visible text on
the clipboard, so a pasted procedure arrives undescribed (C5, C6). HexIDE's undo restores both and its cut carries
the run, a deliberate divergence: losing a description to Undo is the kind of VB6 defect the Fidelity Principle
says not to reproduce. Deleting the declaration line and typing it back identically, with no save between, does
not bring the description back either (IDE probe V7). Attribute text pasted into VB6's code window is code: shown
red as a syntax error, unseen by the dialog, and written by the save as it stands, so only the next load reads it
as an attribute, the first of two wins (V4). HexIDE reads a pasted line that lands directly under a declaration as
a run at once, which is what VB6's next load makes of the same bytes.

**Whole replacements (4.5b).** `GuardedContent.Replace` compares the runs that describe members, keyed by
member, between the document and the composed result, reading the lines of a run whose qualifiers the save
re-writes (a procedure's, and a one-name variable's) without them:

- a run may move with its member, or be renamed with it;
- a run may disappear only together with its member's declaration;
- a run's lines may not otherwise change.

Any other change, addition or loss is refused, with a reason naming each member. Orphans are inert text and
free. The add-in contract, `set_file_content`'s description and AI Chat's prompt say so, and `ApplyEdits` names
the member in its refusal too.

**The corpus (4.6).** The fixtures carrying member attributes are written by the VB6 IDE on the oracle guest,
through Tools > Procedure Attributes, so the shapes the lanes check are VB6's own: procedure, property, event and
variable runs, a run under a continued declaration, a comma list and a colon-separated line, `WithEvents` with the
`VB_VarHelpID = -1` line VB6 adds by itself, a standard module, a form and a UserControl, and every option the
dialog offers. A run the dialog writes for a property goes under the first accessor in the file, `Let` or `Set` as
readily as `Get` (IDE probe V6, eighteen cases), so a Let-first run is VB6's own shape and the corpus carries one.
The first session's "under the Get, even when the Let comes first" rested on one property described with Procedure
ID `(Default)` in the same visit, which the second session reproduced and which is the only case seen to choose the
Get. The fixtures live under `corpus/member-attributes/` with a project of their own, and every lane that
walks the repository's corpora walks it. The hand-written load shapes and VB6's saved output for each are kept beside
them as the evidence for the orphan rules above, not as round-trip fixtures, since VB6 rewrote them. (That reason
is the inputs'; what VB6 saved of them is VB6's bytes, and is walked with the fixtures: settled while building 4.6.)

*Settled while building 4.6.*
- *The lanes walk everything VB6 wrote: the round-trip half, and the evidence's saved output.* The paragraph above
  asks for both halves, so the fixtures are `corpus/member-attributes/round-trip` and the evidence
  `corpus/member-attributes/evidence`, and every lane walking `corpus/designer` walks the first and each
  `evidence/*/saved*` folder: the form, module and project round trips, both whole-file grammars, the server's
  header check, the content read and write-back, and the code window's open and its unchanged save. Each designer
  file's composition walks the round-trip half's three, the evidence holding none. "Not as round-trip fixtures,
  since VB6 rewrote them" is true of the hand-written inputs and not of what VB6 wrote back, which is VB6's bytes by
  the same test that admits the round-trip half, and which carries shapes that half does not: runs under a
  `Declare`, a `Friend Function`, a `Static Function`, a `Private Sub` and a declaration with a trailing comment,
  constants' runs, private variables' in a module and a class, a run under each of a property's accessors, the help
  id VB6 gave a bare `WithEvents`, and a header holding the dotless `VB_Description` VB6 moved into it. The first
  build walked the round-trip half alone; review found it, and the saved output passed every lane as it stood (the
  code window's unchanged save went from 43 documents to 79, every one byte for byte). The inputs are read by two
  tests, neither a round trip: the server-parity check, which compares the two copies of the rule and so reads the
  whole folder, and the test that reads each input against its saved twin (below). Every lane that reads the folder
  fails when it is missing, rather than passing on what else it walks. Not extended, because none of them reads the
  repository's VB6 files: the conformance lanes (JSON snippets), and the lanes over a VB6 install's Template tree or
  `vb6.exe` itself.
- *"A project of their own" is six, each as VB6 left it.* Merging them would need a project file written here, not
  by VB6. Besides the dialog's output the corpus carries what VB6 saved after the second session's edits (a
  variable renamed or retyped, a procedure's kind changed, comma lists edited, pasted text reloaded), since a save
  regenerates every run; and the first session's project as stage 1 left it, for the one description with a
  doubled quote, which stage 2 cleared.
- *VB6's own files decide the reader without reading it.* VB6 writes each run directly under its member, qualified
  with the declared name, and never an orphan, so each run's member is known from the file. Measured: all 58 runs
  of the round-trip half and the 32 of the evidence's saved output describe the declaration above them, none is an
  orphan or a rename's leftover, and every qualifier offset lands on the name. Their keys are known from the file
  too, which the first build left unchecked: the first line's qualifier is the member, the second declarator where
  VB6 wrote the run for that one, and counting equal qualifiers from the top gives the ordinal, which is 1 in one
  place only, the second of the two runs of each of MoreShapes' `BothRuns` classes. What these files cannot check is
  any rule about a qualifier that names something else, which VB6 never writes. The evidence's inputs check the
  procedure half of it: VB6 kept the runs of `NamesNothing`, `NamesOther` and `LowerCase`, whose qualifiers are not
  their procedure's name as declared, so a reader that asks for that name is refuted there. A rename's leftover
  stays with the inline rows.
- *The evidence is executable, and it found one divergence the section did not record.* A hand-written run
  describes a member here exactly when VB6's save kept a line of it under the same line, compared from the two
  texts alone, across all 43 runs of the four projects, except the `Public Const` in a class already recorded above
  (two files). Not recorded until now: a run giving one attribute twice (`DoubleDesc`, two `VB_Description` lines)
  describes the member whole and protects both lines, where VB6's save keeps the first and drops the second (IDE
  probe B, p05g). HexIDE keeps text VB6 would discard, as it keeps an orphan, and VB6 reads the file the same way
  at its next load, the first winning. It is not an orphan's case, though, as review found: the reason for keeping
  an orphan is that it stays visible and editable, and this second line is inside a run that describes a member,
  so it is protected and folded with the run. It cannot be seen or removed by hand until the Procedure Attributes
  dialog exists (#62), and the save keeps it for good. Whether 4.4's save should drop it, as VB6's does, is under
  "Open questions".
- *"Every option the dialog offers" is every option it left enabled.* *Don't show in Property Browser*
  (`FUNCFLAG_FNONBROWSABLE`, 0x400) was greyed once Hide was ticked (IDE probe E), so no file carries it.

## When a document gets its file (#489, settled 2026-09-20)

Task 2.1 names a document `untitled:` when it has no path, which presumes a document can *have* no path.
hexide-io/HexIDE#489 asked whether that state should exist at all, or whether the IDE should write a file the
moment a form or module is created. It is settled by measurement against the real VB6 IDE, recorded in
`docs/vb6-fidelity-oracle.md` under *When a document gets a file, and what a dangling `Startup=` does*.

**VB6 writes nothing until the project is saved** - not for a new project, not for a module added to an
unsaved one, and not for a form added to a project that already has a directory of its own. Discarding an
unsaved document leaves nothing behind, `%TEMP%` included. A pathless component is a first-class state there,
rendered `Form2 (Form2)` in the Project Explorer and listed as a bare `Form2` in the save prompt.

Three consequences for this change, none of which move task 2.1:

- **2.1 stands exactly as written.** The `untitled:` branch was live under every candidate route anyway:
  `IProjectTemplate` creates `Form1` with no path and `NewProject` writes nothing, so a session opens on a
  pathless document on every `File > New Project` today. What #489 could have changed is how *often* that
  happens, never whether it can - so it never gated the naming question, and the four routes it weighed were
  three points on a write axis plus one naming choice that is 2.1 itself.
- **2.4 inherits the harder half of the test burden.** "A first save of a pathless form sends close-then-open"
  becomes the common case rather than the rare one, so it is the scenario 2.11 must press hardest.
- **Two invariants HexIDE must match, one of which it currently breaks.** VB6 asks for each pathless file
  before the project's own, defaulting to the project's directory and the component's `Name`; and it abandons
  the entire save when one of those pickers is cancelled, rather than writing a `.vbp` that names a file it
  did not save. HexIDE can reach that second state, and the `.vbp` it then writes compiles under `vb6.exe`,
  exit 0 and silent, into a program with the wrong entry point. Filed separately - it is a serializer defect,
  not a naming one, and outside this change.

## Open questions

- **A file shared by two projects in a group.** Two documents, one file, one wire name. The protocol allows a
  name to be open once. A reference-counted open would satisfy the protocol, but two editors holding the
  same file can diverge. Whether VB6 allows it is unmeasured, and it is not the collision the oracle settled
  (that was two projects sharing a *name*). Recorded here, and to be filed if the implementation meets it.
  Automation met it once, and answered without settling it: a `get_diagnostics` item for such a file names
  no project or document, rather than the first project to list it ("Settled while building phase 3").
- **What happens to a member's attribute lines when the member goes.** Deleting a procedure through the
  editor leaves its attribute run behind, because read-only text is carved out of a deletion, and cutting
  one moves the procedure without its description. The rule proposed is that a run belongs to the line it
  describes and goes with it. The read-only provider can express that only partly: it may decline to carve
  a run out of a deletion that already spans the run and its line, but it cannot widen one, because
  AvaloniaEdit throws when a provider returns a segment outside the span it was asked about (3.7). An
  orphaned run, however it arises, is inert text the next save preserves. Neither VB6's behaviour here nor
  the paste side is measured.
- **A run that gives one attribute twice, for 4.4 to decide** (found reviewing 4.6). `DoubleDesc`'s shape, two
  `VB_Description` lines under one procedure: HexIDE describes the member and protects and folds both, VB6's save
  keeps the first and drops the second (p05g, IDE probe B), and VB6 never writes one itself. The options:
  (a) keep both, as now: nothing VB6 reads is lost, since its next load takes the first, but the dead line is kept
  hidden and locked for good; (b) have 4.4's save drop every repeat of an attribute name after its first within a
  run, an owner write in the same step as the re-qualification, which is what VB6's save does, at the cost of
  removing text the developer never saw; (c) have a repeat end the run, so the second line and any after it are an
  orphan, visible and editable, which is the orphan rule's own reasoning, but splits a run VB6's compiler reads as
  the one member's, the first line standing (p05g). The section's reasoning does not settle it: "HexIDE does not
  rewrite text the developer cannot see" argues for (a), while 4.4's re-qualification already rewrites protected
  text wherever VB6's save does.
- **Line endings — measured while building 3.1, and the diagnosis here was incomplete.** This said the
  header is written CRLF while *typed* lines are LF. The larger mechanism was that
  `VbFrmFormatDeserializer` rebuilt the code body line by line with `StringBuilder.AppendLine`, which
  terminates with `Environment.NewLine` — so **every** line of a VB6-authored form was re-terminated at
  load with the host's newline, typed or not, and the save path writes `Code` back verbatim. On Windows
  that is invisible (CRLF in, CRLF out). On Linux, where `build-ide` runs, merely opening and saving a form
  rewrote its whole code body to LF beside a designer half pinned to CRLF.

  Measured rather than argued: with the old accumulator restored, `WholeFileCompositionTests` fails **3 of
  14 on Windows and 5 of 14 under WSL** — the two extra being the plain CRLF fixture and the real corpus
  files, which cannot fail on a Windows host by construction. It also always appended a terminator, so a
  file whose last line had none grew one.

  3.1 replaces both halves with slices of the input, so terminators are the file's own and the composition
  is byte-exact. What remains open is the narrower original point: text *typed* into the buffer arrives
  with whatever the editor inserts, so a form edited and saved can still mix them. That is now the only
  live part, and it becomes visible to servers once they see the header.
- **Compiler diagnostics.** Their rows were keyed by `vb6://form/{Name}`, and the expression that parses
  them matches none of what the compiler actually writes (#477), so that consumer has had no live traffic
  at all. This change re-keys the rows to the wire name resolved from the
  compiler's own (absolute) path, and converts its line numbers once, where the log is parsed.
