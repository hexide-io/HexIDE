# Design record

Settled with the maintainer in an interview, against a mapping pass over the codebase. Seven readers each
covered one subsystem, and an adversarial verifier re-checked each reader's claims. Four of the five foreign
servers the suite drives were then run against the proposed `untitled:` shape. The reasoning is recorded
because most of it rests on facts about today's code that the code's own comments contradict.

## What the code does today

These were read, not assumed. Several contradict comments in the tree.

- **One string does five jobs.** `vb6://form/{Name}` or `vb6://module/{Name}` is at once the wire URI, the
  breakpoint and bookmark key, the sidecar key, the diagnostics key and what the automation surface reports.
  It is minted by string interpolation in seven places with no shared helper: the code editor, the Object
  Browser, `EditorService`, `ProjectRunnerService`, the sidecar (twice), the toolchain service and the
  automation tools. Six places read the module name back out of it with `LastIndexOf('/')` or a prefix strip:
  the code editor view's debug module name, Run To Cursor, Set Next Statement, the runner's live breakpoint
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

**Names that form the wire name must not collide.** An `untitled:` name embeds the project name and the
document name. A document name is unique within a project in VB6, and HexIDE does not enforce that
everywhere: the Project Explorer's Add Form reuses `Form{Count+1}` after a delete, and a form rename checks
only the form's own controls. A new project takes `Project{LoadedProjects.Count+1}`, which repeats after an
unload. These become requirements: new names do not repeat a name in use, and a rename that would repeat one
is refused. Whether VB6 lets two projects in one group share a name is measured in task 0.3, and the project
rule follows the answer.

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
  writes and carries the definition, so the handler compares the session's name with the new one);
- a document with no file whose name changes, or whose project's name changes.

On a trigger, each server that has the document open is sent `didClose` under the old name and then
`didOpen` under the new one, with the current text, **in that order on each connection**. Today both are
fire-and-forget, so nothing orders them, and the order is asserted on the wire. Where the trigger was a save,
the save notification follows under the new name. A pull server's result id for the old name is discarded.

**A file that disappears keeps its name.** A saved document whose file is deleted from disk stays `file:`,
following the protocol maintainers' own guidance. `untitled:` means "no file yet", not "file missing".

**Open documents survive a root restart.** Saving a never-saved project moves its workspace root from the
scratch folder to the `.vbp`'s folder. Setting another project as the startup project and Save Project As
move it too. Each move restarts the running servers, and today the restarted connections have lost every
document except the one whose open triggered the restart. Later changes to the others reach a server that
never saw them opened. The registry keeps a record of what is open and re-opens those documents on the
restarted connection. This is filed separately as a defect, and stated here as a requirement because a first
save triggers it.

## Routing

**By extension, for every document.** The `vb6` scheme branch, `SchemeLanguageOf` and the language identifier
doubling as a claim all go.

**Project members on an ambiguous extension are gated.** `.cls` is a VB6 class and also another language's
class file. A project member is known to be VB6, whatever its extension shares. So a project member whose
extension is ambiguous goes only to servers whose claim establishes VB6: they claim at least one unambiguous
VB6 extension, or they declare the identifier `vb6`. The registry has no model of the project, by design, so
it asks the workspace projection it is already given whether a document is a member. Carried files are not
members and route by extension alone.

Two behaviours change, and both are more precise than today:

- An entry that claims only `.bas` stops receiving forms and classes. Today the scheme branch handed it every
  VB6 document.
- An entry that declares `languageId: vb6` and claims no VB6 extension stops receiving VB6 documents. The
  identifier names the language a server is told, and no longer routes anything, except as the ambiguity
  gate above.

## The whole file in the editor

**The model is unchanged, and the buffer is composed.** `Code` stays what it is today: the body for `.bas` and
`.cls`, and everything after the designer block for forms, UserControls and PropertyPages. The serializers,
dirty detection and everything else that reads `Code` stay untouched. The editor's text is the file's
header followed by `Code`. A flush splits the buffer at the end of the header region and writes the rest to
`Code`. Protection guarantees the user cannot have moved that boundary.

**The header region is one rule for every kind.** It runs from the top of the file through the last line of
the leading `Attribute` run: the class `VERSION`/`BEGIN…END` block, or a form's `VERSION`, `Object=` lines and
designer block, followed by the attribute run. For a `.bas` it is usually one line.

**Where the header text comes from:**

