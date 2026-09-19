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
  `FormCodeText`'s remarks, the `set_file_content` description and the closed automation-gap entry all say
  the block is hidden. The code says otherwise, and the running IDE is checked in task 0.1. A `.bas` or
  `.cls` has its whole header split off into `OriginalHeader`. So today the two kinds differ, and in the form
  case the one that "hides" nothing leaves `VB_Name` open to deletion.
- **Member-level `Attribute` lines are in every buffer** (procedure-level, and `VB_Var*` after a declaration),
  because only the leading run is ever split off. The interpreter raises when it reaches one.
- **No line is tracked.** Breakpoints and bookmarks are bare integers. Inserting a line above one leaves it on
  the old number, which is now a different statement. There is no anchor anywhere in the IDE.
- **Almost every document has a file.** Add Form, Module, UserControl and PropertyPage write the file the
  moment they are created. In a never-saved project that file goes into a per-project scratch folder, which
  servers are already told is their workspace root. Only the template's `Form1`, the Project Explorer's Add
  Form and add-in project templates produce a document with no path.

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
- It is minted through the URI type, never by interpolation, so a non-ASCII name is percent-encoded. One
  foreign server silently drops a notification whose URI is not strictly valid and never answers the request
  that follows, which leaves a request hanging for good (measured).
