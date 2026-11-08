# `interact expand` could not open a context menu on a wrapper under the node — **CLOSED** (#2877, 2026-11-08)

**Symptom (2026-11-02, #2854's live check, Windows).** Each row of the Properties window has a context menu, and
Picture's holds "Export picture...". A first-contact caller told to use "the Picture row's export button" dumped the row
with `interactiveOnly: false` and found its text, its edit box and "Load picture", nothing else, and
`dump_visual_tree(match: "Export")` found nothing. Told it was a context menu, a second caller called
`interact(target: "…/ListItem[Picture]", action: "expand")`, which replied `element does not support 'expand'`.
`interact(…, action: "click", value: "10,8 right")` opened it. The `ContextMenu` is on the `Border` at the root of the
row's `DataTemplate`, which the dump collapses into the `ListItem`, and `expand` and the providers list look only at the
addressed control.

**Workaround.** Right-click the row with `interact` action `click` and a value such as `10,8 right`; the menu's items
then appear under the row.

**Fixed (#2877).** Measured on main at 30e1fc284: the Border is no longer collapsed. It's the row's `None` child with
`expandCollapse`, and `expand` on `…/ListItem[Caption]/None` opens the menu. The row itself still refused, with no hint,
and the row is what a caller aims at. `expand` and `collapse` on a node with no menu of its own now reach the menu on its
one unnamed child that has one, and the reply says so. Two such children stay the caller's choice, and are refused as
before. The row doesn't advertise `expandCollapse` itself: that would mean collecting children for every node while
classifying it, and the child already advertises it. Guarded by two cases in `LogicallyParentedPopupTests`.