- `.bas`/`.cls`: `OriginalHeader`, verbatim, which is already kept, or the canonical header for a new module.
  A rename retargets `VB_Name` as an edit the IDE makes itself.
- Forms, UserControls, PropertyPages: the file's own text as read. The reader discards it today, and it is
  now kept so that opening a form does not change its buffer relative to the file. After the model changes
  it is the render the next save will write. A form held read-only because the IDE cannot reproduce it is
  never re-rendered. A form with no file renders its companion references as `<Name>.frx`, the name its
  first save will give it.

So the invariant, stated exactly: **the buffer's header is the file's own header until the model changes,
and afterwards it is what the next save will write.** It is not "equals the disk at every moment", which no
editor with unsaved changes can promise.

**When the header is refreshed.** Once per committed designer change. Never per property write: a drag writes
the model on every pixel, and a refresh per pixel would mean a full render and a whole-document `didChange`
per mouse move. The designer's undo stack signals a commit, and three paths bypass it today and must raise
the same signal: the menu editor, the colour palette, and the automation tool that sets a control property
with no designer open. A single "layout changed" notification on the form carries all four. A form open
only in its designer is not open to the language layer (unchanged), so its header is composed when a code
editor next opens it.

**The interpreter is given the whole file.** Its line numbers are then the editor's, with no conversion at
the breakpoint push, the current-statement bar, Run To Cursor, Set Next Statement or the Call Stack. Both
grammars declare the whole-file shape (module header, references, designer block, class config, module
attributes), and the walk executes only module blocks and procedures. But no test has ever parsed a whole
real file with either grammar, so task 0.2 proves it over the corpus before anything relies on it. The
syntax check before a run parses the same text. The standalone runner gets no change, because it surfaces
no line numbers.

**Line numbers are file lines everywhere.** In a class with the canonical header, the first line of code
becomes line 14. This diverges from VB6, whose code window hid the header, and is recorded as such. Whether
VB6's status bar counted from the hidden header is unmeasured. It cannot be probed with `/make` and is
recorded as unmeasured rather than guessed.

## Protection

**Read-only sections cover typing only.** Every programmatic write in the editor (twenty paths were
inventoried) goes straight to the document and ignores them. The existing whole-document read-only gate for
unfaithful forms has the same hole today. So protection is two things:

1. A read-only section provider over the header and member-attribute regions, combined with the
   whole-document gate, and re-evaluated after a reload that changes the fidelity verdict. That verdict goes
   stale today, which is filed separately. Inserting at the very top of the file is refused, because the
   stock provider allows insertion at a region's edge.
2. One guarded write path that every programmatic writer goes through, with a stated policy for each.

| Writer | Policy |
|---|---|
| Designer or model header refresh; module rename's `VB_Name` | Owner. Writes the region. |
| Procedure rename's `Attribute <Proc>.…` qualifiers | Owner. Follows the rename. |
| Formatting (server answer, any server) | The whole-document edit is reduced to the lines it changes, and changes inside a read-only region are dropped. The bundled formatter also leaves the header alone. |
| Server rename | Refused as a whole if any edit lands in the header region, with a reason. Edits to the renamed procedure's own attribute qualifiers are allowed. |
| Replace, Replace All, Find | Match only outside read-only regions. VB6 never searched the hidden header. |
| Completion, Insert File, Enter auto-close, event stubs, paste | Never write into a read-only region. Text goes after the region. |
| Add-in `SetContent` | Accepted if the header is unchanged, otherwise refused with a reason. |
| Add-in `ApplyEdits` | An edit touching a read-only region is refused. |
| Automation `set_file_content` | Accepts the whole file (header unchanged) or the body alone (header kept). That settles #338's asymmetry. |
| Automation `type_text`, `press_key` | Refused inside a read-only region, and the reply says so. |
| Reload after an external change; Edit-and-Continue revert | Owner. Replaces the whole buffer. |

**Undo, and why the mechanism is open.** A designer change must not be undoable from the code editor, which
the form-designer undo spec requires. But an undo stack is offset-based. An edit it did not record shifts the
offsets of every entry before it, and undoing one of those afterwards changes the wrong text. So "just do not
record it" is unsafe. There are three candidates: record it, and have undo stop at it with a message pointing
to the designer; rebase the stack past it; or clear the code editor's history on a header refresh. Which is
possible depends on AvaloniaEdit 12.0.0 internals that were inferred from the lineage but not verified. A
decompiler is needed to read them, and none is installed. The behaviour is specified, and the mechanism is
task 3.8, which is blocked on that tool.

