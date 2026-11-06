# `open_file` does not give the editor keyboard focus, so a routed menu item refuses until something does — **CLOSED** (#678, 2026-09-22)

> **Fixed.** `open_file`, `view_designer` and `activate_document_tab` now put keyboard focus in the document
> they bring to the front (an editor's text area, or the designer's form surface), as opening it by hand does, and
> say so in their note. Verified live: `open_file {"name":"Form1"}` then `invoke_menu_item {"path":"Tools/Add Procedure"}`
> opened the dialog with no `press_key`, and focus followed `activate_document_tab` between the designer and the
> code window. The entry is otherwise as it was.

**Symptom.** Most built-in menu items are routed commands, which act on the control that has keyboard
focus. `open_file` brings a code window to the front but leaves focus where it was (measured: nowhere), so
`invoke_menu_item {"path":"Tools/Add Procedure"}` straight after `open_file {"name":"Form1"}` is refused.
After `press_key {"target":".../Custom[Root]/None[TextEditor]","key":"Right"}` the same call opens the Add
Procedure dialog. A person who opens a code window has focus in it, so the tool is behind the person here.

**Narrowed by #678's PR.** The refusal used to say only `canExecute returned false`. It now says the item is a
routed command, names what has focus, and says `press_key` on the editor is the way in. What remains is the
focus itself.

**Workaround.** `press_key` on the editor with a key that changes nothing (`Right`) before invoking an item
that belongs to a document.

**Suggested fix.** Have `open_file` and `activate_document_tab` give the opened document keyboard focus, as
opening it by hand does: [#678](https://github.com/hexide-io/HexIDE/issues/678).
