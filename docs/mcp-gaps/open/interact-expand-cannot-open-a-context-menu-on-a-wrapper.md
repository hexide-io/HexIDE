# `interact expand` cannot open a context menu on a wrapper the dump collapsed into the node

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

**Suggested fix.** Let `expand` look through the wrappers collapsed into the addressed node for a `ContextMenu` or context
flyout, and list `expand` among the node's providers when it finds one.
[#2877](https://github.com/hexide-io/HexIDE/issues/2877).
