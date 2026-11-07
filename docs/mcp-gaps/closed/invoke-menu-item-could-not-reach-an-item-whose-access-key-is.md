# `invoke_menu_item` could not reach an item whose access key is not its first letter — **CLOSED** (#501, 2026-09-22)

> **Fixed** by hexide-io/HexIDE#519, which strips the access-key underscore wherever it falls (`MenuPath`,
> `IDE/HexIDE/Automation/`) and makes a miss name the menu it looked in and every item that menu holds.

**Symptom.** `invoke_menu_item(path: "Project/Add Module")` answers
`{"success":false,"error":"Menu item 'Add Module' not found"}`. The item is plainly there:
`dump_visual_tree` after an `expand` lists it as `MenuItem[Add Module]` with an `invoke` provider, and
invoking it that way works. The top-level `Format` menu fails the same way, so `Format/Align/Lefts` never
gets past its first segment.

**Cause.** The resolver matches each segment against the item's raw header, and strips only a **leading**
access-key underscore. `Add _Module` and `F_ormat` carry theirs mid-word, so no spelling a caller would type
matches them. The resolver walks the menu's logical `Items`, which are complete whether or not the menu has
been opened.

**This entry first recorded the wrong cause**, and it is worth keeping as the trap it was: it blamed
Avalonia for not realising a closed menu's children, on the evidence that a dump of the closed menu bar
shows every item with `"children":[]`. That is true of the *visual* tree `dump_visual_tree` walks and
irrelevant to the logical tree the resolver walks. Corrected by the session working hexide-io/HexIDE#501.

**Why the reply misleads.** The tool's description warns that built-in items using routed commands "may not
execute correctly", so the natural reading of a failure is that the item was found and its command would
not run. It was never found, and "not found" cannot tell a caller a wrong path from a spelling the matcher
does not normalise.

**Workaround.** The generic trio: `dump_visual_tree`, `interact` with `expand` on the top-level item,
`dump_visual_tree` scoped to it, then `interact` with `invoke`.