- Comparison is case-insensitive over the path, as it was for `vb6:`, because both segments are VB6 names.
  Servers were measured to echo the ASCII form byte for byte and to percent-encode non-ASCII names. Both
  compare equal after normalisation.

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
   whole-document gate, and re-evaluated after a reload that changes the fidelity verdict. That verdict goes
   stale today (#475). Inserting at the very top of the file is refused, because the
   stock provider allows insertion at a region's edge.
2. One guarded write path that every programmatic writer goes through, with a stated policy for each.

**"Read-only region" means the header and the member-attribute runs, and nothing else.** A form the IDE
cannot reproduce is held read-only as a whole, and that must not make every line of it a read-only *region*:
otherwise a developer could not set a breakpoint anywhere in such a form, and Find would match nothing in it.
The two are separate gates, and the rules below that turn on a region (marks, Find, member attributes) test
the region, never the whole-document gate.

| Writer | Policy |
|---|---|
| Designer or model header refresh; a save's rendered header; module rename's `VB_Name` | Owner. Writes the region, off the undo history, and shifts marks by the change in line count. |
| A member's own `Attribute <Member>.…` qualifiers, on that member's rename | Owner. Follows the rename, whether the IDE or a server makes it. |
| Formatting (server answer, any server) | The whole-document edit is reduced to the lines it changes, and changes inside a read-only region are dropped. The bundled formatter also leaves the header alone. |
| Server rename | Refused as a whole if any edit lands in the header region, with a reason. Edits to the renamed member's own attribute qualifiers are allowed. Refusing is not rare: the bundled server's rename is lexical and whole-word over the buffer, so with the designer block in it, renaming a local called `Text`, `Top`, `Caption` or `Index` would otherwise reach the header. The bundled server therefore skips those regions itself, using the same rule, so the refusal is reserved for a server that does not. |
| Replace, Replace All, Find | Match only outside read-only regions. VB6 never searched the hidden header. |
| Completion, Insert File, Enter auto-close, event stubs, paste | Never write into a read-only region. Text goes after the region. |
| Add-in `SetContent` | The same rule as automation: the whole file with its header unchanged, or the body alone with the header kept. Refused only when the header would change. The shipped chat add-in applies a model's fenced block, which is a body. |
| Add-in `ApplyEdits` | An edit touching a read-only region is refused. |
| Automation `set_file_content` | Accepts the whole file (header unchanged) or the body alone (header kept). That settles #338's asymmetry. |
| Automation `type_text`, `press_key` | Refused inside a read-only region, and the reply says so. |
| Reload after an external change; Edit-and-Continue revert | Owner. Replaces the whole buffer. |

**Undo, and why the mechanism is open.** A designer change must not be undoable from the code editor, which
the form-designer undo spec requires, and it must not stop the code editor undoing an earlier code edit
either — that spec says the two histories do not interfere. But an undo stack is offset-based. An edit it did
not record shifts the offsets of every entry before it, and undoing one of those afterwards changes the wrong
text. So "just do not record it" is unsafe.

Two of the three obvious candidates are ruled out by that second constraint rather than by feasibility:
clearing the code editor's history on a refresh destroys the developer's code undo, and recording the refresh
and having undo stop at it blocks the same thing. What is left is to rebase the stack past the refresh.
Whether AvaloniaEdit 12.0.0 allows it depends on internals inferred from the library's lineage and not read:
a decompiler is needed, and none is installed. If it turns out to be impossible, the fallback is a MODIFIED
delta to the undo capability saying plainly what a developer loses, not a silent breach of it. The behaviour
is specified either way; the mechanism is task 3.8.

**Marks are refused in a read-only region.** A header line never executes, and the folded header is a single
visual line, so a click on it would otherwise set a mark on whichever line it happened to map to. A form the
IDE cannot reproduce is not a read-only region, so its code lines still take marks.

**Edits by the IDE itself never prompt a reset.** Edit-and-Continue's prompt fires on keystrokes today. A
header refresh or reload is not the developer editing.

## Folds and greying

**Folds the editor makes itself, merged into every fold application.** Today an empty server answer, or no
server, clears every fold, so the header fold would vanish with it. The header fold and each attribute fold
are added after the server's folds, and a server fold that partly overlaps one of them is dropped. A region
is folded when its fold is first created, and folded again whenever it is re-created, unless the developer
expanded that region in this editor. The reason not to rely on the fold library's "closed by default" flag:
it is honoured only on a fold manager's first update, and whole-document replacements recreate folds.

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
- unrecognised top-level content is kept (today it is dropped on save, contrary to the user-sidecar spec,
  which is #466);
- the file is rewritten as version 2 only if something changed, and never after a read that failed.

**Downgrade.** An older build cannot be fixed. Given a version-2 sidecar it ignores the new keys and, on its
next save, erases them — which is #466, the unrecognised-content defect, biting where the spec already says
it should not. That is the lesser harm: reusing the old keys with moved lines would instead have the old
build show every mark the prefix's length too low, silently. Fixing #466 first narrows the window to builds
released before that fix, and the rest is recorded rather than mitigated.

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
project as well as its name — as a trailing optional argument, following the convention the diagnostics
change used, so existing add-ins keep compiling.

**Export redaction.** Today `untitled:` would pass through unredacted, carrying the project and document
names. Its path segments are pseudonymised like a `file:` URI's (an extension of #397). The rationale in the
redactor, that VB6 documents ride an opaque scheme and are therefore path-free, is rewritten: saved modules
now carry paths and are pseudonymised as paths. The disclosure's per-document grouping keys on the full name,
not the last segment, so two `Module1`s no longer merge.

## Measurements this depends on (phase 0)

1. **What the editor shows today** for a form's attribute block. The code says visible, and the whole
   composition model above turns on it: if a form's buffer really did begin at its attribute run, the prefix
   is the designer part and the sidecar shift is the designer part alone. Confirm it in the running IDE
   before building either, and correct the comments that say the block is hidden.
2. **Both grammars parse whole real files.** Every corpus `.frm`, `.cls`, `.ctl` and `.bas` goes through the
   interpreter's grammar and the bundled server's grammar with no syntax error, and the bundled server
   raises no diagnostic in a header. No `.pag` exists in any corpus, so one is authored in the VB6 VM.
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
5. **AvaloniaEdit behaviours** the protection and fold design assume: where read-only sections allow
   insertion at an edge, whether `IsReadOnly` replaces the provider, how undo treats an unrecorded edit, and
   the first-update rule for closed folds. All were inferred from the lineage. Reading the binary needs a
   decompiler.

## Open questions

- **A file shared by two projects in a group.** Two documents, one file, one wire name. The protocol allows a
  name to be open once. A reference-counted open would satisfy the protocol, but two editors holding the
  same file can diverge. Whether VB6 allows it is unmeasured, and it is not the collision the oracle settled
  (that was two projects sharing a *name*). Recorded here, and to be filed if the implementation meets it.
- **What happens to a member's attribute lines when the member goes.** Deleting a procedure through the
  editor leaves its attribute run behind, because read-only text is carved out of a deletion, and cutting
  one moves the procedure without its description. The rule proposed is that a run belongs to the line it
  describes and goes with it, which the read-only provider can express by widening the deletable span. An
  orphaned run, however it arises, is inert text the next save preserves. Neither VB6's behaviour here nor
  the paste side is measured.
- **Line endings.** The buffer and the file are the same text by construction. But today the header is
  written CRLF while typed lines are LF, so a saved form can mix the two. That is a pre-existing defect, and
  it becomes visible to servers now that they see the header.
- **Compiler diagnostics.** Their rows were keyed by `vb6://form/{Name}`, and the expression that parses
  them matches none of what the compiler actually writes (#477), so that consumer has had no live traffic
  at all. This change re-keys the rows to the wire name resolved from the
  compiler's own (absolute) path, and converts its line numbers once, where the log is parsed.
