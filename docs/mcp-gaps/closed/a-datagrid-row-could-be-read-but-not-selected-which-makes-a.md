# A DataGrid row could be read but not selected, which makes a master-detail window undrivable — **CLOSED** (moved from the live file 2026-09-22)

> **Closed by #402 (#369 phases 4 and 5).**

**Symptom.** With the protocol inspector's grid on screen and its rows enumerated by
`dump_visual_tree`:

```
interact(".../DataGrid/DataItem[#2]", "select")
-> {"success": false, "mechanism": "peer", "error": "element does not support 'select'"}
```

`inspect_element` on the same row reported `"providers": []` and `"selectionItems": []`.

**Cause.** A `DataGridRow`'s automation peer exposes no `ISelectionItemProvider`, and `DoSelect` refused
when there was no provider. There was no second route either: the reflection actions set a view-model
property by name and coerce the value from a string, and the property that holds a selection is a row
object no string can name. So the grid was fully readable and completely inert.

**How it bit.** Selecting a row is not a detail of this window, it is the window: click a row, read the
body that crossed the wire. Every master-detail surface in the IDE has the same shape, so the gap was one
control wide and the whole pattern deep. It surfaced while verifying the detail pane, which could not be
verified at all until it was fixed.

**Fixed.** `UiAutomationDriver.DoSelect` falls back to selecting through the grid that owns the row —
found by walking up the visual tree, because `DataGridRow.OwningGrid` is internal — and reads
`SelectedItem` back rather than assuming the grid accepted it. `DescribeProviders` now advertises
`selectionItem` on a row, so the verb is discoverable instead of being a thing a caller has to try.
Covered headlessly in `UiAutomationDriverTests`.
