# `inspect_element` on a list row did not say whether it was selected — **CLOSED** (#2863, 2026-11-09)

**Symptom (2026-11-02, #2834's live check, Windows).** Both rows of the Save Changes dialog were selected.
`inspect_element(target: "Window/Custom/List/ScrollViewer[PART_ScrollViewer]/ListItem[Module1]", window: "auto")`
replied with `providers: ["selectionItem", "multiSelectItem"]` and `selectionItems: []`. `selectionItems` describes a
list's selection, so a row has none of its own, and nothing else in the reply says whether the row is selected. The
first-contact caller reported "the rows have no per-row selected flag".

**Workaround.** Inspect the list instead: its `selectionItems` names the selected rows.

**Fixed (#2863).** A node with the `selectionItem` provider now has `isSelected`, read from the provider, or else from
the list's own mark on the row. It's absent on anything without the provider. Guarded by a case in `InspectItemCountTests`.
