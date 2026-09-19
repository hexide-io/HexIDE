# Name a document by where it lives, and show the whole file

Tracks hexide-io/HexIDE#273. Also closes the identity half of #269, and settles for project members the
direction of #279 that runs from a VB6 class towards another language's server.

## Why

HexIDE names its own forms, modules and classes to language servers as `vb6://form/Form1` and
`vb6://module/Module1`. That scheme was chosen because such a document might have no file behind it. For a
saved project that premise is false. Every form and module has a file, the serializer writes it on every
save, and the IDE never tells a server where it is.

A server that reads from disk cannot use a name it cannot open. That includes a server that asks for saves
without text, and every backend that indexes a workspace. It cannot offer a definition in a file it cannot
locate, it cannot watch the file, and it sees the module it indexed from disk and the module the editor
opened as two different documents. The demo that attaches a foreign server (`demo/spring-tide`) has to carry
its module as `RelatedDoc=` rather than `Module=` to work around exactly this. Its README names the switch
back as the test that #273 is fixed.

Fixing the name exposes a second problem. **The editor does not hold the file.** A `.bas` or `.cls` has its
header split off. A form has its whole designer block held elsewhere. So a server reading the file counts
lines differently from the text the editor sent it, every position it reports into an unopened module is off
by the header's length, and the same is true of VB6's own compiler. The obvious fix is a line-offset layer at
the seam. That has to be kept right for every message in both directions, forever.

The decision here goes the other way: **the editor holds the whole file**, with the header folded, greyed out
and read-only. Buffer text is then file text, a position means the same thing everywhere, and there is
nothing to map.

## What changes

- **Identity split.** Every form, module and class has an identity inside the IDE that does not change while
  it is open. Breakpoints, bookmarks, the per-user sidecar, diagnostics, the debugger, the automation surface
  and the add-in surface key on it. Only the language-server seam turns it into a URI. Renaming a document,
  saving it for the first time, or saving it somewhere new no longer moves anything a developer set on it.
- **Wire names that tell the truth.** A document with a file on disk is named by its `file:` URI. A document
  with no file yet is named `untitled:<Project>/<Name>.<ext>`, using the module's name (not a file name,
  because there is none) and the extension its kind will be saved with. Both carry an extension, so both
  route by extension.
- **An identifier changes only by closing and reopening.** When a document gains a file, moves to a new one,
  or (with no file) is renamed, each server is told `didClose` under the old name and then `didOpen` under
  the new one. That is the shape the protocol prescribes for a rename. Make EXE's temporary copies are not
  such a change.
- **Scheme routing is retired.** The `vb6` scheme, and the language identifier doubling as a claim on it, go.
  In their place, a project member whose extension another language also uses (today, `.cls`) is offered
  only to servers whose claim establishes VB6.
- **The editor holds the whole file.** The header (the class `VERSION`/`BEGIN…END` block and the leading
  `Attribute` run, or the designer block for forms, UserControls and PropertyPages) sits in the buffer:
  folded by default, greyed out and read-only. Attribute lines that belong to a procedure or a declaration
  get the same treatment, folded into the line they describe. VB6 hid all of this. HexIDE shows it and
  protects it, and the reasons for that divergence are recorded.
- **Nothing edits the header but the IDE's own model.** Designer changes reach the header without entering
  the code editor's undo history. Formatting is clipped to editable text. A rename that would edit the header
  is refused, except that a procedure's own attribute lines follow its rename.
- **Lines are file lines everywhere**: the line-number margin, the status bar, breakpoints, bookmarks, the
  debugger and Call Stack, the automation surface and the add-in surface. The interpreter is given the whole
  file, so its line numbers are the editor's with no conversion.
- **A one-time sidecar migration** re-keys existing breakpoints and bookmarks and moves them down by each
  document's header length, so they stay on the same statements.

## What this change does not do

- **It does not move scratch files on first save.** A new project's forms and modules are written into a
  scratch folder the moment they are created, and today's first save leaves them there. Under this change
  they are named by their real `file:` URI in that folder, which is the truth. Moving them is #260.
