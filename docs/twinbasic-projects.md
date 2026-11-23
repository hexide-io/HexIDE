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
and Office modules remain read-only too. Adding, renaming and deleting entries are unavailable. This save
path does not invoke or inspect the twinBASIC compiler.
