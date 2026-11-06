# A mark tool answered about the startup project, keyed on the caller's spelling, and said nothing back — **CLOSED** (moved from the live file 2026-09-22)

> **Closed by #495 (#273 phase 1).**

**Symptom.** `set_breakpoints("form1", [5])` on a project holding `Form1` replied `{"success":true}`. The
gutter showed nothing, the run broke nowhere, and `get_breakpoints("Form1")` answered with an empty array.
Two tools, two confident replies, no breakpoint. With a project group open, the four mark tools could not
reach the second project's documents at all — a name they did not recognise was `No form or module named
'X' found`, whether or not the IDE had one open in front of the caller.

**How it bit.** `ResolveDocumentUri` matched a name case-insensitively against the **startup** project and
then built the store key by interpolating the **caller's** spelling into `vb6://form/{name}`
(hexide-io/HexIDE#467). Both mark stores were ordinal dictionaries, so `vb6://form/form1` was a second
entry beside `vb6://form/Form1` — one the gutter, the runner and the sidecar all read past. The reply then
echoed that key back in its `uri` field, which reads as confirmation rather than as the symptom it was.

**Why it survived.** Three separate things each looked correct. The match was case-insensitive, which is
what VB6 does. The key was minted the same way the editor mints it, which is what consistency looks like.
And the reply named what had been written, which is what a mutating tool should do. What nobody wrote down
is that the two had to be the *same* string, and nothing in the surface could show they were not: a caller
who has never seen the IDE cannot tell "set, and shown" from "set under a name nothing reads" when both
answer `success`.

**Fixed** (#273 phase 1). A name resolves to a *document* before anything is keyed, and the stores are
keyed by that document rather than by any spelling of its name. Every tool that names a document searches
**every loaded project**, takes an optional `project` to disambiguate, and refuses an ambiguous bare name
with the candidates listed rather than picking one. Replies carry `project` and `document` — the IDE's own
spelling, not the caller's — beside the wire `uri`, and a mutating reply now reports what the document
holds afterwards, including when that is nothing: `Form1 now has no breakpoints.` rather than a bare
success.

**Two descriptions were corrected rather than fixed.** `clear_all_breakpoints` said "Removes every
breakpoint in the project"; it removes every breakpoint in *every loaded project*, and has always done so.
Whether VB6 agrees is unmeasured and is filed as #492. And `set_bookmarks(name, [])` promised to clear a
document's bookmarks: it emptied the store and raised no change event, so the gutter kept its dots and the
sidecar was never rewritten — the cleared bookmarks came back on the next load. That one was a real defect
and is fixed with the rest.

**The lesson this adds.** A reply that echoes an argument back is not evidence the argument was understood.
Where a tool normalises what it was given — a name matched without regard to case is exactly that — the
reply must carry the **normalised** form, because the difference between the two is the whole of what the
caller cannot otherwise see.

**"Every tool" was not yet true, corrected 2026-10-13 (#273).** The fix above says every tool that names a
document searches every loaded project, and that replies carry `project` and `document`. Neither held
everywhere. `set_control_property`, `move_control` and `add_control` still resolved a form their own way (see
the #575 entry for what that did). Of the replies, only `get_bookmarks` and `get_breakpoints` carried the names.
Now every tool that takes a form, module or class by name resolves it through one rule, and its reply carries
`project` and `document` as the IDE spells them whenever the name found one: the content, designer and mark
tools, `run_to_cursor` and `set_next_statement`. So do `add_file`'s reply and each `get_diagnostics` item. On a
diagnostic they sit beside the server's `uri`, and are absent when that names nothing loaded or a file two
loaded projects share. `get_debug_state` and `get_call_stack` carry the running `project` beside their bare
module names. **Still not every reply:** a tool that reports or acts on a document without taking its name does
not name its project yet. That is the run-control replies' "Paused at" note, `get_locals`' and the watches'
`context`, the designer undo, redo and format tools, which act on the active designer, and the tab tools, which
take and report a tab's title. Filed as #886. `uri` is now explained where it is
returned (`get_bookmarks`, `get_breakpoints`, `get_diagnostics`): it is the name a language server knows the
document by, its `file:` URI, or `untitled:<project>/<name>.<ext>` before it has a file. Until now its only
explanation was a code comment, which no caller can read. The delta's scenario, a breakpoint set in `form1` of a
project holding `Form1`, is `BreakpointByNameTests.SettingABreakpointByNameInTheWrongCase`. It checks that the
code window's gutter draws the breakpoint, a run stops on it, and the sidecar records it under `Form1`. Each of
the three fails when the breakpoint is keyed under the caller's spelling instead.