**Marks are refused on read-only lines.** A header line never executes, and the folded header is a single
visual line, so a click on it would otherwise set a mark on whichever line it happened to map to.

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

A sidecar with `version` 1 (or none) holds `vb6://` keys and lines counted from the first line after the
header. `version` is written today but never read, so reading it starts here. Migration happens while the
project loads, once every document is loaded and each header's length `H` is known:

- a key naming a document in the project is re-keyed to that document's name, and every line in it moves
  down by `H`. Bookmarks stay 0-based and breakpoints 1-based: changing bases would also break the
  automation surface;
- a key naming no document in the project is carried forward unchanged and marked unmigrated. Guessing an
  offset would misplace it, and dropping it would lose marks for a file that is only temporarily missing;
- unrecognised top-level content is kept (today it is dropped on save, contrary to the user-sidecar spec,
  which is filed separately);
- the file is rewritten as version 2 only if something changed, and never after a read that failed.

**Downgrade.** An older build cannot be fixed. Given a version-2 sidecar it ignores the new keys and, on its
next save, erases them. That is the lesser harm: reusing the old keys with moved lines would instead have
the old build show every mark `H` lines too low. It is recorded rather than mitigated.

## The shipped surfaces

**Automation.** Tools that name a document take and return `project` and `document`. The `uri` field, where
there is one, is the wire name and is documented as such. Lines are file lines. Resolution covers every
loaded project, not only the startup one. The existing defect of minting a key from the caller's spelling
disappears, because a name now resolves to a document before anything is keyed. `get_file_content` returns
the whole file, whether the editor is open or not, and `set_file_content` accepts what `get_file_content`
returns. Each change is recorded in `docs/mcp-server-gaps.md`.

**Add-ins.** A document's content is the whole file, and positions are file lines. `FileName` is the
document's name, as it has been in practice. `SetContent` keeps the header or is refused. The shipped AI Chat
add-in is updated to match.

**Export redaction.** Today `untitled:` would pass through unredacted, carrying the project and document
names. Its path segments are pseudonymised like a `file:` URI's (an extension of #397). The rationale in the
redactor, that VB6 documents ride an opaque scheme and are therefore path-free, is rewritten: saved modules
now carry paths and are pseudonymised as paths. The disclosure's per-document grouping keys on the full name,
not the last segment, so two `Module1`s no longer merge.

## Measurements this depends on (phase 0)

1. **What the editor shows today** for a form's attribute block. The code says visible. Confirm it in the
   running IDE and correct the comments.
2. **Both grammars parse whole real files.** Every corpus `.frm`, `.cls`, `.ctl` and `.bas` goes through the
   interpreter's grammar and the bundled server's grammar with no syntax error, and the bundled server
   raises no diagnostic in a header. No `.pag` exists in any corpus, so one is authored in the VB6 VM.
3. **Oracle:** the line base of VB6's compile-error log (file line or code line, 0- or 1-based), whether two
   projects in a group may share a name, and whether a form and a module may share a name.
4. **Foreign servers and `untitled:`.** For each server, a test asserting that diagnostics arrive under the
   exact name sent. The server that refuses non-`file:` URIs is asserted to refuse, on the wire or on stderr,
   because "did not throw" proves nothing. The measurement that seeded this ran with dynamic registration
   accepted, which HexIDE refuses, so one server's delivery mode differs under HexIDE and is re-measured.
5. **AvaloniaEdit behaviours** the protection and fold design assume: where read-only sections allow
   insertion at an edge, whether `IsReadOnly` replaces the provider, how undo treats an unrecorded edit, and
   the first-update rule for closed folds. All were inferred from the lineage. Reading the binary needs a
   decompiler.

## Open questions

- **A file shared by two projects in a group.** Two documents, one file, one wire name. The protocol allows a
  name to be open once. A reference-counted open would satisfy the protocol, but two editors holding the
  same file can diverge. Whether VB6 allows it is unmeasured. Filed rather than decided.
- **Line endings.** The buffer and the file are the same text by construction. But today the header is
  written CRLF while typed lines are LF, so a saved form can mix the two. That is a pre-existing defect, and
  it becomes visible to servers now that they see the header.
- **Compiler diagnostics.** Their rows were keyed by `vb6://form/{Name}`, and the expression that parses
  them appears never to match real compiler output (being verified). This change re-keys them to the wire
  name resolved from the compiler's path. Whether they then work depends on that defect.
