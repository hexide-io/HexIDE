# 5. A designer control is reported under its view-model's type name, not its own — **CLOSED** (#543 for #526, #663 for #661; moved from the live file 2026-09-22)

> **Closed.** What remained after the 2026-09-22 narrowing was the node name, and #543 fixed it for #526: a canvas
> control is now `ListItem[Command0]`, measured again the same day with `dump_visual_tree(root: ".../List[ControlsContainer]")`.
> The multi-selection addendum was closed by #663 (#661). The entry is otherwise as it was.

**Was:** *Can't select / delete / reorder a designer control via MCP.* Measured again for #362, and most of
it no longer holds. With default arguments, `dump_visual_tree(root: ".../Pane[#0]")` lists each canvas
control as a `ListItem` (`ControlItem`) with `selectionItem`. `interact select` on it selected it,
`press_key(key: "Delete")` on it deleted it (`get_form_controls` confirmed `Command0` gone; the Edit menu
then offered *Undo Delete: Command0*), and `invoke_designer_undo` / `invoke_designer_redo` both exist. The
`FormEditor.BringToFront` / `SendToBack` toolbar buttons are addressable by `automationId`; reordering was
not driven in this pass.

**What remains.** The node's name is `HexIDE.VisualDesigner.ComponentInstanceViewModel`, the view model's
`ToString()`, so the path is `ListItem[HexIDE.VisualDesigner.ComponentInstanceViewModel]` for every
control. With more than one control on a form, a caller cannot tell from the tree which node is
`Command1`; it has to fall back on position or index. Filed as #526. The rest of this entry is the original record.

**Multi-selection (#661, closed by the PR that adds this note).** `interact select` replaces a selection, so
a caller could hold one control, or all of them through `press_key(key: "A", modifiers: "Ctrl")`, and nothing
in between. `interact add_to_selection` / `remove_from_selection` now reach a chosen group, and each canvas
control reports the `multiSelectItem` token that promises them. Measured with three buttons:
`select` Command0, then `add_to_selection` Command2 → `added 'Command2' to the selection; 2 items are selected
now: Command0, Command2`; `invoke_format_command(command: "AlignLefts")` then moved Command2 to Command0's
Left (120) and left Command1 at 600.


**Symptom.** A control placed with `add_control` is created and auto-selected, but there is no way to (a) select a
*different, existing* control, (b) delete a control, or (c) exercise undo/redo of a designer edit through MCP. The
controls drawn on the designer canvas are **not individual nodes in `dump_visual_tree`** (the canvas paints them; the
tree shows only the Properties-pane `ObjectSelector` combo and dock chrome), so `interact`/`press_key` have no path
to target a specific control, and there is no `select_control` / `delete_control` tool.

**How it bit (bug-hunt batch-2 designer fixes).** Verifying the four `FormEditViewModel` fixes — duplicate-name
avoidance (needs *delete then re-add*), multi-select **Delete** (needs a multi-selection), and undo **z-order**
restore (needs cut + undo) — was not drivable. Only the no-collision naming path was confirmed live (`add_control`
twice → `Command0`, `Command1`). The rest were verified by build + code review against the already-proven
`CutSelectedControls` path and the standard ascending-index restore invariant.

**Root cause.** The designer canvas is a custom-drawn surface; component VMs aren't surfaced as automation nodes, and
the designer's selection/delete/undo aren't exposed as `ICommand`s reachable via `interact invoke_command`.

**Fix consideration.** Small, high-leverage additions: `select_control(formName, controlName)` (drive the designer's
`SelectedComponent`/`SetSelectedComponents`), `delete_selected_controls`, and `designer_redo` to pair with the
existing `invoke_designer_undo` — plus surfacing each canvas control as a `dump_visual_tree` node with its name, so
`interact` can click/rubber-band it. That would make the whole designer edit loop (add → select → move → delete →
undo/redo) MCP-verifiable.