- **It does not add a Procedure Attributes dialog.** Attribute lines become read-only here, which makes that
  dialog (#62) the only way to edit them once it exists. Until then they cannot be edited in the IDE, and
  their content is preserved exactly.
- **It does not change carried files.** They already use `file:` URIs, never lack a path, and are never
  renamed. They route by extension alone.
- **It does not make member-level `Attribute` statements executable.** The interpreter already raises when it
  reaches one, which is a separate, pre-existing gap. Giving the interpreter the whole file adds only
  module-level attributes, which the grammar parses and the walk never executes.
- **It does not fix the workspace-root restart losing open documents**, as its own defect. It does state the
  requirement, because a first save triggers that restart. The defect is filed on its own.

## Decisions, and what was considered

**Tier.** The identity split and the wire names are *Abstraction*: they make the seam honest for any backend.
The header treatment is *Fidelity* by the project's own rule: it reproduces what VB6 intended (the header is
not the developer's to edit) without reproducing a limitation (being unable to see what the file holds).

**"No file yet", not "project never saved", decides `untitled:`.** A new project's documents are written to
a scratch folder the moment they are created, and servers are already told that folder as their workspace
root. Naming those documents `untitled:` would show a server that indexes the root each module twice: once
from disk under `file:` and once from the editor under `untitled:`. It would also make the transition
invisible to the save event, because a project's documents are written before its `.vbp`. So a document that
has a file is named by it, even when the file is in a temporary folder. Scratch paths therefore reach
servers, and exports pseudonymise them like any other path.

**The internal identity is not built from names.** The wire name embeds two names that can change, and that
is fine for the wire, because a change there is announced by close and reopen. The stores cannot work that
way. A breakpoint set while a module was untitled is written by the first save, and that save changes the
wire name. So the internal identity is the document itself, qualified by the project it belongs to, and
persisted by the document's name *within* its project's sidecar, which is one file per project already.

**Project members are gated on ambiguous extensions.** Pure extension routing would offer every VB6 class
module to any server claiming `.cls`. The test suite uses a LaTeX server for that case because it is the
cheapest foreign claimant on an ambiguous extension, not because such a server is a likely attachment. The
measured cost is a process started and the developer's source sent to a server with nothing to say about it.
The existing guard (an unambiguous VB6 extension, or the identifier `vb6`) is kept and now keyed on the
document being a project member rather than on its URI scheme. Considered and not taken: a first-line
content match, which is a real option now that every class begins `VERSION 1.0 CLASS`. It remains the better
answer for the opposite direction (#279, a carried LaTeX `.cls` reaching the VB6 server) and is left there.

**Considered and rejected:**

- *Keep `vb6://` and map positions at the seam.* It fixes nothing for a server that reads disk, and it adds a
  mapping that every message in both directions must get right forever.
- *`file:` URIs, with the editor still holding only the body, and a line-offset layer.* It is correct only
  while every consumer remembers to apply the offset. A server that re-reads the file after a save sees
  different line numbers from the text it was sent.
- *Show the header editable* (what forms already do with their attribute block today, by accident). Every
  line of it is load-bearing, and a developer who deletes `VB_Name` has broken the file.
- *A custom scheme such as `hexide://` for unsaved documents.* The protocol maintainers' guidance is to branch
  on "not `file:`", and `untitled:` is the name the ecosystem already uses for an unsaved buffer. Every
  foreign server the suite drives was measured to treat the two alike, except one that refuses every scheme
  other than `file:` alike.
- *Base the internal identity on names.* A rename, first save or Save As would then move breakpoints and
  bookmarks. That is #269's defect generalised.
- *Re-render the header on every designer property change.* A drag writes the model on every pixel, which
  would mean a full render and a whole-document `didChange` per mouse move. The header is refreshed when a
  designer edit commits.
- *Give the interpreter the body and add the header length at every debugger site.* It reintroduces the
  mapping this change exists to remove, in five places (breakpoint push, current-line bar, Run To Cursor,
  Set Next Statement, Call Stack).

**Superseded records.** The lsp-client requirement "A document on disk SHALL be identified by a URI carrying
its extension" is rewritten. So is the rule that a language-naming scheme takes precedence over the
extension. Three archived changes deferred this decision to #273: telling a server the document was saved,
opening every document to its servers, and attaching a server without rebuilding. The archived decision to
start a server for an unsaved project rejected leaving documents outside every root as "a far larger behaviour
change". This change does that for documents with no file, and does it knowingly: such a document has no
location to be inside a root.

## Phases

0. **Measurements the rest depends on.** Whether VB6's compiler reports file lines or code lines. Whether
   both grammars parse whole real files. How each foreign server treats an `untitled:` document. What the
   editor shows today.
1. **Identity split.** Internal identity, stores and consumers re-keyed. No wire change and no header change.
2. **Wire names.** `file:` and `untitled:`, close and reopen on change, scheme routing retired, the
   project-member gate, open documents surviving a root restart.
3. **The whole file in the editor.** Header in the buffer, folded, greyed, read-only and guarded. The
   interpreter given the whole file. Lines are file lines. Sidecar migration.
4. **Member attributes.** Procedure-level and declaration-level attribute lines read-only and folded into
   the line they describe.
5. **Documentation and archive.**
