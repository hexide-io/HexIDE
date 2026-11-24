# Editing twinBASIC projects

Open a `.twinproj` through Open Project. Its stored folders and entries appear separately from VB6 projects.
Available UTF-8 `.twin` sources are editable and mark their tabs with an asterisk while unsaved. Ordinary
Save writes all modified source tabs belonging to that container. Closing a modified tab, closing its
project or exiting asks whether to save the changes. Reload also asks before discarding edits.

Save preserves the original UTF-8 BOM choice, entry order, metadata and unchanged entries. Each changed
source's revision increases by 2, reproducing the [measured beta 1044 edit](twinbasic-oracle/a-container-hexide-wrote.md).
HexIDE verifies the replacement in memory, refuses a file that has changed on disk since opening or saving,
and writes through a flushed temporary sibling. Previous bytes remain in a uniquely named `.recovery`
sibling. A refusal leaves the edit unsaved; reload the project to read an external change.

Close the project in the twinBASIC IDE before editing it in HexIDE. The twinBASIC IDE does not notice
HexIDE's replacement while it has the project open, and its next save silently overwrites HexIDE's edit,
as [measured with beta 1044](twinbasic-oracle/a-container-hexide-wrote.md).

Designer `.tbform` entries, resources, JSON and other entries remain read-only. Standalone archive previews
and Office modules remain read-only too.

In Project Explorer, a module's `.twin` offers Rename and Delete; Sources and its folders offer New Folder
(#5144). These commands write immediately through the same verified, recoverable save path. Delete asks
for confirmation naming the entry. Save or discard all modified sources in that container before any of
these commands. A renamed entry keeps its open tab with its new title; deleting it closes its tab. Tree
selection follows a rename or selects the parent after deletion, keeping archive focus rather than
implicitly targeting a loaded VB6 startup project.

Names are deliberately restricted for future source-folder synchronisation: ASCII identifiers
`[A-Za-z_][A-Za-z0-9_]*`, at most 255 bytes with any `.twin` extension included, unique in their folder ignoring case, excluding reserved
Windows device names. This is a HexIDE safety policy, not a claim about twinBASIC's accepted names.

The [measured container operations](twinbasic-oracle/twinproj-container-v1.md) preserve content, revision
and position on rename, remove only the deleted entry, and insert an empty folder first with revision,
flags and category zero. Fixture comparisons establish `05 → 06` for module rename, `05 → 02` for
module deletion and `02 → 07` for folder creation. These reproduce the IDE's saved bytes without
reproducing the order reversal or revision changes caused by reopening in that IDE.

Add Module needs a second measured name before generalising its template. Add Windows Form, folder
rename/delete, rename/delete of a `.twin` with a same-named `.tbform`, and all Settings edits remain
refused because their behaviour has not been measured. This path does not invoke or inspect the
twinBASIC compiler.
