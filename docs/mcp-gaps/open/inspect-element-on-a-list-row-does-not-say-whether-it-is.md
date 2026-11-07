# `inspect_element` on a list row does not say whether it is selected

**Symptom (2026-11-02, #2834's live check, Windows).** Both rows of the Save Changes dialog were selected.
`inspect_element(target: "Window/Custom/List/ScrollViewer[PART_ScrollViewer]/ListItem[Module1]", window: "auto")`
replied with `providers: ["selectionItem", "multiSelectItem"]` and `selectionItems: []`. `selectionItems` describes a
list's selection, so a row has none of its own, and nothing else in the reply says whether the row is selected. The
first-contact caller reported "the rows have no per-row selected flag".

**Workaround.** Inspect the list instead: its `selectionItems` names the selected rows.

**Suggested fix.** Give a node with the `selectionItem` provider an `isSelected` field, read as `SelectVerified`
already reads it: the provider's `IsSelected`, or else the list's own mark on the row.
[#2863](https://github.com/hexide-io/HexIDE/issues/2863).
