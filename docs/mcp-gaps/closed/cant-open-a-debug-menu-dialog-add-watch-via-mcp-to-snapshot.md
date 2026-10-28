# 6. Can't open a Debug-menu dialog (e.g. Add Watch) via MCP to snapshot it — **CLOSED** (#362, 2026-09-22)

> **Closed, measured on the running IDE (#362).** `invoke_menu_item(path: "Debug/Add Watch...")` opened the
> dialog, `take_snapshot()` captured it (`activeDialog: "Add Watch"`), and `dump_visual_tree()` listed its
> expression box, three radio buttons and OK/Cancel, each drivable. The row half is closed by the tree
> fallback in `interact select`: a `TreeViewItem` is now reported with `selectionItem`, and selecting one
> sets the owning tree's `SelectedItem`. That path was driven on a Project Explorer node in the same pass,
> not on a Watches row.

**Symptom.** Verifying debugger P6a's **Add Watch** dialog rendering couldn't be driven through MCP. The dialog opens
from a Debug-menu item / keyboard shortcut backed by a *routed* command (AvaloniaLabs CommandManager), not a VM
`ICommand`: `interact invoke_command AddWatchCommand` finds no such property on `MainViewViewModel`; `interact invoke`
on `MenuItem[Debug]` reports "element does not support 'invoke'" (a top-level menu exposes no invoke provider until
opened, and its children aren't in the tree until then). The Watches rows render as `TreeViewItem`s but don't
advertise a `selectionItem` provider, so selecting a row to enable `EditWatchCommand` (which opens the same dialog)
wasn't reachable either.

**How it bit (debugger P6a).** The Watches *window* was fully verified live — it renders (columns + rows in
`dump_visual_tree`, snapshot after Stop) and evaluates correctly while paused (`get_watches`: x=42, s=hello, x*2=84,
arr expandable). Only the **Add Watch dialog's** live render couldn't be reached; deferred to P6b (where its
Break-type radios become functional and it's exercised again), backed meanwhile by the dialog-VM unit tests.

**Fix consideration.** Best: (a) surface a `selectionItem` provider on tool-window TreeView rows so `interact select`
can set the selection (unlocking the row's context-menu commands like Edit/Delete); also useful: (b) make a top-level
`MenuItem` invoke open the menu and realize its children, or (c) a thin MCP action to open a named debugger dialog.
